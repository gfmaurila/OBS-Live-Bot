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
/// interaction A's reply, producing A chat, B chat, A assistant.
///
/// Strict acceptance order: a group is admitted only after every group accepted before it has been
/// fully admitted, and admission drains in ascending <see cref="DualVoiceNarrationRequest.AcceptedSequence"/>.
/// The key is taken atomically by the interaction pipeline at the moment it accepts the message, so
/// playback follows the order messages were accepted, not the order replies happened to be produced.
/// A fast reply for a later message therefore cannot overtake an earlier slow one. AI generation and
/// TTS synthesis for several interactions still run fully concurrently: what is serialized is only
/// playback eligibility, which is exactly what has to be.
///
/// Bounded by construction: the reorder buffer is a dictionary whose size is capped at
/// <see cref="MaxPendingGroups"/> on every insert, so a burst of accepted messages can never grow it
/// without limit. Past the cap the newcomer is rejected and the interaction still completes as text.
///
/// A slot can never hold the line forever. If an interaction's own audio never becomes available the
/// slot is force-closed after <see cref="NarrationOptions.GroupAdmissionTimeoutSeconds"/> and the
/// following interactions are released; the abandoned group is canceled so it can never enqueue late
/// and break the order behind it. The underlying narration queue is still the single bounded FIFO with
/// a single reader, and playback is still strictly serial, so two clips can never sound together.
/// </summary>
public sealed class DualVoiceNarrationCoordinator(
    INarrationService narration,
    IOptions<NarrationOptions> options,
    ILogger<DualVoiceNarrationCoordinator> logger) : IDualVoiceNarrationCoordinator
{
    /// <summary>Upper bound on interactions holding a slot but not yet fully admitted.</summary>
    public const int MaxPendingGroups = 8;

    private readonly object _gate = new();

    /// <summary>
    /// Accepted but not yet admitted groups, ordered by acceptance sequence. Every insert is checked
    /// against <see cref="MaxPendingGroups"/>, so this never grows beyond the cap.
    /// </summary>
    private readonly SortedDictionary<long, PendingGroup> _pending = new();

    /// <summary>The group currently being drained, so only one drain loop is ever active.</summary>
    private PendingGroup? _active;

    /// <summary>Slots in use, including the active one. This is what the cap is applied to.</summary>
    private int _openSlots;

    public Task SubmitAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken) =>
        Admit(request, bypassAutoplayGate: false);

    public Task SubmitDevelopmentTestAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken) =>
        Admit(request, bypassAutoplayGate: true);

    private Task Admit(DualVoiceNarrationRequest request, bool bypassAutoplayGate)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The autoplay switch is enforced here as well as in the orchestrator. This is the component
        // that actually reaches the queue, so the gate belongs at the last point before playback. Only an
        // explicit development test may pass it, and it never changes the configured switch itself.
        if (!options.Value.Enabled || (!bypassAutoplayGate && !options.Value.AutoPlayInteractions))
        {
            logger.LogInformation(
                "NARRATION_GROUP_SKIPPED interactionId={InteractionId} reason=AUTOPLAY_DISABLED developmentTest={DevelopmentTest}",
                request.InteractionId, bypassAutoplayGate);
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            if (_openSlots >= MaxPendingGroups)
            {
                logger.LogWarning(
                    "NARRATION_GROUP_REJECTED interactionId={InteractionId} reason=COORDINATOR_SATURATED openSlots={OpenSlots} limit={Limit}",
                    request.InteractionId, _openSlots, MaxPendingGroups);
                return Task.CompletedTask;
            }

            if (_pending.ContainsKey(request.AcceptedSequence) ||
                _active?.Request.AcceptedSequence == request.AcceptedSequence)
            {
                logger.LogWarning(
                    "NARRATION_GROUP_REJECTED interactionId={InteractionId} reason=DUPLICATE_ACCEPTED_SEQUENCE acceptedSequence={AcceptedSequence}",
                    request.InteractionId, request.AcceptedSequence);
                return Task.CompletedTask;
            }

            _pending.Add(request.AcceptedSequence, new PendingGroup(request.AcceptedSequence, request));
            _openSlots++;
        }

        // Not awaited: the caller is the interaction pipeline, which must not be held up by narration.
        // Order is carried by the acceptance sequence in the buffer, not by the returned task.
        StartDrain();
        return Task.CompletedTask;
    }

    private void StartDrain()
    {
        // Fire and forget, but the returned task is fully observed: DrainAsync catches everything and
        // never faults, so shutdown cannot leave an unobserved task behind.
        _ = Task.Run(DrainAsync);
    }

    private async Task DrainAsync()
    {
        try
        {
            while (true)
            {
                PendingGroup next;
                lock (_gate)
                {
                    // Another submit already started draining. That drainer re-checks the buffer when it
                    // comes back round, so this one can simply step aside without stalling anything.
                    if (_active is not null) return;
                    if (_pending.Count == 0) return;

                    // Lowest acceptance sequence wins, whatever order the replies arrived in.
                    next = _pending.First().Value;
                    _pending.Remove(next.AcceptedSequence);
                    _active = next;
                }

                try
                {
                    await AdmitWithTimeoutAsync(next).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // The narrator must never take the process down, and one bad group must not stall the
                    // groups behind it. A canceled role counts as a missing role, not a broken group.
                    logger.LogWarning(
                        "NARRATION_GROUP_FAILED interactionId={InteractionId} groupSequence={Group} errorType={ErrorType}",
                        next.Request.InteractionId, next.AcceptedSequence, exception.GetType().Name);
                }
                finally
                {
                    // The slot is released before the next group is picked up, which is what lets the
                    // following interactions move on.
                    lock (_gate)
                    {
                        _active = null;
                        _openSlots--;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "NARRATION_DRAIN_STOPPED errorType={ErrorType}",
                exception.GetType().Name);
        }
    }

    /// <summary>
    /// Admits one group under a deadline. If the interaction's own audio never turns up the slot is
    /// released anyway, so a hung AI call or synthesis cannot stall every later interaction.
    /// </summary>
    private async Task AdmitWithTimeoutAsync(PendingGroup group)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.GroupAdmissionTimeoutSeconds));
        using var slot = new CancellationTokenSource();

        var admission = RunGroupAsync(group, slot.Token);
        var finished = await Task.WhenAny(admission, Task.Delay(timeout, CancellationToken.None))
            .ConfigureAwait(false);

        if (finished == admission)
        {
            await admission.ConfigureAwait(false);
            return;
        }

        // Cancel the abandoned group so a role that resolves later cannot enqueue behind the groups
        // that have already been released.
        await slot.CancelAsync().ConfigureAwait(false);
        Observe(admission);

        logger.LogWarning(
            "NARRATION_GROUP_TIMEOUT interactionId={InteractionId} groupSequence={Group} timeoutSeconds={TimeoutSeconds} released=next-groups",
            group.Request.InteractionId, group.AcceptedSequence, timeout.TotalSeconds);
    }

    /// <summary>Attaches a fault-observing continuation so a late fault is never left unobserved.</summary>
    private static void Observe(Task task) =>
        _ = task.ContinueWith(
            static faulted => _ = faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task RunGroupAsync(PendingGroup group, CancellationToken slotToken)
    {
        var request = group.Request;

        var chat = await ResolveRoleAsync(request.ChatAudio).ConfigureAwait(false);
        if (chat is not null && !slotToken.IsCancellationRequested)
        {
            // Enqueued first and awaited, so the queue is occupied by this interaction's chat audio
            // before its assistant clip can be appended.
            await EnqueueRoleAsync(group.AcceptedSequence, request, chat, NarrationVoiceRole.Chat,
                options.Value.ChatVoice, NarrationOrder.Chat, slotToken).ConfigureAwait(false);
        }

        // A chat failure must not permanently block this interaction's assistant clip, so the two roles
        // are resolved independently and the slot always closes.
        var assistant = await ResolveRoleAsync(request.AssistantAudio).ConfigureAwait(false);
        if (assistant is not null && !slotToken.IsCancellationRequested)
        {
            await EnqueueRoleAsync(group.AcceptedSequence, request, assistant, NarrationVoiceRole.Assistant,
                options.Value.AssistantVoice, NarrationOrder.Assistant, slotToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "NARRATION_GROUP_ADMITTED interactionId={InteractionId} groupSequence={Group} acceptedAtOffsetMs={AcceptedAtOffsetMs} correlationId={CorrelationId}",
            request.InteractionId, group.AcceptedSequence, request.AcceptedAtOffset.TotalMilliseconds,
            request.CorrelationId);
    }

    /// <summary>
    /// A role is submitted only when its audio actually exists. A failed, canceled, or missing role is
    /// skipped rather than propagated, so an AI outage still lets the chat voice speak and a chat
    /// synthesis failure still lets the assistant reply speak.
    /// </summary>
    private static async Task<TextToSpeechArtifact?> ResolveRoleAsync(Task<TextToSpeechResult?>? pending)
    {
        if (pending is null) return null;
        TextToSpeechResult? result;
        try
        {
            result = await pending.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }

        // Null is the pipeline saying this role will not exist, which is a missing role, not a fault.
        if (result is null ||
            !result.Success ||
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
        int order,
        CancellationToken slotToken)
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
            slotToken).ConfigureAwait(false);

        if (!queued.Accepted)
        {
            logger.LogWarning(
                "NARRATION_ROLE_REJECTED interactionId={InteractionId} voiceRole={VoiceRole} order={Order} groupSequence={Group} errorCode={ErrorCode} correlationId={CorrelationId}",
                request.InteractionId, role, order, sequence, queued.RejectionCode, request.CorrelationId);
        }
    }

    private sealed record PendingGroup(long AcceptedSequence, DualVoiceNarrationRequest Request);

    private sealed record TextToSpeechArtifact(
        string Path,
        string Format,
        TimeSpan Duration,
        int SampleRate,
        int BitDepth,
        int Channels,
        Guid ArtifactId);
}
