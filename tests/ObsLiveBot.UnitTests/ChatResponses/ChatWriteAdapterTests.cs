using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.ChatResponses;

namespace ObsLiveBot.UnitTests.ChatResponses;

/// <summary>
/// Per-platform write mapping. Each adapter knows its own platform key, its own message ceiling and how
/// its channel identifier appears on a source, because the three platforms are genuinely different on the
/// write side and a single shared rule would silently get at least one of them wrong.
/// </summary>
public sealed class ChatWriteAdapterTests
{
    [Fact]
    public void TwitchAdapter_MapsItsOwnPlatformAndCeiling()
    {
        var adapter = new TwitchChatWriteAdapter();

        Assert.Equal(LiveChatProviderType.Twitch, adapter.Provider);
        Assert.Equal(TwitchChatWriteAdapter.TwitchChatMessageLimit, adapter.MaxMessageCharacters);
        Assert.True(adapter.RequiresAuthenticatedSession);
        Assert.Equal("SocialStreamNinja:page-composer", adapter.Transport);
    }

    [Fact]
    public void YouTubeAdapter_MapsItsOwnPlatformAndItsOwnShorterCeiling()
    {
        var adapter = new YouTubeChatWriteAdapter();

        Assert.Equal(LiveChatProviderType.YouTube, adapter.Provider);
        Assert.Equal(YouTubeChatWriteAdapter.YouTubeChatMessageLimit, adapter.MaxMessageCharacters);
        Assert.True(adapter.RequiresAuthenticatedSession);
        Assert.Equal("SocialStreamNinja:page-composer", adapter.Transport);
    }

    /// <summary>
    /// Each adapter claims exactly one platform key. This is what stops a Kick reply from being typed into
    /// a Twitch window simply because both happen to use the same transport.
    /// </summary>
    [Fact]
    public void EachAdapter_ResolvesOnlyItsOwnPlatformKey()
    {
        var keys = new[] { "twitch", "youtube", "kick" };

        foreach (var adapter in new IChatWriteAdapter[]
                 {
                     new TwitchChatWriteAdapter(), new YouTubeChatWriteAdapter(), new KickChatWriteAdapter()
                 })
        {
            var accepted = keys
                .Where(key => adapter.ResolveTarget([Source("s", key, Username: "c", Active: true)], "c") is not null)
                .ToArray();

            Assert.Single(accepted);
        }
    }

    [Fact]
    public void KickAdapter_MapsItsOwnPlatformAndCeiling()
    {
        var adapter = new KickChatWriteAdapter();

        Assert.Equal(LiveChatProviderType.Kick, adapter.Provider);
        Assert.Equal(KickChatWriteAdapter.KickChatMessageLimit, adapter.MaxMessageCharacters);
        Assert.True(adapter.RequiresAuthenticatedSession);
        Assert.Equal("SocialStreamNinja:page-composer", adapter.Transport);
    }

    [Fact]
    public void TwitchAdapter_MatchesTheChannelAccountName()
    {
        var adapter = new TwitchChatWriteAdapter();

        var target = adapter.ResolveTarget(
            [Source("s1", "twitch", Username: "gfmaurila", Active: true)], "gfmaurila");

        Assert.NotNull(target);
        Assert.Equal("s1", target!.SourceId);
        Assert.True(target.ExactChannelMatch);
    }

    /// <summary>
    /// A YouTube live is a distinct video every stream, so the video id is what identifies the channel.
    /// Matching only on the account name would let a reply be typed into yesterday's source.
    /// </summary>
    [Fact]
    public void YouTubeAdapter_MatchesTheCurrentVideoId()
    {
        var adapter = new YouTubeChatWriteAdapter();

        var target = adapter.ResolveTarget(
            [Source("youtube-vid-_j0cCIamgpc", "youtube", Username: "gfmaurila",
                VideoId: "_j0cCIamgpc", Active: true)], "_j0cCIamgpc");

        Assert.NotNull(target);
        Assert.Equal("youtube-vid-_j0cCIamgpc", target!.SourceId);
        Assert.True(target.ExactChannelMatch);
    }

    [Fact]
    public void YouTubeAdapter_PrefersTheSourceForTheCurrentLiveOverAnOlderOne()
    {
        var adapter = new YouTubeChatWriteAdapter();
        var sources = new[]
        {
            Source("old", "youtube", Username: "gfmaurila", VideoId: "oldvideo", Active: true),
            Source("current", "youtube", Username: "gfmaurila", VideoId: "currentvideo", Active: true)
        };

        var target = adapter.ResolveTarget(sources, "currentvideo");

        Assert.Equal("current", target!.SourceId);
    }

    [Fact]
    public void YouTubeAdapter_PrefersAnActiveExactMatchOverAPausedOne()
    {
        var adapter = new YouTubeChatWriteAdapter();
        var sources = new[]
        {
            Source("paused", "youtube", Username: "gfmaurila", VideoId: "live", Active: false),
            Source("active", "youtube", Username: "gfmaurila", VideoId: "live", Active: true)
        };

        Assert.Equal("active", adapter.ResolveTarget(sources, "live")!.SourceId);
    }

