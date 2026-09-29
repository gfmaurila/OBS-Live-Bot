using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Application.Features.Chat.Ingest;
using ObsLiveBot.Application.LiveChat;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.UnitTests.Chat;

public sealed class LiveChatCoreTests
{
    [Fact]
    public void NormalizedMessage_PreservesExpectedFields()
    {
        var source = ChatEvent(providerEventId: "provider-1", message: "Olá, mundo 👋🏽");
        var normalized = new LiveChatEventNormalizer(TimeProvider.System).Normalize(source);

        Assert.NotEqual(Guid.Empty, normalized.EventId);
        Assert.Equal(LiveChatEventType.Message, normalized.EventType);
        Assert.Equal(LiveChatProviderType.Twitch, normalized.Provider);
        Assert.Equal("provider-1", normalized.ProviderEventId);
        Assert.Equal("Olá, mundo 👋🏽", normalized.Message);
        Assert.Equal(TimeSpan.Zero, normalized.TimestampUtc.Offset);
        Assert.Equal(TimeSpan.Zero, normalized.ReceivedAtUtc.Offset);
    }

    [Fact]
    public void EventTypes_ContainSupportedNormalizedEvents()
    {
        var names = Enum.GetNames<LiveChatEventType>();
        Assert.Contains("Message", names);
        Assert.Contains("Membership", names);
        Assert.Contains("Donation", names);
        Assert.Contains("Moderation", names);
        Assert.Contains("Unknown", names);
    }

    [Fact]
    public void ProviderTypes_AreExplicit()
    {
        Assert.Equal(
            ["Unknown", "Twitch", "YouTube", "TikTok", "Kick", "SocialStreamNinja", "Development"],
            Enum.GetNames<LiveChatProviderType>());
    }

    [Fact]
    public void ProviderStates_AreExplicitAndConnectedIsDerived()
    {
        var snapshot = new LiveChatProviderSnapshot(
            LiveChatProviderType.YouTube, true, LiveChatProviderState.Connected,
            null, null, null, null);

        Assert.True(snapshot.IsConnected);
        Assert.Contains(LiveChatProviderState.RateLimited, Enum.GetValues<LiveChatProviderState>());
        Assert.Contains(LiveChatProviderState.AuthenticationFailed, Enum.GetValues<LiveChatProviderState>());
    }

    [Fact]
    public void Identity_UsesProviderAndUserId()
    {
        var twitch = User(LiveChatProviderType.Twitch, "123");
        var youtube = User(LiveChatProviderType.YouTube, "123");

        Assert.Equal("Twitch:123", twitch.Identity);
        Assert.Equal("YouTube:123", youtube.Identity);
        Assert.NotEqual(twitch.Identity, youtube.Identity);
    }

    [Theory]
    [InlineData("Olá — Привет — こんにちは")]
    [InlineData("🔥🚛💨 👨🏽‍💻")]
    public async Task UnicodeAndEmoji_AreAcceptedWithoutMutation(string message)
    {
        var source = ChatEvent(message: message);
        var validator = Validator();

        var validation = await validator.ValidateAsync(source);
        var normalized = new LiveChatEventNormalizer(TimeProvider.System).Normalize(source);

        Assert.True(validation.IsValid);
        Assert.Equal(message, normalized.Message);
    }

