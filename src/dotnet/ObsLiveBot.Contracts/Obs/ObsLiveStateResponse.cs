namespace ObsLiveBot.Contracts.Obs;

public sealed record ObsLiveStateResponse(
    string Connection,
    bool Synchronized,
    bool Stale,
    string? ObsVersion,
    string? WebSocketVersion,
    string? Scene,
    string? SceneCollection,
    string? Profile,
    ObsOutputStateResponse Stream,
    ObsRecordingStateResponse Recording,
    ObsOutputStateResponse ReplayBuffer,
    ObsVirtualCameraResponse VirtualCamera,
    string? LastEvent,
    DateTimeOffset? LastEventAtUtc,
    DateTimeOffset LastUpdatedUtc);

public sealed record ObsOutputStateResponse(string State, bool Active);
public sealed record ObsRecordingStateResponse(string State, bool Active, bool Paused);
public sealed record ObsVirtualCameraResponse(string State, bool Active);

public sealed record ObsEventResponse(
    Guid EventId,
    string EventType,
    DateTimeOffset TimestampUtc,
    string Source,
    string CorrelationId,
    Guid ConnectionId,
    long Sequence,
    IReadOnlyDictionary<string, object?> Payload);
