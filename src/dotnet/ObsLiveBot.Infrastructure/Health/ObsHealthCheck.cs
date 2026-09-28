using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Obs;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Infrastructure.Health;

public sealed class ObsHealthCheck(
    IObsClient obsClient,
    IOptions<ObsWebSocketOptions> options,
    IValidateOptions<ObsWebSocketOptions> validator) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(Options.DefaultName, options.Value);
        if (validation.Failed)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("OBS WebSocket configuration is invalid."));
        }

        var result = obsClient.ConnectionState switch
        {
            ObsConnectionState.Connected => HealthCheckResult.Healthy("OBS WebSocket is connected."),
            ObsConnectionState.AuthenticationFailed => HealthCheckResult.Unhealthy("OBS WebSocket authentication failed."),
            ObsConnectionState.Faulted => HealthCheckResult.Unhealthy("OBS integration is faulted."),
            _ => HealthCheckResult.Degraded("OBS is offline or reconnecting.")
        };

        return Task.FromResult(result);
    }
}
