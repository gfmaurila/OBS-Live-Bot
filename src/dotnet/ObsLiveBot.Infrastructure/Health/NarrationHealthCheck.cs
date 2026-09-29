using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Health;

public sealed class NarrationHealthCheck(
    INarrationService narration,
    IOptions<NarrationOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
            return HealthCheckResult.Healthy("Narration is disabled by configuration.",
                new Dictionary<string, object> { ["status"] = "Disabled" });

        var playback = await narration.RefreshPlaybackStateAsync(cancellationToken).ConfigureAwait(false);
        if (playback.Available)
            return HealthCheckResult.Healthy("OBS narration source and routing are ready.",
                new Dictionary<string, object>
                {
                    ["status"] = narration.GetState().Status,
                    ["source"] = options.Value.SourceName,
                    ["monitoring"] = playback.MonitoringMode,
                    ["tracks"] = string.Join(',', playback.Tracks)
                });

        return HealthCheckResult.Degraded(
            "Narration playback is unavailable; other subsystems remain independent.",
            data: new Dictionary<string, object>
            {
                ["status"] = playback.Status,
                ["errorCode"] = playback.ErrorCode ?? "NARRATION_UNAVAILABLE"
            });
    }
}
