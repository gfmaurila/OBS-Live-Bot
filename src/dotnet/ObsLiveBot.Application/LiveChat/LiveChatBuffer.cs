using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.LiveChat;

public sealed class LiveChatBuffer(IOptions<LiveChatOptions> options) : ILiveChatBuffer
{
    private readonly object _gate = new();
    private readonly Queue<LiveChatEvent> _events = new(options.Value.BufferCapacity);
    private long _eventsReceived;
    private long _messagesReceived;
    private DateTimeOffset? _lastMessageAtUtc;

    public int Capacity { get; } = options.Value.BufferCapacity;

    public int Count
    {
        get { lock (_gate) return _events.Count; }
    }

    public void Add(LiveChatEvent chatEvent)
    {
        ArgumentNullException.ThrowIfNull(chatEvent);
        lock (_gate)
        {
            if (_events.Count == Capacity)
            {
                _events.Dequeue();
            }

            _events.Enqueue(chatEvent);
            _eventsReceived++;
            if (chatEvent.EventType == LiveChatEventType.Message)
            {
                _messagesReceived++;
                _lastMessageAtUtc = chatEvent.ReceivedAtUtc;
            }
        }
    }

    public IReadOnlyList<LiveChatEvent> GetRecent(
        int limit,
        LiveChatProviderType? provider = null,
        LiveChatEventType? eventType = null)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        lock (_gate)
        {
            return _events
                .Reverse()
                .Where(item => provider is null || item.Provider == provider)
                .Where(item => eventType is null || item.EventType == eventType)
                .Take(safeLimit)
                .ToArray();
        }
    }

    public LiveChatStateSnapshot GetState(IReadOnlyList<LiveChatProviderSnapshot> providers)
    {
        lock (_gate)
        {
            var enabled = providers.Count(item => item.Enabled);
            var connected = providers.Count(item => item.IsConnected);
            var degraded = providers.Any(item => item.Enabled && item.State is
                LiveChatProviderState.Reconnecting or LiveChatProviderState.RateLimited or
                LiveChatProviderState.AuthenticationFailed or LiveChatProviderState.Faulted);
            return new LiveChatStateSnapshot(
                degraded ? "Degraded" : "Ready",
                connected,
                enabled,
                _eventsReceived,
                _messagesReceived,
                _events.Count,
                Capacity,
                _lastMessageAtUtc);
        }
    }
}
