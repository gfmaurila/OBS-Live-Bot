using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Infrastructure.Events;

public sealed class NoOpNarrationEventPublisher : INarrationEventPublisher
{
    public Task PublishAsync(NarrationEvent narrationEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
