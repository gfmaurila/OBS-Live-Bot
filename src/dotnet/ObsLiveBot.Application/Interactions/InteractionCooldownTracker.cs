using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Interactions;

public sealed class InteractionCooldownTracker(
    IOptions<InteractionOptions> options,
    TimeProvider timeProvider) : IInteractionCooldownTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, DateTimeOffset Timestamp)> _order = new();
    private readonly TimeSpan _cooldown = TimeSpan.FromSeconds(options.Value.CooldownSeconds);
    private readonly InteractionCooldownScope _scope = options.Value.CooldownScope;

    public int Capacity { get; } = options.Value.CooldownCapacity;

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public bool TryAcquire(LiveChatProviderType provider, string channelId, string userId)
    {
        var now = timeProvider.GetUtcNow();
        var key = BuildKey(provider, channelId, userId);
        lock (_gate)
        {
            EvictExpired(now);
            if (_entries.TryGetValue(key, out var last) && now - last < _cooldown)
            {
                return false;
            }

            _entries[key] = now;
            _order.Enqueue((key, now));
            while (_entries.Count > Capacity && _order.TryDequeue(out var oldest))
            {
                if (_entries.TryGetValue(oldest.Key, out var current) && current == oldest.Timestamp)
                {
                    _entries.Remove(oldest.Key);
                }
            }

            return true;
        }
    }

    private string BuildKey(LiveChatProviderType provider, string channelId, string userId) => _scope switch
    {
        InteractionCooldownScope.Global => "global",
        InteractionCooldownScope.Channel => $"{provider}:{channelId}",
        _ => $"{provider}:{channelId}:{userId}"
    };

    private void EvictExpired(DateTimeOffset now)
    {
        while (_order.TryPeek(out var oldest) && now - oldest.Timestamp >= _cooldown)
        {
            _order.Dequeue();
            if (_entries.TryGetValue(oldest.Key, out var current) && current == oldest.Timestamp)
            {
                _entries.Remove(oldest.Key);
            }
        }
    }
}
