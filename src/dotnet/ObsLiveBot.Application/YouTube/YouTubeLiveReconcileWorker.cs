using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ObsLiveBot.Application.YouTube;

/// <summary>
/// Runs discovery and the SSN source lifecycle off the OBS event pipeline.
///
/// The worker wakes on two things: a coalesced live-state signal, and a periodic tick. The tick
/// matters because OBS can stay streaming across a YouTube live change, so a steady OBS state would
/// otherwise never be re-checked. Reconciliation is idempotent and internally throttled, so a wake-up
/// that has nothing to do costs almost nothing. Failures never stop the worker.
/// </summary>
public sealed class YouTubeLiveReconcileWorker(
    YouTubeLiveWorkQueue queue,
    YouTubeLiveOrchestrator orchestrator,
    IOptions<YouTubeLiveDiscoveryOptions> options,
    ILogger<YouTubeLiveReconcileWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup reconcile: covers a StudioOS restart while OBS is already streaming.
        await SafeReconcileAsync(stoppingToken).ConfigureAwait(false);

        var tick = TimeSpan.FromSeconds(Math.Clamp(options.Value.MinDiscoveryIntervalSeconds, 5, 3_600));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wake on the next signal, or on the tick when OBS stays quiet.
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wake.CancelAfter(tick);
                var signaled = await queue.Reader.WaitToReadAsync(wake.Token).ConfigureAwait(false);
                if (signaled && queue.Reader.TryRead(out var signal))
                    logger.LogDebug("YOUTUBE_LIVE_SIGNAL signal={Signal}", signal);
            }
            catch (OperationCanceledException)
            {
                // Either the tick elapsed or the host is shutting down.
            }

            if (stoppingToken.IsCancellationRequested) break;
            await SafeReconcileAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SafeReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.ReconcileAsync(cancellationToken).ConfigureAwait(false);
            // Refresh the observed active source list so the read-only endpoint reflects SSN
            // reality after every reconciliation, including one that changed nothing.
            await orchestrator.RefreshActiveSourcesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
        catch (Exception exception)
        {
            logger.LogWarning("YOUTUBE_LIVE_WORKER_ERROR errorType={ErrorType}", exception.GetType().Name);
        }
    }
}
