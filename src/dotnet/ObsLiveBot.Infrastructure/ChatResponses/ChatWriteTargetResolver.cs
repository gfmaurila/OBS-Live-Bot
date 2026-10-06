using Microsoft.Extensions.Logging;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>
/// One cached view of the sources the write transport can type into, shared by capability reporting and by
/// send resolution.
/// <para>
/// Both need the same answer, and asking the transport on every capability request would make a read-only
/// status endpoint as expensive as - and as failure-prone as - a send. A short-lived cache keeps the
/// capability API cheap and keeps a slow transport from turning into a slow API. The window is short
/// because it is a liveness signal: a source that stopped seconds ago should stop reading as ready
/// seconds later, not minutes later.
/// </para>
/// <para>
/// It asks the write transport through <see cref="IChatWriteSourceLister"/> and never the capture-side
/// source client. Reading a chat and writing into one are separate capabilities here, and the write path
/// must not be able to fail just because capture is failing.
/// </para>
/// <para>
/// A failed probe never fabricates a ready state. It reports the error code and reuses the last good view
/// so a transient transport blip does not make capability reporting flap, while a cold start reports
/// nothing available rather than guessing.
/// </para>
/// </summary>
public sealed class ChatWriteTargetResolver(
    IChatWriteSourceLister sourceClient,
    TimeProvider timeProvider,
    ILogger<ChatWriteTargetResolver> logger)
{
    private static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private IReadOnlyList<ChatWriteSource> _sources = [];
    private DateTimeOffset _loadedAtUtc = DateTimeOffset.MinValue;
    private bool _loaded;
    private string? _lastErrorCode;

    /// <summary>
    /// The cached view, refreshing it when the cache window has expired. Never throws: a transport failure
    /// becomes an empty or stale view plus an error code.
    /// </summary>
    public async Task<IReadOnlyList<ChatWriteSource>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_loaded && timeProvider.GetUtcNow() - _loadedAtUtc < CacheWindow)
                return _sources;
        }

        try
        {
            var sources = await sourceClient.ListSourcesAsync(cancellationToken).ConfigureAwait(false);
            var mapped = sources.ToArray();
            lock (_gate)
            {
                _sources = mapped;
                _loaded = true;
                _loadedAtUtc = timeProvider.GetUtcNow();
                _lastErrorCode = null;
            }

            return mapped;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read Social Stream Ninja sources for write capability");
            lock (_gate)
            {
                _loaded = true;
                _loadedAtUtc = timeProvider.GetUtcNow();
                _lastErrorCode = "CHAT_SOURCE_LIST_UNAVAILABLE";
            }

            // Whatever was last observed stays the answer. An empty view on a cold start is the honest
            // result: nothing has been confirmed, so nothing is reported as ready.
            return _sources;
        }
    }

    /// <summary>What the write transport currently offers for one platform, via its own adapter.</summary>
    public async Task<ChatWriteProbe> ProbeAsync(
        IChatWriteAdapter adapter,
        string channelId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var sources = await GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        // Resolution is delegated to the adapter rather than repeated here, so what capability reporting
        // calls "ready" and what a send would actually type into are decided by the same rules.
        var target = adapter.ResolveTarget(sources, channelId);

        lock (_gate)
        {
            var onPlatform = sources.Where(source => adapter.ResolveTarget([source], channelId) is not null).ToArray();
            return new ChatWriteProbe(
                adapter.Provider,
                onPlatform.Length > 0,
                onPlatform.Any(source => source.Active),
                target?.SourceId,
                target?.ChannelId,
                target?.ExactChannelMatch ?? false,
                _lastErrorCode,
                _loadedAtUtc);
        }
    }
}