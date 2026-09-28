using System.Threading.Channels;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Obs;

public interface IObsProtocolClient
{
    bool IsConnected { get; }

    Task Completion { get; }

    ChannelReader<ObsExternalEvent> Events { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<ObsVersionInfo> GetVersionAsync(CancellationToken cancellationToken);

    Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken);

    Task<bool> GetStreamStatusAsync(CancellationToken cancellationToken);

    Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken);

    Task<string> GetCurrentSceneCollectionAsync(CancellationToken cancellationToken);

    Task<string> GetCurrentProfileAsync(CancellationToken cancellationToken);

    Task<bool?> GetReplayBufferStatusAsync(CancellationToken cancellationToken);

    Task<bool?> GetVirtualCameraStatusAsync(CancellationToken cancellationToken);
}

public sealed class ObsAuthenticationException(string message) : Exception(message);

public sealed class ObsRequestException(string requestType, int statusCode)
    : Exception($"OBS request '{requestType}' failed with status code {statusCode}.")
{
    public string RequestType { get; } = requestType;
    public int StatusCode { get; } = statusCode;
}
