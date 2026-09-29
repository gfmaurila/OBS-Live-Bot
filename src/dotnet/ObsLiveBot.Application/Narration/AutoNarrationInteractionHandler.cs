using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Application.Narration;

public sealed class AutoNarrationInteractionHandler(
    IOptions<NarrationOptions> options,
    INarrationService narration,
    ILogger<AutoNarrationInteractionHandler> logger)
    : INotificationHandler<InteractionCompletedNotification>
{
    public async Task Handle(InteractionCompletedNotification notification, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !options.Value.AutoPlayInteractions) return;
        var result = notification.Result;
        if (result.TtsSuccess != true || result.TtsSimulated == true ||
            string.IsNullOrWhiteSpace(result.AudioPath) || result.AudioDuration is null)
            return;

        var artifact = new NarrationAudioArtifact(
            result.InteractionId,
            result.AudioPath,
            result.AudioFormat ?? "audio/wav",
            result.AudioDuration.Value,
            result.SampleRate ?? 0,
            result.BitDepth ?? 0,
            result.Channels ?? 0);
        var queued = await narration.EnqueueAsync(artifact, result.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        if (!queued.Accepted)
        {
            logger.LogWarning(
                "NARRATION_AUTOPLAY_REJECTED narrationId={NarrationId} interactionId={InteractionId} errorCode={ErrorCode} correlationId={CorrelationId}",
                queued.Result.NarrationId, result.InteractionId, queued.RejectionCode, result.CorrelationId);
        }
    }
}
