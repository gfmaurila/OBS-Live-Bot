namespace ObsLiveBot.Contracts.Chat;

public sealed record ChatResponseStateResponse(
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

/// <summary>
/// One written reply's outcome, with the full correlation chain from the viewer message that caused it.
/// Nothing here is an authentication value: no token, cookie, session key or platform secret is part of
/// the write path's recorded data.
/// </summary>
public sealed record ChatResponseRecordResponse(
    Guid ChatResponseId,
    Guid? InteractionId,
    string? SourceMessageId,
    string Provider,
    string ChannelId,
    string? UserId,
    string? UserName,
    string SenderName,
    string Status,
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
    int Attempt);

public sealed record ChatResponseSenderResponse(
    string Name,
    bool Selected,
    bool Available,
    bool Development,
    string Status,
    IReadOnlyList<string> SupportedProviders,
    long Requests,
    long Successes,
    long Failures,
    DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset? LastFailureAtUtc,
    string? LastErrorCode);

public sealed record ChatResponseSendersResponse(IReadOnlyList<ChatResponseSenderResponse> Senders);

/// <summary>Effective written-reply settings and whether a runtime override is in force.</summary>
public sealed record ChatResponseSettingsResponse(
    bool Enabled,
    bool ConfiguredEnabled,
    bool Overridden,
    string Source);

/// <summary>
/// Request to change written-reply settings at runtime. An absent <c>enabled</c> leaves the current value
/// alone; <c>reset</c> discards any override and returns to shipped configuration.
/// </summary>
public sealed record UpdateChatResponseSettingsRequest(bool? Enabled = null, bool Reset = false);

/// <summary>
/// Read and write capability for one chat platform.
/// <para>
/// Read and write are reported separately and are never inferred from one another. <c>WriteStatus</c> is
/// one of <c>Ready</c>, <c>NotConfigured</c>, <c>Unsupported</c> or <c>Degraded</c>, and a connected capture
/// provider never implies <c>Ready</c> on the write side.
/// </para>
/// </summary>
public sealed record ChatProviderCapabilityResponse(
    string Provider,
    bool CanRead,
    string ReadStatus,
    string ReadDetail,
    bool CanWrite,
    bool WriteReady,
    string WriteStatus,
    string WriteDetail,
    string? WriteTransport,
    bool WriteRequiresAuthentication,
    bool WriteAuthenticated,
    int MaxMessageCharacters);

public sealed record ChatProviderCapabilitiesResponse(IReadOnlyList<ChatProviderCapabilityResponse> Providers);

/// <summary>
/// Controlled development request. It writes operator-supplied text through the exact production gate,
/// queue and sender, so a real platform send can be validated without enabling automatic chat replies.
/// </summary>
public sealed record DevelopmentChatResponseRequest(
    string Provider,
    string ChannelId,
    string Text);

/// <summary>
/// Outcome of a controlled development write. <c>Origin</c> is always <c>development-validation</c>, so a
/// reply produced this way can never be mistaken for one generated from a real viewer message.
/// </summary>
public sealed record DevelopmentChatResponseResponse(
    bool Success,
    Guid ChatResponseId,
    string Provider,
    string ChannelId,
    string SenderName,
    string Status,
    string? Reason,
    bool Simulated,
    double? DurationMilliseconds,
    int CharacterCount,
    bool Truncated,
    string Origin,
    int Attempt);