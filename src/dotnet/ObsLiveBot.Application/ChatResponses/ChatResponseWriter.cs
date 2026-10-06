using System.Threading.Channels;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.ChatResponses;

/// <summary>
/// Executes written replies.
/// <para>
/// The automatic path is a bounded, single-reader queue. Bounded because a stalled platform must not
/// turn the writer into an unbounded memory leak, and single-reader because two concurrent writes into
/// one chat channel are exactly the duplication this capability exists to avoid. The newest reply is
/// the one refused when the queue is full, so a burst never silently discards work that was already
/// accepted.
/// </para>
/// <para>
/// Nothing here throws. Every failure mode - full queue, missing sender, sender exception, timeout,
/// cancelled shutdown, failing notification - becomes a recorded result, because the callers of the
/// automatic path are interactions, OBS, audio and capture, and none of them may be taken down by a
/// problem with writing text into chat.
/// </para>
/// </summary>
public sealed class ChatResponseWriter : BackgroundService, IChatResponseWriter
{
    private const int HistoryLimit = 500;

    private readonly Channel<ChatResponseIntent> _queue;
    private readonly IOptions<ChatResponseOptions> _options;
    private readonly IChatResponseSettingsStore _settings;
    private readonly IChatResponseSenderRegistry _registry;
    private readonly IChatResponseLedger _ledger;
    private readonly ChatResponseCooldownTracker _cooldowns;
    private readonly IChatResponseEventPublisher _eventPublisher;
    private readonly IPublisher _mediator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChatResponseWriter> _logger;
    private readonly object _stateGate = new();
    private readonly LinkedList<ChatResponseResult> _history = new();
    private readonly int _historyCapacity;
    private readonly int _queueCapacity;
    private readonly TimeSpan _commandTimeout;

    private long _sequence;
    private long _queued;
    private long _sent;
    private long _skipped;
    private long _failed;
    private long _cancelled;
    private long _rejected;
    private DateTimeOffset? _lastSentAtUtc;
    private DateTimeOffset? _lastFailureAtUtc;
    private string? _lastFailureReason;

