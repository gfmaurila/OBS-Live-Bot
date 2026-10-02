namespace ObsLiveBot.Contracts.Narration;

/// <summary>
/// One configured narration voice role. The role is semantic and never inferred from the voice file:
/// Chat is the deterministic repeat of the viewer message, Assistant is the generated reply.
/// </summary>
public sealed record NarrationVoiceRoleResponse(
    string Role,
    bool Enabled,
    string VoiceId,
    double? Volume,
    string UserNameFormat);

public sealed record NarrationStateResponse(
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
    DateTimeOffset? LastFailureAtUtc,
    IReadOnlyList<NarrationVoiceRoleResponse> Voices);

public sealed record NarrationResultResponse(
    Guid NarrationId,
    Guid InteractionId,
    string Status,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Sequence,
    string CorrelationId,
    double? PlaybackDurationMilliseconds,
    string? VoiceRole,
    int? OrderWithinInteraction,
    long? GroupSequence);

public sealed record NarrationEventResponse(
    Guid EventId,
    Guid NarrationId,
    Guid InteractionId,
    string EventType,
    string? ErrorCode,
    DateTimeOffset AtUtc,
    long Sequence,
    string CorrelationId);

public sealed record NarrationRecentResponse(
    IReadOnlyList<NarrationResultResponse> Narrations,
    IReadOnlyList<NarrationEventResponse> Events);

public sealed record NarrationDevTestRequest(string Text);

public sealed record NarrationDevTestResponse(
    bool Accepted,
    string? ErrorCode,
    string TtsProvider,
    double? AudioDurationSeconds,
    NarrationResultResponse? Narration);

public sealed record NarrationMuteRequest(bool Muted);

public sealed record NarrationVolumeRequest(double Volume);
