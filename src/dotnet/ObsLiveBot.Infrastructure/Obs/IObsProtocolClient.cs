using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Obs;

public interface IObsProtocolClient
{
    bool IsConnected { get; }

    Task Completion { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<ObsVersionInfo> GetVersionAsync(CancellationToken cancellationToken);

    Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken);

    Task<bool> GetStreamStatusAsync(CancellationToken cancellationToken);

    Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken);
}

public sealed class ObsAuthenticationException(string message) : Exception(message);