    /// <summary>
    /// Capture and write can name one channel differently - a live video id, a vanity URL and an internal
    /// account name are three strings for one thing. Falling back to the platform's active source keeps a
    /// reply deliverable, and the mismatch is reported rather than hidden.
    /// </summary>
    [Fact]
    public void ChannelMismatch_FallsBackToTheActiveSourceAndReportsIt()
    {
        var adapter = new TwitchChatWriteAdapter();

        var target = adapter.ResolveTarget(
            [Source("s1", "twitch", Username: "gfmaurila", Active: true)], "some-other-name");

        Assert.NotNull(target);
        Assert.Equal("s1", target!.SourceId);
        Assert.False(target.ExactChannelMatch);
    }

    [Fact]
    public void ChannelMatchIsCaseInsensitive()
    {
        var adapter = new TwitchChatWriteAdapter();

        var target = adapter.ResolveTarget(
            [Source("s1", "twitch", Username: "GFMAURILA", Active: true)], "gfmaurila");

        Assert.True(target!.ExactChannelMatch);
    }

    [Fact]
    public void AnotherPlatformsSourceIsNeverUsed()
    {
        var adapter = new KickChatWriteAdapter();
        var sources = new[]
        {
            Source("twitch-1", "twitch", Username: "gfmaurila", Active: true),
            Source("youtube-1", "youtube", Username: "gfmaurila", Active: true)
        };

        Assert.Null(adapter.ResolveTarget(sources, "gfmaurila"));
    }

    /// <summary>
    /// No source means no authenticated session, so there is nothing to write through. Returning null is
    /// what makes the platform report NotConfigured instead of pretending to be ready.
    /// </summary>
    [Fact]
    public void NoSourceForThePlatform_ResolvesToNothing()
    {
        var adapter = new TwitchChatWriteAdapter();

        Assert.Null(adapter.ResolveTarget([], "gfmaurila"));
    }

    [Fact]
    public void EmptyChannelId_DoesNotMatchASourceByAccident()
    {
        var adapter = new TwitchChatWriteAdapter();
        var sources = new[] { Source("s1", "twitch", Username: "gfmaurila", Active: true) };

        var target = adapter.ResolveTarget(sources, string.Empty);

        Assert.NotNull(target);
        Assert.False(target!.ExactChannelMatch);
    }

    [Fact]
    public void PlatformKeyComparison_IsCaseInsensitive()
    {
        var adapter = new TwitchChatWriteAdapter();

        var target = adapter.ResolveTarget([Source("s1", "TWITCH", Username: "gfmaurila", Active: true)], "gfmaurila");

        Assert.NotNull(target);
    }

    [Fact]
    public void Registry_ResolvesEachRegisteredAdapterAndReportsUnsupportedPlatforms()
    {
        var registry = new ChatWriteAdapterRegistry(
        [
            new TwitchChatWriteAdapter(),
            new YouTubeChatWriteAdapter(),
            new KickChatWriteAdapter()
        ]);

        Assert.Equal(
            [LiveChatProviderType.Twitch, LiveChatProviderType.YouTube, LiveChatProviderType.Kick],
            registry.GetSupportedProviders());
        Assert.IsType<TwitchChatWriteAdapter>(registry.Find(LiveChatProviderType.Twitch));
        Assert.Null(registry.Find(LiveChatProviderType.TikTok));
        Assert.Null(registry.Find(LiveChatProviderType.Unknown));
    }

    [Fact]
    public void Registry_WithNoAdapters_ReportsEveryPlatformAsUnsupported()
    {
        var registry = new ChatWriteAdapterRegistry([]);

        Assert.Empty(registry.GetSupportedProviders());
        Assert.Empty(registry.GetAdapters());
        Assert.Null(registry.Find(LiveChatProviderType.YouTube));
    }

    [Fact]
    public void Registry_FindIsCaseSensitiveOnTheProviderEnumOnly()
    {
        var registry = new ChatWriteAdapterRegistry([new YouTubeChatWriteAdapter()]);

        Assert.NotNull(registry.Find(LiveChatProviderType.YouTube));
    }

    /// <summary>
    /// YouTube's live chat ceiling is well below the global reply budget and below Twitch's. Without this
    /// the adapter would let a 500-character reply through and the platform would reject it in front of
    /// viewers.
    /// </summary>
    [Fact]
    public void YouTubeCeiling_IsStricterThanTheTwitchCeiling()
    {
        Assert.True(YouTubeChatWriteAdapter.YouTubeChatMessageLimit < TwitchChatWriteAdapter.TwitchChatMessageLimit);
        Assert.Equal(500, KickChatWriteAdapter.KickChatMessageLimit);
    }

    [Fact]
    public void Adapters_SurviveReuseAcrossManyResolutions()
    {
        var adapter = new YouTubeChatWriteAdapter();
        var sources = new[] { Source("v1", "youtube", Username: "c", VideoId: "abc", Active: true) };

        for (var i = 0; i < 100; i++)
            Assert.Equal("v1", adapter.ResolveTarget(sources, "abc")!.SourceId);
    }

    internal static ChatWriteSource Source(
        string id,
        string target,
        string? Username = null,
        string? VideoId = null,
        bool Active = false) =>
        new(id, target, Username, VideoId, Active, Active ? "running" : "stopped");
}