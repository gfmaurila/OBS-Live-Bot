using System.Security.Cryptography;
using System.Text;

namespace ObsLiveBot.Infrastructure.Chat;

public sealed record YouTubeLiveChatSourceLocator(
    string Url,
    string VideoId,
    string IdempotencyKey)
{
    public static bool TryCreate(string? value, out YouTubeLiveChatSourceLocator? locator)
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

        if (!popout || videoId is not { Length: 11 } ||
            !videoId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            return false;

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(videoId)))
            .ToLowerInvariant()[..16];
        locator = new YouTubeLiveChatSourceLocator(
            $"https://www.youtube.com/live_chat?is_popout=1&v={videoId}",
            videoId,
            $"studioos-youtube-livechat-{digest}");
        return true;
    }
}
