using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>
/// Writes a reply into a live chat channel through Social Stream Ninja.
/// <para>
/// SSN 0.4.18 has no send command, so the write is: resolve the source that matches the platform and
/// channel, inspect its page, identify the chat composer among the fields SSN reports as fillable, fill
/// it, and press Enter. That identification is the safety-critical step - see <see cref="SelectComposer"/>.
/// </para>
/// <para>
/// Two properties of that route shape this sender. Page references expire after 30 seconds and a fresh
/// inspection invalidates every earlier one, so inspect and interact happen back to back with no
/// intervening work. And a fill is applied to the DOM before the Enter key is delivered to the window, so
/// a crash between the two leaves unsent text in the composer rather than a lost message.
/// </para>
/// <para>
/// That second property is what makes retrying safe here, and it is deliberately narrow. Only conditions
/// detected <em>before</em> anything was typed are retried, and the retry re-runs the whole
/// inspect/fill/Enter sequence from a fresh page reference. Once a fill has been accepted the sequence is
/// never repeated, because at that point StudioOS can no longer tell whether the Enter reached the page -
/// and a duplicate public message is a worse failure than a missed one. Failing closed there is a
/// deliberate trade: at most one reply is ever lost, and never one is posted twice.
/// </para>
/// <para>
/// The runtime counters follow the same distinction. A request is one reply StudioOS was asked to deliver,
/// counted once at the entry to this sender however many transport attempts it took; the attempts belong to
/// the individual result, so a bounded retry can never inflate the request count an operator reads.
/// </para>
/// </summary>
public sealed class SocialStreamNinjaChatResponseSender(
    SocialStreamNinjaChatWriteClient writeClient,
    ChatWriteTargetResolver targetResolver,
    ChatWriteAdapterRegistry adapters,
    TimeProvider timeProvider,
    ILogger<SocialStreamNinjaChatResponseSender> logger) : IChatResponseSender
{
    private const int InspectElementLimit = 120;
    private const int MaxAttempts = 2;

    /// <summary>
    /// Ranked hints for identifying a chat composer from its accessible name. Ordered by specificity so
    /// an exact match on a chat phrase outranks a generic "send" that could be a login form.
    /// </summary>
    private static readonly string[] ComposerNameHints =
    [
        "send a message",
        "send message",
        "chat message",
        "message the streamer",
        "message",
        "chatbox",
        "chat box",
        "composer",
        "say something",
        "post a message",
        "send"
    ];

    /// <summary>
    /// Accessible-name fragments that rule a fillable field out as the chat composer. A page that offers a
    /// search box, a login form or a settings panel offers them beside the real composer, and their names
    /// say so; a field that says any of these is never typed into regardless of how it scores otherwise.
    /// </summary>
    private static readonly string[] NonComposerNameHints =
    [
        "search",
        "filter",
        "sign in",
        "log in",
        "login",
        "sign up",
        "password",
        "email",
        "e-mail",
        "username",
        "user name",
        "account",
        "settings",
        "config",
        "url"
    ];

    private readonly object _counters = new();
    private long _requests;
    private long _successes;
    private long _failures;
    private DateTimeOffset? _lastSuccessAtUtc;
    private DateTimeOffset? _lastFailureAtUtc;
    private string? _lastErrorCode;

    public string Name => "SocialStreamNinja";

    public bool IsDevelopment => false;

    /// <summary>
    /// Availability reflects the configuration this sender was given, not a probe. Probing here would
    /// need a live SSN window and a real channel, and a gate that depended on one would refuse writes
    /// whenever the probe was slow - which is exactly when a reply matters. Per-platform readiness is
    /// reported separately by the capability API.
    /// </summary>
    public bool IsAvailable => true;

    public IReadOnlyList<LiveChatProviderType> SupportedProviders => adapters.GetSupportedProviders();

    public ChatResponseSenderRuntimeState GetRuntimeState()
    {
        lock (_counters)
        {
            return new ChatResponseSenderRuntimeState(
                IsAvailable,
                IsAvailable ? "Available" : "Unavailable",
                _requests,
                _successes,
                _failures,
                _lastSuccessAtUtc,
                _lastFailureAtUtc,
                _lastErrorCode);
        }
    }

    public async Task<ChatResponseSendResult> SendAsync(
        ChatResponseSendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        lock (_counters) _requests++;

        var adapter = adapters.Find(request.Provider);
        if (adapter is null)
            return Fail(stopwatch, request, "PROVIDER_WRITE_UNSUPPORTED", MaxAttempts);

        try
        {
            var probe = await targetResolver
                .ProbeAsync(adapter, request.ChannelId, cancellationToken).ConfigureAwait(false);
            if (probe.SourceId is null)
                return Fail(stopwatch, request, "CHAT_SOURCE_NOT_FOUND", MaxAttempts);

            var outcome = await AttemptSequenceAsync(adapter, probe, request, cancellationToken)
                .ConfigureAwait(false);
            if (outcome.Success)
                return Succeed(stopwatch, outcome.Attempt, adapter.MaxMessageCharacters, probe.SourceId);

            // A failure that happened after the composer was filled is ambiguous: the Enter may or may not
            // have landed. No further attempt is made from here, which is the fail-closed rule.
            return Fail(stopwatch, request, outcome.ErrorCode!, outcome.Attempt, adapter.MaxMessageCharacters);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Written reply {ChatResponseId} failed on Social Stream Ninja",
                request.ChatResponseId);
            return Fail(stopwatch, request, "CHAT_SEND_EXCEPTION", MaxAttempts);
        }
    }

    private async Task<(bool Success, string? ErrorCode, int Attempt)> AttemptSequenceAsync(
        IChatWriteAdapter adapter,
        ChatWriteProbe probe,
        ChatResponseSendRequest request,
        CancellationToken cancellationToken)
    {
        var sourceId = probe.SourceId!;
        var lastError = "CHAT_SEND_FAILED";
        var lastAttempt = 1;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            lastAttempt = attempt;
            cancellationToken.ThrowIfCancellationRequested();

            // Nothing has been typed at the top of an attempt. The flag flips the moment a fill is
            // accepted, and from then on this attempt's failure - retryable code or not - ends the
            // sequence instead of starting it again.
            var typed = false;
            try
            {
                var (inspectOutcome, elements) = await writeClient
                    .InspectElementsAsync(sourceId, InspectElementLimit, cancellationToken)
                    .ConfigureAwait(false);

                if (!inspectOutcome.Ok)
                {
                    // Detected before anything was typed, so retrying cannot duplicate a message.
                    lastError = MapErrorCode(inspectOutcome.ErrorCode);
                    if (IsRetryable(lastError)) continue;
                    return (false, lastError, attempt);
                }

                var composer = SelectComposer(elements);
                if (composer is null)
                {
                    // Nothing on the page announced itself as a chat composer, so there is no field it is
                    // safe to type into. Re-inspecting an already-loaded page would answer the same way,
                    // so this is reported instead of retried.
                    return (false, "CHAT_COMPOSER_NOT_FOUND", attempt);
                }

                var text = request.Text;
                if (text.Length > adapter.MaxMessageCharacters)
                {
                    // The platform would reject this outright, and the page would either silently drop it
                    // or post something the operator did not write. Reporting is the only safe answer.
                    return (false, "CHAT_TEXT_TOO_LONG", attempt);
                }

                var fill = await writeClient
                    .FillAsync(sourceId, composer.Ref!, text, cancellationToken)
                    .ConfigureAwait(false);
                if (!fill.Ok)
                {
                    lastError = MapErrorCode(fill.ErrorCode);
                    if (IsRetryable(lastError)) continue;
                    return (false, lastError, attempt);
                }

                // From here on StudioOS cannot know whether Enter reached the page. Never repeat the
                // sequence; report and let the outcome be unknown rather than risk a second public post.
                typed = true;
                var enter = await writeClient
                    .PressEnterAsync(sourceId, composer.Ref!, cancellationToken)
                    .ConfigureAwait(false);
                if (!enter.Ok)
                    return (false, MapErrorCode(enter.ErrorCode), attempt);

                logger.LogInformation(
                    "Written reply {ChatResponseId} delivered to {Provider} chat via source {SourceId}",
                    request.ChatResponseId, request.Provider, sourceId);
                return (true, null, attempt);
            }
            catch (SocialStreamNinjaCommandRejectedException ex)
            {
                // Recorded before any decision: if the last attempt is spent this is the reason the reply
                // is being reported, and "the window closed again" is far more useful than a generic code.
                lastError = MapErrorCode(ex.ErrorCode);

                // A rejection before anything was typed is a proven pre-send failure, so a bounded retry
                // cannot duplicate anything. A rejection after the fill is not: Enter may or may not have
                // reached the page, and STALE_PAGE_REF here means exactly that StudioOS cannot tell. The
                // sequence ends either way, and the reply is reported undelivered.
                if (!typed && IsRetryable(lastError)) continue;
                return (false, lastError, attempt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        return (false, lastError, lastAttempt);
    }

    /// <summary>
    /// Picks the element the reply may be typed into, or nothing when the page offers no safe answer.
    ///
    /// <para>
    /// Every field here ends up in a public chat, so "fillable" is nowhere near enough to act on. A live
    /// chat page carries search boxes, login forms and settings panels; picking one of those would put a
    /// viewer-facing reply somewhere the operator never wrote it and never sees it. Selection therefore
    /// demands positive evidence that the field is the chat composer, and fails closed without it.
    /// </para>
    /// <para>
    /// The evidence is ranked, not filtered: an exact chat phrase in the accessible name outranks a
    /// generic "send", which outranks the shape of the control. Elements are returned in reverse document
    /// order, so a stable score - never position - decides between equally plausible fields.
    /// </para>
    /// <para>
    /// One structural fallback exists. Some chat composers are exposed as a content-editable box with no
    /// accessible name at all, and a page whose only fillable surface is a multi-line text box is strong
    /// evidence for one. A lone single-line input is not accepted: on a chat page that is far more often
    /// a search or login field than the composer.
    /// </para>
    /// </summary>
    internal static SocialStreamNinjaPageElement? SelectComposer(
        IReadOnlyList<SocialStreamNinjaPageElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        // Fillable is a hard requirement because SSN itself refuses a fill on anything else. Disabled and
        // unnamed-reference elements are skipped because a fill on them cannot do anything useful, and a
        // field whose own name marks it as a credential or lookup control is skipped entirely - it is
        // never a composer, whatever the rest of the page looks like.
        var candidates = elements.Where(IsPossibleComposer).ToArray();
        if (candidates.Length == 0) return null;

        SocialStreamNinjaPageElement? best = null;
        var bestScore = int.MinValue;

        foreach (var element in candidates)
        {
            var hint = MatchComposerHint(element.Name);
            if (hint < 0) continue;

            // A lower hint index is a more specific phrase, so it is worth far more than the shape of the
            // control. Shape only separates candidates that matched the same phrase.
            var score = (ComposerNameHints.Length - hint) * 100;
            if (element.FrameIndex == 0) score += 10;
            if (string.Equals(element.Role, "textbox", StringComparison.OrdinalIgnoreCase)) score += 5;
            if (string.Equals(element.Tag, "textarea", StringComparison.OrdinalIgnoreCase)) score += 3;

            if (score > bestScore)
            {
                bestScore = score;
                best = element;
            }
        }

        if (best is not null) return best;

        return candidates.Length == 1 && IsMultiLineTextSurface(candidates[0]) ? candidates[0] : null;
    }

    private static bool IsPossibleComposer(SocialStreamNinjaPageElement element) =>
        element.Fillable && !element.Disabled && !string.IsNullOrWhiteSpace(element.Ref) &&
        !ContainsAny(element.Name, NonComposerNameHints);

    /// <summary>Index of the most specific composer phrase in the name, or -1 when it reads like nothing.</summary>
    private static int MatchComposerHint(string name)
    {
        var lower = name.ToLowerInvariant();
        for (var i = 0; i < ComposerNameHints.Length; i++)
        {
            if (lower.Contains(ComposerNameHints[i], StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private static bool ContainsAny(string name, string[] hints)
    {
        var lower = name.ToLowerInvariant();
        return hints.Any(hint => lower.Contains(hint, StringComparison.Ordinal));
    }

    /// <summary>
    /// A free-text surface built for more than one line: a textarea, or any non-input control exposed as a
    /// textbox - which is how a content-editable chat composer appears in the accessibility tree.
    /// </summary>
    private static bool IsMultiLineTextSurface(SocialStreamNinjaPageElement element) =>
        string.Equals(element.Tag, "textarea", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(element.Role, "textbox", StringComparison.OrdinalIgnoreCase) &&
         !string.Equals(element.Tag, "input", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Only conditions proven to have happened before anything was typed are retried. Each one means the
    /// page or the window went away while StudioOS still held the text, so nothing reached the chat.
    /// </summary>
    private static bool IsRetryable(string errorCode) =>
        errorCode is "CHAT_STALE_PAGE_REF" or "CHAT_PAGE_UNAVAILABLE" or "CHAT_SOURCE_WINDOW_UNAVAILABLE";

    /// <summary>
    /// Preserves SSApp's own codes where they mean something actionable and folds the rest into StudioOS
    /// codes, so a caller sees one vocabulary without losing the distinction between "try again" and
    /// "this will never work".
    /// </summary>
    internal static string MapErrorCode(string? ssnCode) => ssnCode switch
    {
        "STALE_PAGE_REF" => "CHAT_STALE_PAGE_REF",
        "SOURCE_WINDOW_UNAVAILABLE" => "CHAT_SOURCE_WINDOW_UNAVAILABLE",
        "SOURCE_PAGE_UNAVAILABLE" => "CHAT_PAGE_UNAVAILABLE",
        "UNSAFE_FILL_TARGET" => "CHAT_COMPOSER_UNSAFE",
        "SOURCE_NOT_FOUND" => "CHAT_SOURCE_NOT_FOUND",
        "TEXT_TOO_LONG" => "CHAT_TEXT_TOO_LONG",
        "INVALID_TARGET" => "CHAT_COMMAND_REJECTED",
        _ => "CHAT_COMMAND_REJECTED"
    };

    private ChatResponseSendResult Succeed(
        Stopwatch stopwatch,
        int attempt,
        int maxMessageCharacters,
        string sourceId) =>
        Complete(stopwatch, success: true, errorCode: null, sourceId, attempt, maxMessageCharacters);

    private ChatResponseSendResult Fail(
        Stopwatch stopwatch,
        ChatResponseSendRequest request,
        string errorCode,
        int attempt,
        int maxMessageCharacters = 0)
    {
        logger.LogWarning(
            "Written reply {ChatResponseId} for interaction {InteractionId} not delivered after {Attempt} attempt(s) ({ErrorCode})",
            request.ChatResponseId, request.InteractionId, attempt, errorCode);
        return Complete(stopwatch, success: false, errorCode, sourceId: null, attempt, maxMessageCharacters);
    }

    private ChatResponseSendResult Complete(
        Stopwatch stopwatch,
        bool success,
        string? errorCode,
        string? sourceId,
        int attempt,
        int maxMessageCharacters)
    {
        var now = timeProvider.GetUtcNow();
        lock (_counters)
        {
            if (success)
            {
                _successes++;
                _lastSuccessAtUtc = now;
                _lastErrorCode = null;
            }
            else
            {
                _failures++;
                _lastFailureAtUtc = now;
                _lastErrorCode = errorCode;
            }
        }

        return new ChatResponseSendResult(
            success, errorCode, stopwatch.Elapsed, IsSimulated: false, sourceId, attempt, maxMessageCharacters);
    }
}