    [Fact]
    public async Task EmptyMessage_IsRejected()
    {
        var result = await Validator().ValidateAsync(ChatEvent(message: null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task OversizedMessage_IsRejectedWithoutTruncation()
    {
        var source = ChatEvent(message: new string('x', 11));
        var result = await Validator(maxMessageLength: 10).ValidateAsync(source);

        Assert.False(result.IsValid);
        Assert.Equal(11, source.Message!.Length);
    }

    [Fact]
    public async Task SensitiveMetadata_IsRejectedAndDefensivelyRemoved()
    {
        var source = ChatEvent(metadata: new Dictionary<string, string?> { ["accessToken"] = "never-return" });
        var result = await Validator().ValidateAsync(source);
        var normalized = new LiveChatEventNormalizer(TimeProvider.System).Normalize(source);

        Assert.False(result.IsValid);
        Assert.Empty(normalized.Metadata);
    }

    [Fact]
    public async Task SameProviderEventId_IsDeduplicated()
    {
        var pipeline = Pipeline(out _, deduplicationCapacity: 10);

        var first = await pipeline.IngestAsync(ChatEvent(providerEventId: "same"), CancellationToken.None);
        var second = await pipeline.IngestAsync(ChatEvent(providerEventId: "same"), CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Duplicate);
    }

    [Fact]
    public async Task SameTextFromDifferentUsers_IsNotDuplicate()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var pipeline = Pipeline(out _);

        var first = await pipeline.IngestAsync(
            ChatEvent(providerEventId: null, userId: "user-1", message: "same", timestamp: timestamp),
            CancellationToken.None);
        var second = await pipeline.IngestAsync(
            ChatEvent(providerEventId: null, userId: "user-2", message: "same", timestamp: timestamp),
            CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
    }

    [Fact]
    public void DeduplicationCache_IsBoundedAndEvictsOldestKey()
    {
        var deduplicator = new LiveChatDeduplicator(Options.Create(new LiveChatOptions { DeduplicationCapacity = 2 }));

        Assert.True(deduplicator.TryAccept(Normalized("1")));
        Assert.True(deduplicator.TryAccept(Normalized("2")));
        Assert.True(deduplicator.TryAccept(Normalized("3")));
        Assert.Equal(2, deduplicator.Count);
        Assert.True(deduplicator.TryAccept(Normalized("1")));
    }

    [Fact]
    public async Task Sequence_IsMonotonicInProcessingOrder()
    {
        var pipeline = Pipeline(out _);

        var results = new[]
        {
            await pipeline.IngestAsync(ChatEvent(providerEventId: "1"), CancellationToken.None),
            await pipeline.IngestAsync(ChatEvent(providerEventId: "2"), CancellationToken.None),
            await pipeline.IngestAsync(ChatEvent(providerEventId: "3"), CancellationToken.None)
        };

        Assert.Equal([1L, 2L, 3L], results.Select(result => result.Event!.Sequence));
    }

    [Fact]
    public void Buffer_DefaultCapacityIs500()
    {
        var buffer = new LiveChatBuffer(Options.Create(new LiveChatOptions()));

        Assert.Equal(500, buffer.Capacity);
    }

    [Fact]
    public void Buffer_IsBoundedAndEvictsOldestEvent()
    {
        var buffer = new LiveChatBuffer(Options.Create(new LiveChatOptions { BufferCapacity = 2 }));
        buffer.Add(Normalized("1") with { Sequence = 1 });
        buffer.Add(Normalized("2") with { Sequence = 2 });
        buffer.Add(Normalized("3") with { Sequence = 3 });

        var events = buffer.GetRecent(100);
        Assert.Equal(2, buffer.Count);
        Assert.Equal([3L, 2L], events.Select(item => item.Sequence));
        Assert.DoesNotContain(events, item => item.ProviderEventId == "1");
    }

    [Fact]
    public async Task MediatRNotification_AddsEventToBufferAndInvokesPublisher()
    {
        var externalPublisher = new CollectingEventPublisher();
        var buffer = new LiveChatBuffer(Options.Create(new LiveChatOptions()));
        var handler = new LiveChatEventReceivedHandler(
            buffer,
            externalPublisher,
            NullLogger<LiveChatEventReceivedHandler>.Instance);
        var pipeline = Pipeline(out _, publisher: new ForwardingMediator(handler));

        var result = await pipeline.IngestAsync(ChatEvent(), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Single(buffer.GetRecent(10));
        Assert.Single(externalPublisher.Events);
    }

    [Fact]
    public async Task PublisherFailure_DoesNotPreventBuffering()
    {
        var buffer = new LiveChatBuffer(Options.Create(new LiveChatOptions()));
        var handler = new LiveChatEventReceivedHandler(
            buffer,
            new ThrowingEventPublisher(),
            NullLogger<LiveChatEventReceivedHandler>.Instance);
        var pipeline = Pipeline(out _, publisher: new ForwardingMediator(handler));

        var result = await pipeline.IngestAsync(ChatEvent(), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Single(buffer.GetRecent(10));
    }

    [Fact]
    public void ChatState_TracksTotalsBeyondBoundedBuffer()
    {
        var buffer = new LiveChatBuffer(Options.Create(new LiveChatOptions { BufferCapacity = 1 }));
        buffer.Add(Normalized("1"));
        buffer.Add(Normalized("2"));

        var state = buffer.GetState([]);
        Assert.Equal(2, state.EventsReceived);
        Assert.Equal(2, state.MessagesReceived);
        Assert.Equal(1, state.BufferSize);
    }

    private static ProviderLiveChatEventValidator Validator(int maxMessageLength = 4_000) =>
        new(Options.Create(new LiveChatOptions { MaxMessageLength = maxMessageLength }));

    private static LiveChatIngestionPipeline Pipeline(
        out CollectingMediator mediator,
        int deduplicationCapacity = 100,
        IPublisher? publisher = null)
    {
        mediator = new CollectingMediator();
        var options = Options.Create(new LiveChatOptions { DeduplicationCapacity = deduplicationCapacity });
        return new LiveChatIngestionPipeline(
            new ProviderLiveChatEventValidator(options),
            new LiveChatEventNormalizer(TimeProvider.System),
            new LiveChatDeduplicator(options),
            publisher ?? mediator);
    }

    internal static ProviderLiveChatEvent ChatEvent(
        LiveChatProviderType provider = LiveChatProviderType.Twitch,
        string? providerEventId = "event-1",
        string userId = "user-1",
        string? message = "hello",
        DateTimeOffset? timestamp = null,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(
            provider,
            LiveChatEventType.Message,
            providerEventId,
            "channel-1",
            "Channel",
            User(provider, userId),
            message,
            timestamp ?? DateTimeOffset.UtcNow,
            null,
            metadata ?? new Dictionary<string, string?>());

    internal static LiveChatUser User(LiveChatProviderType provider, string userId) =>
        new(provider, userId, "username", "Display", false, false, false, false, false, []);

    private static LiveChatEvent Normalized(string providerEventId) =>
        new LiveChatEventNormalizer(TimeProvider.System).Normalize(ChatEvent(providerEventId: providerEventId));

    private sealed class CollectingMediator : IPublisher
    {
        public List<object> Notifications { get; } = [];
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Publish((object)notification, cancellationToken);
    }

    private sealed class ForwardingMediator(LiveChatEventReceivedHandler handler) : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            notification is LiveChatEventReceivedNotification chat
                ? handler.Handle(chat, cancellationToken)
                : Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Publish((object)notification, cancellationToken);
    }

    private sealed class CollectingEventPublisher : ILiveChatEventPublisher
    {
        public List<LiveChatEvent> Events { get; } = [];
        public Task PublishAsync(LiveChatEvent chatEvent, CancellationToken cancellationToken)
        {
            Events.Add(chatEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEventPublisher : ILiveChatEventPublisher
    {
        public Task PublishAsync(LiveChatEvent chatEvent, CancellationToken cancellationToken) =>
            throw new IOException("n8n unavailable");
    }
}
