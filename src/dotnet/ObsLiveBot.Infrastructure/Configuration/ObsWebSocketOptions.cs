using Microsoft.Extensions.Options;

namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class ObsWebSocketOptions
{
    public const string SectionName = "ObsWebSocket";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 4455;

    public string? Password { get; set; }
}

public sealed class ObsWebSocketOptionsValidator : IValidateOptions<ObsWebSocketOptions>
{
    public ValidateOptionsResult Validate(string? name, ObsWebSocketOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            return ValidateOptionsResult.Fail("OBS WebSocket host is required.");
        }

        if (options.Port is < 1 or > 65535)
        {
            return ValidateOptionsResult.Fail("OBS WebSocket port must be between 1 and 65535.");
        }

        return ValidateOptionsResult.Success;
    }
}
