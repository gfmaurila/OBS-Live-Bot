using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.LiveChat;

public sealed class LiveChatDeduplicator(IOptions<LiveChatOptions> options) : ILiveChatDeduplicator
{
    private readonly object _gate = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new(options.Value.DeduplicationCapacity);

    public int Capacity { get; } = options.Value.DeduplicationCapacity;

    public int Count
    {
        get { lock (_gate) return _keys.Count; }
    }

    public bool TryAccept(LiveChatEvent chatEvent)
    {
        var key = CreateKey(chatEvent);
        lock (_gate)
        {
            if (!_keys.Add(key))
            {
                return false;
            }

            _order.Enqueue(key);
            if (_order.Count > Capacity)
            {
                _keys.Remove(_order.Dequeue());
            }

            return true;
        }
    }

    private static string CreateKey(LiveChatEvent chatEvent)
    {
        if (!string.IsNullOrWhiteSpace(chatEvent.ProviderEventId))
        {
            return $"{chatEvent.Provider}|{chatEvent.ChannelId}|{chatEvent.ProviderEventId}";
        }

        var fallback = string.Join(
            '\u001f',
            chatEvent.Provider,
            chatEvent.ChannelId,
            chatEvent.User.UserId,
            chatEvent.TimestampUtc.UtcTicks,
            chatEvent.EventType,
            chatEvent.Message);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fallback)));
    }
}
