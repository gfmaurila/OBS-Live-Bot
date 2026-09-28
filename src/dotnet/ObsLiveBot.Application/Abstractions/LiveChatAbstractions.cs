using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.Abstractions;

public sealed class LiveChatOptions
{
    public const string SectionName = "LiveChat";
    public int BufferCapacity { get; set; } = 500;
    public int DeduplicationCapacity { get; set; } = 2_000;
    public int MaxMessageLength { get; set; } = 4_000;
    public int MaxProviderEventIdLength { get; set; } = 512;
    public int MaxChannelIdLength { get; set; } = 512;
    public int MaxUserIdLength { get; set; } = 512;
    public int MaxTextFieldLength { get; set; } = 512;
    public int MaxBadges { get; set; } = 32;
    public int MaxBadgeLength { get; set; } = 128;
    public int MaxMetadataEntries { get; set; } = 32;
    public int MaxMetadataKeyLength { get; set; } = 256;
    public int MaxMetadataValueLength { get; set; } = 2_048;
}

public sealed record ProviderLiveChatEvent(
    LiveChatProviderType Provider,
    LiveChatEventType EventType,
    string? ProviderEventId,
    string? ChannelId,
    string? ChannelName,
    LiveChatUser User,
    string? Message,
    DateTimeOffset TimestampUtc,
    string? CorrelationId,
    IReadOnlyDictionary<string, string?> Metadata);

public interface ILiveChatProvider
{
    LiveChatProviderType Provider { get; }
    LiveChatProviderSnapshot Snapshot { get; }
    TimeSpan? RetryAfter { get; }
    Task Completion { get; }
    void SetLifecycleState(LiveChatProviderState state, string? error = null);
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}

public interface ILiveChatProviderRegistry
{
    IReadOnlyList<LiveChatProviderSnapshot> GetProviders();
    IReadOnlyList<ILiveChatProvider> GetEnabledProviders();
    ILiveChatProvider? Find(LiveChatProviderType provider);
}

public interface ILiveChatBuffer
{
    int Count { get; }
    int Capacity { get; }
    LiveChatStateSnapshot GetState(IReadOnlyList<LiveChatProviderSnapshot> providers);
    IReadOnlyList<LiveChatEvent> GetRecent(int limit, LiveChatProviderType? provider = null, LiveChatEventType? eventType = null);
    void Add(LiveChatEvent chatEvent);
}

public interface ILiveChatDeduplicator
{
    bool TryAccept(LiveChatEvent chatEvent);
    int Count { get; }
    int Capacity { get; }
}

public interface ILiveChatEventNormalizer
{
    LiveChatEvent Normalize(ProviderLiveChatEvent providerEvent);
}

public interface ILiveChatIngestionPipeline
{
    Task<LiveChatIngestionResult> IngestAsync(ProviderLiveChatEvent providerEvent, CancellationToken cancellationToken);
}

public sealed record LiveChatIngestionResult(bool Accepted, bool Duplicate, LiveChatEvent? Event);

public interface ILiveChatEventPublisher
{
    Task PublishAsync(LiveChatEvent chatEvent, CancellationToken cancellationToken);
}

public interface ILiveChatReconnectDelay
{
    TimeSpan GetDelay(int attempt);
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}
