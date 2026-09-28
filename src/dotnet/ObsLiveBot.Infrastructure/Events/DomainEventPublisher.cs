using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Infrastructure.Events;

public sealed class DomainEventPublisher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventPublisher> logger) : IDomainEventPublisher
{
    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        logger.LogDebug("OBS_DOMAIN_EVENT eventType={EventType}", domainEvent.GetType().Name);

        var handlerInterface = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handlers = serviceProvider.GetServices(handlerInterface);
        var handleMethod = handlerInterface.GetMethod("HandleAsync")
            ?? throw new InvalidOperationException("Domain event handler contract is invalid.");

        foreach (var handler in handlers)
        {
            var task = (Task)handleMethod.Invoke(handler, [domainEvent, cancellationToken])!;
            await task.ConfigureAwait(false);
        }
    }
}
