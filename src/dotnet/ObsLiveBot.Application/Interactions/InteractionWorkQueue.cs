using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Interactions;

public interface IInteractionWorkQueue
{
    bool TryEnqueue(LiveChatEvent chatEvent);
    int Count { get; }
    int Capacity { get; }
    long RejectedCount { get; }
    Guid? CurrentEventId { get; }
    string CurrentState { get; }
    string? LastFailureReason { get; }
}

public sealed class InteractionWorkQueue : IInteractionWorkQueue
{
    private readonly Channel<LiveChatEvent> _channel = Channel.CreateBounded<LiveChatEvent>(
        new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private long _rejected;
    private int _count;
    private readonly object _stateGate = new();
    private Guid? _currentEventId;
    private string _currentState = "Idle";
    private string? _lastFailureReason;

    public int Count => Volatile.Read(ref _count);
    public int Capacity => 32;
    public long RejectedCount => Interlocked.Read(ref _rejected);
    public Guid? CurrentEventId { get { lock (_stateGate) return _currentEventId; } }
    public string CurrentState { get { lock (_stateGate) return _currentState; } }
    public string? LastFailureReason { get { lock (_stateGate) return _lastFailureReason; } }

    public bool TryEnqueue(LiveChatEvent chatEvent)
    {
        Interlocked.Increment(ref _count);
        if (_channel.Writer.TryWrite(chatEvent))
        {
            return true;
        }
        Interlocked.Decrement(ref _count);
        Interlocked.Increment(ref _rejected);
        return false;
    }

    internal IAsyncEnumerable<LiveChatEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    internal void SetProcessing(Guid eventId)
    {
        Interlocked.Decrement(ref _count);
        lock (_stateGate)
        {
            _currentEventId = eventId;
            _currentState = "Processing";
        }
    }

    internal void SetFinished(string state, string? reason = null)
    {
        lock (_stateGate)
        {
            _currentEventId = null;
            _currentState = state;
            if (reason is not null) _lastFailureReason = reason;
        }
    }
}

public sealed class InteractionWorkQueueProcessor(
    InteractionWorkQueue queue,
    IInteractionOrchestrator orchestrator,
    ILogger<InteractionWorkQueueProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var chatEvent in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            queue.SetProcessing(chatEvent.EventId);
            try
            {
                var result = await orchestrator.ProcessAsync(chatEvent, null, stoppingToken).ConfigureAwait(false);
                queue.SetFinished(result.Status.ToString(), result.ErrorCode ??
                    (result.Decision.DecisionType == InteractionDecisionType.Ignore ? result.Decision.Reason : null));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                queue.SetFinished("Failed", exception.GetType().Name);
                logger.LogWarning(
                    "INTERACTION_CHAT_EVENT_FAILED chatEventId={ChatEventId} errorType={ErrorType}",
                    chatEvent.EventId,
                    exception.GetType().Name);
            }
        }
    }
}
