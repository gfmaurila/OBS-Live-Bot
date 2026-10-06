using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.Chat;
using ObsLiveBot.Infrastructure.ChatResponses;

namespace ObsLiveBot.UnitTests.ChatResponses;

/// <summary>
/// Read and write capability are decided independently, from real runtime state. These tests exist to pin
/// the one rule that matters most: a connected capture provider never implies a ready write side.
/// </summary>
public sealed class ChatProviderCapabilityServiceTests
{
    [Fact]
    public async Task EverySupportedPlatformIsReported_InAStableOrder()
    {
        var capabilities = await Capabilities().GetCapabilitiesAsync(CancellationToken.None);

        Assert.Equal(
            [LiveChatProviderType.Twitch, LiveChatProviderType.YouTube, LiveChatProviderType.Kick],
            capabilities.Select(capability => capability.Provider));
    }

    [Fact]
    public async Task ConnectedCapture_WithAnActiveAuthenticatedSource_ReportsWriteReady()
    {
        var capabilities = await Capabilities().GetCapabilitiesAsync(CancellationToken.None);
        var twitch = capabilities.Single(capability => capability.Provider == LiveChatProviderType.Twitch);

        Assert.True(twitch.CanRead);
        Assert.Equal(ChatCapabilityStatus.Ready, twitch.ReadStatus);
        Assert.True(twitch.CanWrite);
        Assert.True(twitch.WriteReady);
        Assert.Equal(ChatCapabilityStatus.Ready, twitch.WriteStatus);
        Assert.True(twitch.WriteAuthenticated);
        Assert.Equal("SocialStreamNinja:page-composer", twitch.WriteTransport);
        Assert.Equal(500, twitch.MaxMessageCharacters);
    }

    [Fact]
    public async Task YouTube_ReportsItsOwnShorterMessageCeiling()
    {
        var capabilities = await Capabilities().GetCapabilitiesAsync(CancellationToken.None);

        Assert.Equal(
            200,
            capabilities.Single(capability => capability.Provider == LiveChatProviderType.YouTube).MaxMessageCharacters);
    }

