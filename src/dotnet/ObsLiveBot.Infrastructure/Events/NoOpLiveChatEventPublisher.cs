using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.Events;

public sealed class NoOpLiveChatEventPublisher : ILiveChatEventPublisher
{
    public Task PublishAsync(LiveChatEvent chatEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}
