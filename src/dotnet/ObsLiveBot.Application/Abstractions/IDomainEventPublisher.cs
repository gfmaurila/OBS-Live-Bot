using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Application.Abstractions;

public interface IDomainEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
