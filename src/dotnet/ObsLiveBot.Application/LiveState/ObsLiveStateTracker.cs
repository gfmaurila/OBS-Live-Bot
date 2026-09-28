using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Application.LiveState;

public sealed class ObsLiveStateTracker(
    IPublisher mediator,
    ILiveEventPublisher liveEventPublisher,
    TimeProvider timeProvider,
    ILogger<ObsLiveStateTracker> logger) : IObsLiveStateTracker
{
    public const int EventBufferCapacity = 100;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _processingGate = new(1, 1);
    private readonly Queue<ObsEventEnvelope> _events = new(EventBufferCapacity);
    private ObsLiveState _state = ObsLiveState.Initial;
    private long _sequence;

    public ObsLiveState State { get { lock (_stateLock) return _state; } }

    public IReadOnlyList<ObsEventEnvelope> GetRecentEvents(int limit)
    {
        var safeLimit = Math.Clamp(limit, 1, EventBufferCapacity);
        lock (_stateLock) return _events.TakeLast(safeLimit).Reverse().ToArray();
    }

    public async Task SynchronizeAsync(ObsStateSnapshot snapshot, Guid connectionId, CancellationToken cancellationToken)
    {
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _sequence = 0;
            var now = timeProvider.GetUtcNow();
            lock (_stateLock)
            {
                _state = new ObsLiveState(
                    ObsConnectionState.Connected, true, false, connectionId,
                    snapshot.ObsVersion, snapshot.WebSocketVersion, snapshot.CurrentProgramScene,
                    snapshot.CurrentSceneCollection, snapshot.CurrentProfile,
                    snapshot.IsStreaming ? ObsStreamState.Live : ObsStreamState.Offline,
                    snapshot.IsRecordingPaused ? ObsRecordingState.Paused : snapshot.IsRecording ? ObsRecordingState.Recording : ObsRecordingState.Stopped,
                    snapshot.IsReplayBufferActive is null ? ObsReplayBufferState.NotAvailable : snapshot.IsReplayBufferActive.Value ? ObsReplayBufferState.Running : ObsReplayBufferState.Stopped,
                    snapshot.IsVirtualCameraActive is null ? ObsVirtualCameraState.NotAvailable : snapshot.IsVirtualCameraActive.Value ? ObsVirtualCameraState.Active : ObsVirtualCameraState.Stopped,
                    "ObsStateSynchronized", now, now);
            }

            await EmitAsync("ObsStateSynchronized", now, new Dictionary<string, object?> { ["synchronized"] = true }, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("OBS_STATE_SYNCHRONIZED connectionId={ConnectionId}", connectionId);
        }
        finally { _processingGate.Release(); }
    }

    public async Task MarkStaleAsync(ObsConnectionState connectionState, CancellationToken cancellationToken)
    {
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                _state = _state with { ConnectionState = connectionState, IsSynchronized = false, IsStale = true, LastUpdatedUtc = timeProvider.GetUtcNow() };
            }
        }
        finally { _processingGate.Release(); }
    }

    public async Task ProcessAsync(ObsExternalEvent externalEvent, CancellationToken cancellationToken)
    {
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var changed = Apply(externalEvent, out var internalEventType, out var payload);
            if (!changed || internalEventType is null || payload is null) return;
            await EmitAsync(internalEventType, externalEvent.TimestampUtc, payload, cancellationToken).ConfigureAwait(false);
        }
        finally { _processingGate.Release(); }
    }

    private bool Apply(ObsExternalEvent message, out string? eventType, out IReadOnlyDictionary<string, object?>? payload)
    {
        eventType = null;
        payload = null;
        var data = message.Data;
        var current = State;
        ObsLiveState next = current;
        var values = new Dictionary<string, object?>();

        switch (message.EventType)
        {
            case "CurrentProgramSceneChanged":
                var scene = RequiredString(data, "sceneName");
                if (scene == current.CurrentProgramScene) return false;
                values["previousScene"] = current.CurrentProgramScene; values["currentScene"] = scene;
                next = current with { CurrentProgramScene = scene }; eventType = "ObsSceneChanged"; break;
            case "CurrentSceneCollectionChanged":
                var collection = RequiredString(data, "sceneCollectionName");
                if (collection == current.CurrentSceneCollection) return false;
                values["previous"] = current.CurrentSceneCollection; values["current"] = collection;
                next = current with { CurrentSceneCollection = collection }; eventType = "ObsSceneCollectionChanged"; break;
            case "CurrentProfileChanged":
                var profile = RequiredString(data, "profileName");
                if (profile == current.CurrentProfile) return false;
                values["previous"] = current.CurrentProfile; values["current"] = profile;
                next = current with { CurrentProfile = profile }; eventType = "ObsProfileChanged"; break;
            case "StreamStateChanged":
                var stream = MapStream(RequiredString(data, "outputState"));
                if (stream == current.StreamState) return false;
                values["previous"] = current.StreamState.ToString(); values["current"] = stream.ToString();
                next = current with { StreamState = stream }; eventType = "ObsStreamStateChanged"; break;
            case "RecordStateChanged":
                var recording = MapRecording(RequiredString(data, "outputState"));
                if (recording == current.RecordingState) return false;
                values["previous"] = current.RecordingState.ToString(); values["current"] = recording.ToString();
                next = current with { RecordingState = recording }; eventType = "ObsRecordingStateChanged"; break;
            case "ReplayBufferStateChanged":
                var replay = MapReplay(RequiredString(data, "outputState"));
                if (replay == current.ReplayBufferState) return false;
                values["previous"] = current.ReplayBufferState.ToString(); values["current"] = replay.ToString();
                next = current with { ReplayBufferState = replay }; eventType = "ObsReplayBufferStateChanged"; break;
            case "VirtualcamStateChanged":
                var camera = MapCamera(RequiredString(data, "outputState"));
                if (camera == current.VirtualCameraState) return false;
                values["previous"] = current.VirtualCameraState.ToString(); values["current"] = camera.ToString();
                next = current with { VirtualCameraState = camera }; eventType = "ObsVirtualCameraStateChanged"; break;
            case "ReplayBufferSaved":
                values["saved"] = true; eventType = "ObsReplayBufferSaved"; break;
            case "InputMuteStateChanged":
                values["inputName"] = OptionalString(data, "inputName"); values["muted"] = data.GetProperty("inputMuted").GetBoolean(); eventType = "ObsInputMuteChanged"; break;
            case "InputVolumeChanged":
                values["inputName"] = OptionalString(data, "inputName"); values["volumeDb"] = data.GetProperty("inputVolumeDb").GetDouble(); eventType = "ObsInputVolumeChanged"; break;
            case "InputCreated":
            case "InputRemoved":
            case "InputNameChanged":
            case "InputActiveStateChanged":
            case "InputShowStateChanged":
            case "SceneItemEnableStateChanged":
                values["changeType"] = message.EventType; values["name"] = OptionalString(data, "inputName") ?? OptionalString(data, "sceneName"); eventType = "ObsSourceChanged"; break;
            default: return false;
        }

        lock (_stateLock)
        {
            _state = next with { LastEvent = eventType, LastEventAtUtc = message.TimestampUtc, LastUpdatedUtc = message.TimestampUtc };
        }
        payload = values;
        return true;
    }

    private async Task EmitAsync(string eventType, DateTimeOffset timestamp, IReadOnlyDictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        ObsEventEnvelope envelope;
        lock (_stateLock)
        {
            envelope = new ObsEventEnvelope(Guid.NewGuid(), eventType, timestamp, "OBS", _state.ConnectionId.ToString("N"), _state.ConnectionId, ++_sequence, payload);
            if (_events.Count == EventBufferCapacity) _events.Dequeue();
            _events.Enqueue(envelope);
        }
        await mediator.Publish(new ObsStateChangedNotification(envelope), cancellationToken).ConfigureAwait(false);
        await liveEventPublisher.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("OBS_STATE_EVENT eventType={EventType} sequence={Sequence} connectionId={ConnectionId}", eventType, envelope.Sequence, envelope.ConnectionId);
    }

    private static string RequiredString(JsonElement data, string name) => data.GetProperty(name).GetString() ?? throw new InvalidDataException($"OBS event property '{name}' is missing.");
    private static string? OptionalString(JsonElement data, string name) => data.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static ObsStreamState MapStream(string value) => value switch { "OBS_WEBSOCKET_OUTPUT_STARTING" => ObsStreamState.Starting, "OBS_WEBSOCKET_OUTPUT_STARTED" or "OBS_WEBSOCKET_OUTPUT_RECONNECTED" => ObsStreamState.Live, "OBS_WEBSOCKET_OUTPUT_STOPPING" or "OBS_WEBSOCKET_OUTPUT_RECONNECTING" => ObsStreamState.Stopping, _ => ObsStreamState.Offline };
    private static ObsRecordingState MapRecording(string value) => value switch { "OBS_WEBSOCKET_OUTPUT_STARTING" => ObsRecordingState.Starting, "OBS_WEBSOCKET_OUTPUT_STARTED" or "OBS_WEBSOCKET_OUTPUT_RESUMED" => ObsRecordingState.Recording, "OBS_WEBSOCKET_OUTPUT_PAUSED" => ObsRecordingState.Paused, "OBS_WEBSOCKET_OUTPUT_STOPPING" => ObsRecordingState.Stopping, _ => ObsRecordingState.Stopped };
    private static ObsReplayBufferState MapReplay(string value) => value switch { "OBS_WEBSOCKET_OUTPUT_STARTING" => ObsReplayBufferState.Starting, "OBS_WEBSOCKET_OUTPUT_STARTED" => ObsReplayBufferState.Running, "OBS_WEBSOCKET_OUTPUT_STOPPING" => ObsReplayBufferState.Stopping, _ => ObsReplayBufferState.Stopped };
    private static ObsVirtualCameraState MapCamera(string value) => value switch { "OBS_WEBSOCKET_OUTPUT_STARTING" => ObsVirtualCameraState.Starting, "OBS_WEBSOCKET_OUTPUT_STARTED" => ObsVirtualCameraState.Active, "OBS_WEBSOCKET_OUTPUT_STOPPING" => ObsVirtualCameraState.Stopping, _ => ObsVirtualCameraState.Stopped };
}
