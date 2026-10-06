using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Health;

/// <summary>
/// Reports whether the written-reply subsystem could deliver a reply if it were asked to.
/// <para>
/// Disabled is Healthy, not Degraded. The capability ships off on purpose, and a machine that has not
/// opted in is in exactly the state it asked to be in. Degraded is reserved for the genuinely broken case:
/// the capability is switched on but cannot write, which is the situation that would silently drop replies
/// in front of a live audience.
/// </para>
/// <para>
/// The switch is read from the settings store rather than from configuration, so a deliberate runtime
/// override is judged by the value actually in force. An override that is never cleared does not survive a
/// restart, so this check can never report "enabled" after the process comes back.
/// </para>
/// </summary>
public sealed class ChatResponseHealthCheck(
    IOptions<ChatResponseOptions> options,
    IChatResponseSettingsStore settings,
    IChatResponseSenderRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        var effective = settings.Get();

        if (!effective.Enabled)
            return Task.FromResult(effective.Overridden
                ? HealthCheckResult.Healthy(
                    $"Written chat responses are disabled by runtime override (configured value is " +
                    $"{(configured.Enabled ? "enabled" : "disabled")}).")
                : HealthCheckResult.Healthy(
                    "Written chat responses are disabled (ChatResponses:Enabled is false)."));

        var sender = registry.GetSelected();
        if (sender is null)
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Written chat responses are enabled but sender '{configured.Sender}' is not registered."));

        if (sender.IsDevelopment)
            return Task.FromResult(HealthCheckResult.Degraded(
                "Written chat responses are enabled with the Development sender; no reply reaches a platform."));

        if (!sender.IsAvailable)
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Written chat responses are enabled but sender '{sender.Name}' is unavailable."));

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Written chat responses are ready through sender '{sender.Name}'."));
    }
}