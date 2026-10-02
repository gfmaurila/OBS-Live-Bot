using Microsoft.Extensions.Options;
using ObsLiveBot.Application.YouTube;
using ObsLiveBot.Infrastructure.Chat;

namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class YouTubeLiveDiscoveryOptionsValidator : IValidateOptions<YouTubeLiveDiscoveryOptions>
{
    public ValidateOptionsResult Validate(string? name, YouTubeLiveDiscoveryOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;

        if (string.IsNullOrWhiteSpace(options.Channel))
            return ValidateOptionsResult.Fail("YouTube live discovery requires a public channel handle or channel ID.");
        if (!YouTubeStreamsPageRules.TryBuildStreamsUrl(options.Channel, out _))
            return ValidateOptionsResult.Fail("YouTube live discovery channel must be a plain handle or a UC channel ID.");
        if (options.RequestTimeoutSeconds is < 1 or > 120)
            return ValidateOptionsResult.Fail("YouTube live discovery RequestTimeoutSeconds must be between 1 and 120.");
        if (options.MaxResponseBytes is < 65_536 or > 33_554_432)
            return ValidateOptionsResult.Fail("YouTube live discovery MaxResponseBytes must be between 65536 and 33554432.");
        if (options.MinDiscoveryIntervalSeconds is < 0 or > 3_600)
            return ValidateOptionsResult.Fail("YouTube live discovery MinDiscoveryIntervalSeconds must be between 0 and 3600.");
        if (!string.IsNullOrWhiteSpace(options.ManualLiveChatUrl) &&
            !YouTubeLiveChatSourceLocator.TryCreate(options.ManualLiveChatUrl, out _))
            return ValidateOptionsResult.Fail("YouTube live discovery ManualLiveChatUrl must be an official YouTube live_chat popout URL.");

        return ValidateOptionsResult.Success;
    }
}
