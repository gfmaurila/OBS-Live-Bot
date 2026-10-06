namespace ObsLiveBot.Domain.Chat;

/// <summary>
/// Lifecycle of one attempt to write a reply into a chat channel. A written reply is a separate
/// concern from audio narration: it has its own gate, its own queue and its own outcome.
/// </summary>
public enum ChatResponseStatus
{
    Disabled = 0,
    Queued = 1,
    Sending = 2,
    Sent = 3,
    Skipped = 4,
    Failed = 5,
    Cancelled = 6
}

/// <summary>
/// One accepted intent to write text into a chat channel. The intent is already past every gate,
/// so it carries the exact text to deliver plus the identity that makes the write idempotent.
/// <para>
/// Correlation is carried in full on purpose. A written reply leaves the process and appears in a public
/// chat, so an operator later has to be able to answer "which viewer message produced this line, and did
/// it arrive" from StudioOS alone. Every field here is non-secret; none of it is an authentication value.
/// </para>
/// </summary>
public sealed record ChatResponseIntent(
    Guid ChatResponseId,
    Guid? InteractionId,
    string? SourceMessageId,
    LiveChatProviderType Provider,
    string ChannelId,
    string? UserId,
    string? UserName,
    string Text,
    string IdempotencyKey,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// One written reply's outcome. It carries the delivered text and the full correlation chain, because
/// unlike audio narration this artefact exists in a place the operator cannot otherwise inspect.
/// </summary>
public sealed record ChatResponseResult(
    Guid ChatResponseId,
    Guid? InteractionId,
    string? SourceMessageId,
    LiveChatProviderType Provider,
    string ChannelId,
    string? UserId,
    string? UserName,
    string SenderName,
    ChatResponseStatus Status,
    string? Reason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long Sequence,
    string CorrelationId,
    string ResponseText,
    int CharacterCount,
    string IdempotencyKey,
    bool Simulated,
    double? DurationMilliseconds,
    int Attempt = 1);

/// <summary>Read-only projection of the written-reply subsystem.</summary>
public sealed record ChatResponseStateSnapshot(
    bool Enabled,
    string Status,
    string SelectedSender,
    string? SenderStatus,
    bool SenderAvailable,
    int QueueDepth,
    int QueueCapacity,
    long Queued,
    long Sent,
    long Skipped,
    long Failed,
    long Cancelled,
    long Rejected,
    int HistorySize,
    int HistoryCapacity,
    int CooldownEntries,
    int CooldownCapacity,
    int IdempotencyEntries,
    int IdempotencyCapacity,
    int EchoEntries,
    int EchoCapacity,
    DateTimeOffset? LastSentAtUtc,
    DateTimeOffset? LastFailureAtUtc,
    string? LastFailureReason);

/// <summary>Runtime counters of one <see cref="ChatResponseSenderRuntimeState"/> implementation.</summary>
public sealed record ChatResponseSenderRuntimeState(
    bool Available,
    string Status,
    long Requests,
    long Successes,
    long Failures,
    DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset? LastFailureAtUtc,
    string? LastErrorCode);

/// <summary>Effective written-reply settings, after any deliberate runtime override.</summary>
/// <param name="Enabled">The value actually in force right now.</param>
/// <param name="ConfiguredEnabled">The shipped configuration value, which a restart restores.</param>
/// <param name="Overridden">True while a runtime override is in force.</param>
/// <param name="Source">Either <c>Configuration</c> or <c>RuntimeOverride</c>.</param>
public sealed record ChatResponseSettingsSnapshot(
    bool Enabled,
    bool ConfiguredEnabled,
    bool Overridden,
    string Source);

/// <summary>A request to change written-reply settings at runtime. Absent values are left untouched.</summary>
public sealed record ChatResponseSettingsUpdate(bool? Enabled = null, bool Reset = false);

/// <summary>One state transition of a written reply, for observers outside the process.</summary>
public sealed record ChatResponseEvent(
    Guid EventId,
    Guid ChatResponseId,
    Guid? InteractionId,
    string EventType,
    string? Reason,
    LiveChatProviderType Provider,
    string ChannelId,
    DateTimeOffset OccurredAtUtc,
    long Sequence,
    string CorrelationId);

/// <summary>Deterministic outcome of preparing reply text for a chat platform.</summary>
public sealed record ChatResponseText(
    bool Success,
    string? Text,
    string? ErrorCode,
    int SourceCharacterCount = 0,
    bool Truncated = false);

/// <summary>
/// Pure, side-effect free rules for written replies. Domain owns them because they are invariants of
/// the capability itself: what may be written, how it is normalized, and how one write is identified.
/// No option object, clock, HTTP client or platform SDK may appear here.
/// </summary>
public static class ChatResponsePolicy
{
    /// <summary>
    /// Collapses every run of whitespace into a single space and removes control characters.
    ///
    /// Collapsing is not cosmetic. Chat platforms render a written line as a single message, and a
    /// reply that differs from a previous one only by extra spacing would defeat text-based loop and
    /// duplicate detection while looking identical to a viewer.
    /// </summary>
    public static string Collapse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;
        var wrote = false;
        foreach (var character in value)
        {
            // Whitespace becomes a single space so a line break reflows into one chat line. Other
            // control characters are dropped outright: they are never separators, they are artefacts,
            // and letting one stand in for a space would make the text look different from what a
            // viewer sees.
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = wrote;
                continue;
            }

            if (char.IsControl(character))
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
            wrote = true;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Normalizes reply text and truncates it on a rune boundary so a multi-byte character can never
    /// be split in half by the platform.
    /// </summary>
    public static ChatResponseText PrepareText(string? raw, string? prefix, int maxCharacters)
    {
        var source = raw ?? string.Empty;
        var collapsed = Collapse(source);
        if (collapsed.Length == 0)
            return new ChatResponseText(false, null, "EMPTY_TEXT", source.Length);

        var body = collapsed;
        if (!string.IsNullOrEmpty(prefix))
        {
            var safePrefix = Collapse(prefix);
            body = safePrefix.EndsWith(' ') ? safePrefix + body : safePrefix + " " + body;
        }

        var truncated = false;
        if (maxCharacters > 0 && body.Length > maxCharacters)
        {
            truncated = true;
            body = string.Concat(body.EnumerateRunes().Take(maxCharacters).Select(rune => rune.ToString()));
            body = body.TrimEnd();
            // Trimming can empty the text when the limit lands inside leading whitespace only.
            if (body.Length == 0)
                return new ChatResponseText(false, null, "EMPTY_TEXT", source.Length);
        }

        return new ChatResponseText(true, body, null, source.Length, truncated);
    }

    /// <summary>
    /// The identity of one write. Derived from the interaction, so replaying the same completed
    /// interaction - by a retry, a duplicated notification or a manual request - resolves to the same
    /// key and can therefore be suppressed instead of posted twice.
    /// </summary>
    public static string ComposeIdempotencyKey(
        LiveChatProviderType provider,
        string channelId,
        Guid chatResponseId) =>
        string.Join('|', provider.ToString(), channelId ?? string.Empty, chatResponseId.ToString("N"));

    /// <summary>
    /// Normalizes a channel identity for comparisons that must ignore provider-specific casing but
    /// still be provider scoped.
    /// </summary>
    public static string NormalizeChannel(string? channelId) => (channelId ?? string.Empty).Trim();

    /// <summary>Whether a written reply may be attempted at all, and why not when it may not.</summary>
    public sealed record Admission(bool Allowed, string? Reason)
    {
        public static Admission Accept() => new(true, null);

        public static Admission Reject(string reason) => new(false, reason);
    }

    /// <summary>
    /// The platform-neutral gate rules. Everything here is a decision the capability makes on its own,
    /// independent of which sender or which transport is selected.
    /// </summary>
    public static Admission Evaluate(
        bool enabled,
        string? providerName,
        string channelId,
        ChatResponseText text)
    {
        if (!enabled) return Admission.Reject("WRITTEN_RESPONSES_DISABLED");
        if (string.IsNullOrWhiteSpace(providerName))
            return Admission.Reject("SENDER_NOT_SELECTED");
        if (NormalizeChannel(channelId).Length == 0)
            return Admission.Reject("MISSING_CHANNEL");
        if (!text.Success || string.IsNullOrWhiteSpace(text.Text))
            return Admission.Reject(text.ErrorCode ?? "EMPTY_TEXT");
        return Admission.Accept();
    }
}
