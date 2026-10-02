namespace ObsLiveBot.Application.Abstractions;

/// <summary>
/// How the current YouTube live was located. No implementation may ever place credentials,
/// stream keys or owner tokens in a discovery result.
/// </summary>
public enum YouTubeLiveDiscoveryMethod
{
    /// <summary>No discovery was performed or no live was found.</summary>
    None,

    /// <summary>The live came from an explicit per-live URL configured by the operator.</summary>
    ManualOverride,

    /// <summary>The live came from the channel's public streams page live badge.</summary>
    ChannelStreamsPage
}

public sealed record YouTubeLiveDiscoveryResult(
    bool IsLive,
    string? Channel,
    string? VideoId,
    string? PublicUrl,
    string? ChatUrl,
    DateTimeOffset DiscoveredAtUtc,
    YouTubeLiveDiscoveryMethod DiscoveryMethod,
    string? Reason,
    IReadOnlyList<string> LiveVideoIds)
{
    /// <summary>Canonical SSN source identity for a live. Stable for the same live and different for a new one.</summary>
    public string? SourceId => VideoId is null ? null : YouTubeLiveSourceIdentity.ForVideoId(VideoId);

    public static YouTubeLiveDiscoveryResult NotLive(
        string? channel,
        DateTimeOffset discoveredAtUtc,
        string reason,
        YouTubeLiveDiscoveryMethod method = YouTubeLiveDiscoveryMethod.ChannelStreamsPage) =>
        new(false, channel, null, null, null, discoveredAtUtc, method, reason, []);

    public static YouTubeLiveDiscoveryResult Live(
        string? channel,
        string videoId,
        DateTimeOffset discoveredAtUtc,
        YouTubeLiveDiscoveryMethod method,
        IReadOnlyList<string> liveVideoIds) =>
        new(true,
            channel,
            videoId,
            YouTubeLiveSourceIdentity.PublicUrl(videoId),
            YouTubeLiveSourceIdentity.ChatUrl(videoId),
            discoveredAtUtc,
            method,
            null,
            liveVideoIds);
}

/// <summary>
/// Locates the current public YouTube live for the configured channel.
/// Implementations must be side-effect free and must never require Google Cloud or official API credentials.
/// </summary>
public interface IYouTubeLiveDiscovery
{
    Task<YouTubeLiveDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Single source of truth for the canonical per-live YouTube chat source identity and URLs.
/// The canonical source identity must stay <c>youtube-vid-&lt;videoId&gt;</c> so the same live always
/// reuses one source and a new live never reuses the previous one.
/// </summary>
public static class YouTubeLiveSourceIdentity
{
    public const string SourcePrefix = "youtube-vid-";

    public static string ForVideoId(string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        if (!IsVideoId(videoId))
            throw new ArgumentException("YouTube video ID must be 11 URL-safe characters.", nameof(videoId));
        return SourcePrefix + videoId;
    }

    public static string? VideoIdFromSourceId(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) ||
            !sourceId.StartsWith(SourcePrefix, StringComparison.Ordinal))
            return null;
        var videoId = sourceId[SourcePrefix.Length..];
        return IsVideoId(videoId) ? videoId : null;
    }

    public static bool IsVideoId(string? videoId) =>
        videoId is { Length: 11 } &&
        videoId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    public static string PublicUrl(string videoId) => $"https://www.youtube.com/live/{videoId}";

    public static string ChatUrl(string videoId) => $"https://www.youtube.com/live_chat?is_popout=1&v={videoId}";

    /// <summary>
    /// Builds the validated SSN locator for a discovered videoId by reusing the official
    /// live_chat popout URL rules already enforced for manual configuration.
    /// </summary>
    public static bool TryCreateLocator(string? videoId, out YouTubeLiveChatLocator? locator)
    {
        locator = null;
        if (!IsVideoId(videoId)) return false;
        if (!YouTubeLiveChatLocator.TryCreate(ChatUrl(videoId!), out var parsed)) return false;
        locator = parsed;
        return true;
    }
}

/// <summary>
/// Minimal per-live locator: the official live_chat popout URL, the video ID and the
/// SSN idempotency key that makes the same live resolve to exactly one source.
/// </summary>
public sealed record YouTubeLiveChatLocator(string Url, string VideoId, string IdempotencyKey)
{
    public static bool TryCreate(string? value, out YouTubeLiveChatLocator? locator)
    {
        locator = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host is not ("youtube.com" or "www.youtube.com") ||
            uri.AbsolutePath != "/live_chat" ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        string? videoId = null;
        var popout = false;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var name = Uri.UnescapeDataString(pair[0]);
            var queryValue = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            if (name == "v") videoId = queryValue;
            else if (name == "is_popout" && queryValue == "1") popout = true;
            else return false;
        }

        if (!popout || !YouTubeLiveSourceIdentity.IsVideoId(videoId)) return false;

        var digest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(videoId!)))
            .ToLowerInvariant()[..16];
        locator = new YouTubeLiveChatLocator(
            YouTubeLiveSourceIdentity.ChatUrl(videoId!),
            videoId!,
            $"studioos-youtube-livechat-{digest}");
        return true;
    }
}
