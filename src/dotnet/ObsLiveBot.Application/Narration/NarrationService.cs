using System.Threading.Channels;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Application.Narration;

public sealed class NarrationService : BackgroundService, INarrationService
{
    private readonly NarrationOptions _options;
    private readonly IAudioPlaybackService _playback;
    private readonly INarrationArtifactValidator _artifactValidator;
    private readonly IAudioArtifactLeaseRegistry _leases;
    private readonly INarrationEventPublisher _eventPublisher;
    private readonly IMediator _mediator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NarrationService> _logger;
    private readonly Channel<QueuedNarration> _queue;
    private readonly object _gate = new();
    private readonly List<NarrationEvent> _events = [];
    private readonly List<NarrationResult> _recent = [];
    private NarrationPlaybackSnapshot _playbackSnapshot = new(
        false, false, null, null, null, "MonitorOff", [],
        ObsMediaPlaybackState.Unknown, "Degraded", "OBS_NOT_READY");
    private NarrationResult? _current;
    private int _queueLength;
    private long _sequence;
    private long _queued;
    private long _started;
    private long _completed;
    private long _failed;
    private long _cancelled;
    private long _queueRejected;
    private long _playbackTicks;
    private DateTimeOffset? _lastPlaybackAtUtc;
    private DateTimeOffset? _lastFailureAtUtc;
    private double _volume;
    private bool _muted;

