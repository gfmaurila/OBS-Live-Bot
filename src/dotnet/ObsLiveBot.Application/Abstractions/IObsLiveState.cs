using System.Text.Json;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Application.Abstractions;

public interface IObsLiveStateReader
{
    ObsLiveState State { get; }
    IReadOnlyList<ObsEventEnvelope> GetRecentEvents(int limit);
}

public interface IObsLiveStateTracker : IObsLiveStateReader
{
    Task SynchronizeAsync(ObsStateSnapshot snapshot, Guid connectionId, CancellationToken cancellationToken);
    Task MarkStaleAsync(ObsConnectionState connectionState, CancellationToken cancellationToken);
    Task ProcessAsync(ObsExternalEvent externalEvent, CancellationToken cancellationToken);
}

public sealed record ObsExternalEvent(string EventType, JsonElement Data, DateTimeOffset TimestampUtc);

public interface ILiveEventPublisher
{
    Task PublishAsync(ObsEventEnvelope envelope, CancellationToken cancellationToken);
}
