using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>
/// One platform's write adapter: how a reply is delivered to that platform and what that platform
/// accepts.
/// <para>
/// Adapters exist because the three supported platforms are genuinely different at the write side. They
/// carry different message length ceilings, their capture transports report different channel
/// identifiers, and a reply has to be located against a different source shape on each. Collapsing that
/// into one code path would mean either ignoring the limits or pretending the platforms are the same.
/// </para>
/// <para>
/// An adapter carries no credential and performs no authentication. Authentication belongs to the
/// transport the adapter is bound to, which is a separate, separately authenticated surface.
/// </para>
/// </summary>
public interface IChatWriteAdapter
{
    LiveChatProviderType Provider { get; }

    /// <summary>Stable adapter name, reported by the capability API.</summary>
    string Name { get; }

    /// <summary>Human-readable description of the write transport, reported by the capability API.</summary>
    string Transport { get; }

    /// <summary>
    /// Whether delivering a reply on this platform needs an authenticated platform session. It always
    /// does for a real chat: a chat that accepts anonymous writes is not a chat.
    /// </summary>
    bool RequiresAuthenticatedSession { get; }

    /// <summary>The platform's own ceiling for one chat message.</summary>
    int MaxMessageCharacters { get; }

    /// <summary>
    /// Finds the source a reply should be typed into, preferring an exact channel match and falling back
    /// to the platform's single active source. Returns null when this platform has no source at all.
    /// </summary>
    ChatWriteTarget? ResolveTarget(IReadOnlyList<ChatWriteSource> sources, string channelId);
}

/// <summary>
/// The subset of a Social Stream Ninja source that write resolution needs. Declared separately from the
/// capture-side source record so the write adapters cannot drift into depending on capture state.
/// </summary>
public sealed record ChatWriteSource(
    string Id,
    string Target,
    string? Username,
    string? VideoId,
    bool Active,
    string? Status);

/// <summary>
/// Resolves the write adapter for a platform, or reports that none exists.
/// <para>
/// The registry is what makes "unsupported" honest: a platform with no adapter registered is reported as
/// unsupported rather than being quietly attempted through some other platform's transport.
/// </para>
/// </summary>
public sealed class ChatWriteAdapterRegistry(IEnumerable<IChatWriteAdapter> adapters)
{
    private readonly IReadOnlyDictionary<LiveChatProviderType, IChatWriteAdapter> _adapters =
        adapters.ToDictionary(adapter => adapter.Provider);

    public IReadOnlyList<IChatWriteAdapter> GetAdapters() =>
        _adapters.Values.OrderBy(adapter => adapter.Provider).ToArray();

    public IReadOnlyList<LiveChatProviderType> GetSupportedProviders() => _adapters.Keys.Order().ToArray();

    public IChatWriteAdapter? Find(LiveChatProviderType provider) =>
        _adapters.TryGetValue(provider, out var adapter) ? adapter : null;
}