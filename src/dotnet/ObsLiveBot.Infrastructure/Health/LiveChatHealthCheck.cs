using Microsoft.Extensions.Diagnostics.HealthChecks;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.Health;

public sealed class LiveChatHealthCheck(ILiveChatProviderRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var providers = registry.GetProviders();
        var degraded = providers.Where(item => item.Enabled && item.State is
            LiveChatProviderState.Reconnecting or LiveChatProviderState.RateLimited or
            LiveChatProviderState.AuthenticationFailed or LiveChatProviderState.Faulted).ToArray();
        return Task.FromResult(degraded.Length == 0
            ? HealthCheckResult.Healthy("Live chat providers are ready or not configured.")
            : HealthCheckResult.Degraded($"{degraded.Length} live chat provider(s) require attention."));
    }
}
