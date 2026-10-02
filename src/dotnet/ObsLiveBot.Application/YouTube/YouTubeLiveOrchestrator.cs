using System.Threading.Channels;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Application.YouTube;

public enum YouTubeLiveSignal
{
    /// <summary>OBS went from not-streaming to streaming.</summary>
    WentLive,

    /// <summary>OBS went from streaming to not-streaming.</summary>
    WentOffline,

    /// <summary>Reconnect, state resynchronization or process start: reconcile against the truth.</summary>
    Reconcile
}

/// <summary>
/// Coalesces OBS live-state signals. Capacity 1 with drop-write means redundant signals never
/// queue up; the worker re-reads the authoritative OBS state instead of trusting a queued signal.
/// </summary>
public sealed class YouTubeLiveWorkQueue
{
    private readonly Channel<YouTubeLiveSignal> _channel = Channel.CreateBounded<YouTubeLiveSignal>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public bool TryEnqueue(YouTubeLiveSignal signal) => _channel.Writer.TryWrite(signal);

    public ChannelReader<YouTubeLiveSignal> Reader => _channel.Reader;
}

/// <summary>
/// Binds the OBS live-state trigger to YouTube live discovery and the SSN source lifecycle.
///
/// The reconciler is the only decision point and it always works from the current OBS state, which
/// makes every entry point idempotent: false-&gt;true triggers discovery, true-&gt;true does not
/// duplicate it, true-&gt;false releases ownership, and OBS reconnect / StudioOS restart /
/// SSN restart all converge to the same result without recreating a working source.
/// </summary>
public sealed class YouTubeLiveOrchestrator(
    IYouTubeLiveDiscovery discovery,
    IYouTubeChatSourceManager sourceManager,
    IObsLiveStateReader obsLiveState,
    YouTubeLiveWorkQueue workQueue,
    IOptions<YouTubeLiveDiscoveryOptions> options,
    TimeProvider timeProvider,
    ILogger<YouTubeLiveOrchestrator> logger) : INotificationHandler<ObsStateChangedNotification>, IYouTubeLiveOwnershipReader
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private YouTubeLiveRuntimeState _state = YouTubeLiveRuntimeState.Initial;
    private DateTimeOffset _nextDiscoveryAtUtc = DateTimeOffset.MinValue;

    public YouTubeLiveOwnershipSnapshot State
    {
        get
        {
            var state = _state;
            var configured = ResolveOptions();
            return new YouTubeLiveOwnershipSnapshot(
                configured.Enabled,
                configured.DisabledReason,
                obsLiveState.State.IsStreaming,
                state.OwnsCurrentLive,
                state.CurrentVideoId,
                state.CurrentSourceId,
                configured.Channel,
                state.LastDiscoveryMethod,
                state.LastReason,
                state.LastDiscoveryAtUtc,
                state.LastReconciledAtUtc,
                state.DiscoveryAttempts,
                state.SourceEnsures,
                state.SourceReleases,
                state.ActiveSourceIds);
        }
    }

    public Task Handle(ObsStateChangedNotification notification, CancellationToken cancellationToken)
    {
        // Never throw into the OBS event pipeline and never block it on network or SSN work.
        var signal = Classify(notification.Envelope);
        if (signal is null) return Task.CompletedTask;
        workQueue.TryEnqueue(signal.Value);
        return Task.CompletedTask;
    }

    /// <summary>Kicks a reconciliation, used at startup so a restart while already live converges.</summary>
    public void RequestReconcile() => workQueue.TryEnqueue(YouTubeLiveSignal.Reconcile);

    /// <summary>
    /// Maps an OBS envelope to a reconciliation signal without any network or SSN access:
    /// false-&gt;true means go live, true-&gt;false means release, everything else that matters is a
    /// reconcile, and a steady state is ignored.
    /// </summary>
    public static YouTubeLiveSignal? Classify(ObsEventEnvelope envelope)
    {
        if (envelope.EventType is "ObsStreamStateChanged")
        {
            var previousStreaming = ReadIsStreaming(envelope.Payload, "previous");
            var currentStreaming = ReadIsStreaming(envelope.Payload, "current");
            if (previousStreaming is null || currentStreaming is null) return YouTubeLiveSignal.Reconcile;
            if (!previousStreaming.Value && currentStreaming.Value) return YouTubeLiveSignal.WentLive;
            if (previousStreaming.Value && !currentStreaming.Value) return YouTubeLiveSignal.WentOffline;
            return null;
        }

        // Reconnect resync and process start both need an idempotent reconcile.
        if (envelope.EventType is "ObsStateSynchronized" or "ObsConnectionEstablished")
            return YouTubeLiveSignal.Reconcile;

        return null;
    }

    private static bool? ReadIsStreaming(IReadOnlyDictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value) || value is null) return null;
        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Enum.TryParse<ObsStreamState>(text, ignoreCase: false, out var state)
            ? state is ObsStreamState.Starting or ObsStreamState.Live or ObsStreamState.Stopping
            : null;
    }

    /// <summary>
    /// Brings ownership in line with reality. Safe to call any number of times and from any trigger.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var configured = ResolveOptions();
        var streaming = obsLiveState.State.IsStreaming;
        if (!configured.Enabled)
        {
            Update(state => state with
            {
                ObsStreaming = streaming,
                LastReconciledAtUtc = timeProvider.GetUtcNow()
            });
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!streaming)
            {
                await ReleaseAsync(configured, cancellationToken).ConfigureAwait(false);
                return;
            }

            await AcquireAsync(configured, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Discovery must never affect the OBS connection, chat ingestion or the API process.
            Update(state => state with
            {
                LastReason = "reconcile_failed",
                LastReconciledAtUtc = timeProvider.GetUtcNow()
            });
            logger.LogWarning(
                "YOUTUBE_LIVE_RECONCILE_FAILED errorType={ErrorType}",
                exception.GetType().Name);
        }
        finally { _gate.Release(); }
    }

    private async Task AcquireAsync(YouTubeLiveDiscoveryConfiguration configured, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var state = _state;
        var ownedVideoId = state.OwnsCurrentLive ? state.CurrentVideoId : null;

        if (now < _nextDiscoveryAtUtc)
        {
            // Either we already own a healthy live, or a recent attempt failed and must back off.
            Update(current => current with { ObsStreaming = true, LastReconciledAtUtc = now });
            logger.LogInformation(
                "YOUTUBE_LIVE_DISCOVERY_THROTTLED reason={Reason}",
                ownedVideoId is null ? "retry_backoff" : "heartbeat_interval");
            return;
        }

        _nextDiscoveryAtUtc = now.AddSeconds(Math.Max(0, configured.MinDiscoveryIntervalSeconds));
        Update(current => current with
        {
            ObsStreaming = true,
            DiscoveryAttempts = current.DiscoveryAttempts + 1
        });

        var result = await discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsLive || result.VideoId is null)
        {
            Update(current => current with
            {
                LastDiscoveryMethod = result.DiscoveryMethod.ToString(),
                LastReason = result.Reason ?? "not_live",
                LastDiscoveryAtUtc = result.DiscoveredAtUtc,
                LastReconciledAtUtc = now
            });
            logger.LogInformation(
                "YOUTUBE_LIVE_NOT_FOUND channel={Channel} method={Method} reason={Reason}",
                configured.Channel,
                result.DiscoveryMethod,
                result.Reason ?? "not_live");
            return;
        }

        // A channel can briefly expose more than one live badge. The live already validated wins
        // whenever it is still live, which avoids flapping between two simultaneous lives.
        var stillLive = ownedVideoId is not null &&
                        result.LiveVideoIds.Contains(ownedVideoId, StringComparer.Ordinal);
        var videoId = stillLive ? ownedVideoId! : result.VideoId;

        if (stillLive)
        {
            // The owned live is still the current one: keep the existing source, never duplicate it.
            Update(current => current with
            {
                ObsStreaming = true,
                LastDiscoveryMethod = result.DiscoveryMethod.ToString(),
                LastReason = null,
                LastDiscoveryAtUtc = result.DiscoveredAtUtc,
                LastReconciledAtUtc = now
            });
            logger.LogInformation(
                "YOUTUBE_LIVE_ALREADY_OWNED videoId={VideoId} sourceId={SourceId} liveCandidates={LiveCandidates}",
                videoId,
                state.CurrentSourceId,
                result.LiveVideoIds.Count);
            return;
        }

        // The owned live is gone (or there was none): the previous live must never be reused.
        if (ownedVideoId is not null)
        {
            var previous = await sourceManager
                .ReleaseCurrentLiveSourceAsync(ownedVideoId, cancellationToken)
                .ConfigureAwait(false);
            Update(current => current with
            {
                OwnsCurrentLive = false,
                CurrentVideoId = null,
                CurrentSourceId = null,
                SourceReleases = current.SourceReleases + (previous.Stopped ? 1 : 0)
            });
            logger.LogInformation(
                "YOUTUBE_LIVE_SUPERSEDED previousVideoId={PreviousVideoId} stopped={Stopped}",
                ownedVideoId,
                previous.Stopped);
        }

        var ensure = await sourceManager
            .EnsureCurrentLiveSourceAsync(videoId, cancellationToken)
            .ConfigureAwait(false);

        if (!ensure.Success)
        {
            Update(current => current with
            {
                LastDiscoveryMethod = result.DiscoveryMethod.ToString(),
                LastReason = ensure.Reason ?? "source_ensure_failed",
                LastDiscoveryAtUtc = result.DiscoveredAtUtc,
                LastReconciledAtUtc = now
            });
            logger.LogWarning(
                "YOUTUBE_LIVE_SOURCE_ENSURE_FAILED videoId={VideoId} reason={Reason}",
                videoId,
                ensure.Reason ?? "source_ensure_failed");
            return;
        }

        Update(current => current with
        {
            OwnsCurrentLive = true,
            CurrentVideoId = videoId,
            CurrentSourceId = ensure.SourceId,
            LastDiscoveryMethod = result.DiscoveryMethod.ToString(),
            LastReason = null,
            LastDiscoveryAtUtc = result.DiscoveredAtUtc,
            LastReconciledAtUtc = now,
            SourceEnsures = current.SourceEnsures + 1
        });
        logger.LogInformation(
            "YOUTUBE_LIVE_ATTACHED videoId={VideoId} sourceId={SourceId} method={Method} outcome={Outcome} retiredDuplicates={RetiredDuplicates}",
            videoId,
            ensure.SourceId,
            result.DiscoveryMethod,
            ensure.Outcome,
            ensure.RetiredDuplicateCount);
    }

    private async Task ReleaseAsync(YouTubeLiveDiscoveryConfiguration configured, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var state = _state;
        if (!state.OwnsCurrentLive || state.CurrentVideoId is null)
        {
            Update(current => current with { ObsStreaming = false, LastReconciledAtUtc = now });
            return;
        }

        var released = configured.AutoReleaseOnEnd
            ? await sourceManager
                .ReleaseCurrentLiveSourceAsync(state.CurrentVideoId, cancellationToken)
                .ConfigureAwait(false)
            : new YouTubeChatSourceReleaseResult(true, state.CurrentSourceId, state.CurrentVideoId, false, "auto_release_disabled");

        Update(current => current with
        {
            // Ownership is always cleared, so a finished live can never be reused for the next one.
            OwnsCurrentLive = false,
            CurrentVideoId = null,
            CurrentSourceId = null,
            ObsStreaming = false,
            LastReason = released.Success ? null : released.Reason ?? "release_failed",
            LastReconciledAtUtc = now,
            SourceReleases = current.SourceReleases + (released.Stopped ? 1 : 0)
        });
        // The next going-live must be discovered immediately, not after a stale backoff.
        _nextDiscoveryAtUtc = DateTimeOffset.MinValue;
        logger.LogInformation(
            "YOUTUBE_LIVE_RELEASED videoId={VideoId} sourceId={SourceId} stopped={Stopped} reason={Reason}",
            state.CurrentVideoId,
            state.CurrentSourceId,
            released.Stopped,
            released.Reason ?? "released");
    }

    /// <summary>Refreshes the observed active source list for diagnostics without changing ownership.</summary>
    public async Task RefreshActiveSourcesAsync(CancellationToken cancellationToken)
    {
        if (!ResolveOptions().Enabled) return;
        try
        {
            var active = await sourceManager.GetActiveSourceIdsAsync(cancellationToken).ConfigureAwait(false);
            Update(state => state with { ActiveSourceIds = active });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("YOUTUBE_LIVE_ACTIVE_SOURCES_UNAVAILABLE errorType={ErrorType}", exception.GetType().Name);
        }
    }

    /// <summary>
    /// Resolves effective configuration. An explicit per-live manual URL takes precedence over
    /// automatic discovery, so a manually managed live is never overridden by a discovered one.
    /// </summary>
    private YouTubeLiveDiscoveryConfiguration ResolveOptions()
    {
        var value = options.Value;
        var manualOverride = !string.IsNullOrWhiteSpace(value.ManualLiveChatUrl);
        string? disabledReason = null;
        if (!value.Enabled) disabledReason = "disabled_by_configuration";
        else if (manualOverride) disabledReason = "manual_live_chat_url_override";

        return new(
            disabledReason is null,
            disabledReason,
            string.IsNullOrWhiteSpace(value.Channel) ? null : value.Channel,
            value.ManualLiveChatUrl,
            Math.Clamp(value.MinDiscoveryIntervalSeconds, 0, 3_600),
            value.AutoReleaseOnEnd);
    }

    private void Update(Func<YouTubeLiveRuntimeState, YouTubeLiveRuntimeState> mutate)
    {
        lock (_stateLock) _state = mutate(_state);
    }

    private sealed record YouTubeLiveDiscoveryConfiguration(
        bool Enabled,
        string? DisabledReason,
        string? Channel,
        string? ManualLiveChatUrl,
        int MinDiscoveryIntervalSeconds,
        bool AutoReleaseOnEnd);

    private sealed record YouTubeLiveRuntimeState
    {
        public bool OwnsCurrentLive { get; init; }
        public bool ObsStreaming { get; init; }
        public string? CurrentVideoId { get; init; }
        public string? CurrentSourceId { get; init; }
        public string? LastDiscoveryMethod { get; init; }
        public string? LastReason { get; init; }
        public DateTimeOffset? LastDiscoveryAtUtc { get; init; }
        public DateTimeOffset? LastReconciledAtUtc { get; init; }
        public int DiscoveryAttempts { get; init; }
        public int SourceEnsures { get; init; }
        public int SourceReleases { get; init; }
        public IReadOnlyList<string> ActiveSourceIds { get; init; } = [];

        public static YouTubeLiveRuntimeState Initial { get; } = new();
    }
}
