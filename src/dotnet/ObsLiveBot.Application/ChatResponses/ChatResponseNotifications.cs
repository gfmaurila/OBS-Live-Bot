using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.ChatResponses;

/// <summary>
/// Emitted once a written reply has been admitted and handed to the bounded queue. Observers must not
/// assume the write has happened yet.
/// </summary>
public sealed record ChatResponseQueuedNotification(
    Guid ChatResponseId,
    Guid? InteractionId,
    string? SourceMessageId,
    LiveChatProviderType Provider,
    string ChannelId,
    string CorrelationId,
    int CharacterCount) : INotification;

/// <summary>
/// Emitted when a written reply reaches a terminal state, after its own result has been recorded.
/// Nothing downstream of this may influence a write that already happened.
/// </summary>
public sealed record ChatResponseCompletedNotification(
    Guid ChatResponseId,
    Guid? InteractionId,
    string? SourceMessageId,
    LiveChatProviderType Provider,
    string ChannelId,
    ChatResponseStatus Status,
    string? Reason,
    string CorrelationId,
    string SenderName,
    bool Simulated,
    double? DurationMilliseconds,
    int Attempt = 1) : INotification;

/// <summary>
/// Default publisher. Kept as an explicit no-op rather than removed from the interface, so a host that
/// wants written-reply facts delivered outside the process can register a real one later without
/// touching the writer.
/// </summary>
public sealed class NoOpChatResponseEventPublisher : IChatResponseEventPublisher
{
    public Task PublishAsync(ChatResponseEvent chatResponseEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
