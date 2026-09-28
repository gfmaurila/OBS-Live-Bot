namespace ObsLiveBot.Domain.Obs;

public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}

public sealed record ObsConnected(DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record ObsDisconnected(DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record ObsReconnecting(DateTimeOffset OccurredAtUtc, int Attempt, TimeSpan Delay) : IDomainEvent;

public sealed record ObsConnectionFailed(DateTimeOffset OccurredAtUtc, string Reason) : IDomainEvent;