    /// <summary>
    /// The rule the whole capability model exists for. Read can be Ready while write is NotConfigured, and
    /// nothing in the read path may be allowed to imply otherwise.
    /// </summary>
    [Fact]
    public async Task ConnectedCapture_WithoutAnyWriteSource_ReportsReadReadyAndWriteNotConfigured()
    {
        var capabilities = await Capabilities(sources: []).GetCapabilitiesAsync(CancellationToken.None);

        foreach (var capability in capabilities)
        {
            Assert.True(capability.CanRead);
            Assert.Equal(ChatCapabilityStatus.Ready, capability.ReadStatus);
            Assert.True(capability.CanWrite);
            Assert.False(capability.WriteReady);
            Assert.Equal(ChatCapabilityStatus.NotConfigured, capability.WriteStatus);
            Assert.False(capability.WriteAuthenticated);
            Assert.Contains("No authenticated", capability.WriteDetail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SourceLoadedButNotActive_ReportsWriteDegraded()
    {
        var capabilities = await Capabilities(sources: [Source("s1", "youtube", Active: false)])
            .GetCapabilitiesAsync(CancellationToken.None);

        var youtube = capabilities.Single(capability => capability.Provider == LiveChatProviderType.YouTube);

        Assert.True(youtube.CanWrite);
        Assert.False(youtube.WriteReady);
        Assert.Equal(ChatCapabilityStatus.Degraded, youtube.WriteStatus);
        Assert.Contains("not active", youtube.WriteDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformWithNoAdapter_IsReportedUnsupportedRatherThanFaked()
    {
        var capabilities = await Capabilities(
                adapters: [new TwitchChatWriteAdapter(), new YouTubeChatWriteAdapter()])
            .GetCapabilitiesAsync(CancellationToken.None);

        var kick = capabilities.Single(capability => capability.Provider == LiveChatProviderType.Kick);

        Assert.False(kick.CanWrite);
        Assert.False(kick.WriteReady);
        Assert.Equal(ChatCapabilityStatus.Unsupported, kick.WriteStatus);
        Assert.Null(kick.WriteTransport);
        Assert.Equal(0, kick.MaxMessageCharacters);
    }

    [Fact]
    public async Task DevelopmentSender_ReportsNotConfiguredAndNeverReady()
    {
        var capabilities = await Capabilities(sender: DevelopmentSender())
            .GetCapabilitiesAsync(CancellationToken.None);

        foreach (var capability in capabilities)
        {
            Assert.False(capability.WriteReady);
            Assert.Equal(ChatCapabilityStatus.NotConfigured, capability.WriteStatus);
            Assert.Contains("simulated", capability.WriteDetail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UnavailableSender_ReportsWriteDegraded()
    {
        var capabilities = await Capabilities(sender: new StubChatResponseSender { IsAvailable = false })
            .GetCapabilitiesAsync(CancellationToken.None);

        var twitch = capabilities.Single(capability => capability.Provider == LiveChatProviderType.Twitch);

        Assert.False(twitch.WriteReady);
        Assert.Equal(ChatCapabilityStatus.Degraded, twitch.WriteStatus);
        Assert.Contains("unavailable", twitch.WriteDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenderThatDoesNotSupportThePlatform_ReportsUnsupported()
    {
        var capabilities = await Capabilities(
                sender: new StubChatResponseSender
                {
                    SupportedProviders = [LiveChatProviderType.Twitch, LiveChatProviderType.Kick]
                })
            .GetCapabilitiesAsync(CancellationToken.None);

        var youtube = capabilities.Single(capability => capability.Provider == LiveChatProviderType.YouTube);
        var twitch = capabilities.Single(capability => capability.Provider == LiveChatProviderType.Twitch);

        Assert.Equal(ChatCapabilityStatus.Unsupported, youtube.WriteStatus);
        Assert.Equal(ChatCapabilityStatus.Ready, twitch.WriteStatus);
    }

    [Fact]
    public async Task DisabledCaptureProvider_ReportsReadNotConfigured()
    {
        var capabilities = await Capabilities(
                twitchSnapshot: new LiveChatProviderSnapshot(
                    LiveChatProviderType.Twitch,
                    Enabled: false,
                    State: LiveChatProviderState.Disabled,
                    Channel: null,
                    null,
                    null,
                    null))
            .GetCapabilitiesAsync(CancellationToken.None);

        var twitch = capabilities.Single(capability => capability.Provider == LiveChatProviderType.Twitch);

        Assert.False(twitch.CanRead);
        Assert.Equal(ChatCapabilityStatus.NotConfigured, twitch.ReadStatus);
    }

    [Fact]
    public async Task DisconnectedCaptureProvider_ReportsReadDegraded()
    {
        var capabilities = await Capabilities(
                twitchSnapshot: new LiveChatProviderSnapshot(
                    LiveChatProviderType.Twitch,
                    Enabled: true,
                    State: LiveChatProviderState.Reconnecting,
                    Channel: "gfmaurila",
                    null,
                    null,
                    "reconnecting"))
            .GetCapabilitiesAsync(CancellationToken.None);

        var twitch = capabilities.Single(capability => capability.Provider == LiveChatProviderType.Twitch);

        Assert.Equal(ChatCapabilityStatus.Degraded, twitch.ReadStatus);
        Assert.Contains("Reconnecting", twitch.ReadDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureAwaitingAuthentication_ReportsReadDegradedNotReady()
    {
        var capabilities = await Capabilities(
                twitchSnapshot: new LiveChatProviderSnapshot(
                    LiveChatProviderType.Twitch,
                    Enabled: true,
                    State: LiveChatProviderState.AuthenticationRequired,
                    Channel: "gfmaurila",
                    null,
                    null,
                    null))
            .GetCapabilitiesAsync(CancellationToken.None);

        var twitch = capabilities.Single(capability => capability.Provider == LiveChatProviderType.Twitch);

        Assert.Equal(ChatCapabilityStatus.Degraded, twitch.ReadStatus);
    }

    /// <summary>
    /// Every write on every supported platform needs the platform's own authenticated session. Reporting it
    /// per platform is what lets a future interface say "authenticate here" instead of guessing.
    /// </summary>
    [Fact]
    public async Task EveryWriteTransport_DeclaresThatItNeedsAuthentication()
    {
        var capabilities = await Capabilities().GetCapabilitiesAsync(CancellationToken.None);

        Assert.All(capabilities, capability => Assert.True(capability.WriteRequiresAuthentication));
    }

    /// <summary>
    /// Capability is reported per platform, not per live, so the probe has no channel to match on. That is
    /// still enough to report readiness, and the detail says out loud that the channel could not be
    /// confirmed here - the sender resolves the real target when it actually writes.
    /// </summary>
    [Fact]
    public async Task ReadyWrite_SaysWhenTheChannelCouldNotBeConfirmed()
    {
        var youtube = (await Capabilities().GetCapabilitiesAsync(CancellationToken.None))
            .Single(capability => capability.Provider == LiveChatProviderType.YouTube);

        Assert.True(youtube.WriteReady);
        Assert.Contains("identifiers differ", youtube.WriteDetail, StringComparison.Ordinal);
    }

    private static ChatProviderCapabilityService Capabilities(
        IReadOnlyList<ChatWriteSource>? sources = null,
        IChatWriteAdapter[]? adapters = null,
        StubChatResponseSender? sender = null,
        LiveChatProviderSnapshot? twitchSnapshot = null)
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var writeClient = new StubChatWriteSourceLister
        {
            Sources = sources ??
            [
                Source("twitch-1", "twitch", Username: "gfmaurila", Active: true),
                Source("youtube-vid-_j0cCIamgpc", "youtube", Username: "gfmaurila",
                    VideoId: "_j0cCIamgpc", Active: true),
                Source("kick-1", "kick", Username: "gfmaurila", Active: true)
            ]
        };

        var liveChat = new StubLiveChatProviderRegistry(twitchSnapshot ?? ConnectedTwitch());
        var selected = sender ?? new StubChatResponseSender { Name = "Stub" };
        // The stub registry selects by name, so the sender under test has to be the selected one.
        var registry = new StubChatResponseSenderRegistry([selected]) { SelectedName = selected.Name };
        var adapterRegistry = new ChatWriteAdapterRegistry(adapters ??
        [
            new TwitchChatWriteAdapter(), new YouTubeChatWriteAdapter(), new KickChatWriteAdapter()
        ]);

        var service = new ChatProviderCapabilityService(
            liveChat,
            registry,
            adapterRegistry,
            new ChatWriteTargetResolver(writeClient, time, NullLogger<ChatWriteTargetResolver>.Instance),
            NullLogger<ChatProviderCapabilityService>.Instance);

        return service;
    }

    private static LiveChatProviderSnapshot ConnectedTwitch() =>
        new(
            LiveChatProviderType.Twitch,
            Enabled: true,
            State: LiveChatProviderState.Connected,
            Channel: "gfmaurila",
            null,
            null,
            null,
            ProcessRunning: true,
            TransportReady: true,
            CaptureReady: true);

    private static ChatWriteSource Source(
        string id,
        string target,
        string? Username = null,
        string? VideoId = null,
        bool Active = false) =>
        new(id, target, Username, VideoId, Active, Active ? "running" : "stopped");

    /// <summary>
    /// The development sender never delivers anything. Whichever sender is selected, a platform that
    /// cannot really be written to must never be reported as ready.
    /// </summary>
    private static StubChatResponseSender DevelopmentSender() =>
        new() { Name = "Development", IsDevelopment = true, IsSimulated = true };

    /// <summary>
    /// Stands in for the write transport. The outgoing path only ever talks to
    /// <see cref="IChatWriteSourceLister"/>, which is what keeps the write side testable without a
    /// capture-side command client.
    /// </summary>
    private sealed class StubChatWriteSourceLister : IChatWriteSourceLister
    {
        public IReadOnlyList<ChatWriteSource> Sources { get; init; } = [];

        public Task<IReadOnlyList<ChatWriteSource>> ListSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Sources);
    }

    private sealed class StubLiveChatProviderRegistry(LiveChatProviderSnapshot twitch) : ILiveChatProviderRegistry
    {
        public IReadOnlyList<LiveChatProviderSnapshot> GetProviders() =>
            [twitch, Snapshot(LiveChatProviderType.YouTube), Snapshot(LiveChatProviderType.Kick)];

        public IReadOnlyList<ILiveChatProvider> GetEnabledProviders() => [];

        public ILiveChatProvider? Find(LiveChatProviderType provider) => null;

        public void NotifyCredentialsChanged(LiveChatProviderType provider)
        {
        }

        private static LiveChatProviderSnapshot Snapshot(LiveChatProviderType provider) =>
            new(provider, true, LiveChatProviderState.Connected, "gfmaurila", null, null, null,
                true, true, true);
    }
}