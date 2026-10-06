using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.ChatResponses;

/// <summary>
/// Bounded, thread-safe record of the chat accounts StudioOS posts from.
/// <para>
/// Text equality is a weak loop guard on its own. A viewer can quote a reply verbatim, a platform can
/// reflow or re-case a line, and truncation makes two genuinely different replies collide. So text is
/// used only for the narrow job it is actually good at - recognising the very first echo of a reply,
/// before StudioOS has ever seen the account behind it - and the identity observed there is learned.
/// After that, the account alone is enough to suppress, and the text is never consulted again for it.
/// </para>
/// <para>
/// Both identities and display names are recorded because capture transports differ: Twitch always
/// reports a numeric user id, while the Social Stream Ninja YouTube route frequently reports only a
/// display name and synthesises the id from a hash of it. Both forms therefore have to be able to name
/// the same account.
/// </para>
/// </summary>
public sealed class ChatResponseSelfIdentityRegistry : IChatResponseSelfIdentityRegistry
{
    private const char Separator = '\u001F';

    private readonly object _gate = new();
    private readonly HashSet<string> _identities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _identityOrder = new();
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _nameOrder = new();
    private readonly Dictionary<string, string> _namesByIdentity = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _capacity;

    public ChatResponseSelfIdentityRegistry(IOptions<ChatResponseOptions> options)
    {
        _capacity = Math.Max(1, options.Value.SelfActorCapacity);
        foreach (var configured in options.Value.SelfActorIdentities ?? [])
            if (TryParseIdentity(configured, out var provider, out var userId, out var userName))
                Add(provider, userId, userName);
    }

    public int Count
    {
        get { lock (_gate) return _identities.Count; }
    }

    public int Capacity => _capacity;

    public bool IsStudioOsActor(LiveChatProviderType provider, string? userId, string? username = null)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(userId) &&
                _identities.Contains(IdentityKey(provider, userId)))
                return true;

            // Name-only match. Deliberately not combined with the channel: an account identity that
            // StudioOS posts from is the same account everywhere on that platform, and the echo arrives
            // on the channel the reply was written to, so the channel adds nothing but a false negative.
            return !string.IsNullOrWhiteSpace(username) && _names.Contains(NameKey(provider, username));
        }
    }

    public bool Learn(LiveChatProviderType provider, string? userId, string? username)
    {
        var id = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        var name = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        if (id is null && name is null)
            return false;

        lock (_gate)
        {
            if ((id is not null && _identities.Contains(IdentityKey(provider, id))) ||
                (name is not null && _names.Contains(NameKey(provider, name))))
                return false;

            Add(provider, id, name);
            return true;
        }
    }

    public IReadOnlyList<string> GetIdentities()
    {
        lock (_gate) return _identities.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private void Add(LiveChatProviderType provider, string? userId, string? userName)
    {
        string? identityKey = null;
        string? nameKey = null;

        if (userId is not null)
        {
            identityKey = IdentityKey(provider, userId);
            if (_identities.Add(identityKey))
                _identityOrder.Enqueue(identityKey);
        }

        if (userName is not null)
        {
            nameKey = NameKey(provider, userName);
            if (_names.Add(nameKey))
            {
                _nameOrder.Enqueue(nameKey);
                // Remembered so the name is evicted together with the identity that introduced it,
                // rather than lingering until its own queue overflows.
                if (identityKey is not null) _namesByIdentity[identityKey] = nameKey;
            }
        }

        // Identity capacity is the budget. Names are evicted with their identity rather than counted
        // separately, so the total memory the registry can occupy stays bounded by one number.
        while (_identityOrder.Count > _capacity)
            Evict(_identityOrder.Dequeue());
        while (_nameOrder.Count > _capacity)
            EvictOrphan(_nameOrder.Dequeue());
    }

    private void Evict(string identityKey)
    {
        _identities.Remove(identityKey);
        if (_namesByIdentity.Remove(identityKey, out var nameKey)) _names.Remove(nameKey);
    }

    private void EvictOrphan(string nameKey) => _names.Remove(nameKey);

    /// <summary>
    /// Accepts <c>Provider:Account</c>, where <c>Account</c> is a platform user id or a display name.
    /// <para>
    /// The value is registered under both the id key and the name key because capture transports differ:
    /// Twitch always reports a numeric id, while the Social Stream Ninja YouTube route frequently reports
    /// only a display name and derives its id from a hash of it. An operator should not have to know which
    /// of the two a given platform will report. A bare value with no provider is rejected rather than
    /// guessed at, so a typo cannot widen suppression across every platform.
    /// </para>
    /// </summary>
    internal static bool TryParseIdentity(
        string? value,
        out LiveChatProviderType provider,
        out string? userId,
        out string? userName)
    {
        provider = LiveChatProviderType.Unknown;
        userId = null;
        userName = null;

        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        var separator = trimmed.IndexOf(':');
        if (separator <= 0) return false;

        var providerName = trimmed[..separator].Trim();
        var account = trimmed[(separator + 1)..].Trim();
        if (account.Length == 0) return false;
        if (!Enum.TryParse(providerName, ignoreCase: true, out provider)) return false;
        // Enum.TryParse also accepts any numeric value, so a value like "7:account" would otherwise be
        // accepted under a provider that does not exist. Such an entry can never match a real event, which
        // makes it a silently ignored setting rather than a working one.
        if (provider == LiveChatProviderType.Unknown || !Enum.IsDefined(provider)) return false;

        userId = account;
        userName = account;
        return true;
    }

    private static string IdentityKey(LiveChatProviderType provider, string userId) =>
        string.Concat(((int)provider).ToString(), Separator, userId);

    private static string NameKey(LiveChatProviderType provider, string userName) =>
        string.Concat("name", Separator, ((int)provider).ToString(), Separator, userName);
}