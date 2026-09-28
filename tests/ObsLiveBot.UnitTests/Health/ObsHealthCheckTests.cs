using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Obs;
using ObsLiveBot.Infrastructure.Configuration;
using ObsLiveBot.Infrastructure.Health;

namespace ObsLiveBot.UnitTests.Health;

public sealed class ObsHealthCheckTests
{
    [Theory]
    [InlineData(ObsConnectionState.Connected, HealthStatus.Healthy)]
    [InlineData(ObsConnectionState.Disconnected, HealthStatus.Degraded)]
    [InlineData(ObsConnectionState.Reconnecting, HealthStatus.Degraded)]
    [InlineData(ObsConnectionState.AuthenticationFailed, HealthStatus.Unhealthy)]
    public async Task MapsConnectionState(ObsConnectionState state, HealthStatus expected)
    {
        var options = Options.Create(new ObsWebSocketOptions { Host = "localhost", Port = 4455 });
        var healthCheck = new ObsHealthCheck(
            new StubObsClient(state),
            options,
            new ObsWebSocketOptionsValidator());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task InvalidConfiguration_IsUnhealthy()
    {
        var options = Options.Create(new ObsWebSocketOptions { Host = string.Empty, Port = 0 });
        var healthCheck = new ObsHealthCheck(
            new StubObsClient(ObsConnectionState.Disconnected),
            options,
            new ObsWebSocketOptionsValidator());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private sealed class StubObsClient(ObsConnectionState state) : IObsClient
    {
        public ObsConnectionState ConnectionState => state;

        public ObsRuntimeState RuntimeState => ObsRuntimeState.Initial with { ConnectionState = state };

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ObsVersionInfo> GetVersionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> GetStreamStatusAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
