using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Application.YouTube;
using ObsLiveBot.Domain.Obs;
using ObsLiveBot.Infrastructure.Chat;
using ObsLiveBot.Infrastructure.YouTube;
using Xunit.Abstractions;

namespace ObsLiveBot.UnitTests.YouTube;

public sealed class YouTubeLiveDiscoveryTests(ITestOutputHelper output)
{
    private const string FirstVideoId = "_j0cCIamgpc";
    private const string SecondVideoId = "gH_3PK1WLmw";
    private const string ManualVideoId = "WH7l_CMYCfU";

    private static class VideoIdPlaceholders
    {
        public const string Live = FirstVideoId;
    }

    [Fact]
    public void DiscoveryContract_ExposesCanonicalPerLiveIdentityAndPublicUrls()
    {
        Assert.Equal($"youtube-vid-{FirstVideoId}", YouTubeLiveSourceIdentity.ForVideoId(FirstVideoId));
        Assert.Equal(FirstVideoId, YouTubeLiveSourceIdentity.VideoIdFromSourceId($"youtube-vid-{FirstVideoId}"));
        Assert.Equal(
            $"https://www.youtube.com/live/{FirstVideoId}",
            YouTubeLiveSourceIdentity.PublicUrl(FirstVideoId));
        Assert.Equal(
            $"https://www.youtube.com/live_chat?is_popout=1&v={FirstVideoId}",
            YouTubeLiveSourceIdentity.ChatUrl(FirstVideoId));

        Assert.True(YouTubeLiveSourceIdentity.TryCreateLocator(FirstVideoId, out var first));
        Assert.True(YouTubeLiveSourceIdentity.TryCreateLocator(FirstVideoId, out var second));
        Assert.Equal(first!.IdempotencyKey, second!.IdempotencyKey);
        Assert.Equal(FirstVideoId, first.VideoId);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("way-too-long-video-id")]
    [InlineData("bad!chars1234")]
    [InlineData("https://www.youtube.com/live/_j0cCIamgpc")]
    public void DiscoveryContract_RejectsAnythingThatIsNotAPlainVideoId(string candidate)
    {
        Assert.False(YouTubeLiveSourceIdentity.IsVideoId(candidate));
        Assert.False(YouTubeLiveSourceIdentity.TryCreateLocator(candidate, out _));
    }

    [Fact]
    public async Task WentLive_DiscoversAndAttachesExactlyOneCanonicalSource()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Discovery.Candidates.Add(FirstVideoId);

        await harness.ReconcileAsync();

