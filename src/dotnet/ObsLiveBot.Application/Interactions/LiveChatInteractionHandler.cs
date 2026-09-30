using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;

namespace ObsLiveBot.Application.Interactions;

public sealed class LiveChatInteractionHandler(
    IInteractionWorkQueue queue,
    IOptions<InteractionOptions> options,
    ILogger<LiveChatInteractionHandler> logger)
    : INotificationHandler<LiveChatEventReceivedNotification>
{
    public Task Handle(LiveChatEventReceivedNotification notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = new Dictionary<string, string?>(notification.ChatEvent.Metadata, StringComparer.Ordinal)
        {
            ["interaction.autoPlayAtReceipt"] = options.Value.AutoPlayInteractions ? "true" : "false"
        };
        var chatEvent = notification.ChatEvent with { Metadata = metadata };
        if (!queue.TryEnqueue(chatEvent))
        {
            logger.LogWarning(
                "INTERACTION_REJECTED chatEventId={ChatEventId} reason={Reason}",
                chatEvent.EventId,
                "InteractionQueueFull");
        }
        return Task.CompletedTask;
    }
}