    public ChatResponseWriter(
        IOptions<ChatResponseOptions> options,
        IChatResponseSettingsStore settings,
        IChatResponseSenderRegistry registry,
        IChatResponseLedger ledger,
        ChatResponseCooldownTracker cooldowns,
        IChatResponseEventPublisher eventPublisher,
        IPublisher mediator,
        TimeProvider timeProvider,
        ILogger<ChatResponseWriter> logger)
    {
        _options = options;
        _settings = settings;
        _registry = registry;
        _ledger = ledger;
        _cooldowns = cooldowns;
        _eventPublisher = eventPublisher;
        _mediator = mediator;
        _timeProvider = timeProvider;
        _logger = logger;
        _queueCapacity = Math.Max(1, options.Value.MaxQueueSize);
        _historyCapacity = Math.Max(1, options.Value.HistoryCapacity);
        _commandTimeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.CommandTimeoutSeconds));
        _queue = Channel.CreateBounded<ChatResponseIntent>(new BoundedChannelOptions(_queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            // Wait mode is what makes TryWrite report a full queue instead of silently dropping an
            // already accepted reply on the floor.
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public ChatResponseStateSnapshot GetState()
    {
        var settings = _options.Value;
        var effective = _settings.Get();
        var sender = _registry.GetSelected();
        lock (_stateGate)
        {
            return new ChatResponseStateSnapshot(
                effective.Enabled,
                StatusText(effective.Enabled, sender),
                settings.Sender,
                sender?.GetRuntimeState().Status,
                sender?.IsAvailable ?? false,
                _queue.Reader.Count,
                _queueCapacity,
                _queued,
                _sent,
                _skipped,
                _failed,
                _cancelled,
                _rejected,
                _history.Count,
                _historyCapacity,
                _cooldowns.Count,
                _cooldowns.Capacity,
                _ledger.IdempotencyCount,
                _ledger.IdempotencyCapacity,
                _ledger.EchoCount,
                _ledger.EchoCapacity,
                _lastSentAtUtc,
                _lastFailureAtUtc,
                _lastFailureReason);
        }
    }

    public IReadOnlyList<ChatResponseResult> GetRecent(int limit)
    {
        var take = Math.Clamp(limit, 1, HistoryLimit);
        lock (_stateGate)
            return _history.TakeLast(take).Reverse().ToList();
    }

    /// <summary>Never throws and never waits on the platform.</summary>
    public async Task<ChatResponseEnqueueResult> EnqueueAsync(
        ChatResponseIntent intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        try
        {
            if (!_queue.Writer.TryWrite(intent))
            {
                // The reply is refused rather than queued behind a burst, and its idempotency key is
                // released because the write never started, so a later explicit attempt is not treated
                // as a duplicate. A stopped writer is reported distinctly from a full queue because the
                // operator's remedy is different.
                _ledger.Forget(intent.IdempotencyKey);
                lock (_stateGate) _rejected++;
                var stopped = _queue.Reader.Completion.IsCompleted;
                return new ChatResponseEnqueueResult(
                    false,
                    null,
                    stopped ? "CHAT_WRITER_STOPPED" : "CHAT_QUEUE_FULL",
                    ChatResponseStatus.Skipped);
            }

            lock (_stateGate) _queued++;
            await PublishQueuedSafeAsync(intent, cancellationToken).ConfigureAwait(false);
            return new ChatResponseEnqueueResult(true, null, null, ChatResponseStatus.Queued);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat response {ChatResponseId} could not be queued", intent.ChatResponseId);
            lock (_stateGate) _rejected++;
            return new ChatResponseEnqueueResult(
                false, null, "CHAT_QUEUE_REJECTED", ChatResponseStatus.Skipped);
        }
    }

    /// <summary>
    /// Development-only path. Runs the sender inline so an operator sees the outcome of one controlled
    /// send without waiting behind the automatic queue. Every consequence is shared with the queue path
    /// because it uses the same ledger, history and sender resolution.
    /// </summary>
    public async Task<ChatResponseEnqueueResult> ExecuteDirectAsync(
        ChatResponseIntent intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var result = await SendAsync(intent, cancellationToken).ConfigureAwait(false);
        return new ChatResponseEnqueueResult(
            result.Status == ChatResponseStatus.Sent,
            result,
            result.Reason,
            result.Status);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var intent in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await SendAsync(intent, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Last line of defence: even an unforeseen exception must not escape into the host's
                    // background loop and become an unhandled task failure.
                    _logger.LogError(ex, "Unhandled error while writing chat response {ChatResponseId}",
                        intent.ChatResponseId);
                    lock (_stateGate) _failed++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Chat response writer stopping after {Queued} queued and {Sent} written",
                QueuedCount(),
                SentCount());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat response writer loop stopped unexpectedly");
        }
        finally
        {
            await ReleaseQueuedOnShutdownAsync().ConfigureAwait(false);
            _queue.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Releases replies that were accepted but never attempted because the host stopped reading. Their
    /// idempotency keys are released so the same reply is not mistaken for a duplicate afterwards, and
    /// each one is recorded, because a reply that vanished silently would be worse than one visibly
    /// cancelled.
    /// </summary>
    private async Task ReleaseQueuedOnShutdownAsync()
    {
        var released = 0;
        while (_queue.Reader.TryRead(out var intent))
        {
            _ledger.Forget(intent.IdempotencyKey);
            await RecordAsync(intent, ChatResponseStatus.Cancelled, "CHAT_WRITE_CANCELLED", null,
                TimeSpan.Zero).ConfigureAwait(false);
            released++;
        }

        if (released > 0)
            _logger.LogWarning(
                "Chat response writer released {Released} queued replies that were never attempted", released);
    }

    private async Task<ChatResponseResult> SendAsync(
        ChatResponseIntent intent,
        CancellationToken cancellationToken)
    {
        var sender = _registry.GetSelected();
        if (sender is null)
            return await RecordAsync(
                intent, ChatResponseStatus.Failed, "CHAT_SENDER_NOT_SELECTED", null, TimeSpan.Zero)
                .ConfigureAwait(false);

        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_commandTimeout);

            var request = new ChatResponseSendRequest(
                intent.ChatResponseId,
                intent.InteractionId,
                intent.SourceMessageId,
                intent.Provider,
                intent.ChannelId,
                intent.UserId,
                intent.UserName,
                intent.Text,
                intent.CorrelationId,
                intent.IdempotencyKey);

            var send = await sender.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var elapsed = _timeProvider.GetUtcNow() - startedAt;

            if (!send.Success)
                return await RecordAsync(intent, ChatResponseStatus.Failed,
                    send.ErrorCode ?? "CHAT_SEND_FAILED", sender.Name, elapsed, attempt: send.Attempt)
                    .ConfigureAwait(false);

            // The text becomes "ours" only after the platform accepted it. Recording earlier would
            // suppress the echo of a message that was never actually written.
            _ledger.RecordWrite(intent.Provider, intent.ChannelId, intent.Text, _timeProvider.GetUtcNow());
            return await RecordAsync(intent, ChatResponseStatus.Sent, null, sender.Name, elapsed, send.IsSimulated,
                attempt: send.Attempt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown rather than our own ceiling: release the key so the reply is not lost.
            _ledger.Forget(intent.IdempotencyKey);
            return await RecordAsync(intent, ChatResponseStatus.Cancelled, "CHAT_WRITE_CANCELLED",
                sender.Name, _timeProvider.GetUtcNow() - startedAt).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Chat response {ChatResponseId} exceeded the {Timeout} write ceiling",
                intent.ChatResponseId, _commandTimeout);
            return await RecordAsync(intent, ChatResponseStatus.Failed, "CHAT_WRITE_TIMEOUT",
                sender.Name, _timeProvider.GetUtcNow() - startedAt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat response {ChatResponseId} failed on sender {Sender}",
                intent.ChatResponseId, sender.Name);
            return await RecordAsync(intent, ChatResponseStatus.Failed, "CHAT_SEND_EXCEPTION",
                sender.Name, _timeProvider.GetUtcNow() - startedAt).ConfigureAwait(false);
        }
    }

    private async Task<ChatResponseResult> RecordAsync(
        ChatResponseIntent intent,
        ChatResponseStatus status,
        string? reason,
        string? senderName,
        TimeSpan duration,
        bool simulated = false,
        int attempt = 1)
    {
        long sequence;
        lock (_stateGate)
        {
            sequence = ++_sequence;
            switch (status)
            {
                case ChatResponseStatus.Sent:
                    _sent++;
                    _lastSentAtUtc = _timeProvider.GetUtcNow();
                    break;
                case ChatResponseStatus.Skipped:
                    _skipped++;
                    break;
                case ChatResponseStatus.Cancelled:
                    _cancelled++;
                    break;
                default:
                    _failed++;
                    _lastFailureAtUtc = _timeProvider.GetUtcNow();
                    _lastFailureReason = reason;
                    break;
            }
        }

        var result = new ChatResponseResult(
            intent.ChatResponseId,
            intent.InteractionId,
            intent.SourceMessageId,
            intent.Provider,
            intent.ChannelId,
            intent.UserId,
            intent.UserName,
            senderName ?? "none",
            status,
            reason,
            intent.CreatedAtUtc,
            _timeProvider.GetUtcNow(),
            sequence,
            intent.CorrelationId,
            intent.Text,
            intent.Text.Length,
            intent.IdempotencyKey,
            simulated,
            duration.TotalMilliseconds,
            attempt);

        lock (_stateGate)
        {
            _history.AddLast(result);
            while (_history.Count > _historyCapacity)
                _history.RemoveFirst();
        }

        if (status != ChatResponseStatus.Sent)
            _logger.LogWarning(
                "Chat response {ChatResponseId} finished as {Status} ({Reason})",
                intent.ChatResponseId, status, reason ?? "-");

        await PublishCompletedSafeAsync(result).ConfigureAwait(false);
        return result;
    }

    private async Task PublishQueuedSafeAsync(ChatResponseIntent intent, CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.Publish(new ChatResponseQueuedNotification(
                intent.ChatResponseId,
                intent.InteractionId,
                intent.SourceMessageId,
                intent.Provider,
                intent.ChannelId,
                intent.CorrelationId,
                intent.Text.Length), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat response {ChatResponseId} queued notification failed",
                intent.ChatResponseId);
        }
    }

    private async Task PublishCompletedSafeAsync(ChatResponseResult result)
    {
        try
        {
            await _eventPublisher.PublishAsync(new ChatResponseEvent(
                Guid.NewGuid(),
                result.ChatResponseId,
                result.InteractionId,
                result.Status.ToString(),
                result.Reason,
                result.Provider,
                result.ChannelId,
                result.CompletedAtUtc,
                result.Sequence,
                result.CorrelationId), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat response {ChatResponseId} event publication failed",
                result.ChatResponseId);
        }

        try
        {
            await _mediator.Publish(new ChatResponseCompletedNotification(
                result.ChatResponseId,
                result.InteractionId,
                result.SourceMessageId,
                result.Provider,
                result.ChannelId,
                result.Status,
                result.Reason,
                result.CorrelationId,
                result.SenderName,
                result.Simulated,
                result.DurationMilliseconds,
                result.Attempt), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat response {ChatResponseId} completion notification failed",
                result.ChatResponseId);
        }
    }

    private static string StatusText(bool enabled, IChatResponseSender? sender)
    {
        if (!enabled) return "Disabled";
        if (sender is null) return "NoSenderSelected";
        if (!sender.IsAvailable) return "SenderUnavailable";
        return "Ready";
    }

    private long QueuedCount()
    {
        lock (_stateGate) return _queued;
    }

    private long SentCount()
    {
        lock (_stateGate) return _sent;
    }
}
