using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Infrastructure.Events;

public sealed class NoOpLiveEventPublisher : ILiveEventPublisher
{
    public Task PublishAsync(ObsEventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
}
