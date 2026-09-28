using ObsLiveBot.Contracts.Interactions;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Features.Interactions;

internal static class InteractionResponseMapper
{
    public static InteractionResponse Map(InteractionResult result) => new(
        result.InteractionId,
        new InteractionDecisionResponse(
            result.Decision.DecisionId,
            result.Decision.ChatEventId,
            result.Decision.Provider.ToString(),
            result.Decision.ChannelId,
            result.Decision.UserId,
            result.Decision.UserDisplayName,
            result.Decision.DecisionType.ToString(),
            result.Decision.Reason,
            result.Decision.RequestedResponseMode.ToString(),
            result.Decision.CreatedAtUtc,
            result.Decision.Sequence,
            result.Decision.CorrelationId),
        result.Status.ToString(),
        result.ResponseText,
        result.AiProviderName,
        result.AiModelName,
        result.AiSuccess,
        result.AiDuration?.TotalMilliseconds,
        result.AiFallbackUsed,
        result.PrimaryAiErrorCode,
        result.TtsProviderName,
        result.TtsSuccess,
        result.AudioFormat,
        result.AudioPath,
        result.TtsDuration?.TotalMilliseconds,
        result.ErrorCode,
        result.CreatedAtUtc,
        result.CompletedAtUtc,
        result.Sequence,
        result.CorrelationId);
}
