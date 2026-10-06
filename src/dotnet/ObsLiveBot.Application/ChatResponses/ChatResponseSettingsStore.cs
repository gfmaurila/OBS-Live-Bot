using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.ChatResponses;

/// <summary>
/// The written-reply master switch as the running process sees it: shipped configuration, plus at most
/// one deliberate runtime override.
/// <para>
/// The override is intentionally volatile. Nothing here is written to disk, so no restart, crash or
/// redeploy can carry an enabled switch forward; the process always comes back at the configured value,
/// which ships <c>false</c>. A capability that posts into a public chat must not be able to survive a
/// reboot by itself.
/// </para>
/// <para>
/// Every read returns a fresh immutable snapshot, so a caller that reads the switch twice - once to
/// decide and once to report - cannot observe it changing underneath itself.
/// </para>
/// </summary>
public sealed class ChatResponseSettingsStore(
    IOptions<ChatResponseOptions> options,
    ILogger<ChatResponseSettingsStore> logger) : IChatResponseSettingsStore
{
    private readonly object _gate = new();
    private bool? _override;

    /// <summary>The value from configuration. Never changes for the lifetime of the process.</summary>
    public bool ConfiguredEnabled => options.Value.Enabled;

    public bool? OverriddenEnabled
    {
        get { lock (_gate) return _override; }
    }

    public ChatResponseSettingsSnapshot Get()
    {
        lock (_gate)
        {
            return _override is { } value
                ? new ChatResponseSettingsSnapshot(value, ConfiguredEnabled, true, "RuntimeOverride")
                : new ChatResponseSettingsSnapshot(ConfiguredEnabled, ConfiguredEnabled, false, "Configuration");
        }
    }

    /// <summary>The switch actually in force. The single value every gate reads.</summary>
    public bool IsEnabled => Get().Enabled;

    public ChatResponseSettingsSnapshot Update(ChatResponseSettingsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_gate)
        {
            if (update.Reset)
            {
                var had = _override.HasValue;
                _override = null;
                if (had)
                    logger.LogInformation(
                        "Written chat responses returned to the configured value (enabled={Enabled})",
                        ConfiguredEnabled);
                return Get();
            }

            if (update.Enabled is { } enabled)
            {
                _override = enabled;
                logger.LogWarning(
                    "Written chat responses were {State} at runtime by an operator request. " +
                    "The override is in memory only and will not survive a restart, where the configured " +
                    "value (enabled={Configured}) applies again.",
                    enabled ? "ENABLED" : "disabled",
                    ConfiguredEnabled);
            }

            return Get();
        }
    }
}