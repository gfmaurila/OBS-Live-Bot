using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>
/// Shared resolution for the three adapters that all write through the same authenticated browser
/// session, and differ only in platform key, message ceiling and which source field identifies the
/// channel.
/// </summary>
public abstract class SocialStreamNinjaChatWriteAdapter : IChatWriteAdapter
{
    /// <summary>The <c>target</c> Social Stream Ninja reports for this platform.</summary>
    protected abstract string PlatformKey { get; }

    public abstract LiveChatProviderType Provider { get; }

    public abstract string Name { get; }

    public abstract int MaxMessageCharacters { get; }

    public string Transport => "SocialStreamNinja:page-composer";

    public bool RequiresAuthenticatedSession => true;

    /// <summary>
    /// Whether this platform identifies its channel by the source's video id rather than by the source's
    /// account name. YouTube does: the same channel is a different video on every stream, so the video id
    /// is the only identifier that distinguishes one live from another. Matching on the account name alone
    /// would let a reply written during yesterday's stream be typed into a stale source.
    /// </summary>
    protected virtual bool MatchesOnVideoId => false;

    public ChatWriteTarget? ResolveTarget(IReadOnlyList<ChatWriteSource> sources, string channelId)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var channel = ChatResponsePolicy.NormalizeChannel(channelId);

        var candidates = sources
            .Where(source => string.Equals(source.Target, PlatformKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0) return null;

        // Exact channel match, preferring an active source: a reply should go to the live that is running,
        // not to a paused window still holding an old conversation.
        var exact = candidates
            .Where(source => ChannelMatches(source, channel))
            .OrderByDescending(source => source.Active)
            .FirstOrDefault();
        if (exact is not null)
            return new ChatWriteTarget(exact.Id, channel, true);

        // Fallback: the platform's active source. Reached when the capture side and the write side name
        // the same channel differently, which is routine - a live video id, a vanity URL and an internal
        // account name are three different strings for one channel.
        var active = candidates.OrderByDescending(source => source.Active).First();
        return new ChatWriteTarget(active.Id, ChatResponsePolicy.NormalizeChannel(active.Username), false);
    }

    private bool ChannelMatches(ChatWriteSource source, string channel)
    {
        if (channel.Length == 0) return false;
        if (MatchesOnVideoId && string.Equals(source.VideoId, channel, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(source.Username, channel, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Twitch. Its chat rejects anything longer than 500 characters, and a channel is addressed by its vanity
/// name, which is what the source reports.
/// </summary>
public sealed class TwitchChatWriteAdapter : SocialStreamNinjaChatWriteAdapter
{
    public const int TwitchChatMessageLimit = 500;

    public override LiveChatProviderType Provider => LiveChatProviderType.Twitch;

    public override string Name => "Twitch";

    protected override string PlatformKey => "twitch";

    public override int MaxMessageCharacters => TwitchChatMessageLimit;
}

/// <summary>
/// YouTube. Live chat messages are capped well below Twitch's, so the global 500-character reply budget
/// has to be reduced for this platform rather than silently truncated by the page.
/// </summary>
public sealed class YouTubeChatWriteAdapter : SocialStreamNinjaChatWriteAdapter
{
    public const int YouTubeChatMessageLimit = 200;

    public override LiveChatProviderType Provider => LiveChatProviderType.YouTube;

    public override string Name => "YouTube";

    protected override string PlatformKey => "youtube";

    public override int MaxMessageCharacters => YouTubeChatMessageLimit;

    protected override bool MatchesOnVideoId => true;
}

/// <summary>
/// Kick. Same ceiling as Twitch and addressed by account name. The write route is the same authenticated
/// session as capture; no official Kick API is used and none is required.
/// </summary>
public sealed class KickChatWriteAdapter : SocialStreamNinjaChatWriteAdapter
{
    public const int KickChatMessageLimit = 500;

    public override LiveChatProviderType Provider => LiveChatProviderType.Kick;

    public override string Name => "Kick";

    protected override string PlatformKey => "kick";

    public override int MaxMessageCharacters => KickChatMessageLimit;
}