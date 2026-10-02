using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.YouTube;

namespace ObsLiveBot.Infrastructure.YouTube;

/// <summary>
/// Discovers the current public YouTube live for a configured channel by reading that channel's own
/// public streams page and reading the entry that carries YouTube's live badge.
///
/// This is deliberately channel-scoped public information: no Google Cloud project, no OAuth, no API
/// key, no stream key and no search results page. Only the public video ID leaves this class, and a
/// public video ID is already part of the public live URL.
/// </summary>
public sealed class YouTubeChannelStreamsPageDiscovery(
    HttpClient client,
    IOptions<YouTubeLiveDiscoveryOptions> options,
    TimeProvider timeProvider,
    ILogger<YouTubeChannelStreamsPageDiscovery> logger) : IYouTubeLiveDiscovery
{
    private readonly YouTubeLiveDiscoveryOptions _options = options.Value;

    public async Task<YouTubeLiveDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var channel = _options.Channel?.Trim();
        if (string.IsNullOrWhiteSpace(channel))
            return YouTubeLiveDiscoveryResult.NotLive(null, timeProvider.GetUtcNow(), "channel_not_configured");

        if (!YouTubeStreamsPageRules.TryBuildStreamsUrl(channel, out var url))
        {
            logger.LogWarning("YOUTUBE_DISCOVERY_CHANNEL_INVALID");
            return YouTubeLiveDiscoveryResult.NotLive(null, timeProvider.GetUtcNow(), "channel_invalid");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.RequestTimeoutSeconds, 1, 120)));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("YOUTUBE_DISCOVERY_HTTP_STATUS status={StatusCode}", (int)response.StatusCode);
                return YouTubeLiveDiscoveryResult.NotLive(channel, timeProvider.GetUtcNow(), "http_error");
            }

            var html = await ReadBoundedAsync(
                    response,
                    Math.Clamp(_options.MaxResponseBytes, 64 * 1024, 32 * 1024 * 1024),
                    timeout.Token)
                .ConfigureAwait(false);
            if (html is null)
                return YouTubeLiveDiscoveryResult.NotLive(channel, timeProvider.GetUtcNow(), "response_too_large");

            if (!YouTubeStreamsPageRules.TryExtractLiveVideoIds(html, out var discovered) ||
                discovered.Count == 0)
            {
                logger.LogInformation("YOUTUBE_DISCOVERY_NO_LIVE channel={Channel}", channel);
                return YouTubeLiveDiscoveryResult.NotLive(channel, timeProvider.GetUtcNow(), "no_live_badge");
            }

            var liveVideoIds = discovered.ToArray();
            var discoveredAtUtc = timeProvider.GetUtcNow();
            var channelId = YouTubeStreamsPageRules.TryExtractChannelId(html);
            logger.LogInformation(
                "YOUTUBE_DISCOVERY_LIVE_FOUND channel={Channel} channelId={ChannelId} videoId={VideoId} liveCandidates={LiveCandidates}",
                channel,
                channelId ?? "unknown",
                liveVideoIds[0],
                liveVideoIds.Length);
            return YouTubeLiveDiscoveryResult.Live(
                channel,
                liveVideoIds[0],
                discoveredAtUtc,
                YouTubeLiveDiscoveryMethod.ChannelStreamsPage,
                liveVideoIds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return YouTubeLiveDiscoveryResult.NotLive(channel, timeProvider.GetUtcNow(), "timeout");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("YOUTUBE_DISCOVERY_REQUEST_FAILED errorType={ErrorType}", exception.GetType().Name);
            return YouTubeLiveDiscoveryResult.NotLive(channel, timeProvider.GetUtcNow(), "request_failed");
        }
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/>; a page beyond the cap is refused rather than
    /// buffered, so a hostile or broken response cannot exhaust memory.
    /// </summary>
    private static async Task<string?> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var memory = new MemoryStream(capacity: Math.Min(maxBytes, 128 * 1024));
            while (memory.Length < maxBytes)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > maxBytes) return null;
                memory.Write(buffer, 0, read);
            }
            return System.Text.Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
