using System.Text.RegularExpressions;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Application.YouTube;

/// <summary>
/// Rules for reading a public YouTube channel streams page.
///
/// The page is public channel information, so parsing it needs no credential, no API key and no
/// Google Cloud project. These rules live in the Application layer because "what counts as a live
/// entry" is domain knowledge; the HTTP fetch stays in Infrastructure.
/// </summary>
public static partial class YouTubeStreamsPageRules
{
    // YouTube marks the currently live entry with a LIVE thumbnail badge whose animation
    // activation target is that entry's video ID. A finished live, a playlist and a scheduled
    // premiere all use different badge styles, so they are never reported as live.
    [GeneratedRegex(
        "THUMBNAIL_OVERLAY_BADGE_STYLE_LIVE\",\"animationActivationTargetId\":\"(?<videoId>[A-Za-z0-9_-]{11})\"",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        2000)]
    private static partial Regex LiveBadgeRegex();

    [GeneratedRegex(
        "\"browseId\":\"(?<channelId>UC[A-Za-z0-9_-]{22})\"",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        2000)]
    private static partial Regex ChannelIdRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ChannelHandleRegex();

    /// <summary>
    /// Extracts the public video IDs of entries carrying YouTube's live badge, in page order.
    /// A channel's streams page is recency ordered, so the first live badge is the most recent live.
    /// </summary>
    public static bool TryExtractLiveVideoIds(string html, out IReadOnlyList<string> videoIds)
    {
        var matches = LiveBadgeRegex()
            .Matches(html)
            .Select(match => match.Groups["videoId"].Value)
            .Where(YouTubeLiveSourceIdentity.IsVideoId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        videoIds = matches;
        return matches.Length > 0;
    }

    /// <summary>Reads the public channel ID, when the page exposes one, for logging only.</summary>
    public static string? TryExtractChannelId(string html)
    {
        var match = ChannelIdRegex().Match(html);
        return match.Success ? match.Groups["channelId"].Value : null;
    }

    /// <summary>
    /// Builds the public streams page URL from a plain handle or a UC channel ID.
    /// Anything else is rejected, so a configured channel can never become an arbitrary URL,
    /// a different host, or a path that escapes the channel.
    /// </summary>
    public static bool TryBuildStreamsUrl(string? channel, out string url)
    {
        url = string.Empty;
        var trimmed = channel?.Trim().TrimStart('@');
        if (string.IsNullOrEmpty(trimmed)) return false;

        if (trimmed.StartsWith("UC", StringComparison.Ordinal) && trimmed.Length == 24 &&
            trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
        {
            url = $"https://www.youtube.com/channel/{trimmed}/streams";
            return true;
        }

        if (!ChannelHandleRegex().IsMatch(trimmed)) return false;
        url = $"https://www.youtube.com/@{trimmed}/streams";
        return true;
    }
}
