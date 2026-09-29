namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class TwitchOAuthOptions
{
    public const string SectionName = "LiveChat:Providers:Twitch";
    public bool Enabled { get; set; }
    public string? ClientId { get; set; }
    public string? Channel { get; set; }
    public string[] Scopes { get; set; } = ["user:read:chat"];
    public int DeviceAuthorizationTimeoutSeconds { get; set; } = 900;
}

public sealed class TwitchOAuthOptionsValidator : Microsoft.Extensions.Options.IValidateOptions<TwitchOAuthOptions>
{
    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, TwitchOAuthOptions options)
    {
        if (options.DeviceAuthorizationTimeoutSeconds is < 60 or > 1800)
            return Microsoft.Extensions.Options.ValidateOptionsResult.Fail(
                "Twitch device authorization timeout must be 60-1800 seconds.");
        if (options.Scopes.Length != 1 || options.Scopes[0] != "user:read:chat")
            return Microsoft.Extensions.Options.ValidateOptionsResult.Fail(
                "Task 09 is read-only and requests only user:read:chat.");
        return Microsoft.Extensions.Options.ValidateOptionsResult.Success;
    }
}

public sealed class CredentialHelperClientOptions
{
    public const string SectionName = "CredentialHelper";
    public string BaseUrl { get; set; } = "http://host.docker.internal:51823/";
    public string SharedKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 3;
}
