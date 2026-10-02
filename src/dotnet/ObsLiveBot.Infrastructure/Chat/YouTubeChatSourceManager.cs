using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Chat;

/// <summary>
/// Owns the SSN source lifecycle for exactly one current live.
/// Guarantees the canonical identity <c>youtube-vid-&lt;videoId&gt;</c>, reuses an existing source for
/// the same live, retires only duplicates of the same live, and never touches another live's sources.
/// </summary>
public sealed class YouTubeChatSourceManager(
    SocialStreamNinjaCommandClient commands,
    ILogger<YouTubeChatSourceManager> logger) : IYouTubeChatSourceManager
{
    public async Task<YouTubeChatSourceEnsureResult> EnsureCurrentLiveSourceAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        if (!YouTubeLiveSourceIdentity.IsVideoId(videoId))
            return YouTubeChatSourceEnsureResult.Failed(videoId ?? string.Empty, "invalid_video_id");

        var canonicalId = YouTubeLiveSourceIdentity.ForVideoId(videoId);

        var sources = await commands.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        var existing = sources.FirstOrDefault(source =>
            string.Equals(source.Id, canonicalId, StringComparison.Ordinal));

        int retiredDuplicates = 0;
        if (existing is not null)
        {
            // The same live already has its canonical source: never add a second one.
            if (existing.Active)
            {
                retiredDuplicates = await RetireDuplicatesAsync(sources, canonicalId, videoId, cancellationToken)
                    .ConfigureAwait(false);
                return new YouTubeChatSourceEnsureResult(
                    true, canonicalId, videoId, YouTubeChatSourceOutcome.Reused, retiredDuplicates, null);
            }

            await commands.StartSourceAsync(canonicalId, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("YOUTUBE_CHAT_SOURCE_RESTARTED sourceId={SourceId} videoId={VideoId}", canonicalId, videoId);
            sources = await commands.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
            retiredDuplicates = await RetireDuplicatesAsync(sources, canonicalId, videoId, cancellationToken)
                .ConfigureAwait(false);
            return new YouTubeChatSourceEnsureResult(
                true, canonicalId, videoId, YouTubeChatSourceOutcome.Started, retiredDuplicates, null);
        }

        var added = await commands.AddYouTubeLiveChatSourceAsync(videoId, cancellationToken).ConfigureAwait(false);
        if (added is null)
            return YouTubeChatSourceEnsureResult.Failed(videoId, "source_not_returned");

        var sourceId = string.IsNullOrWhiteSpace(added.Id) ? canonicalId : added.Id;
        if (!string.Equals(sourceId, canonicalId, StringComparison.Ordinal))
            logger.LogWarning(
                "YOUTUBE_CHAT_SOURCE_ID_MISMATCH expected={Expected} actual={Actual}",
                canonicalId,
                sourceId);

        if (!added.Active)
            await commands.StartSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);

        sources = await commands.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        if (sources.All(source => !string.Equals(source.Id, sourceId, StringComparison.Ordinal)))
            return YouTubeChatSourceEnsureResult.Failed(videoId, "source_not_listed");

        retiredDuplicates = await RetireDuplicatesAsync(sources, sourceId, videoId, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "YOUTUBE_CHAT_SOURCE_CREATED sourceId={SourceId} videoId={VideoId} retiredDuplicates={RetiredDuplicates}",
            sourceId,
            videoId,
            retiredDuplicates);
        return new YouTubeChatSourceEnsureResult(
            true, sourceId, videoId, YouTubeChatSourceOutcome.Created, retiredDuplicates, null);
    }

    public async Task<YouTubeChatSourceReleaseResult> ReleaseCurrentLiveSourceAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        if (!YouTubeLiveSourceIdentity.IsVideoId(videoId))
            return new YouTubeChatSourceReleaseResult(false, null, videoId, false, "invalid_video_id");

        var canonicalId = YouTubeLiveSourceIdentity.ForVideoId(videoId);
        var sources = await commands.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        var source = sources.FirstOrDefault(item => string.Equals(item.Id, canonicalId, StringComparison.Ordinal));
        if (source is null)
            return new YouTubeChatSourceReleaseResult(true, canonicalId, videoId, false, "source_absent");

        if (!source.Active)
            return new YouTubeChatSourceReleaseResult(true, canonicalId, videoId, false, "already_inactive");

        // Stopping preserves the SSN source record, so the finished live stays auditable and reversible.
        await commands.StopSourceAsync(canonicalId, cancellationToken).ConfigureAwait(false);
        return new YouTubeChatSourceReleaseResult(true, canonicalId, videoId, true, null);
    }

    public async Task<IReadOnlyList<string>> GetActiveSourceIdsAsync(CancellationToken cancellationToken)
    {
        var sources = await commands.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        return sources
            .Where(source => source.Active)
            .Select(source => source.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Stops only active duplicates of the same live. Sources of other lives and non-YouTube
    /// sources are left untouched so a previous live is never reused for a new one.
    /// </summary>
    private async Task<int> RetireDuplicatesAsync(
        IReadOnlyList<SocialStreamNinjaSourceInfo> sources,
        string canonicalId,
        string videoId,
        CancellationToken cancellationToken)
    {
        var duplicates = sources.Where(source =>
            string.Equals(source.Target, "youtube", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(source.Id, canonicalId, StringComparison.Ordinal) &&
            source.Active &&
            string.Equals(source.VideoId, videoId, StringComparison.Ordinal)).ToArray();

        foreach (var duplicate in duplicates)
        {
            await commands.StopSourceAsync(duplicate.Id, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "YOUTUBE_CHAT_SOURCE_DUPLICATE_STOPPED sourceId={SourceId} videoId={VideoId} canonical={CanonicalId}",
                duplicate.Id,
                videoId,
                canonicalId);
        }
        return duplicates.Length;
    }
}
