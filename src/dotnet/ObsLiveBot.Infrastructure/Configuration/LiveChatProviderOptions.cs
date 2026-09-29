namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class LiveChatProvidersOptions
{
    public const string SectionName = "LiveChat:Providers";
    public LiveChatProviderConfiguration Twitch { get; set; } = new();
    public LiveChatProviderConfiguration YouTube { get; set; } = new();
    public LiveChatProviderConfiguration TikTok { get; set; } = new();
    public LiveChatProviderConfiguration Kick { get; set; } = new();
}

public sealed class StudioOsProviderConfiguration
{
    public StudioOsPublicProviderSettings Twitch { get; set; } = new();
    public StudioOsPublicProviderSettings YouTube { get; set; } = new();
    public StudioOsPublicProviderSettings Kick { get; set; } = new();
}

public sealed class StudioOsPublicProviderSettings
{
    public bool Enabled { get; set; }
    public bool OfficialApiEnabled { get; set; }
    public string? ClientId { get; set; }
    public string? Channel { get; set; }
    public string? ChannelId { get; set; }
    public string? BroadcasterUserId { get; set; }
}

public sealed class LiveChatProviderConfiguration
{
    public bool Enabled { get; set; }
    public string? Channel { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
}
