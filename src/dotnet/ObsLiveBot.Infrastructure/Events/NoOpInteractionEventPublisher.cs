using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Infrastructure.Events;

public sealed class NoOpInteractionEventPublisher : IInteractionEventPublisher
{
    public Task PublishAsync(InteractionResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
