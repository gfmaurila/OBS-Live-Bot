using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.ChatResponses;

    /// <summary>
/// Bounded, thread-safe, in-memory record of what StudioOS already wrote.
///
/// Two independent bounded rings share a single lock. That is both cheaper than two locks and easier
/// to reason about: a claimed key always exists in the idempotency ring, and a recorded text always
/// exists in the echo ring. Nothing is persisted, so a restart forgets everything and simply starts
/// clean - which is correct, because the platform itself is the durable record of what was sent.
/// </summary>
public sealed class ChatResponseLedger(IOptions<ChatResponseOptions> options) : IChatResponseLedger
{
    /// <summary>
    /// Separator for the composite echo key. A control character is used so it can never occur inside a
    /// channel id or a message, which would otherwise let two different replies collide on one key.
    /// </summary>
    private const char Separator = '\u001F';

    private readonly object _gate = new();
    private readonly HashSet<string> _claimedKeys = new(StringComparer.Ordinal);
    private readonly Queue<string> _claimedOrder = new();
    private readonly Dictionary<string, DateTimeOffset> _writtenTexts = new(StringComparer.Ordinal);
    private readonly Queue<string> _writeOrder = new();
    private readonly int _idempotencyCapacity = Math.Max(1, options.Value.IdempotencyCapacity);
    private readonly int _echoCapacity = Math.Max(1, options.Value.EchoCapacity);
    private readonly TimeSpan _echoWindow =
        TimeSpan.FromSeconds(Math.Max(0, options.Value.EchoWindowSeconds));

    public int IdempotencyCapacity => _idempotencyCapacity;

    public int EchoCapacity => _echoCapacity;

    public int IdempotencyCount
    {
        get { lock (_gate) return _claimedKeys.Count; }
    }

    public int EchoCount
    {
        get { lock (_gate) return _writtenTexts.Count; }
    }

    public bool TryReserve(string idempotencyKey, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        lock (_gate)
        {
            if (!_claimedKeys.Add(idempotencyKey)) return false;
            _claimedOrder.Enqueue(idempotencyKey);
            while (_claimedOrder.Count > _idempotencyCapacity)
                _claimedKeys.Remove(_claimedOrder.Dequeue());
            return true;
        }
    }

    public void Forget(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        lock (_gate) _claimedKeys.Remove(idempotencyKey);
    }

    public bool WasWritten(LiveChatProviderType provider, string channelId, string text, DateTimeOffset now)
    {
        if (_echoWindow <= TimeSpan.Zero || string.IsNullOrEmpty(text)) return false;
        var key = EchoKey(provider, channelId, text);
        lock (_gate)
        {
            if (!_writtenTexts.TryGetValue(key, out var writtenAt)) return false;
            if (now - writtenAt <= _echoWindow) return true;
            // Expired but not yet evicted. Dropped eagerly so a stale entry cannot keep the ring alive.
            _writtenTexts.Remove(key);
            return false;
        }
    }

    public void RecordWrite(LiveChatProviderType provider, string channelId, string text, DateTimeOffset now)
    {
        if (_echoWindow <= TimeSpan.Zero || string.IsNullOrEmpty(text)) return;
        var key = EchoKey(provider, channelId, text);
        lock (_gate)
        {
            if (_writtenTexts.ContainsKey(key))
            {
                // The echo window restarts from the most recent delivery. Writing the same line twice
                // means two echoes to recognise, so only the latest one's window needs to stay alive.
                _writtenTexts[key] = now;
                return;
            }

            _writtenTexts[key] = now;
            _writeOrder.Enqueue(key);
            while (_writeOrder.Count > _echoCapacity)
                _writtenTexts.Remove(_writeOrder.Dequeue());
        }
    }

    /// <summary>
    /// Echo matching is provider and channel scoped and compares collapsed text case-insensitively, so
    /// the key is the normalized form itself. The same sentence on another channel is not our echo, and
    /// a copy of our own line that a platform reflowed or re-cased is.
    /// </summary>
    private static string EchoKey(LiveChatProviderType provider, string channelId, string text) =>
        string.Join(Separator, (int)provider,
            ChatResponsePolicy.NormalizeChannel(channelId).ToUpperInvariant(),
            ChatResponsePolicy.Collapse(text).ToUpperInvariant());
}
