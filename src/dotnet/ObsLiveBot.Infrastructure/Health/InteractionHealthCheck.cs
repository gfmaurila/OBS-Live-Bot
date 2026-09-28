using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Health;

public sealed class InteractionHealthCheck(
    IInteractionProviderRegistry providers,
    IOptions<InteractionOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Interaction subsystem is disabled."));
        }

        var ai = providers.GetAiProvider();
        var tts = providers.GetTtsProvider();
        if (ai is null || !ai.IsAvailable)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Configured AI interaction provider is unavailable."));
        }

        if (tts is null || !tts.IsAvailable)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Configured TTS provider is unavailable."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Interaction providers are ready."));
    }
}
