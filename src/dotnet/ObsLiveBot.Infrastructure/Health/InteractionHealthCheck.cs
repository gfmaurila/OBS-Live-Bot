using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Health;

public sealed class InteractionHealthCheck(
    IInteractionProviderRegistry providers,
    IOptions<InteractionOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return HealthCheckResult.Degraded("Interaction subsystem is disabled.");
        }

        var ai = providers.GetAiProvider();
        var tts = providers.GetTtsProvider();
        if (ai is null || !await ai.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            return HealthCheckResult.Degraded("Configured AI interaction provider is unavailable.");
        }

        if (tts is null || !await tts.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            return HealthCheckResult.Degraded("Configured TTS provider is unavailable.");
        }

        return HealthCheckResult.Healthy("Interaction providers are ready.");
    }
}
