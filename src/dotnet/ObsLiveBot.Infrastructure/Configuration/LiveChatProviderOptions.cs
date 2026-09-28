namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class LiveChatProvidersOptions
{
    public const string SectionName = "LiveChat:Providers";
    public LiveChatProviderConfiguration Twitch { get; set; } = new();
    public LiveChatProviderConfiguration YouTube { get; set; } = new();
    public LiveChatProviderConfiguration TikTok { get; set; } = new();
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
