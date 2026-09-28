using MediatR;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Infrastructure.Events;

public sealed class DomainEventPublisher(
    IPublisher publisher,
    ILogger<DomainEventPublisher> logger) : IDomainEventPublisher
{
    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        logger.LogDebug("OBS_DOMAIN_EVENT eventType={EventType}", domainEvent.GetType().Name);

        await publisher.Publish(
            new DomainEventNotification(domainEvent),
            cancellationToken).ConfigureAwait(false);
    }
}
