namespace ObsLiveBot.Domain.Chat;

public enum LiveChatProviderType
{
    Unknown = 0,
    Twitch = 1,
    YouTube = 2,
    TikTok = 3,
    Kick = 4,
    SocialStreamNinja = 50,
    Development = 100
}

public enum LiveChatProviderState
{
    Disabled = 0,
    NotConfigured = 1,
    Disconnected = 2,
    Connecting = 3,
    Connected = 4,
    Reconnecting = 5,
    AuthenticationRequired = 6,
    AuthenticationFailed = 7,
    RateLimited = 8,
    Faulted = 9
}

public enum LiveChatEventType
{
    Unknown = 0,
    Message = 1,
    SystemMessage = 2,
    Membership = 3,
    Subscription = 4,
    Gift = 5,
    Donation = 6,
    Follow = 7,
    Raid = 8,
    Moderation = 9
}

public sealed record LiveChatUser(
    LiveChatProviderType Provider,
    string UserId,
    string? Username,
    string? DisplayName,
    bool IsBroadcaster,
    bool IsModerator,
    bool IsSubscriber,
    bool IsVerified,
    bool IsBot,
    IReadOnlyList<string> Badges)
{
    public string Identity => $"{Provider}:{UserId}";
}

public sealed record LiveChatEvent(
    Guid EventId,
    LiveChatEventType EventType,
    LiveChatProviderType Provider,
    string? ProviderEventId,
    string? ChannelId,
    string? ChannelName,
    LiveChatUser User,
    string? Message,
    DateTimeOffset TimestampUtc,
    DateTimeOffset ReceivedAtUtc,
    long Sequence,
    string CorrelationId,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record LiveChatProviderSnapshot(
    LiveChatProviderType Provider,
    bool Enabled,
    LiveChatProviderState State,
    string? Channel,
    DateTimeOffset? LastConnectedAtUtc,
    DateTimeOffset? LastEventAtUtc,
    string? Error,
    bool ProcessRunning = false,
    bool TransportReady = false,
    bool CaptureReady = false,
    IReadOnlyList<string>? PlatformsObserved = null)
{
    public bool IsConnected => State == LiveChatProviderState.Connected;
}

public sealed record LiveChatStateSnapshot(
    string Status,
    int ConnectedProviders,
    int EnabledProviders,
    long EventsReceived,
    long MessagesReceived,
    int BufferSize,
    int BufferCapacity,
    DateTimeOffset? LastMessageAtUtc);
