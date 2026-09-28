namespace ObsLiveBot.Contracts.Interactions;

public sealed record InteractionDecisionResponse(
    Guid DecisionId,
    Guid ChatEventId,
    string Provider,
    string ChannelId,
    string UserId,
    string UserDisplayName,
    string DecisionType,
    string Reason,
    string RequestedResponseMode,
    DateTimeOffset CreatedAtUtc,
    long Sequence,
    string CorrelationId);

public sealed record InteractionResponse(
    Guid InteractionId,
    InteractionDecisionResponse Decision,
    string Status,
    string? ResponseText,
    string? AiProviderName,
    string? AiModelName,
    bool? AiSuccess,
    double? AiDurationMilliseconds,
    string? TtsProviderName,
    bool? TtsSuccess,
    string? AudioFormat,
    string? AudioPath,
    double? TtsDurationMilliseconds,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long Sequence,
    string CorrelationId);

public sealed record InteractionStateResponse(
    string Status,
    long Total,
    long Ignored,
    long Completed,
    long Failed,
    int BufferSize,
    int BufferCapacity,
    int CooldownEntries,
    int CooldownCapacity,
    DateTimeOffset? LastInteractionAtUtc);

public sealed record InteractionProviderResponse(
    string Kind,
    string Name,
    bool Selected,
    bool Available,
    bool Development,
    string Status);

public sealed record InteractionProvidersResponse(IReadOnlyList<InteractionProviderResponse> Providers);

public sealed record DevelopmentInteractionTestRequest(
    string Provider,
    string ChannelId,
    string UserId,
    string UserDisplayName,
    string Message,
    string ResponseMode);
