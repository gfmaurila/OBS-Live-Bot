using System.Text.Json;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Narration;
using ObsLiveBot.Infrastructure.Obs;

namespace ObsLiveBot.Infrastructure.Interactions;

public sealed class ObsAudioPlaybackService(
    IObsRequestClient obs,
    IOptions<NarrationOptions> narrationOptions,
    IOptions<InteractionOptions> interactionOptions,
    TimeProvider timeProvider) : IAudioPlaybackService
{
    private const string MediaInputKind = "ffmpeg_source";
    private readonly NarrationOptions _options = narrationOptions.Value;
    private readonly PiperTtsOptions _tts = interactionOptions.Value.Tts;

    public async Task<NarrationPlaybackSnapshot> GetStateAsync(
        string sourceName,
        CancellationToken cancellationToken)
    {
        if (!obs.IsConnected)
            return Snapshot(false, false, sourceName, null, null, "MonitorOff", [],
                ObsMediaPlaybackState.Unknown, "Unavailable", "OBS_UNAVAILABLE");

        var inputs = await RequestAsync("GetInputList", new { }, cancellationToken).ConfigureAwait(false);
        var input = inputs.GetProperty("inputs").EnumerateArray()
            .FirstOrDefault(candidate => GetString(candidate, "inputName") == sourceName);
        if (input.ValueKind == JsonValueKind.Undefined)
            return Snapshot(true, false, sourceName, null, null, _options.MonitoringMode, [],
                ObsMediaPlaybackState.Unknown, "Degraded", "NARRATION_SOURCE_MISSING");

        var kind = GetString(input, "inputKind");
        if (!string.Equals(kind, MediaInputKind, StringComparison.Ordinal))
            return Snapshot(true, true, sourceName, null, null, _options.MonitoringMode, [],
                ObsMediaPlaybackState.Unknown, "Degraded", "NARRATION_SOURCE_KIND_INVALID");

        var volumeTask = RequestAsync("GetInputVolume", new { inputName = sourceName }, cancellationToken);
        var muteTask = RequestAsync("GetInputMute", new { inputName = sourceName }, cancellationToken);
        var monitorTask = RequestAsync("GetInputAudioMonitorType", new { inputName = sourceName }, cancellationToken);
        var tracksTask = RequestAsync("GetInputAudioTracks", new { inputName = sourceName }, cancellationToken);
        var mediaTask = RequestAsync("GetMediaInputStatus", new { inputName = sourceName }, cancellationToken);
        await Task.WhenAll(volumeTask, muteTask, monitorTask, tracksTask, mediaTask).ConfigureAwait(false);
        var volume = volumeTask.Result.GetProperty("inputVolumeMul").GetDouble() * 100d;
        var muted = muteTask.Result.GetProperty("inputMuted").GetBoolean();
        var monitor = MapMonitor(monitorTask.Result.GetProperty("monitorType").GetString());
        var tracks = ReadTracks(tracksTask.Result);
        var media = MapMediaState(mediaTask.Result.TryGetProperty("mediaState", out var mediaState)
            ? mediaState.GetString()
            : null);
        var routingValid = monitor == _options.MonitoringMode && tracks.SequenceEqual([1]);
        return Snapshot(true, true, sourceName, volume, muted, monitor, tracks, media,
            routingValid ? "Ready" : "Degraded",
            routingValid ? null : "NARRATION_ROUTING_MISMATCH");
    }

    public async Task EnsureSourceAsync(
        string sourceName,
        string runtimeDirectory,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        var sceneList = await RequestAsync("GetSceneList", new { }, cancellationToken).ConfigureAwait(false);
        var scenes = sceneList.GetProperty("scenes").EnumerateArray()
            .Select(scene => GetString(scene, "sceneName"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        if (scenes.Length == 0) throw new InvalidOperationException("OBS_SCENE_LIST_EMPTY");

        var inputList = await RequestAsync("GetInputList", new { }, cancellationToken).ConfigureAwait(false);
        var existingInput = inputList.GetProperty("inputs").EnumerateArray()
            .FirstOrDefault(candidate => GetString(candidate, "inputName") == sourceName);
        if (existingInput.ValueKind == JsonValueKind.Undefined)
        {
            var inputKinds = await RequestAsync("GetInputKindList", new { unversioned = true }, cancellationToken)
                .ConfigureAwait(false);
            if (!inputKinds.GetProperty("inputKinds").EnumerateArray()
                    .Any(kind => string.Equals(kind.GetString(), MediaInputKind, StringComparison.Ordinal)))
                throw new InvalidOperationException("OBS_MEDIA_SOURCE_UNAVAILABLE");

            var settings = new
            {
                is_local_file = true,
                local_file = string.Empty,
                looping = false,
                close_when_inactive = false,
                restart_on_activate = false,
                clear_on_media_end = true
            };
            await RequestAsync("CreateInput", new
            {
                sceneName = scenes[0],
                inputName = sourceName,
                inputKind = MediaInputKind,
                inputSettings = settings,
                sceneItemEnabled = true
            }, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(GetString(existingInput, "inputKind"), MediaInputKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("NARRATION_SOURCE_NAME_COLLISION");
        }

        foreach (var sceneName in scenes)
        {
            var sceneItems = await RequestAsync("GetSceneItemList", new { sceneName }, cancellationToken)
                .ConfigureAwait(false);
            var found = sceneItems.GetProperty("sceneItems").EnumerateArray()
                .Any(item => GetString(item, "sourceName") == sourceName);
            if (!found)
            {
                await RequestAsync("CreateSceneItem", new { sceneName, sourceName }, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await SetRoutingAsync(sourceName, cancellationToken).ConfigureAwait(false);
        var snapshot = await GetStateAsync(sourceName, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Available)
            throw new InvalidOperationException(snapshot.ErrorCode ?? "NARRATION_SOURCE_NOT_READY");
    }

    public async Task ConfigureAsync(
        string sourceName,
        double volumePercent,
        bool muted,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        var inputs = await RequestAsync("GetInputList", new { }, cancellationToken).ConfigureAwait(false);
        var input = inputs.GetProperty("inputs").EnumerateArray()
            .FirstOrDefault(candidate => GetString(candidate, "inputName") == sourceName);
        if (input.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("NARRATION_SOURCE_MISSING");
        if (!string.Equals(GetString(input, "inputKind"), MediaInputKind, StringComparison.Ordinal))
            throw new InvalidOperationException("NARRATION_SOURCE_KIND_INVALID");

        await RequestAsync("SetInputVolume", new { inputName = sourceName, inputVolumeMul = volumePercent / 100d }, cancellationToken)
            .ConfigureAwait(false);
        await RequestAsync("SetInputMute", new { inputName = sourceName, inputMuted = muted }, cancellationToken)
            .ConfigureAwait(false);
        await SetRoutingAsync(sourceName, cancellationToken).ConfigureAwait(false);
    }

    public async Task PlayAsync(
        string sourceName,
        NarrationAudioArtifact artifact,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        var containerPath = Path.GetFullPath(artifact.Path);
        var hostPath = ResolveHostArtifactPath(artifact.Path);
        if (!File.Exists(containerPath) || new FileInfo(containerPath).Length <= 0)
            throw new InvalidOperationException("NARRATION_AUDIO_NOT_ACCESSIBLE_TO_OBS");

        await RequestAsync("SetInputSettings", new
        {
            inputName = sourceName,
            inputSettings = new { local_file = hostPath, is_local_file = true, looping = false },
            overlay = true
        }, cancellationToken).ConfigureAwait(false);
        await RequestAsync("TriggerMediaInputAction", new
        {
            inputName = sourceName,
            mediaAction = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART"
        }, cancellationToken).ConfigureAwait(false);

        var deadline = timeProvider.GetUtcNow().AddSeconds(_options.StartTimeoutSeconds);
        while (timeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await GetMediaStateAsync(sourceName, cancellationToken).ConfigureAwait(false);
            if (state == ObsMediaPlaybackState.Playing) return;
            if (state == ObsMediaPlaybackState.Error)
                throw new InvalidOperationException("NARRATION_PLAYBACK_DID_NOT_START");
            await Task.Delay(TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds), timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException("NARRATION_PLAYBACK_START_TIMEOUT");
    }

    public async Task<ObsMediaPlaybackState> WaitForCompletionAsync(
        string sourceName,
        TimeSpan expectedDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var effectiveTimeout = TimeSpan.FromSeconds(
            Math.Min(timeout.TotalSeconds, expectedDuration.TotalSeconds + 5));
        var deadline = timeProvider.GetUtcNow().Add(effectiveTimeout);
        var sawPlaying = false;
        while (timeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await GetMediaStateAsync(sourceName, cancellationToken).ConfigureAwait(false);
            if (state == ObsMediaPlaybackState.Playing) sawPlaying = true;
            if (state == ObsMediaPlaybackState.Ended && sawPlaying) return state;
            if (state == ObsMediaPlaybackState.Error)
                throw new InvalidOperationException("NARRATION_MEDIA_ERROR");
            if (state == ObsMediaPlaybackState.Stopped)
                throw new InvalidOperationException("NARRATION_MEDIA_STOPPED_UNEXPECTEDLY");
            await Task.Delay(TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds), timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException("NARRATION_PLAYBACK_TIMEOUT");
    }

    public async Task StopAndClearAsync(string sourceName, CancellationToken cancellationToken)
    {
        if (!obs.IsConnected) return;
        var inputs = await RequestAsync("GetInputList", new { }, cancellationToken).ConfigureAwait(false);
        var input = inputs.GetProperty("inputs").EnumerateArray()
            .FirstOrDefault(candidate => GetString(candidate, "inputName") == sourceName);
        if (input.ValueKind == JsonValueKind.Undefined) return;
        try
        {
            await RequestAsync("TriggerMediaInputAction", new
            {
                inputName = sourceName,
                mediaAction = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_STOP"
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (ObsRequestException exception) when (exception.StatusCode is 600 or 601)
        {
            // A stopped or not-yet-loaded source has no active media to stop.
        }
        await RequestAsync("SetInputSettings", new
        {
            inputName = sourceName,
            inputSettings = new { local_file = string.Empty },
            overlay = true
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SetRoutingAsync(string sourceName, CancellationToken cancellationToken)
    {
        await RequestAsync("SetInputAudioTracks", new
        {
            inputName = sourceName,
            inputAudioTracks = new Dictionary<string, bool>
            {
                ["1"] = true,
                ["2"] = false,
                ["3"] = false,
                ["4"] = false,
                ["5"] = false,
                ["6"] = false
            }
        }, cancellationToken).ConfigureAwait(false);
        await RequestAsync("SetInputAudioMonitorType", new
        {
            inputName = sourceName,
            monitorType = "OBS_MONITORING_TYPE_NONE"
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ObsMediaPlaybackState> GetMediaStateAsync(
        string sourceName,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        var response = await RequestAsync("GetMediaInputStatus", new { inputName = sourceName }, cancellationToken)
            .ConfigureAwait(false);
        return MapMediaState(response.TryGetProperty("mediaState", out var state) ? state.GetString() : null);
    }

    private async Task<JsonElement> RequestAsync(
        string requestType,
        object requestData,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        return await obs.SendRequestAsync(requestType, requestData, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveHostArtifactPath(string containerPath)
    {
        var containerRoot = Path.GetFullPath(_options.AllowedRuntimeDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullContainerPath = Path.GetFullPath(containerPath);
        if (!fullContainerPath.StartsWith(containerRoot, PathComparison))
            throw new InvalidOperationException("NARRATION_PATH_OUTSIDE_RUNTIME");
        var fileName = Path.GetFileName(fullContainerPath);
        if (fileName != $"{Path.GetFileNameWithoutExtension(fileName)}.wav" ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _))
            throw new InvalidOperationException("NARRATION_ARTIFACT_UNREGISTERED");
        // This service runs in the Linux API container while OBS runs on Windows.
        // Do not normalize the host path with the container's Path implementation.
        var hostRoot = _options.HostRuntimeDirectory.TrimEnd('\\', '/') + "/";
        if (!IsWindowsAbsolutePath(hostRoot))
            throw new InvalidOperationException("NARRATION_HOST_RUNTIME_PATH_INVALID");
        return hostRoot + fileName;
    }

    private void EnsureConnected()
    {
        if (!obs.IsConnected) throw new IOException("OBS_UNAVAILABLE");
    }

    private static NarrationPlaybackSnapshot Snapshot(
        bool connected,
        bool exists,
        string? source,
        double? volume,
        bool? muted,
        string monitoringMode,
        IReadOnlyList<int> tracks,
        ObsMediaPlaybackState mediaState,
        string status,
        string? errorCode) =>
        new(connected && exists && errorCode is null, exists, source, volume, muted,
            monitoringMode, tracks, mediaState, status, errorCode);

    private static IReadOnlyList<int> ReadTracks(JsonElement response)
    {
        if (!response.TryGetProperty("inputAudioTracks", out var tracks)) return [];
        return tracks.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.True &&
                               int.TryParse(property.Name, out _))
            .Select(property => int.Parse(property.Name))
            .Order()
            .ToArray();
    }

    private static string MapMonitor(string? monitorType) => monitorType switch
    {
        "OBS_MONITORING_TYPE_NONE" => "MonitorOff",
        "OBS_MONITORING_TYPE_MONITOR_ONLY" => "MonitorOnly",
        "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT" => "MonitorAndOutput",
        _ => "Unknown"
    };

    private static ObsMediaPlaybackState MapMediaState(string? state) => state switch
    {
        "OBS_MEDIA_STATE_OPENING" => ObsMediaPlaybackState.Opening,
        "OBS_MEDIA_STATE_BUFFERING" => ObsMediaPlaybackState.Buffering,
        "OBS_MEDIA_STATE_PLAYING" => ObsMediaPlaybackState.Playing,
        "OBS_MEDIA_STATE_PAUSED" => ObsMediaPlaybackState.Paused,
        "OBS_MEDIA_STATE_ENDED" => ObsMediaPlaybackState.Ended,
        "OBS_MEDIA_STATE_STOPPED" => ObsMediaPlaybackState.Stopped,
        "OBS_MEDIA_STATE_ERROR" => ObsMediaPlaybackState.Error,
        _ => ObsMediaPlaybackState.Unknown
    };

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static bool IsWindowsAbsolutePath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/';

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
