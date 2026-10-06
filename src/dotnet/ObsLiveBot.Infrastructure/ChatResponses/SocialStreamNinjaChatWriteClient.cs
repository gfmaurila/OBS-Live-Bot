using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>One actionable element of an inspected source page.</summary>
public sealed record SocialStreamNinjaPageElement(
    string? Ref,
    int FrameIndex,
    string Tag,
    string Role,
    string Name,
    bool Fillable,
    bool Disabled);

/// <summary>Result of one SSN command: either a payload or a stable SSApp error code.</summary>
public sealed record SocialStreamNinjaCommandOutcome(bool Ok, string? ErrorCode, string? ErrorMessage);

/// <summary>
/// Write-side wrapper over the SSN page observation API.
/// <para>
/// Social Stream Ninja 0.4.18 exposes no dedicated "send chat message" command. The supported write
/// route is its page observation surface: inspect the live chat page, fill the composer it reports as
/// fillable, then press Enter. That is what this client wraps, and nothing more.
/// </para>
/// <para>
/// It is a separate type from the capture-side command client on purpose. Capture and write are
/// different capabilities with different failure consequences, and sharing one HttpClient and one
/// command surface between them would couple reading a chat to being able to type in one.
/// </para>
/// </summary>
public sealed class SocialStreamNinjaChatWriteClient(
    HttpClient client,
    ILogger<SocialStreamNinjaChatWriteClient> logger) : IChatWriteSourceLister
{
    /// <summary>SSN refuses a fill longer than this, so anything beyond it cannot be written at all.</summary>
    public const int MaxFillCharacters = 2000;

    /// <summary>SSN invalidates every page reference for a source on each new inspection.</summary>
    public static readonly TimeSpan ReferenceTtl = TimeSpan.FromSeconds(30);

    public async Task<SocialStreamNinjaCommandOutcome> FillAsync(
        string sourceId,
        string reference,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (text.Length > MaxFillCharacters)
            return new SocialStreamNinjaCommandOutcome(
                false, "CHAT_TEXT_TOO_LONG", $"SSN accepts at most {MaxFillCharacters} characters per fill.");

        return await InteractAsync(
            sourceId,
            reference,
            "fill",
            text,
            key: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<SocialStreamNinjaCommandOutcome> PressEnterAsync(
        string sourceId,
        string reference,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return await InteractAsync(
            sourceId,
            reference,
            "pressKey",
            text: null,
            key: "Enter",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inspects a source page and returns its actionable elements.
    /// </summary>
    public async Task<(SocialStreamNinjaCommandOutcome Outcome, IReadOnlyList<SocialStreamNinjaPageElement> Elements)>
        InspectElementsAsync(string sourceId, int maxElements, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        using var document = await SendAsync(sourceId, "inspectSourcePage", new
        {
            sourceId,
            maxElements = Math.Clamp(maxElements, 1, 200),
            maxTextChars = 4000,
            elementOrder = "reverse"
        }, cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("elements", out var elements) ||
            elements.ValueKind != JsonValueKind.Array)
            return (new SocialStreamNinjaCommandOutcome(true, null, null), []);

        var result = new List<SocialStreamNinjaPageElement>();
        foreach (var item in elements.EnumerateArray())
        {
            var reference = ReadString(item, "ref");
            if (string.IsNullOrWhiteSpace(reference))
                continue; // Non-actionable elements are reported without a reference and cannot be used.

            result.Add(new SocialStreamNinjaPageElement(
                reference,
                ReadInt(item, "frameIndex") ?? 0,
                ReadString(item, "tag") ?? string.Empty,
                ReadString(item, "role") ?? string.Empty,
                ReadString(item, "name") ?? string.Empty,
                ReadBoolean(item, "fillable"),
                ReadBoolean(item, "disabled")));
        }

        return (new SocialStreamNinjaCommandOutcome(true, null, null), result);
    }

    /// <summary>
    /// Lists the sources this transport can type into, projected straight onto the write-side source
    /// shape. The projection happens here so no capture-side source type enters the write path: what the
    /// write path knows about a source is only what it needs to locate and post a message.
    /// </summary>
    public async Task<IReadOnlyList<ChatWriteSource>> ListSourcesAsync(CancellationToken cancellationToken)
    {
        using var document = await SendAsync(
            string.Empty, "getSources", new { }, allowEmptySource: true, cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("payload", out var payload))
            return [];

        var array = payload.ValueKind == JsonValueKind.Array
            ? payload
            : payload.TryGetProperty("sources", out var sources) ? sources : default;
        if (array.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<ChatWriteSource>();
        foreach (var item in array.EnumerateArray())
        {
            var id = ReadString(item, "id") ?? ReadString(item, "sourceId");
            var target = ReadString(item, "target");
            // A source with no id cannot be typed into and a source with no target belongs to no platform,
            // so both are dropped rather than becoming targets an adapter could pick up.
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target))
                continue;

            var status = ReadString(item, "status") ?? ReadString(item, "state");
            var active = ReadBoolean(item, "active") || ReadBoolean(item, "isActive") ||
                         string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);
            result.Add(new ChatWriteSource(
                id,
                target,
                ReadString(item, "username") ?? ReadString(item, "channel") ?? ReadString(item, "value"),
                ReadString(item, "videoId"),
                active,
                status));
        }

        return result;
    }

    private async Task<SocialStreamNinjaCommandOutcome> InteractAsync(
        string sourceId,
        string reference,
        string action,
        string? text,
        string? key,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(sourceId, "interactSourcePage", new
        {
            sourceId,
            @ref = reference,
            action,
            text,
            key
        }, cancellationToken).ConfigureAwait(false);

        return new SocialStreamNinjaCommandOutcome(true, null, null);
    }

    /// <summary>
    /// Posts one SSN command. A rejection is returned as a typed outcome carrying SSApp's own error
    /// code, because those codes - STALE_PAGE_REF in particular - are what the sender turns into an
    /// actionable decision, and losing them would turn a retryable condition into a generic failure.
    /// </summary>
    private async Task<JsonDocument> SendAsync(
        string sourceId,
        string action,
        object value,
        CancellationToken cancellationToken) =>
        await SendAsync(sourceId, action, value, allowEmptySource: false, cancellationToken).ConfigureAwait(false);

    private async Task<JsonDocument> SendAsync(
        string sourceId,
        string action,
        object value,
        bool allowEmptySource,
        CancellationToken cancellationToken)
    {
        if (!allowEmptySource) ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        try
        {
            using var response = await client
                .PostAsJsonAsync("api/v1/command", new { action, value }, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return document;

            var code = "SSN_COMMAND_REJECTED";
            var message = (string?)null;
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                code = ReadString(error, "code") ?? code;
                message = ReadString(error, "message");
            }

            document.Dispose();
            logger.LogWarning(
                "SSN chat write command {Action} rejected for source {SourceId} with {ErrorCode}",
                action, sourceId, code);
            throw new SocialStreamNinjaCommandRejectedException(code, message ?? action, action);
        }
        catch (SocialStreamNinjaCommandRejectedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller's own cancellation or its write ceiling reached. It must stay a cancellation,
            // otherwise the writer reports a timeout or a shutdown as a platform failure and records
            // reason codes that point away from the real cause.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "SSN chat write command {Action} failed for source {SourceId}", action, sourceId);
            throw new SocialStreamNinjaCommandRejectedException("SSN_COMMAND_FAILED", ex.Message, action, ex);
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}

/// <summary>
/// A rejection SSApp returned for a chat write command. Carries SSApp's own code so callers can react
/// to STALE_PAGE_REF and UNSAFE_FILL_TARGET specifically instead of treating every rejection alike.
/// </summary>
public sealed class SocialStreamNinjaCommandRejectedException(
    string errorCode,
    string message,
    string action,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;

    public string Action { get; } = action;
}
