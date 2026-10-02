using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Application.Narration;

/// <summary>
/// Serializes dual voice playback across interactions without adding a second audio system.
///
/// Why a plain FIFO queue is not enough: the chat and assistant audio of one interaction are produced
/// at different times (the chat clip is ready immediately, the reply only after the model answers), and
/// interactions overlap. A bare FIFO would therefore let interaction B's chat clip be queued before
/// interaction A's reply, producing A chat, B chat, A assistant. This coordinator instead gives each
/// interaction a monotonically increasing group sequence and admits groups in that order, which yields
/// "A chat, A assistant, B chat, B assistant".
///
/// Ordering key: a group is ordered by the moment it is submitted, and the pipeline submits as soon as
/// the reply text is ready, with the reply synthesis still in flight. So a slow reply is spoken after a
/// fast one that arrived later, but its chat clip is never held back by its own reply. Two properties
/// are deliberately preserved:
/// the underlying narration queue is still the single bounded FIFO with a single reader, and playback is
/// still strictly serial, so two clips can never sound at the same time.
///
/// The admission gate is bounded by <see cref="MaxPendingGroups"/>: if more interactions are waiting to
/// be admitted than that, the newcomer is rejected instead of being buffered, so memory cannot grow
/// without limit and callers observe backpressure.
/// </summary>
public sealed class DualVoiceNarrationCoordinator(
    INarrationService narration,
    IOptions<NarrationOptions> options,
    ILogger<DualVoiceNarrationCoordinator> logger) : IDualVoiceNarrationCoordinator
{
    /// <summary>Upper bound on interactions admitted but not yet handed to the narration queue.</summary>
    public const int MaxPendingGroups = 8;

    private readonly object _gate = new();
    private long _nextAdmitSequence;
    private Task _tail = Task.CompletedTask;
    private int _pending;

    public Task SubmitAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The autoplay switch is enforced here as well as in the orchestrator. This is the component
        // that actually reaches the queue, so the gate belongs at the last point before playback.
        if (!options.Value.Enabled || !options.Value.AutoPlayInteractions)
        {
            logger.LogInformation(
                "NARRATION_GROUP_SKIPPED interactionId={InteractionId} reason=AUTOPLAY_DISABLED",
                request.InteractionId);
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            if (_pending >= MaxPendingGroups)
            {
                logger.LogWarning(
                    "NARRATION_GROUP_REJECTED interactionId={InteractionId} reason=COORDINATOR_SATURATED pending={Pending}",
                    request.InteractionId, _pending);
                return Task.CompletedTask;
            }

            // The sequence number and the chain link are taken together, so two concurrent submissions
            // can never overwrite each other's link or be admitted out of the order they were issued.
            var sequence = ++_nextAdmitSequence;
            _pending++;
            _tail = _tail.ContinueWith(
                _ => AdmitAsync(sequence, request),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }

        // The submit is intentionally not awaited: the caller is the interaction pipeline, which must
        // not be held up by narration. Ordering is carried by the chain, not by the returned task.
        return Task.CompletedTask;
    }

    private async Task AdmitAsync(long sequence, DualVoiceNarrationRequest request)
    {
        try
        {
            await RunGroupAsync(sequence, request).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The narrator must never take the process down, and one bad group must not stall the
            // groups behind it. A canceled role counts as a missing role, not as a broken group.
            logger.LogWarning(
                "NARRATION_GROUP_FAILED interactionId={InteractionId} groupSequence={Group} errorType={ErrorType}",
                request.InteractionId, sequence, exception.GetType().Name);
        }
        finally
        {
            lock (_gate) _pending--;
        }
    }

    private async Task RunGroupAsync(long sequence, DualVoiceNarrationRequest request)
    {
        var chat = await ResolveRoleAsync(request.ChatAudio).ConfigureAwait(false);

        // The chat clip is submitted first and is awaited, so the queue is occupied by this
        // interaction's chat audio before the assistant clip can be appended.
        if (chat is not null)
        {
            await EnqueueRoleAsync(sequence, request, chat, NarrationVoiceRole.Chat,
                options.Value.ChatVoice, NarrationOrder.Chat).ConfigureAwait(false);
        }

        var assistant = await ResolveRoleAsync(request.AssistantAudio).ConfigureAwait(false);
        if (assistant is not null)
        {
            await EnqueueRoleAsync(sequence, request, assistant, NarrationVoiceRole.Assistant,
                options.Value.AssistantVoice, NarrationOrder.Assistant).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A role is submitted only when its audio actually exists. A failed, canceled, or missing role is
    /// skipped rather than propagated, so an AI outage still lets the chat voice speak and a chat
    /// synthesis failure still lets the assistant reply speak.
    /// </summary>
    private static async Task<TextToSpeechArtifact?> ResolveRoleAsync(Task<TextToSpeechResult>? pending)
    {
        if (pending is null) return null;
        TextToSpeechResult result;
        try
        {
            result = await pending.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }

        if (!result.Success ||
            string.IsNullOrWhiteSpace(result.AudioPath) ||
            result.AudioDuration is null ||
            result.SampleRate is null ||
            result.BitDepth is null ||
            result.Channels is null)
        {
            return null;
        }

        return new TextToSpeechArtifact(
            result.AudioPath,
            result.AudioFormat,
            result.AudioDuration.Value,
            result.SampleRate.Value,
            result.BitDepth.Value,
            result.Channels.Value,
            result.ArtifactId);
    }

    private async Task EnqueueRoleAsync(
        long sequence,
        DualVoiceNarrationRequest request,
        TextToSpeechArtifact audio,
        NarrationVoiceRole role,
        NarrationVoiceRoleOptions roleOptions,
        int order)
    {
        if (!roleOptions.Enabled) return;

        var artifact = new NarrationAudioArtifact(
            request.InteractionId,
            audio.Path,
            audio.Format,
            audio.Duration,
            audio.SampleRate,
            audio.BitDepth,
            audio.Channels,
            audio.ArtifactId,
            role,
            roleOptions.Volume);

        var queued = await narration.EnqueueAsync(
            artifact,
            request.CorrelationId,
            roleOptions,
            order,
            sequence,
            CancellationToken.None).ConfigureAwait(false);

        if (!queued.Accepted)
        {
            logger.LogWarning(
                "NARRATION_ROLE_REJECTED interactionId={InteractionId} voiceRole={VoiceRole} order={Order} groupSequence={Group} errorCode={ErrorCode} correlationId={CorrelationId}",
                request.InteractionId, role, order, sequence, queued.RejectionCode, request.CorrelationId);
        }
    }

    private sealed record TextToSpeechArtifact(
        string Path,
        string Format,
        TimeSpan Duration,
        int SampleRate,
        int BitDepth,
        int Channels,
        Guid ArtifactId);
}
