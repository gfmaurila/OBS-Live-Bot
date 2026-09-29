namespace ObsLiveBot.Domain.Narration;

public enum NarrationStatus
{
    Queued = 0,
    Preparing = 1,
    Playing = 2,
    Completed = 3,
    Skipped = 4,
    Failed = 5,
    Cancelled = 6
}

public enum NarrationServiceStatus
{
    Disabled = 0,
    Ready = 1,
    Playing = 2,
    Degraded = 3,
    Unavailable = 4
}

public sealed record NarrationAudioArtifact(
    Guid InteractionId,
    string Path,
    string AudioFormat,
    TimeSpan Duration,
    int SampleRate,
    int BitDepth,
    int Channels);

public sealed record NarrationRequest(
    Guid NarrationId,
    Guid InteractionId,
    NarrationAudioArtifact AudioArtifact,
    DateTimeOffset CreatedAtUtc,
    int Priority,
    string CorrelationId);

public sealed record NarrationResult(
    Guid NarrationId,
    Guid InteractionId,
    NarrationStatus Status,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Sequence,
    string CorrelationId,
    TimeSpan? PlaybackDuration = null);

public sealed record NarrationEvent(
    Guid EventId,
    Guid NarrationId,
    Guid InteractionId,
    string EventType,
    string? ErrorCode,
    DateTimeOffset AtUtc,
    long Sequence,
    string CorrelationId);

public sealed record NarrationStateSnapshot(
    bool Enabled,
    bool AutoPlayInteractions,
    string Status,
    Guid? CurrentNarrationId,
    int QueueLength,
    int MaxQueueSize,
    int MaxConcurrentPlayback,
    string Source,
    double Volume,
    bool Muted,
    string MonitoringMode,
    IReadOnlyList<int> Tracks,
    long Queued,
    long Started,
    long Completed,
    long Failed,
    long Cancelled,
    long QueueRejected,
    double? AveragePlaybackDurationMilliseconds,
    DateTimeOffset? LastPlaybackAtUtc,
    DateTimeOffset? LastFailureAtUtc);

public sealed record NarrationEnqueueResult(
    bool Accepted,
    NarrationResult Result,
    string? RejectionCode);

public sealed record NarrationArtifactValidation(
    bool Valid,
    string? ErrorCode,
    TimeSpan Duration,
    int SampleRate,
    int BitDepth,
    int Channels);

public enum ObsMediaPlaybackState
{
    Unknown = 0,
    Opening = 1,
    Buffering = 2,
    Playing = 3,
    Paused = 4,
    Ended = 5,
    Stopped = 6,
    Error = 7
}

public sealed record NarrationPlaybackSnapshot(
    bool Available,
    bool SourceExists,
    string? SourceName,
    double? Volume,
    bool? Muted,
    string MonitoringMode,
    IReadOnlyList<int> Tracks,
    ObsMediaPlaybackState MediaState,
    string Status,
    string? ErrorCode);
