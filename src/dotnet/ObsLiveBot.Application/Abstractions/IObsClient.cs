using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Application.Abstractions;

public interface IObsClient
{
    ObsConnectionState ConnectionState { get; }

    ObsRuntimeState RuntimeState { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<ObsVersionInfo> GetVersionAsync(CancellationToken cancellationToken = default);

    Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken = default);

    Task<bool> GetStreamStatusAsync(CancellationToken cancellationToken = default);

    Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken = default);
}

public sealed record ObsVersionInfo(string ObsVersion, string WebSocketVersion);

public sealed record ObsRecordStatus(bool IsRecording, bool IsPaused);
