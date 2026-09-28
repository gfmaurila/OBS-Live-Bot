using MediatR;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;

namespace ObsLiveBot.Application.LiveChat;

public sealed class LiveChatEventReceivedHandler(
    ILiveChatBuffer buffer,
    ILiveChatEventPublisher publisher,
    ILogger<LiveChatEventReceivedHandler> logger)
    : INotificationHandler<LiveChatEventReceivedNotification>
{
    public async Task Handle(LiveChatEventReceivedNotification notification, CancellationToken cancellationToken)
    {
        buffer.Add(notification.ChatEvent);
        try
        {
            await publisher.PublishAsync(notification.ChatEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "LIVE_CHAT_PUBLISH_FAILED provider={Provider} eventType={EventType} errorType={ErrorType}",
                notification.ChatEvent.Provider,
                notification.ChatEvent.EventType,
                exception.GetType().Name);
        }
    }
}
