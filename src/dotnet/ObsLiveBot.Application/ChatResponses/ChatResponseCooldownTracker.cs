using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.ChatResponses;

    /// <summary>Outcome of a cooldown check for one candidate reply.</summary>
    public sealed record ChatResponseCooldownResult(bool Accepted, string? Reason)
{
    public static ChatResponseCooldownResult Accept() => new(true, null);

    public static ChatResponseCooldownResult Reject(string reason) => new(false, reason);
}

    /// <summary>
/// Rate limiter for written replies: one global gate per channel plus one gate per addressed user.
///
/// The global gate exists because a written reply is public and visible to every viewer - replying at
/// the speed of an assistant turn would bury the chat. The per-user gate exists so one frequently
/// addressed viewer cannot spend the whole budget. Entries are bounded and evicted oldest-first, so a
/// long stream cannot grow the tracker without limit.
/// </summary>
public sealed class ChatResponseCooldownTracker(IOptions<ChatResponseOptions> options)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly int _capacity = Math.Max(1, options.Value.CooldownCapacity);
    private readonly TimeSpan _globalWindow =
        TimeSpan.FromSeconds(Math.Max(0, options.Value.GlobalCooldownSeconds));
    private readonly TimeSpan _userWindow =
        TimeSpan.FromSeconds(Math.Max(0, options.Value.UserCooldownSeconds));

    public int Capacity => _capacity;

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public TimeSpan GlobalWindow => _globalWindow;

    public TimeSpan UserWindow => _userWindow;

    public ChatResponseCooldownResult Check(
        LiveChatProviderType provider,
        string channelId,
        string? userId,
        DateTimeOffset now)
    {
        var channel = ChatResponsePolicy.NormalizeChannel(channelId).ToUpperInvariant();
        lock (_gate)
        {
            // The two gates are evaluated against their own last reply rather than against a shared
            // "most recent" timestamp. Using one timestamp would make a per-user gate fire because
            // somebody else was answered, which is exactly the behaviour a per-user gate exists to
            // avoid.
            if (_entries.TryGetValue(GlobalKey(provider, channel), out var lastGlobal) &&
                now - lastGlobal < _globalWindow)
            {
                return ChatResponseCooldownResult.Reject("GLOBAL_COOLDOWN");
            }

            if (!string.IsNullOrWhiteSpace(userId) &&
                _entries.TryGetValue(UserKey(provider, channel, userId), out var lastUser) &&
                now - lastUser < _userWindow)
            {
                return ChatResponseCooldownResult.Reject("USER_COOLDOWN");
            }

            return ChatResponseCooldownResult.Accept();
        }
    }

    /// <summary>
    /// Records a reply for both the global and the per-user gate in one step, so a check that passed
    /// can never be followed by a commit that records only half of the decision.
    /// </summary>
    public void Commit(
        LiveChatProviderType provider,
        string channelId,
        string? userId,
        DateTimeOffset now)
    {
        var channel = ChatResponsePolicy.NormalizeChannel(channelId).ToUpperInvariant();
        lock (_gate)
        {
            Track(GlobalKey(provider, channel), now);
            if (!string.IsNullOrWhiteSpace(userId))
                Track(UserKey(provider, channel, userId), now);
        }
    }

    private void Track(string key, DateTimeOffset now)
    {
        if (_entries.ContainsKey(key))
        {
            _entries[key] = now;
            return;
        }

        _entries[key] = now;
        _order.Enqueue(key);
        while (_order.Count > _capacity)
            _entries.Remove(_order.Dequeue());
    }

    private static string GlobalKey(LiveChatProviderType provider, string channel) =>
        $"g\u001F{(int)provider}\u001F{channel}";

    private static string UserKey(LiveChatProviderType provider, string channel, string userId) =>
        $"u\u001F{(int)provider}\u001F{channel}\u001F{userId}";
}