    public NarrationService(
        IOptions<NarrationOptions> options,
        IAudioPlaybackService playback,
        INarrationArtifactValidator artifactValidator,
        IAudioArtifactLeaseRegistry leases,
        INarrationEventPublisher eventPublisher,
        IMediator mediator,
        TimeProvider timeProvider,
        ILogger<NarrationService> logger)
    {
        _options = options.Value;
        _playback = playback;
        _artifactValidator = artifactValidator;
        _leases = leases;
        _eventPublisher = eventPublisher;
        _mediator = mediator;
        _timeProvider = timeProvider;
        _logger = logger;
        _volume = _options.DefaultVolume;
        _queue = Channel.CreateBounded<QueuedNarration>(new BoundedChannelOptions(_options.MaxQueueSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public NarrationStateSnapshot GetState()
    {
        lock (_gate)
        {
            var status = !_options.Enabled
                ? NarrationServiceStatus.Disabled
                : _current is not null
                    ? NarrationServiceStatus.Playing
                    : !_playbackSnapshot.Available || !_playbackSnapshot.SourceExists
                        ? NarrationServiceStatus.Degraded
                        : NarrationServiceStatus.Ready;
            var completed = Interlocked.Read(ref _completed);
            double? average = completed == 0
                ? null
                : TimeSpan.FromTicks(Interlocked.Read(ref _playbackTicks) / completed).TotalMilliseconds;
            return new NarrationStateSnapshot(
                _options.Enabled,
                _options.AutoPlayInteractions,
                status.ToString(),
                _current?.NarrationId,
                _queueLength,
                _options.MaxQueueSize,
                1,
                _options.SourceName,
                _volume,
                _muted,
                _playbackSnapshot.MonitoringMode,
                _playbackSnapshot.Tracks.Count == 0 ? [1] : _playbackSnapshot.Tracks,
                Interlocked.Read(ref _queued),
                Interlocked.Read(ref _started),
                completed,
                Interlocked.Read(ref _failed),
                Interlocked.Read(ref _cancelled),
                Interlocked.Read(ref _queueRejected),
                average,
                _lastPlaybackAtUtc,
                _lastFailureAtUtc);
        }
    }

    public IReadOnlyList<NarrationEvent> GetRecentEvents(int limit)
    {
        lock (_gate) return _events.TakeLast(Math.Clamp(limit, 1, _options.EventBufferCapacity)).ToArray();
    }

    public IReadOnlyList<NarrationResult> GetRecentResults(int limit)
    {
        lock (_gate) return _recent.TakeLast(Math.Clamp(limit, 1, _options.EventBufferCapacity)).Reverse().ToArray();
    }

    public async Task<NarrationEnqueueResult> EnqueueAsync(
        NarrationAudioArtifact artifact,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        var id = Guid.NewGuid();
        var validation = _artifactValidator.Validate(artifact, _options.AllowedRuntimeDirectory);
        if (!validation.Valid)
        {
            Interlocked.Increment(ref _failed);
            _lastFailureAtUtc = now;
            return Reject(id, artifact.InteractionId, correlationId, validation.ErrorCode ?? "NARRATION_AUDIO_INVALID", now);
        }

        if (validation.Duration <= TimeSpan.Zero || validation.Duration.TotalSeconds > _options.MaxNarrationSeconds)
        {
            return Reject(id, artifact.InteractionId, correlationId, "NARRATION_TOO_LONG", now, NarrationStatus.Skipped);
        }

        var verifiedArtifact = artifact with
        {
            Duration = validation.Duration,
            SampleRate = validation.SampleRate,
            BitDepth = validation.BitDepth,
            Channels = validation.Channels
        };
        var request = new NarrationRequest(id, artifact.InteractionId, verifiedArtifact, now, 0,
            string.IsNullOrWhiteSpace(correlationId) ? id.ToString("N") : correlationId);
        var lease = _leases.Acquire(verifiedArtifact.Path);
        var result = new NarrationResult(id, artifact.InteractionId, NarrationStatus.Queued, null,
            now, now, 0, request.CorrelationId);

        lock (_gate)
        {
            if (!_options.Enabled)
            {
                lease.Dispose();
                return RejectLocked(id, artifact.InteractionId, request.CorrelationId, "NARRATION_DISABLED", now);
            }

            if (!_queue.Writer.TryWrite(new QueuedNarration(request, lease)))
            {
                lease.Dispose();
                Interlocked.Increment(ref _queueRejected);
                return RejectLocked(id, artifact.InteractionId, request.CorrelationId, "NARRATION_QUEUE_FULL", now);
            }

            _queueLength++;
            result = AddResultLocked(result);
            Interlocked.Increment(ref _queued);
        }

        await PublishTransitionAsync(result, "NarrationQueued", cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "NARRATION_QUEUED narrationId={NarrationId} interactionId={InteractionId} queueLength={QueueLength} correlationId={CorrelationId}",
            result.NarrationId, result.InteractionId, GetState().QueueLength, result.CorrelationId);
        return new NarrationEnqueueResult(true, result, null);
    }

    public async Task SetMutedAsync(bool muted, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await _playback.EnsureSourceAsync(_options.SourceName, _options.AllowedRuntimeDirectory, cancellationToken)
            .ConfigureAwait(false);
        await _playback.ConfigureAsync(_options.SourceName, GetState().Volume, muted, cancellationToken)
            .ConfigureAwait(false);
        lock (_gate)
        {
            _muted = muted;
            _playbackSnapshot = _playbackSnapshot with { Muted = muted };
        }
    }

    public async Task<NarrationStateSnapshot> SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (!double.IsFinite(volume) || volume < _options.MinimumVolume || volume > _options.MaximumVolume)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume is outside the configured safe range.");
        await _playback.EnsureSourceAsync(_options.SourceName, _options.AllowedRuntimeDirectory, cancellationToken)
            .ConfigureAwait(false);
        await _playback.ConfigureAsync(_options.SourceName, volume, GetState().Muted, cancellationToken)
            .ConfigureAwait(false);
        lock (_gate)
        {
            _volume = volume;
            _playbackSnapshot = _playbackSnapshot with { Volume = volume };
        }
        return GetState();
    }

    public async Task<NarrationPlaybackSnapshot> RefreshPlaybackStateAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _playback.GetStateAsync(_options.SourceName, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _playbackSnapshot = snapshot;
            if (snapshot.Volume is { } volume) _volume = volume;
            if (snapshot.Muted is { } muted) _muted = muted;
        }
        return snapshot;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ClearStalePlaybackSafelyAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            await foreach (var queued in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                lock (_gate) _queueLength = Math.Max(0, _queueLength - 1);
                await PlayOneAsync(queued, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            while (_queue.Reader.TryRead(out var remaining))
            {
                remaining.Lease.Dispose();
                await TransitionAsync(remaining.Request, NarrationStatus.Cancelled, "NARRATION_SHUTDOWN", null,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task PlayOneAsync(QueuedNarration queued, CancellationToken stoppingToken)
    {
        var request = queued.Request;
        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            var check = _artifactValidator.Validate(request.AudioArtifact, _options.AllowedRuntimeDirectory);
            if (!check.Valid) throw new NarrationPlaybackException(check.ErrorCode ?? "NARRATION_AUDIO_INVALID");
            if (check.Duration.TotalSeconds > _options.MaxNarrationSeconds)
                throw new NarrationPlaybackException("NARRATION_TOO_LONG");

            await TransitionAsync(request, NarrationStatus.Preparing, null, null, stoppingToken).ConfigureAwait(false);
            await _playback.EnsureSourceAsync(_options.SourceName, _options.AllowedRuntimeDirectory, stoppingToken)
                .ConfigureAwait(false);
            await _playback.ConfigureAsync(_options.SourceName, GetState().Volume, GetState().Muted, stoppingToken)
                .ConfigureAwait(false);
            await _playback.PlayAsync(_options.SourceName, request.AudioArtifact, stoppingToken).ConfigureAwait(false);
            lock (_gate) _current = AddResultLocked(new NarrationResult(request.NarrationId,
                request.InteractionId, NarrationStatus.Playing, null, request.CreatedAtUtc,
                _timeProvider.GetUtcNow(), 0, request.CorrelationId));
            Interlocked.Increment(ref _started);
            await PublishTransitionAsync(_current!, "NarrationStarted", stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "NARRATION_STARTED narrationId={NarrationId} interactionId={InteractionId} source={Source} correlationId={CorrelationId}",
                request.NarrationId, request.InteractionId, _options.SourceName, request.CorrelationId);

            var playbackState = await _playback.WaitForCompletionAsync(
                _options.SourceName,
                check.Duration,
                TimeSpan.FromSeconds(_options.PlaybackTimeoutSeconds),
                stoppingToken).ConfigureAwait(false);
            if (playbackState != ObsMediaPlaybackState.Ended)
                throw new NarrationPlaybackException("NARRATION_PLAYBACK_NOT_COMPLETED");

            var duration = _timeProvider.GetUtcNow() - startedAt;
            var completed = new NarrationResult(request.NarrationId, request.InteractionId,
                NarrationStatus.Completed, null, request.CreatedAtUtc, _timeProvider.GetUtcNow(),
                0, request.CorrelationId, duration);
            lock (_gate)
            {
                _current = null;
                _lastPlaybackAtUtc = _timeProvider.GetUtcNow();
                Interlocked.Increment(ref _completed);
                Interlocked.Add(ref _playbackTicks, duration.Ticks);
                completed = AddResultLocked(completed);
            }
            await PublishTransitionAsync(completed, "NarrationCompleted", stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "NARRATION_COMPLETED narrationId={NarrationId} durationMilliseconds={DurationMilliseconds} correlationId={CorrelationId}",
                request.NarrationId, duration.TotalMilliseconds, request.CorrelationId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await TransitionAsync(request, NarrationStatus.Cancelled, "NARRATION_CANCELLED", null,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var errorCode = exception switch
            {
                NarrationPlaybackException narrationException => narrationException.ErrorCode,
                InvalidOperationException when exception.Message.StartsWith("NARRATION_", StringComparison.Ordinal)
                    => exception.Message,
                InvalidOperationException when exception.Message == "OBS WebSocket is not connected."
                    => "OBS_UNAVAILABLE",
                IOException => "OBS_UNAVAILABLE",
                TimeoutException => "NARRATION_PLAYBACK_TIMEOUT",
                _ => "NARRATION_PLAYBACK_FAILED"
            };
            _logger.LogWarning(
                "NARRATION_FAILED narrationId={NarrationId} errorCode={ErrorCode} errorType={ErrorType} correlationId={CorrelationId}",
                request.NarrationId, errorCode, exception.GetType().Name, request.CorrelationId);
            await TransitionAsync(request, NarrationStatus.Failed, errorCode, null,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _playback.StopAndClearAsync(_options.SourceName, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "NARRATION_CLEAR_FAILED source={Source} errorType={ErrorType}",
                    _options.SourceName, exception.GetType().Name);
            }
            queued.Lease.Dispose();
            lock (_gate) _current = null;
        }
    }

    private async Task ClearStalePlaybackSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await RefreshPlaybackStateAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.SourceExists)
                await _playback.StopAndClearAsync(_options.SourceName, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("NARRATION_STARTUP_CHECK_FAILED errorType={ErrorType}", exception.GetType().Name);
        }
    }

    private async Task TransitionAsync(
        NarrationRequest request,
        NarrationStatus status,
        string? errorCode,
        TimeSpan? playbackDuration,
        CancellationToken cancellationToken)
    {
        NarrationResult result;
        lock (_gate)
        {
            if (status is NarrationStatus.Failed or NarrationStatus.Cancelled or NarrationStatus.Skipped)
            {
                _current = null;
                if (status == NarrationStatus.Failed)
                {
                    Interlocked.Increment(ref _failed);
                    _lastFailureAtUtc = _timeProvider.GetUtcNow();
                }
                else if (status == NarrationStatus.Cancelled)
                {
                    Interlocked.Increment(ref _cancelled);
                }
            }
            result = AddResultLocked(new NarrationResult(request.NarrationId, request.InteractionId,
                status, errorCode, request.CreatedAtUtc, _timeProvider.GetUtcNow(), 0,
                request.CorrelationId, playbackDuration));
        }

        var eventName = status switch
        {
            NarrationStatus.Queued => "NarrationQueued",
            NarrationStatus.Preparing => "NarrationPreparing",
            NarrationStatus.Playing => "NarrationStarted",
            NarrationStatus.Completed => "NarrationCompleted",
            NarrationStatus.Skipped => "NarrationSkipped",
            NarrationStatus.Cancelled => "NarrationCancelled",
            _ => "NarrationFailed"
        };
        await PublishTransitionAsync(result, eventName, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishTransitionAsync(
        NarrationResult result,
        string eventType,
        CancellationToken cancellationToken)
    {
        await PublishEventSafelyAsync(result, eventType, cancellationToken).ConfigureAwait(false);
        try
        {
            switch (result.Status)
            {
                case NarrationStatus.Queued:
                    await _mediator.Publish(new NarrationQueuedNotification(result), cancellationToken).ConfigureAwait(false);
                    break;
                case NarrationStatus.Playing:
                    await _mediator.Publish(new NarrationStartedNotification(result), cancellationToken).ConfigureAwait(false);
                    break;
                case NarrationStatus.Completed:
                    await _mediator.Publish(new NarrationCompletedNotification(result), cancellationToken).ConfigureAwait(false);
                    break;
                case NarrationStatus.Cancelled:
                    await _mediator.Publish(new NarrationCancelledNotification(result), cancellationToken).ConfigureAwait(false);
                    break;
                case NarrationStatus.Failed or NarrationStatus.Skipped:
                    await _mediator.Publish(new NarrationFailedNotification(result), cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("NARRATION_NOTIFICATION_FAILED narrationId={NarrationId} errorType={ErrorType}",
                result.NarrationId, exception.GetType().Name);
        }
    }

    private async Task PublishEventSafelyAsync(
        NarrationResult result,
        string eventType,
        CancellationToken cancellationToken)
    {
        NarrationEvent narrationEvent;
        lock (_gate)
        {
            narrationEvent = new NarrationEvent(Guid.NewGuid(), result.NarrationId, result.InteractionId,
                eventType, result.ErrorCode, _timeProvider.GetUtcNow(), ++_sequence, result.CorrelationId);
            _events.Add(narrationEvent);
            if (_events.Count > _options.EventBufferCapacity) _events.RemoveAt(0);
        }

        try
        {
            await _eventPublisher.PublishAsync(narrationEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("NARRATION_EVENT_PUBLISH_FAILED eventType={EventType} errorType={ErrorType}",
                eventType, exception.GetType().Name);
        }
    }

    private NarrationEnqueueResult Reject(
        Guid id,
        Guid interactionId,
        string correlationId,
        string errorCode,
        DateTimeOffset now,
        NarrationStatus status = NarrationStatus.Failed)
    {
        lock (_gate) return RejectLocked(id, interactionId, correlationId, errorCode, now, status);
    }

    private NarrationEnqueueResult RejectLocked(
        Guid id,
        Guid interactionId,
        string correlationId,
        string errorCode,
        DateTimeOffset now,
        NarrationStatus status = NarrationStatus.Failed)
    {
        var result = AddResultLocked(new NarrationResult(id, interactionId, status, errorCode,
            now, now, 0, correlationId));
        return new NarrationEnqueueResult(false, result, errorCode);
    }

    private NarrationResult AddResultLocked(NarrationResult result)
    {
        var sequenced = result with { Sequence = ++_sequence };
        var existingIndex = _recent.FindIndex(item => item.NarrationId == result.NarrationId);
        if (existingIndex >= 0)
            _recent[existingIndex] = sequenced;
        else
            _recent.Add(sequenced);
        if (_recent.Count > _options.EventBufferCapacity) _recent.RemoveAt(0);
        return sequenced;
    }

    private void EnsureEnabled()
    {
        if (!_options.Enabled) throw new InvalidOperationException("Narration is disabled.");
    }

    private sealed record QueuedNarration(NarrationRequest Request, IDisposable Lease);

}
