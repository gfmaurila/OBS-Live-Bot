namespace ObsLiveBot.Application.Abstractions;

/// <summary>
/// Read-only view of automatic YouTube live ownership. Exposed so the current live, the
/// discovery method and any failure reason are observable without touching OBS or SSN.
/// </summary>
public sealed record YouTubeLiveOwnershipSnapshot(
    bool Enabled,
    string? DisabledReason,
    bool ObsStreaming,
    bool OwnsCurrentLive,
    string? CurrentVideoId,
    string? CurrentSourceId,
    string? Channel,
    string? LastDiscoveryMethod,
    string? LastReason,
    DateTimeOffset? LastDiscoveryAtUtc,
    DateTimeOffset? LastReconciledAtUtc,
    int DiscoveryAttempts,
    int SourceEnsures,
    int SourceReleases,
    IReadOnlyList<string> ActiveSourceIds)
{
    public static YouTubeLiveOwnershipSnapshot Idle(bool enabled, string? disabledReason, bool obsStreaming) =>
        new(enabled, disabledReason, obsStreaming, false, null, null, null, null, null, null, null, 0, 0, 0, []);
}

public interface IYouTubeLiveOwnershipReader
{
    YouTubeLiveOwnershipSnapshot State { get; }
}
