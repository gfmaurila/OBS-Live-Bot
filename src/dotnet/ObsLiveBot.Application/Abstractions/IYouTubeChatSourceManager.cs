namespace ObsLiveBot.Application.Abstractions;

public enum YouTubeChatSourceOutcome
{
    /// <summary>The canonical source did not exist and was created, then started.</summary>
    Created,

    /// <summary>The canonical source already existed and was reused without duplication.</summary>
    Reused,

    /// <summary>The canonical source existed but was stopped and had to be started again.</summary>
    Started,

    /// <summary>Discovery or SSN rejected the request; nothing was changed.</summary>
    Failed
}

public sealed record YouTubeChatSourceEnsureResult(
    bool Success,
    string? SourceId,
    string? VideoId,
    YouTubeChatSourceOutcome Outcome,
    int RetiredDuplicateCount,
    string? Reason)
{
    public static YouTubeChatSourceEnsureResult Failed(string videoId, string reason) =>
        new(false, null, videoId, YouTubeChatSourceOutcome.Failed, 0, reason);
}

public sealed record YouTubeChatSourceReleaseResult(
    bool Success,
    string? SourceId,
    string? VideoId,
    bool Stopped,
    string? Reason);

/// <summary>
/// Owns the Social Stream Ninja source lifecycle for exactly one current live.
/// This is the only boundary that creates, starts, reuses or stops the canonical
/// <c>youtube-vid-&lt;videoId&gt;</c> source, keeping OBS WebSocket code decoupled from SSN.
/// </summary>
public interface IYouTubeChatSourceManager
{
    Task<YouTubeChatSourceEnsureResult> EnsureCurrentLiveSourceAsync(string videoId, CancellationToken cancellationToken);

    /// <summary>Stops the canonical source for the finished live, preserving the record for audit/rollback.</summary>
    Task<YouTubeChatSourceReleaseResult> ReleaseCurrentLiveSourceAsync(string videoId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetActiveSourceIdsAsync(CancellationToken cancellationToken);
}
