using MediatR;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.ChatResponses;

/// <summary>
/// Bridges the automatic path: a completed interaction with reply text becomes a queued written reply.
///
/// Three properties matter more than the plumbing:
/// <list type="bullet">
/// <item>It never throws. This runs inside interaction completion, so a written-reply problem must never
/// turn a successful voice interaction into a failed one.</item>
/// <item>It never delays. The only work done inline is the gate; the platform write happens on the
/// bounded queue's reader.</item>
/// <item>It never bypasses the gate. Every reply, automatic or operator-initiated, passes through the
/// same <see cref="ChatResponseGatekeeper"/>.</item>
/// </list>
/// </summary>
public sealed class WrittenChatResponseHandler(
    IChatResponseSettingsStore settings,
    ChatResponseGatekeeper gatekeeper,
    IChatResponseWriter writer,
    ILogger<WrittenChatResponseHandler> logger) : INotificationHandler<InteractionCompletedNotification>
{
    public async Task Handle(InteractionCompletedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            if (!settings.Get().Enabled) return;

            var result = notification.Result;
            if (!ChatResponseGatekeeper.IsEligible(result)) return;

            var admission = gatekeeper.Admit(gatekeeper.CandidateFrom(result));
            if (!admission.Allowed || admission.Intent is null)
            {
                logger.LogDebug(
                    "Written reply for interaction {InteractionId} not admitted ({Reason})",
                    result.InteractionId,
                    admission.Reason);
                return;
            }

            var enqueued = await writer.EnqueueAsync(admission.Intent, cancellationToken)
                .ConfigureAwait(false);
            if (!enqueued.Accepted)
                logger.LogWarning(
                    "Written reply {ChatResponseId} for interaction {InteractionId} refused ({Reason})",
                    admission.Intent.ChatResponseId,
                    result.InteractionId,
                    enqueued.Reason);
        }
        catch (Exception ex)
        {
            // Absolute last guard. The interaction has already completed successfully at this point and
            // its result is recorded; swallowing here is what keeps that true.
            logger.LogError(ex, "Written chat reply for interaction {InteractionId} failed",
                notification.Result.InteractionId);
        }
    }
}
