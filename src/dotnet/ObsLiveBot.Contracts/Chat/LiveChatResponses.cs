namespace ObsLiveBot.Contracts.Chat;

public sealed record LiveChatUserResponse(
    string Provider,
    string UserId,
    string Identity,
    string? Username,
    string? DisplayName,
    bool IsBroadcaster,
    bool IsModerator,
    bool IsSubscriber,
    bool IsVerified,
    bool IsBot,
    IReadOnlyList<string> Badges);

public sealed record LiveChatEventResponse(
    Guid EventId,
    string EventType,
    string Provider,
    string? ProviderEventId,
    string? ChannelId,
    string? ChannelName,
    LiveChatUserResponse User,
    string? Message,
    DateTimeOffset TimestampUtc,
    DateTimeOffset ReceivedAtUtc,
    long Sequence,
    string CorrelationId,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record LiveChatProviderResponse(
    string Provider,
    string ConnectionMode,
    bool Enabled,
    string State,
    bool Connected,
    string? Channel,
    DateTimeOffset? LastConnectedAtUtc,
    DateTimeOffset? LastEventAtUtc,
    string? Error,
    bool ProcessRunning,
    bool TransportReady,
    bool CaptureReady,
    IReadOnlyList<string> PlatformsObserved);

public sealed record LiveChatProvidersResponse(IReadOnlyList<LiveChatProviderResponse> Providers);

public sealed record LiveChatStateResponse(
    string Status,
    int ConnectedProviders,
    int EnabledProviders,
    long EventsReceived,
    long MessagesReceived,
    int BufferSize,
    int BufferCapacity,
    DateTimeOffset? LastMessageAtUtc);
