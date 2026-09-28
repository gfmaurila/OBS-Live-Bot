using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Domain.Interactions;

public enum InteractionDecisionType
{
    Ignore = 0,
    Respond = 1
}

public enum InteractionResponseMode
{
    None = 0,
    Text = 1,
    Voice = 2,
    TextAndVoice = 3
}

public enum InteractionStatus
{
    Ignored = 0,
    Completed = 1,
    Failed = 2
}

public enum InteractionCooldownScope
{
    User = 0,
    Channel = 1,
    Global = 2
}

public sealed record InteractionDecision(
    Guid DecisionId,
    Guid ChatEventId,
    LiveChatProviderType Provider,
    string ChannelId,
    string UserId,
    string UserDisplayName,
    InteractionDecisionType DecisionType,
    string Reason,
    InteractionResponseMode RequestedResponseMode,
    DateTimeOffset CreatedAtUtc,
    long Sequence,
    string CorrelationId);

public sealed record AiInteractionRequest(
    Guid InteractionId,
    LiveChatProviderType Provider,
    string Channel,
    string User,
    string UserDisplayName,
    string UserMessage,
    IReadOnlyList<string> ConversationContext,
    string SystemInstructions,
    string CorrelationId);

public sealed record AiInteractionResponse(
    string? Text,
    string ProviderName,
    string ModelName,
    TimeSpan Duration,
    bool Success,
    string? ErrorCode,
    string CorrelationId,
    bool IsSimulated);

public sealed record TextToSpeechRequest(
    Guid InteractionId,
    string Text,
    string? Voice,
    string Language,
    string CorrelationId);

public sealed record TextToSpeechResult(
    bool Success,
    string ProviderName,
    string AudioFormat,
    string? AudioPath,
    TimeSpan Duration,
    string? ErrorCode,
    string CorrelationId,
    bool IsSimulated);

public sealed record InteractionResult(
    Guid InteractionId,
    InteractionDecision Decision,
    InteractionStatus Status,
    string? ResponseText,
    string? AiProviderName,
    string? AiModelName,
    bool? AiSuccess,
    TimeSpan? AiDuration,
    string? TtsProviderName,
    bool? TtsSuccess,
    string? AudioFormat,
    string? AudioPath,
    TimeSpan? TtsDuration,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long Sequence,
    string CorrelationId);

public sealed record InteractionStateSnapshot(
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

public sealed record InteractionProviderSnapshot(
    string Kind,
    string Name,
    bool Selected,
    bool Available,
    bool Development,
    string Status);
