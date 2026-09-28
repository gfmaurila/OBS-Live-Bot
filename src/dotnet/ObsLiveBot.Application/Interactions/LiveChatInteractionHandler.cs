using MediatR;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;

namespace ObsLiveBot.Application.Interactions;

public sealed class LiveChatInteractionHandler(
    IInteractionOrchestrator orchestrator,
    ILogger<LiveChatInteractionHandler> logger)
    : INotificationHandler<LiveChatEventReceivedNotification>
{
    public async Task Handle(LiveChatEventReceivedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.ProcessAsync(notification.ChatEvent, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "INTERACTION_CHAT_EVENT_FAILED chatEventId={ChatEventId} errorType={ErrorType}",
                notification.ChatEvent.EventId,
                exception.GetType().Name);
        }
    }
}