        var state = harness.Orchestrator.State;
        Assert.True(state.OwnsCurrentLive);
        Assert.Equal(FirstVideoId, state.CurrentVideoId);
        Assert.Equal($"youtube-vid-{FirstVideoId}", state.CurrentSourceId);
        Assert.Equal(FirstVideoId, harness.Sources.AddedVideoIds[0]);
        Assert.Single(harness.Sources.AddedVideoIds);
        Assert.Single(harness.Sources.Active, id => id == $"youtube-vid-{FirstVideoId}");
    }

    [Fact]
    public async Task RepeatedLiveSignals_DiscoverOnceAndNeverDuplicateTheSource()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Discovery.Candidates.Add(FirstVideoId);

        await harness.ReconcileAsync();
        await harness.ReconcileAsync();
        await harness.ReconcileAsync();

        Assert.Equal(1, harness.Discovery.CallCount);
        Assert.Single(harness.Sources.AddedVideoIds);
        Assert.Single(harness.Sources.Active, id => id.StartsWith("youtube-vid-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SameLive_ReusesExistingSourceWithoutCreatingASecondOne()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Sources.SeedActive($"youtube-vid-{FirstVideoId}", "youtube", FirstVideoId);
        harness.Discovery.Candidates.Add(FirstVideoId);

        await harness.ReconcileAsync();

        Assert.Empty(harness.Sources.AddedVideoIds);
        Assert.True(harness.Orchestrator.State.OwnsCurrentLive);
        Assert.Single(harness.Sources.Active, id => id == $"youtube-vid-{FirstVideoId}");
    }

    [Fact]
    public async Task DifferentLive_StopsReusingTheFinishedLiveAndAttachesTheNewOne()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Sources.SeedActive($"youtube-vid-{FirstVideoId}", "youtube", FirstVideoId);
        harness.Discovery.Candidates.Add(FirstVideoId);
        await harness.ReconcileAsync();
        Assert.Equal(FirstVideoId, harness.Orchestrator.State.CurrentVideoId);

        // The live ends and a new one starts.
        harness.IsStreaming = false;
        await harness.ReconcileAsync();
        harness.Discovery.Candidates.Clear();
        harness.Discovery.Candidates.Add(SecondVideoId);
        harness.IsStreaming = true;
        await harness.ReconcileAsync();

        Assert.Equal(SecondVideoId, harness.Orchestrator.State.CurrentVideoId);
        Assert.DoesNotContain($"youtube-vid-{FirstVideoId}", harness.Sources.Active);
        Assert.Contains($"youtube-vid-{SecondVideoId}", harness.Sources.Active);
        Assert.Contains(SecondVideoId, harness.Sources.AddedVideoIds);
    }

    [Fact]
    public async Task FinishedLiveIsNeverReused_EvenWhileOBSStaysStreaming()
    {
        // Interval 0 makes the heartbeat immediate, modeling an OBS that never reports OFF.
        var harness = new Harness(output) { IsStreaming = true, MinDiscoveryIntervalSeconds = 0 };
        harness.Discovery.Candidates.Add(FirstVideoId);
        await harness.ReconcileAsync();
        Assert.Equal(FirstVideoId, harness.Orchestrator.State.CurrentVideoId);

        // The heartbeat sees a different live: ownership must move forward, because the previous
        // live can no longer be receiving chat.
        harness.Discovery.Candidates.Clear();
        harness.Discovery.Candidates.Add(SecondVideoId);
        await harness.ReconcileAsync();

        Assert.Equal(SecondVideoId, harness.Orchestrator.State.CurrentVideoId);
        Assert.DoesNotContain($"youtube-vid-{FirstVideoId}", harness.Sources.Active);
        Assert.Contains($"youtube-vid-{FirstVideoId}", harness.Sources.Stopped);
        Assert.Contains($"youtube-vid-{SecondVideoId}", harness.Sources.Active);
    }

    [Fact]
    public async Task StillLiveAlongsideAnotherLive_KeepsTheOwnedLiveInsteadOfFlapping()
    {
        var harness = new Harness(output) { IsStreaming = true, MinDiscoveryIntervalSeconds = 0 };
        harness.Discovery.Candidates.Add(FirstVideoId);
        await harness.ReconcileAsync();

        // The page briefly exposes a second live badge while the owned live is still live.
        harness.Discovery.Candidates.Clear();
        harness.Discovery.Candidates.AddRange([FirstVideoId, SecondVideoId]);
        await harness.ReconcileAsync();

        Assert.Equal(FirstVideoId, harness.Orchestrator.State.CurrentVideoId);
        Assert.Equal([FirstVideoId], harness.Sources.AddedVideoIds);
        Assert.Single(harness.Sources.Active, id => id.StartsWith("youtube-vid-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingDiscoveryResult_LeavesEverythingUntouchedAndIsReported()
    {
        var harness = new Harness(output) { IsStreaming = true };

        await harness.ReconcileAsync();

        var state = harness.Orchestrator.State;
        Assert.False(state.OwnsCurrentLive);
        Assert.Equal("no_live_badge", state.LastReason);
        Assert.Empty(harness.Sources.AddedVideoIds);
        Assert.Empty(harness.Sources.Stopped);
        Assert.Equal(1, state.DiscoveryAttempts);
        Assert.Equal(0, state.SourceEnsures);
    }

    [Fact]
    public async Task DiscoveryFailure_IsolatedAndDoesNotDisturbObsTwitchOrKick()
    {
        var harness = new Harness(output) { IsStreaming = true, ThrowOnDiscover = true };

        await harness.ReconcileAsync();

        var state = harness.Orchestrator.State;
        Assert.False(state.OwnsCurrentLive);
        Assert.Equal("reconcile_failed", state.LastReason);
        // The trigger source is still live and the other platforms are untouched.
        Assert.True(harness.Orchestrator.State.ObsStreaming);
        Assert.Contains("twitch-user-gfmaurila", harness.Sources.Active);
        Assert.Contains("kick-user-gfmaurila", harness.Sources.Active);
        Assert.Empty(harness.Sources.AddedVideoIds);
    }

    [Fact]
    public async Task SourceEnsureFailure_IsolatedAndNeverClaimsOwnership()
    {
        var harness = new Harness(output) { IsStreaming = true, ThrowOnEnsure = true };
        harness.Discovery.Candidates.Add(FirstVideoId);

        await harness.ReconcileAsync();

        Assert.False(harness.Orchestrator.State.OwnsCurrentLive);
        Assert.Equal("reconcile_failed", harness.Orchestrator.State.LastReason);
        Assert.Contains("twitch-user-gfmaurila", harness.Sources.Active);
        Assert.Contains("kick-user-gfmaurila", harness.Sources.Active);
    }

    [Fact]
    public async Task StudioOsRestartWhileLive_ReconcileReusesTheExistingSource()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Sources.SeedActive($"youtube-vid-{FirstVideoId}", "youtube", FirstVideoId);
        harness.Discovery.Candidates.Add(FirstVideoId);
        await harness.ReconcileAsync();

        // A restart loses in-memory ownership but not the SSN source.
        var restarted = new Harness(output) { IsStreaming = true };
        restarted.Sources.Import(harness.Sources);
        restarted.Discovery.Candidates.Add(FirstVideoId);

        await restarted.ReconcileAsync();

        Assert.True(restarted.Orchestrator.State.OwnsCurrentLive);
        Assert.Empty(restarted.Sources.AddedVideoIds);
        Assert.Single(restarted.Sources.Active, id => id == $"youtube-vid-{FirstVideoId}");
    }

    [Fact]
    public async Task SsnRestartWithLostSource_IsRecreatedForTheSameLive()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Sources.SeedActive($"youtube-vid-{FirstVideoId}", "youtube", FirstVideoId);
        harness.Discovery.Candidates.Add(FirstVideoId);
        await harness.ReconcileAsync();
        Assert.Empty(harness.Sources.AddedVideoIds);

        // SSN lost the source while the live continues, and in-memory ownership was also lost.
        var restarted = new Harness(output) { IsStreaming = true };
        restarted.Discovery.Candidates.Add(FirstVideoId);

        await restarted.ReconcileAsync();

        Assert.True(restarted.Orchestrator.State.OwnsCurrentLive);
        Assert.Equal([FirstVideoId], restarted.Sources.AddedVideoIds);
        Assert.Contains($"youtube-vid-{FirstVideoId}", restarted.Sources.Active);
    }

    [Fact]
    public async Task StreamingFalse_ReleasesOwnershipAndStopsOnlyTheOwnedSource()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Sources.SeedActive("youtube-vid-OLDERLIVE01", "youtube", "OLDERLIVE01");
        harness.Discovery.Candidates.Add(FirstVideoId);
        await harness.ReconcileAsync();

        harness.IsStreaming = false;
        await harness.ReconcileAsync();

        var state = harness.Orchestrator.State;
        Assert.False(state.OwnsCurrentLive);
        Assert.Null(state.CurrentVideoId);
        Assert.Equal(1, state.SourceReleases);
        // The finished live's record is preserved, and other platforms keep running.
        Assert.Contains($"youtube-vid-{FirstVideoId}", harness.Sources.All.Select(source => source.Id));
        Assert.Contains("twitch-user-gfmaurila", harness.Sources.Active);
        Assert.Contains("kick-user-gfmaurila", harness.Sources.Active);
        Assert.DoesNotContain($"youtube-vid-{FirstVideoId}", harness.Sources.Active);
    }

    [Fact]
    public async Task SteadyStreamingWithoutSignals_StillRechecksSoAFinishedLiveIsNotReused()
    {
        // The worker ticks as well as reacting to signals, so an OBS that never reports OFF still
        // discovers that the finished live was replaced. This is the tick's contract.
        var harness = new Harness(output) { IsStreaming = true, MinDiscoveryIntervalSeconds = 5 };
        harness.Discovery.Candidates.Add(FirstVideoId);
        await harness.ReconcileAsync();
        Assert.Equal(FirstVideoId, harness.Orchestrator.State.CurrentVideoId);

        // No signal is enqueued: only the elapsed tick lets the next reconcile through.
        await Task.Delay(5_100);
        harness.Discovery.Candidates.Clear();
        harness.Discovery.Candidates.Add(SecondVideoId);
        await harness.ReconcileAsync();

        Assert.Equal(SecondVideoId, harness.Orchestrator.State.CurrentVideoId);
        Assert.Contains($"youtube-vid-{SecondVideoId}", harness.Sources.Active);
        Assert.DoesNotContain($"youtube-vid-{FirstVideoId}", harness.Sources.Active);
    }

    [Fact]
    public async Task ManualOverride_TakesPrecedenceOverAutomaticDiscovery()
    {
        var harness = new Harness(output) { IsStreaming = true, ManualLiveChatUrl = ManualChatUrl(ManualVideoId) };
        harness.Discovery.Candidates.Add(FirstVideoId);

        await harness.ReconcileAsync();

        Assert.Equal(0, harness.Discovery.CallCount);
        Assert.Equal("manual_live_chat_url_override", harness.Orchestrator.State.DisabledReason);
        Assert.False(harness.Orchestrator.State.OwnsCurrentLive);
        Assert.Empty(harness.Sources.AddedVideoIds);
    }

    [Fact]
    public async Task DisabledConfiguration_NeverDiscoversOrTouchesSources()
    {
        var harness = new Harness(output) { IsStreaming = true, Enabled = false };
        harness.Discovery.Candidates.Add(FirstVideoId);

        await harness.ReconcileAsync();

        Assert.Equal(0, harness.Discovery.CallCount);
        Assert.Equal("disabled_by_configuration", harness.Orchestrator.State.DisabledReason);
        Assert.Empty(harness.Sources.AddedVideoIds);
    }

    [Theory]
    [InlineData("ObsStreamStateChanged", "Offline", "Live", YouTubeLiveSignal.WentLive)]
    [InlineData("ObsStreamStateChanged", "Live", "Offline", YouTubeLiveSignal.WentOffline)]
    [InlineData("ObsStreamStateChanged", "Live", "Live", null)]
    [InlineData("ObsStreamStateChanged", "Offline", "Offline", null)]
    [InlineData("ObsStateSynchronized", "Live", "Live", YouTubeLiveSignal.Reconcile)]
    [InlineData("ObsConnectionEstablished", "Offline", "Offline", YouTubeLiveSignal.Reconcile)]
    public void SignalClassification_UsesTheEnvelopeWithoutConsultingTheNetwork(
        string eventType,
        string previous,
        string current,
        YouTubeLiveSignal? expected)
    {
        var envelope = Envelope(
            eventType,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["previous"] = previous, ["current"] = current });

        Assert.Equal(expected, YouTubeLiveOrchestrator.Classify(envelope));
    }

    [Fact]
    public void LiveBadgeExtraction_ReadsOnlyTheLiveEntryAndToleratesOtherBadges()
    {
        // Mirrors the real page: a live badge whose animation target is the video ID, plus a
        // playlist badge that must never be mistaken for a live.
        const string liveEntry =
            "THUMBNAIL_OVERLAY_BADGE_STYLE_LIVE\",\"animationActivationTargetId\":\"" + VideoIdPlaceholders.Live + "\"";
        const string playlistEntry =
            "THUMBNAIL_OVERLAY_BADGE_STYLE_PLAYLISTS\",\"animationActivationTargetId\":\"PLxxxxxxxxxxxxx\"";
        var html = string.Join(" ",
            "{\"browseId\":\"UCjy19AugQHIhyE0Nv558jcQ\"}",
            $"{{\"thumbnailOverlayBadgeStyleRenderer\":{{\"thumbnailOverlayBadgeStyle\":\"{liveEntry}\"}}}}",
            $"{{\"thumbnailOverlayBadgeStyleRenderer\":{{\"thumbnailOverlayBadgeStyle\":\"{playlistEntry}\"}}}}");

        Assert.True(YouTubeStreamsPageRules.TryExtractLiveVideoIds(html, out var ids));
        Assert.Equal([FirstVideoId], ids);
    }

    [Fact]
    public void LiveBadgeExtraction_ReportsNoLiveWhenThePageHasNoLiveBadge()
    {
        var html = """{"thumbnailOverlayBadgeStyle":"THUMBNAIL_OVERLAY_BADGE_STYLE_PLAYLISTS"}""";

        Assert.False(YouTubeStreamsPageRules.TryExtractLiveVideoIds(html, out var ids));
        Assert.Empty(ids);
    }

    [Theory]
    [InlineData("gfmaurila", "https://www.youtube.com/@gfmaurila/streams")]
    [InlineData("@gfmaurila", "https://www.youtube.com/@gfmaurila/streams")]
    [InlineData("UCjy19AugQHIhyE0Nv558jcQ", "https://www.youtube.com/channel/UCjy19AugQHIhyE0Nv558jcQ/streams")]
    public void StreamsUrl_IsBuiltOnlyFromAHandleOrChannelId(string channel, string expected)
    {
        Assert.True(YouTubeStreamsPageRules.TryBuildStreamsUrl(channel, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("https://evil.example/@gfmaurila")]
    [InlineData("gfmaurila/streams/../../evil")]
    [InlineData("channel with spaces")]
    [InlineData("@gfmaurila?redirect=evil.example")]
    [InlineData("UCwithInvalidCharacter$")]
    public void StreamsUrl_RejectsAnythingThatIsNotAPlainHandleOrChannelId(string channel)
    {
        Assert.False(YouTubeStreamsPageRules.TryBuildStreamsUrl(channel, out _));
    }

    [Fact]
    public async Task DiscoveryResult_ExposesOnlyPublicValues()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Discovery.Candidates.Add(FirstVideoId);
        await harness.ReconcileAsync();

        var response = new ObsLiveBot.Contracts.YouTube.YouTubeLiveDiscoveryResponse(
            harness.Orchestrator.State.Enabled,
            harness.Orchestrator.State.DisabledReason,
            harness.Orchestrator.State.ObsStreaming,
            harness.Orchestrator.State.OwnsCurrentLive,
            harness.Orchestrator.State.Channel,
            harness.Orchestrator.State.CurrentVideoId,
            YouTubeLiveSourceIdentity.PublicUrl(harness.Orchestrator.State.CurrentVideoId!),
            YouTubeLiveSourceIdentity.ChatUrl(harness.Orchestrator.State.CurrentVideoId!),
            harness.Orchestrator.State.CurrentSourceId,
            harness.Orchestrator.State.LastDiscoveryMethod,
            harness.Orchestrator.State.LastReason,
            harness.Orchestrator.State.LastDiscoveryAtUtc,
            harness.Orchestrator.State.LastReconciledAtUtc,
            harness.Orchestrator.State.DiscoveryAttempts,
            harness.Orchestrator.State.SourceEnsures,
            harness.Orchestrator.State.SourceReleases,
            harness.Orchestrator.State.ActiveSourceIds);

        var serialized = JsonSerializer.Serialize(response);
        foreach (var forbidden in new[]
                 {
                     "streamKey", "accessToken", "refreshToken", "password", "cookie", "authorization",
                     "clientSecret", "obsWebSocketPassword", "liveChatUrl"
                 })
            Assert.DoesNotContain(forbidden, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(FirstVideoId, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Orchestrator_LogsContainNoSecrets()
    {
        var logger = new RecordingLogger(output);
        var harness = new Harness(output, logger) { IsStreaming = true };
        harness.Discovery.Candidates.Add(FirstVideoId);

        await harness.ReconcileAsync();

        var lines = logger.Lines;
        Assert.NotEmpty(lines);
        Assert.DoesNotContain(lines, line => line.Contains("sk-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("streamkey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("token=", StringComparison.OrdinalIgnoreCase));
        // The only identifiers logged are the public video ID and the public source ID.
        Assert.All(lines, line => Assert.DoesNotContain("password", line, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ObsEvents_DoNotBlockOrFailOnTheLiveStatePipeline()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Discovery.Candidates.Add(FirstVideoId);
        var notification = new ObsStateChangedNotification(Envelope(
            "ObsStreamStateChanged",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["previous"] = "Offline", ["current"] = "Live" }));

        var handling = harness.Orchestrator.Handle(notification, CancellationToken.None);

        // Publishing the notification must complete without blocking or throwing.
        Assert.True(handling.IsCompletedSuccessfully);
        await handling;
        Assert.Equal(0, harness.Discovery.CallCount);
        await harness.ReconcileAsync();
        Assert.Equal(1, harness.Discovery.CallCount);
    }

    [Fact]
    public async Task CoalescedSignals_CollapseIntoASingleReconcilePerAuthority()
    {
        var harness = new Harness(output) { IsStreaming = true };
        harness.Discovery.Candidates.Add(FirstVideoId);

        // A burst of redundant live signals must collapse: the worker always re-reads the truth,
        // so the second and third signals cannot cause a second discovery or a duplicate source.
        harness.Queue.TryEnqueue(YouTubeLiveSignal.WentLive);
        harness.Queue.TryEnqueue(YouTubeLiveSignal.WentLive);
        harness.Queue.TryEnqueue(YouTubeLiveSignal.Reconcile);

        await harness.ReconcileAsync();
        await harness.ReconcileAsync();

        Assert.Equal(1, harness.Discovery.CallCount);
        Assert.Single(harness.Sources.AddedVideoIds);
        Assert.Single(harness.Sources.Active, id => id.StartsWith("youtube-vid-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThrottledRetries_DoNotHotLoopWhileNoLiveIsPublished()
    {
        var harness = new Harness(output) { IsStreaming = true, MinDiscoveryIntervalSeconds = 3_600 };

        await harness.ReconcileAsync();
        await harness.ReconcileAsync();
        await harness.ReconcileAsync();

        Assert.Equal(1, harness.Discovery.CallCount);
        Assert.Equal("no_live_badge", harness.Orchestrator.State.LastReason);
        Assert.Empty(harness.Sources.AddedVideoIds);
    }

    [Fact]
    public async Task ActiveSourceList_IsRefreshableForDiagnostics()
    {
        var harness = new Harness(output) { IsStreaming = true };

        await harness.RefreshActiveSourcesAsync();

        Assert.Contains("twitch-user-gfmaurila", harness.Orchestrator.State.ActiveSourceIds);
        Assert.Contains("kick-user-gfmaurila", harness.Orchestrator.State.ActiveSourceIds);
    }

    private static string ManualChatUrl(string videoId) =>
        $"https://www.youtube.com/live_chat?is_popout=1&v={videoId}";

    private static ObsEventEnvelope Envelope(
        string eventType,
        IReadOnlyDictionary<string, object?>? payload = null) =>
        new(
            Guid.NewGuid(),
            eventType,
            DateTimeOffset.UtcNow,
            "obs",
            "correlation-1",
            Guid.NewGuid(),
            1,
            payload ?? new Dictionary<string, object?>(StringComparer.Ordinal));

    private sealed class Harness
    {
        public Harness(ITestOutputHelper? output = null, RecordingLogger? logger = null)
        {
            Discovery = new FakeDiscovery();
            Sources = new FakeSourceManager();
            // SSN always holds the other platforms, so isolation assertions have something real
            // to protect: a YouTube discovery problem must never disturb them.
            Sources.SeedActive("twitch-user-gfmaurila", "twitch", "");
            Sources.SeedActive("kick-user-gfmaurila", "kick", "");
            Settings = Options.Create(new YouTubeLiveDiscoveryOptions
            {
                Enabled = true,
                Channel = "gfmaurila"
            });
            Orchestrator = new YouTubeLiveOrchestrator(
                Discovery,
                Sources,
                LiveState,
                Queue,
                Settings,
                TimeProvider.System,
                (ILogger<YouTubeLiveOrchestrator>?)logger ?? NullLogger<YouTubeLiveOrchestrator>.Instance);
            _ = output;
        }

        public YouTubeLiveWorkQueue Queue { get; } = new();
        public FakeObsLiveState LiveState { get; } = new();
        public FakeDiscovery Discovery { get; }
        public FakeSourceManager Sources { get; }
        public IOptions<YouTubeLiveDiscoveryOptions> Settings { get; }
        public YouTubeLiveOrchestrator Orchestrator { get; }

        public bool IsStreaming
        {
            get => LiveState.IsStreaming;
            set => LiveState.IsStreaming = value;
        }

        public bool ThrowOnDiscover
        {
            get => Discovery.ThrowOnDiscover;
            set => Discovery.ThrowOnDiscover = value;
        }

        public bool ThrowOnEnsure
        {
            get => Sources.ThrowOnEnsure;
            set => Sources.ThrowOnEnsure = value;
        }

        public bool Enabled
        {
            get => Settings.Value.Enabled;
            set => Settings.Value.Enabled = value;
        }

        public int MinDiscoveryIntervalSeconds
        {
            get => Settings.Value.MinDiscoveryIntervalSeconds;
            set => Settings.Value.MinDiscoveryIntervalSeconds = value;
        }

        public string? ManualLiveChatUrl
        {
            get => Settings.Value.ManualLiveChatUrl;
            set => Settings.Value.ManualLiveChatUrl = value;
        }

        public Task ReconcileAsync() => Orchestrator.ReconcileAsync(CancellationToken.None);
        public Task RefreshActiveSourcesAsync() => Orchestrator.RefreshActiveSourcesAsync(CancellationToken.None);
    }

    private sealed class FakeObsLiveState : IObsLiveStateReader
    {
        public bool IsStreaming { get; set; }

        public ObsLiveState State => IsStreaming
            ? ObsLiveState.Initial with { StreamState = ObsStreamState.Live }
            : ObsLiveState.Initial;

        public IReadOnlyList<ObsEventEnvelope> GetRecentEvents(int limit) => [];
    }

    private sealed class FakeDiscovery : IYouTubeLiveDiscovery
    {
        /// <summary>Public video IDs the page would report as live, in page order.</summary>
        public List<string> Candidates { get; } = [];

        public int CallCount { get; private set; }
        public bool ThrowOnDiscover { get; set; }

        public Task<YouTubeLiveDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            if (ThrowOnDiscover) throw new InvalidOperationException("discovery unavailable");
            if (Candidates.Count == 0)
                return Task.FromResult(YouTubeLiveDiscoveryResult.NotLive(
                    "gfmaurila", DateTimeOffset.UtcNow, "no_live_badge"));
            return Task.FromResult(YouTubeLiveDiscoveryResult.Live(
                "gfmaurila",
                Candidates[0],
                DateTimeOffset.UtcNow,
                YouTubeLiveDiscoveryMethod.ChannelStreamsPage,
                Candidates.ToArray()));
        }
    }

    private sealed class FakeSourceManager : IYouTubeChatSourceManager
    {
        public List<FakeSource> All { get; } = [];
        public List<string> AddedVideoIds { get; } = [];
        public List<string> Stopped { get; } = [];
        public bool ThrowOnEnsure { get; set; }

        public IEnumerable<string> Active => All.Where(source => source.Active).Select(source => source.Id);

        public void SeedActive(string id, string target, string videoId)
        {
            All.Add(new FakeSource(id, target, videoId) { Active = true });
        }

        public void Import(FakeSourceManager other)
        {
            All.AddRange(other.All.Select(source => new FakeSource(source.Id, source.Target, source.VideoId)
            {
                Active = source.Active
            }));
        }

        public Task<YouTubeChatSourceEnsureResult> EnsureCurrentLiveSourceAsync(
            string videoId,
            CancellationToken cancellationToken)
        {
            if (ThrowOnEnsure) throw new InvalidOperationException("ssn unavailable");
            var canonical = $"youtube-vid-{videoId}";
            var existing = All.SingleOrDefault(source => source.Id == canonical);
            YouTubeChatSourceOutcome outcome;
            if (existing is null)
            {
                existing = new FakeSource(canonical, "youtube", videoId) { Active = true };
                All.Add(existing);
                AddedVideoIds.Add(videoId);
                outcome = YouTubeChatSourceOutcome.Created;
            }
            else
            {
                outcome = existing.Active
                    ? YouTubeChatSourceOutcome.Reused
                    : YouTubeChatSourceOutcome.Started;
                existing.Active = true;
            }
            return Task.FromResult(new YouTubeChatSourceEnsureResult(
                true, canonical, videoId, outcome, 0, null));
        }

        public Task<YouTubeChatSourceReleaseResult> ReleaseCurrentLiveSourceAsync(
            string videoId,
            CancellationToken cancellationToken)
        {
            var canonical = $"youtube-vid-{videoId}";
            var source = All.SingleOrDefault(item => item.Id == canonical);
            if (source is null)
                return Task.FromResult(new YouTubeChatSourceReleaseResult(true, canonical, videoId, false, "source_absent"));
            var stopped = source.Active;
            source.Active = false;
            if (stopped) Stopped.Add(canonical);
            return Task.FromResult(new YouTubeChatSourceReleaseResult(true, canonical, videoId, stopped, null));
        }

        public Task<IReadOnlyList<string>> GetActiveSourceIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Active.Order(StringComparer.Ordinal).ToArray());
    }

    private sealed class FakeSource(string id, string target, string videoId)
    {
        public string Id { get; } = id;
        public string Target { get; } = target;
        public string VideoId { get; } = videoId;
        public bool Active { get; set; }
    }

    private sealed class RecordingLogger(ITestOutputHelper? output = null) : ILogger<YouTubeLiveOrchestrator>
    {
        private readonly List<string> _lines = [];
        private ITestOutputHelper? Output { get; } = output;
        public IReadOnlyList<string> Lines
        {
            get { lock (_lines) return _lines.ToArray(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var line = formatter(state, exception);
            lock (_lines) _lines.Add(line);
            Output?.WriteLine(line);
        }
    }
}
