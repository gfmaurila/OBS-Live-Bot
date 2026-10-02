using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Narration;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Application.Features.Narration;

internal static class NarrationMappings
{
    public static NarrationStateResponse Map(
        NarrationStateSnapshot state,
        NarrationOptions options) => new(
        state.Enabled, state.AutoPlayInteractions, state.Status, state.CurrentNarrationId,
        state.QueueLength, state.MaxQueueSize, state.MaxConcurrentPlayback, state.Source,
        state.Volume, state.Muted, state.MonitoringMode, state.Tracks, state.Queued,
        state.Started, state.Completed, state.Failed, state.Cancelled, state.QueueRejected,
        state.AveragePlaybackDurationMilliseconds, state.LastPlaybackAtUtc, state.LastFailureAtUtc,
        MapVoices(options));

    /// <summary>
    /// Reports the two roles in playback order, so the configured voices can be verified from the API
    /// without reading the configuration file or the model filenames.
    /// </summary>
    public static IReadOnlyList<NarrationVoiceRoleResponse> MapVoices(NarrationOptions options) =>
    [
        new(
            nameof(NarrationVoiceRole.Chat),
            options.ChatVoice.Enabled,
            options.ChatVoice.VoiceId,
            options.ChatVoice.Volume,
            options.ChatVoice.UserNameFormat),
        new(
            nameof(NarrationVoiceRole.Assistant),
            options.AssistantVoice.Enabled,
            options.AssistantVoice.VoiceId,
            options.AssistantVoice.Volume,
            string.Empty)
    ];

    public static NarrationResultResponse Map(NarrationResult result) => new(
        result.NarrationId, result.InteractionId, result.Status.ToString(), result.ErrorCode,
        result.CreatedAtUtc, result.UpdatedAtUtc, result.Sequence, result.CorrelationId,
        result.PlaybackDuration?.TotalMilliseconds, result.VoiceRole.ToString(),
        result.OrderWithinInteraction, result.GroupSequence);

    public static NarrationEventResponse Map(NarrationEvent narrationEvent) => new(
        narrationEvent.EventId, narrationEvent.NarrationId, narrationEvent.InteractionId,
        narrationEvent.EventType, narrationEvent.ErrorCode, narrationEvent.AtUtc,
        narrationEvent.Sequence, narrationEvent.CorrelationId);
}
