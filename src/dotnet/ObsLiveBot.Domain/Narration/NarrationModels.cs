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

/// <summary>
/// Semantic role of the audio being narrated. This is a domain concept, not a filename or a
/// gender/voice-quality label: Chat reads an incoming chat message aloud, Assistant speaks the
/// generated StudioOS reply. Each role is independently configurable and may use a different voice.
/// </summary>
public enum NarrationVoiceRole
{
    // Assistant is deliberately the zero value: every narration recorded before dual voice existed
    // was an assistant response, so defaulting to Assistant keeps historical data meaningful.
    Assistant = 0,
    Chat = 1
}

/// <summary>
/// Ordering of the two narration items that belong to a single interaction. Chat is always 1 and
/// Assistant is always 2, which is what guarantees the chat message is heard before the reply.
/// </summary>
public static class NarrationOrder
{
    public const int Chat = 1;
    public const int Assistant = 2;
}

public sealed record NarrationAudioArtifact(
    Guid InteractionId,
    string Path,
    string AudioFormat,
    TimeSpan Duration,
    int SampleRate,
    int BitDepth,
    int Channels,
    Guid ArtifactId = default,
    NarrationVoiceRole VoiceRole = NarrationVoiceRole.Assistant,
    double? Volume = null)
{
    /// <summary>
    /// Identifies the WAV itself. One interaction can own two artifacts (chat and assistant), so the
    /// artifact identity is distinct from the interaction identity and drives the file name.
    /// </summary>
    public Guid EffectiveArtifactId => ArtifactId == Guid.Empty ? InteractionId : ArtifactId;
}

public sealed record NarrationRequest(
    Guid NarrationId,
    Guid InteractionId,
    NarrationAudioArtifact AudioArtifact,
    DateTimeOffset CreatedAtUtc,
    int Priority,
    string CorrelationId,
    NarrationVoiceRole VoiceRole = NarrationVoiceRole.Assistant,
    int OrderWithinInteraction = 0,
    long GroupSequence = 0,
    double? Volume = null,
    Guid ArtifactId = default,
    string? VoiceId = null)
{
    public Guid EffectiveArtifactId => ArtifactId == Guid.Empty ? InteractionId : ArtifactId;
}

public sealed record NarrationResult(
    Guid NarrationId,
    Guid InteractionId,
    NarrationStatus Status,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Sequence,
    string CorrelationId,
    TimeSpan? PlaybackDuration = null,
    NarrationVoiceRole VoiceRole = NarrationVoiceRole.Assistant,
    int OrderWithinInteraction = 0,
    long GroupSequence = 0,
    string? VoiceId = null,
    DateTimeOffset? PlaybackStartedAtUtc = null,
    DateTimeOffset? PlaybackCompletedAtUtc = null,
    TimeSpan? QueueWaitDuration = null);

public sealed record NarrationEvent(
    Guid EventId,
    Guid NarrationId,
    Guid InteractionId,
    string EventType,
    string? ErrorCode,
    DateTimeOffset AtUtc,
    long Sequence,
    string CorrelationId,
    NarrationVoiceRole VoiceRole = NarrationVoiceRole.Assistant);

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
