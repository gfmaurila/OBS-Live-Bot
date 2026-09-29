using ObsLiveBot.Contracts.Narration;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Application.Features.Narration;

internal static class NarrationMappings
{
    public static NarrationStateResponse Map(NarrationStateSnapshot state) => new(
        state.Enabled, state.AutoPlayInteractions, state.Status, state.CurrentNarrationId,
        state.QueueLength, state.MaxQueueSize, state.MaxConcurrentPlayback, state.Source,
        state.Volume, state.Muted, state.MonitoringMode, state.Tracks, state.Queued,
        state.Started, state.Completed, state.Failed, state.Cancelled, state.QueueRejected,
        state.AveragePlaybackDurationMilliseconds, state.LastPlaybackAtUtc, state.LastFailureAtUtc);

    public static NarrationResultResponse Map(NarrationResult result) => new(
        result.NarrationId, result.InteractionId, result.Status.ToString(), result.ErrorCode,
        result.CreatedAtUtc, result.UpdatedAtUtc, result.Sequence, result.CorrelationId,
        result.PlaybackDuration?.TotalMilliseconds);

    public static NarrationEventResponse Map(NarrationEvent narrationEvent) => new(
        narrationEvent.EventId, narrationEvent.NarrationId, narrationEvent.InteractionId,
        narrationEvent.EventType, narrationEvent.ErrorCode, narrationEvent.AtUtc,
        narrationEvent.Sequence, narrationEvent.CorrelationId);
}
