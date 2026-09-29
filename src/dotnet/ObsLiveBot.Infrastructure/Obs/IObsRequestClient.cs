using System.Text.Json;

namespace ObsLiveBot.Infrastructure.Obs;

public interface IObsRequestClient
{
    bool IsConnected { get; }
    Task<JsonElement> SendRequestAsync(string requestType, object requestData, CancellationToken cancellationToken);
}
