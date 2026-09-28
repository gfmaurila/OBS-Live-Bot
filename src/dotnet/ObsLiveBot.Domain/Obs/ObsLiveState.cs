namespace ObsLiveBot.Domain.Obs;

public enum ObsStreamState { Offline, Starting, Live, Stopping }
public enum ObsRecordingState { Stopped, Starting, Recording, Paused, Stopping }
public enum ObsReplayBufferState { NotAvailable, Stopped, Starting, Running, Stopping }
public enum ObsVirtualCameraState { NotAvailable, Stopped, Starting, Active, Stopping }

public sealed record ObsLiveState(
    ObsConnectionState ConnectionState,
    bool IsSynchronized,
    bool IsStale,
    Guid ConnectionId,
    string? ObsVersion,
    string? WebSocketVersion,
    string? CurrentProgramScene,
    string? CurrentSceneCollection,
    string? CurrentProfile,
    ObsStreamState StreamState,
    ObsRecordingState RecordingState,
    ObsReplayBufferState ReplayBufferState,
    ObsVirtualCameraState VirtualCameraState,
    string? LastEvent,
    DateTimeOffset? LastEventAtUtc,
    DateTimeOffset LastUpdatedUtc)
{
    public bool IsStreaming => StreamState is ObsStreamState.Starting or ObsStreamState.Live or ObsStreamState.Stopping;
    public bool IsRecording => RecordingState is ObsRecordingState.Starting or ObsRecordingState.Recording or ObsRecordingState.Paused or ObsRecordingState.Stopping;
    public bool IsRecordingPaused => RecordingState == ObsRecordingState.Paused;

    public static ObsLiveState Initial { get; } = new(
        ObsConnectionState.Disconnected, false, true, Guid.Empty, null, null, null, null, null,
        ObsStreamState.Offline, ObsRecordingState.Stopped, ObsReplayBufferState.NotAvailable,
        ObsVirtualCameraState.NotAvailable, null, null, DateTimeOffset.UtcNow);
}

public sealed record ObsStateSnapshot(
    string ObsVersion,
    string WebSocketVersion,
    string CurrentProgramScene,
    string CurrentSceneCollection,
    string CurrentProfile,
    bool IsStreaming,
    bool IsRecording,
    bool IsRecordingPaused,
    bool? IsReplayBufferActive,
    bool? IsVirtualCameraActive);

public sealed record ObsEventEnvelope(
    Guid EventId,
    string EventType,
    DateTimeOffset TimestampUtc,
    string Source,
    string CorrelationId,
    Guid ConnectionId,
    long Sequence,
    IReadOnlyDictionary<string, object?> Payload);
