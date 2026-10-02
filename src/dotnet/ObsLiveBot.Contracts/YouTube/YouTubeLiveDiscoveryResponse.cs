namespace ObsLiveBot.Contracts.YouTube;

/// <summary>
/// Read-only view of automatic YouTube live ownership. Contains only public values:
/// a channel, a public video ID, a public live URL and public chat URL. No secrets.
/// </summary>
public sealed record YouTubeLiveDiscoveryResponse(
    bool Enabled,
    string? DisabledReason,
    bool ObsStreaming,
    bool OwnsCurrentLive,
    string? Channel,
    string? CurrentVideoId,
    string? CurrentPublicUrl,
    string? CurrentChatUrl,
    string? CurrentSourceId,
    string? LastDiscoveryMethod,
    string? LastReason,
    DateTimeOffset? LastDiscoveryAtUtc,
    DateTimeOffset? LastReconciledAtUtc,
    int DiscoveryAttempts,
    int SourceEnsures,
    int SourceReleases,
    IReadOnlyList<string> ActiveSourceIds);
