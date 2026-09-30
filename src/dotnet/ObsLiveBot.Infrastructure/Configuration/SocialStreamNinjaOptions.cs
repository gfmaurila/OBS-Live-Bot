using Microsoft.Extensions.Options;

namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class SocialStreamNinjaOptions
{
    public const string SectionName = "SocialStreamNinja";
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "http://gfm-studioos-socialstream:17778";
    public int MaxEventBytes { get; set; } = 64 * 1024;
    public bool ConfigureSources { get; set; } = true;
    public SocialStreamNinjaProviderSettings Twitch { get; set; } = new();
    public SocialStreamNinjaProviderSettings YouTube { get; set; } = new();
    public SocialStreamNinjaProviderSettings Kick { get; set; } = new();
}

public sealed class SocialStreamNinjaProviderSettings
{
    public bool Enabled { get; set; }
    public string? Channel { get; set; }
    public string? AuthMode { get; set; }
}

public sealed class SocialStreamNinjaOptionsValidator : IValidateOptions<SocialStreamNinjaOptions>
{
    public ValidateOptionsResult Validate(string? name, SocialStreamNinjaOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
            return ValidateOptionsResult.Fail("Social Stream Ninja endpoint must be a non-credentialed HTTP(S) origin.");
        if (options.MaxEventBytes is < 4_096 or > 1_048_576)
            return ValidateOptionsResult.Fail("Social Stream Ninja MaxEventBytes must be between 4096 and 1048576.");
        if (!options.Twitch.Enabled && !options.YouTube.Enabled && !options.Kick.Enabled)
            return ValidateOptionsResult.Fail("At least one Social Stream Ninja platform must be enabled.");
        if (options.YouTube.Enabled &&
            !string.IsNullOrWhiteSpace(options.YouTube.AuthMode) &&
            options.YouTube.AuthMode is not ("oauth" or "url"))
            return ValidateOptionsResult.Fail("YouTube simple capture AuthMode must be oauth or url.");
        return ValidateOptionsResult.Success;
    }
}
