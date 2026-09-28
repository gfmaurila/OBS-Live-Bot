using System.Text.Json;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Features.Obs.GetStatus;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.UnitTests.Features;

public sealed class GetObsStatusTests
{
    [Fact]
    public async Task PublicResponse_DoesNotExposeSecrets()
    {
        var handler = new GetObsStatusQueryHandler(new FakeObsClient());

        var response = await handler.HandleAsync(
            new GetObsStatusQuery(),
            CancellationToken.None);
        var json = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Connected", response.Connection);
    }

    private sealed class FakeObsClient : IObsClient
    {
        public ObsConnectionState ConnectionState => ObsConnectionState.Connected;

        public ObsRuntimeState RuntimeState { get; } = new(
            ObsConnectionState.Connected,
            "32.1.2",
            "5.6.3",
            "Program",
            false,
            false,
            false,
            DateTimeOffset.UtcNow);

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ObsVersionInfo> GetVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ObsVersionInfo("32.1.2", "5.6.3"));

        public Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("Program");

        public Task<bool> GetStreamStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ObsRecordStatus(false, false));
    }
}
