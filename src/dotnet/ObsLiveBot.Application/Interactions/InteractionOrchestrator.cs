using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;
using ObsLiveBot.Domain.Narration;
using System.Collections.Generic;

namespace ObsLiveBot.Application.Interactions;

public sealed class InteractionOrchestrator(
    IInteractionDecisionPolicy decisionPolicy,
    IInteractionCooldownTracker cooldown,
    IInteractionContextBuilder contextBuilder,
    IChatSpeechBuilder chatSpeechBuilder,
    IDualVoiceNarrationCoordinator dualVoiceCoordinator,
    IInteractionProviderRegistry providers,
    IAiResponseSanitizer sanitizer,
    IInteractionBuffer buffer,
    IInteractionEventPublisher eventPublisher,
    IPublisher mediator,
    IOptions<InteractionOptions> options,
    IOptions<NarrationOptions> narrationOptions,
    TimeProvider timeProvider,
    ILogger<InteractionOrchestrator> logger) : IInteractionOrchestrator
{
    private long _sequence;
    private readonly object _dedupeGate = new();
    private readonly HashSet<string> _processedEvents = new(StringComparer.Ordinal);
    private readonly Queue<string> _processedOrder = new();

    public async Task<InteractionResult> ProcessAsync(
        LiveChatEvent chatEvent,
        InteractionResponseMode? requestedMode,
        CancellationToken cancellationToken)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var decision = decisionPolicy.Decide(chatEvent, requestedMode, sequence);
        if (!TryMarkUnique(chatEvent))
        {
            decision = decision with
            {
                DecisionType = InteractionDecisionType.Ignore,
                Reason = "Duplicate",
                RequestedResponseMode = InteractionResponseMode.None
            };
        }
        else if (decision.DecisionType == InteractionDecisionType.Respond && requestedMode is null)
        {
            var cooldownResult = cooldown.CheckAutomatic(
                decision.Provider, decision.ChannelId, decision.UserId);
            if (!cooldownResult.Accepted)
            {
                decision = decision with
                {
                    DecisionType = InteractionDecisionType.Ignore,
                    Reason = cooldownResult.RejectionReason ?? "CooldownActive",
                    RequestedResponseMode = InteractionResponseMode.None
                };
            }
        }
        else if (decision.DecisionType == InteractionDecisionType.Respond &&
                 !cooldown.TryAcquire(decision.Provider, decision.ChannelId, decision.UserId))
        {
            decision = decision with
            {
                DecisionType = InteractionDecisionType.Ignore,
                Reason = "CooldownActive",
                RequestedResponseMode = InteractionResponseMode.None
            };
        }

        logger.LogInformation(
            "INTERACTION_DECISION interactionId={InteractionId} provider={Provider} providerMessageId={ProviderMessageId} providerUserId={ProviderUserId} trigger={Trigger} decision={Decision} reason={Reason} receivedAt={ReceivedAt} acceptedAt={AcceptedAt} correlationId={CorrelationId}",
            decision.DecisionId,
            decision.Provider,
            decision.ProviderMessageId,
            decision.UserId,
            decision.Reason is "CommandTrigger" or "BotMention" ? decision.Reason : null,
            decision.DecisionType,
            decision.Reason,
            decision.CreatedAtUtc,
            decision.DecisionType == InteractionDecisionType.Respond ? timeProvider.GetUtcNow() : null,
            decision.CorrelationId);

        await mediator.Publish(new InteractionDecidedNotification(decision), cancellationToken)
            .ConfigureAwait(false);

        if (decision.DecisionType == InteractionDecisionType.Ignore)
        {
            var ignored = CreateResult(decision, InteractionStatus.Ignored);
            await CompleteAsync(ignored, cancellationToken).ConfigureAwait(false);
            return ignored;
        }

        var acceptedAt = timeProvider.GetUtcNow();
        var acceptedAtOffset = acceptedAt - decision.CreatedAtUtc;
        var voiceRequested = decision.RequestedResponseMode is
            InteractionResponseMode.Voice or InteractionResponseMode.TextAndVoice;

        // The chat clip does not depend on the model, so it is prepared and started before the AI call
        // and awaited afterwards. Both branches therefore overlap instead of running back to back.
        var speechBuildStartedAt = timeProvider.GetUtcNow();
        var chatSpeech = BuildChatSpeech(chatEvent, decision, voiceRequested);
        var speechBuildDuration = timeProvider.GetUtcNow() - speechBuildStartedAt;
        var chatTtsStartedAt = timeProvider.GetUtcNow();
        var chatTtsTask = chatSpeech is null
            ? null
            : StartChatSynthesis(decision, chatSpeech);

        // The admission slot is reserved here, at acceptance, and not after the model answers. The
        // coordinator admits groups in acceptance order, so a fast reply for a later message can no
        // longer overtake an earlier slow one, while the AI call and the synthesis of every accepted
        // interaction still run concurrently with one another.
        //
        // The assistant half does not exist yet, so the slot is completed through a promise that every
        // exit path settles. A null result means the assistant clip is never going to happen, which is
        // how a failed AI call, an unusable reply or a text-only interaction still closes its own slot
        // and releases the interactions waiting behind it.
        var assistantSlot = new TaskCompletionSource<TextToSpeechResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ReserveAdmissionSlot(decision, chatTtsTask, assistantSlot, voiceRequested, acceptedAtOffset);

        try
        {
            var aiStartedAt = timeProvider.GetUtcNow();
            logger.LogInformation(
                "INTERACTION_AI_STARTED interactionId={InteractionId} provider={Provider} providerMessageId={ProviderMessageId} correlationId={CorrelationId} startedAt={StartedAt}",
                decision.DecisionId, decision.Provider, decision.ProviderMessageId, decision.CorrelationId, aiStartedAt);
            var aiOutcome = await GenerateAiAsync(
                contextBuilder.Build(chatEvent, decision), decision, cancellationToken).ConfigureAwait(false);
            var aiResponse = aiOutcome.Response;
            var aiReadyAt = timeProvider.GetUtcNow();
            logger.LogInformation(
                "INTERACTION_AI_COMPLETED interactionId={InteractionId} provider={Provider} success={Success} providerName={AiProvider} durationMs={DurationMs} correlationId={CorrelationId} completedAt={CompletedAt}",
                decision.DecisionId, decision.Provider, aiResponse.Success, aiResponse.ProviderName,
                aiResponse.Duration.TotalMilliseconds, decision.CorrelationId, timeProvider.GetUtcNow());

                if (!aiResponse.Success)
                {
                    // The model failed. The reserved slot is closed by the finally below, which leaves the
                    // chat clip to be spoken on its own instead of being discarded with the interaction.
                    return await FailAsync(
                    decision,
                    aiResponse.ErrorCode ?? "AI_PROVIDER_FAILED",
                    cancellationToken,
                    aiResponse.ProviderName,
                    aiResponse.ModelName,
                    false,
                    aiResponse.Duration,
                    aiFallbackUsed: aiOutcome.FallbackUsed,
                    primaryAiErrorCode: aiOutcome.PrimaryErrorCode).ConfigureAwait(false);
            }

            var sanitized = sanitizer.Sanitize(aiResponse.Text);
                if (!sanitized.Success)
                {
                    // Same here: the slot closes without an assistant clip and the chat voice still speaks.
                    return await FailAsync(
                    decision,
                    sanitized.ErrorCode ?? "AI_RESPONSE_INVALID",
                    cancellationToken,
                    aiResponse.ProviderName,
                    aiResponse.ModelName,
                    true,
                    aiResponse.Duration,
                    aiFallbackUsed: aiOutcome.FallbackUsed,
                    primaryAiErrorCode: aiOutcome.PrimaryErrorCode).ConfigureAwait(false);
            }

            if (requestedMode is null)
                cooldown.CommitAutomatic(decision.Provider, decision.ChannelId, decision.UserId);

            TextToSpeechResult? ttsResult = null;
            var ttsFallbackUsed = false;
            string? primaryTtsErrorCode = null;
            var assistantTtsStartedAt = (DateTimeOffset?)null;
            Task<TtsGenerationOutcome>? assistantSynthesis = null;
            // The assistant clip is the interaction's own reply, so it follows the requested response mode
            // and is not gated by the narration switch. Whether it is ever spoken is decided downstream by
            // the coordinator, which refuses to enqueue anything while autoplay is off.
            if (voiceRequested)
            {
                assistantTtsStartedAt = timeProvider.GetUtcNow();
                logger.LogInformation(
                    "INTERACTION_TTS_STARTED interactionId={InteractionId} provider={Provider} correlationId={CorrelationId} startedAt={StartedAt}",
                    decision.DecisionId, decision.Provider, decision.CorrelationId, assistantTtsStartedAt);
                // Started but not awaited: the group is handed over below with the reply still in flight, so
                // the chat clip can already be speaking while this synthesis finishes.
                assistantSynthesis = GenerateTtsAsync(
                    new TextToSpeechRequest(
                        decision.DecisionId,
                        sanitized.Text!,
                        ResolveVoice(narrationOptions.Value.AssistantVoice.VoiceId),
                        options.Value.Language,
                        decision.CorrelationId,
                        ArtifactIdFor(decision, NarrationVoiceRole.Assistant)),
                    decision,
                    cancellationToken);
            }

            if (assistantSynthesis is not null)
            {
                var ttsOutcome = await assistantSynthesis.ConfigureAwait(false);
                ttsResult = ttsOutcome.Response;
                ttsFallbackUsed = ttsOutcome.FallbackUsed;
                primaryTtsErrorCode = ttsOutcome.PrimaryErrorCode;
                logger.LogInformation(
                    "INTERACTION_TTS_COMPLETED interactionId={InteractionId} provider={Provider} success={Success} ttsProvider={TtsProvider} durationMs={DurationMs} correlationId={CorrelationId} completedAt={CompletedAt}",
                    decision.DecisionId, decision.Provider, ttsResult.Success, ttsResult.ProviderName,
                    ttsResult.Duration.TotalMilliseconds, decision.CorrelationId, timeProvider.GetUtcNow());

                // The reply is handed to the slot reserved at acceptance the moment it exists, so the
                // assistant clip is admitted without waiting for the rest of the interaction
                // bookkeeping. A failed synthesis is handed over as it is: the coordinator skips a role
                // whose audio never appears, which closes this slot deterministically rather than
                // stalling every interaction accepted after it.
                assistantSlot.TrySetResult(ttsResult);

                if (!ttsResult.Success)
                {
                    // A reply that could not be voiced must not silence the viewer's own message, and must
                    // not hold the admission slot either. The chat half is already with the coordinator.
                    return await FailAsync(
                        decision,
                        ttsResult.ErrorCode ?? "TTS_PROVIDER_FAILED",
                        cancellationToken,
                        aiResponse.ProviderName,
                        aiResponse.ModelName,
                        true,
                        aiResponse.Duration,
                        ttsResult.ProviderName,
                        false,
                        ttsResult.Duration,
                        sanitized.Text,
                        ttsResult.AudioFormat,
                        ttsResult.AudioPath,
                        aiOutcome.FallbackUsed,
                        aiOutcome.PrimaryErrorCode,
                        ttsFallbackUsed,
                        primaryTtsErrorCode,
                        ttsResult.IsSimulated,
                        ttsResult.VoiceName,
                        ttsResult.AudioDuration,
                        ttsResult.SampleRate,
                        ttsResult.BitDepth,
                        ttsResult.Channels).ConfigureAwait(false);
                }
            }

            // The chat synthesis has been running since before the AI call, so by this point its result is
            // normally already available. Reading it here records the chat half on the interaction result.
            var chatTts = chatTtsTask is null ? null : await chatTtsTask.ConfigureAwait(false);
            var chatTtsCompletedAt = chatTtsTask is null ? (DateTimeOffset?)null : timeProvider.GetUtcNow();

            var completedAt = timeProvider.GetUtcNow();
            var latency = new InteractionAudioLatency(
                SpeechBuildMs: Ms(speechBuildDuration),
                ChatTtsMs: chatTts is null ? null : Ms(chatTts.Duration),
                AiMs: Ms(aiResponse.Duration),
                AssistantTtsMs: ttsResult is null ? null : Ms(ttsResult.Duration),
                AiStartedAfterAcceptedMs: Ms(aiStartedAt - acceptedAt),
                ChatTtsStartedAfterAcceptedMs: Ms(chatTtsStartedAt - acceptedAt),
                AcceptedToAiReadyMs: Ms(aiReadyAt - acceptedAt),
                AcceptedToAssistantReadyMs: ttsResult is null ? null : Ms(completedAt - acceptedAt),
                TotalMs: Ms(completedAt - decision.CreatedAtUtc));
            var completed = new InteractionResult(
                decision.DecisionId,
                decision,
                InteractionStatus.Completed,
                sanitized.Text,
                aiResponse.ProviderName,
                aiResponse.ModelName,
                true,
                aiResponse.Duration,
                aiOutcome.FallbackUsed,
                aiOutcome.PrimaryErrorCode,
                ttsResult?.ProviderName,
                ttsResult?.Success,
                ttsResult?.AudioFormat,
                ttsResult?.AudioPath,
                ttsResult?.Duration,
                null,
                decision.CreatedAtUtc,
                completedAt,
                decision.Sequence,
                decision.CorrelationId,
                ttsFallbackUsed,
                primaryTtsErrorCode,
                ttsResult?.IsSimulated,
                ttsResult?.VoiceName,
                ttsResult?.AudioDuration,
                ttsResult?.SampleRate,
                ttsResult?.BitDepth,
                ttsResult?.Channels,
                ChatSpeechText: chatSpeech?.Text,
                ChatTtsSuccess: chatTts?.Success,
                ChatTtsErrorCode: chatTts?.ErrorCode,
                ChatTtsVoice: chatTts?.VoiceName ?? chatSpeech?.VoiceId,
                ChatAudioPath: chatTts?.AudioPath,
                ChatAudioFormat: chatTts?.AudioFormat,
                ChatAudioDuration: chatTts?.AudioDuration,
                ChatArtifactId: chatTts?.ArtifactId ?? default,
                ChatSampleRate: chatTts?.SampleRate,
                ChatBitDepth: chatTts?.BitDepth,
                ChatChannels: chatTts?.Channels,
                AudioLatency: latency);
            await CompleteAsync(completed, cancellationToken).ConfigureAwait(false);
            return completed;
        }
        finally
        {
            // Settles on every path, including a failed AI call, an unusable reply, a canceled
            // interaction and an unexpected throw, so an accepted interaction can never hold the
            // admission line open for the ones behind it. TrySetResult is idempotent, so the success
            // path already settled with the real reply and this null is simply a no-op.
            assistantSlot.TrySetResult(null);
        }
    }

    private static long Ms(TimeSpan value) => (long)value.TotalMilliseconds;

    /// <summary>
    /// Whether the chat clip should be synthesized at all. Synthesis is gated on the autoplay switch,
    /// not only on the role switch, so a stream with autoplay off does no Piper work for audio that
    /// would never be heard.
    /// </summary>
    private bool ChatVoiceActive =>
        NarrationAudioActive &&
        narrationOptions.Value.ChatVoice.Enabled;

    /// <summary>
    /// The single switch that decides whether anything reaches the speaker. The assistant reply is still
    /// synthesized when this is off, but no clip is ever queued, so the interaction stays silent.
    /// </summary>
    private bool NarrationAudioActive =>
        narrationOptions.Value.Enabled && narrationOptions.Value.AutoPlayInteractions;

    /// <summary>
    /// Resolves the voice for a role, falling back to the pre-existing interaction setting so an
    /// unconfigured role keeps the legacy behavior instead of silently using an empty voice id.
    /// </summary>
    private string ResolveVoice(string? roleVoiceId) =>
        string.IsNullOrWhiteSpace(roleVoiceId)
            ? options.Value.Voice ?? options.Value.Tts.Voice
            : roleVoiceId;

    /// <summary>
    /// Derives a stable, role-specific artifact id from the interaction id. The two roles of one
    /// interaction must not map to the same WAV file name, so the role is folded into the GUID bits
    /// themselves rather than appended outside them.
    /// </summary>
    private static Guid ArtifactIdFor(InteractionDecision decision, NarrationVoiceRole role)
    {
        Span<byte> bytes = stackalloc byte[16];
        decision.DecisionId.TryWriteBytes(bytes);
        bytes[15] ^= RoleTag(role);
        return new Guid(bytes);
    }

    /// <summary>
    /// A fixed, distinct tag per role. Written explicitly rather than derived from the enum value,
    /// because Assistant is the zero value and a naive arithmetic tag would collide with Chat.
    /// </summary>
    private static byte RoleTag(NarrationVoiceRole role) => role switch
    {
        NarrationVoiceRole.Chat => 0x5A,
        NarrationVoiceRole.Assistant => 0xA5,
        _ => 0x3C
    };

    private ChatSpeechResult? BuildChatSpeech(
        LiveChatEvent chatEvent,
        InteractionDecision decision,
        bool voiceRequested)
    {
        if (!voiceRequested || !ChatVoiceActive) return null;
        var speech = chatSpeechBuilder.Build(chatEvent, decision);
        if (!speech.Success)
        {
            logger.LogInformation(
                "INTERACTION_CHAT_SPEECH_SKIPPED interactionId={InteractionId} errorCode={ErrorCode} correlationId={CorrelationId}",
                decision.DecisionId, speech.ErrorCode, decision.CorrelationId);
            return null;
        }

        return speech;
    }

    private Task<TextToSpeechResult> StartChatSynthesis(
        InteractionDecision decision,
        ChatSpeechResult speech)
    {
        logger.LogInformation(
            "INTERACTION_CHAT_TTS_STARTED interactionId={InteractionId} voice={Voice} characters={Characters} correlationId={CorrelationId} startedAt={StartedAt}",
            decision.DecisionId, speech.VoiceId, speech.SourceCharacterCount, decision.CorrelationId,
            timeProvider.GetUtcNow());
        return GenerateTtsAsync(
            new TextToSpeechRequest(
                decision.DecisionId,
                speech.Text!,
                ResolveVoice(speech.VoiceId),
                options.Value.Language,
                decision.CorrelationId,
                ArtifactIdFor(decision, NarrationVoiceRole.Chat)),
            decision,
            CancellationToken.None).ContinueWith(
                task => task.Result.Response,
                TaskContinuationOptions.ExecuteSynchronously);
    }

    private bool TryMarkUnique(LiveChatEvent chatEvent)
    {
        var eventIdentity = !string.IsNullOrWhiteSpace(chatEvent.ProviderEventId)
            ? $"{chatEvent.Provider}:{chatEvent.ProviderEventId}"
            : $"{chatEvent.Provider}:{chatEvent.EventId:N}";
        lock (_dedupeGate)
        {
            if (!_processedEvents.Add(eventIdentity)) return false;
            _processedOrder.Enqueue(eventIdentity);
            while (_processedOrder.Count > Math.Max(1, options.Value.CooldownCapacity))
                _processedEvents.Remove(_processedOrder.Dequeue());
            return true;
        }
    }

    /// <summary>
    /// Reserves this interaction's admission slot with the coordinator.
    ///
    /// The call is deliberately not awaited. Narration is downstream of the interaction, so a slow or
    /// stuck narrator must never delay, fail or reorder the reply the viewer already received, and the
    /// reservation itself is synchronous: by the time this returns, the slot exists and holds this
    /// interaction's place in the acceptance order. Which role is audible, and in what order, is the
    /// coordinator's decision, not this method's.
    /// </summary>
    private void ReserveAdmissionSlot(
        InteractionDecision decision,
        Task<TextToSpeechResult>? chatAudio,
        TaskCompletionSource<TextToSpeechResult?> assistantSlot,
        bool voiceRequested,
        TimeSpan acceptedAtOffset)
    {
        if (!NarrationAudioActive) return;
        // Nothing to speak at all, so there is no reason to hold a place in the playback order.
        if (chatAudio is null && !voiceRequested) return;

        var request = new DualVoiceNarrationRequest(
            decision.DecisionId,
            decision.Sequence,
            decision.CorrelationId,
            chatAudio is null ? null : AsOptionalAsync(chatAudio),
            assistantSlot.Task,
            acceptedAtOffset);

        try
        {
            // Not awaited, by design. SubmitAsync never faults, and the slot is already reserved.
            _ = dualVoiceCoordinator.SubmitAsync(request, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // A narrator that cannot even accept the group must not fail the interaction. Settling the
            // promise here keeps the slot closed instead of waiting for the admission timeout.
            logger.LogWarning(
                "NARRATION_GROUP_RESERVE_FAILED interactionId={InteractionId} errorType={ErrorType} correlationId={CorrelationId}",
                decision.DecisionId, exception.GetType().Name, decision.CorrelationId);
            assistantSlot.TrySetResult(null);
        }
    }

    /// <summary>
    /// Presents a role's own synthesis task under the shared "this clip may not exist" shape, so the
    /// coordinator can treat a missing role and a failed role the same way.
    /// </summary>
    private static async Task<TextToSpeechResult?> AsOptionalAsync(Task<TextToSpeechResult> synthesis) =>
        await synthesis.ConfigureAwait(false);

    private InteractionResult CreateResult(
        InteractionDecision decision,
        InteractionStatus status,
        string? errorCode = null,
        string? aiProvider = null,
        string? aiModel = null,
        bool? aiSuccess = null,
        TimeSpan? aiDuration = null,
        string? ttsProvider = null,
        bool? ttsSuccess = null,
        TimeSpan? ttsDuration = null,
        string? responseText = null,
        string? audioFormat = null,
        string? audioPath = null,
        bool aiFallbackUsed = false,
        string? primaryAiErrorCode = null,
        bool ttsFallbackUsed = false,
        string? primaryTtsErrorCode = null,
        bool? ttsSimulated = null,
        string? ttsVoice = null,
        TimeSpan? audioDuration = null,
        int? sampleRate = null,
        int? bitDepth = null,
        int? channels = null) =>
        new(
            decision.DecisionId,
            decision,
            status,
            responseText,
            aiProvider,
            aiModel,
            aiSuccess,
            aiDuration,
            aiFallbackUsed,
            primaryAiErrorCode,
            ttsProvider,
            ttsSuccess,
            audioFormat,
            audioPath,
            ttsDuration,
            errorCode,
            decision.CreatedAtUtc,
            timeProvider.GetUtcNow(),
            decision.Sequence,
            decision.CorrelationId,
            ttsFallbackUsed,
            primaryTtsErrorCode,
            ttsSimulated,
            ttsVoice,
            audioDuration,
            sampleRate,
            bitDepth,
            channels);

    private async Task<InteractionResult> FailAsync(
        InteractionDecision decision,
        string errorCode,
        CancellationToken cancellationToken,
        string? aiProvider = null,
        string? aiModel = null,
        bool? aiSuccess = null,
        TimeSpan? aiDuration = null,
        string? ttsProvider = null,
        bool? ttsSuccess = null,
        TimeSpan? ttsDuration = null,
        string? responseText = null,
        string? audioFormat = null,
        string? audioPath = null,
        bool aiFallbackUsed = false,
        string? primaryAiErrorCode = null,
        bool ttsFallbackUsed = false,
        string? primaryTtsErrorCode = null,
        bool? ttsSimulated = null,
        string? ttsVoice = null,
        TimeSpan? audioDuration = null,
        int? sampleRate = null,
        int? bitDepth = null,
        int? channels = null)
    {
        var failed = CreateResult(
            decision,
            InteractionStatus.Failed,
            errorCode,
            aiProvider,
            aiModel,
            aiSuccess,
            aiDuration,
            ttsProvider,
            ttsSuccess,
            ttsDuration,
            responseText,
            audioFormat,
            audioPath,
            aiFallbackUsed,
            primaryAiErrorCode,
            ttsFallbackUsed,
            primaryTtsErrorCode,
            ttsSimulated,
            ttsVoice,
            audioDuration,
            sampleRate,
            bitDepth,
            channels);
        buffer.Add(failed);
        await mediator.Publish(new InteractionFailedNotification(failed), cancellationToken)
            .ConfigureAwait(false);
        await PublishSafelyAsync(failed, cancellationToken).ConfigureAwait(false);
        return failed;
    }

    private async Task<AiGenerationOutcome> GenerateAiAsync(
        AiInteractionRequest request,
        InteractionDecision decision,
        CancellationToken cancellationToken)
    {
        var primary = providers.GetAiProvider();
        if (primary is null)
        {
            return new AiGenerationOutcome(
                FailedAiResponse(request, "AI_PROVIDER_UNAVAILABLE"), false, null);
        }

        var primaryResponse = await GenerateSafelyAsync(primary, request, decision, cancellationToken)
            .ConfigureAwait(false);
        if (primaryResponse.Success || primary.IsDevelopment || !providers.AllowDevelopmentFallback)
        {
            return new AiGenerationOutcome(primaryResponse, false, null);
        }

        var fallback = providers.GetDevelopmentAiProvider();
        if (fallback is null || ReferenceEquals(fallback, primary))
        {
            return new AiGenerationOutcome(primaryResponse, false, null);
        }

        logger.LogWarning(
            "INTERACTION_AI_FALLBACK interactionId={InteractionId} primaryProvider={PrimaryProvider} primaryError={PrimaryError} fallbackProvider={FallbackProvider}",
            decision.DecisionId,
            primary.Name,
            primaryResponse.ErrorCode,
            fallback.Name);
        var fallbackResponse = await GenerateSafelyAsync(fallback, request, decision, cancellationToken)
            .ConfigureAwait(false);
        return new AiGenerationOutcome(fallbackResponse, true, primaryResponse.ErrorCode);
    }

    private async Task<AiInteractionResponse> GenerateSafelyAsync(
        IAiInteractionProvider provider,
        AiInteractionRequest request,
        InteractionDecision decision,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "INTERACTION_AI_FAILED interactionId={InteractionId} provider={Provider} errorType={ErrorType}",
                decision.DecisionId,
                provider.Name,
                exception.GetType().Name);
            return FailedAiResponse(request, "AI_PROVIDER_EXCEPTION", provider.Name, provider.ModelName);
        }
    }

    private static AiInteractionResponse FailedAiResponse(
        AiInteractionRequest request,
        string errorCode,
        string providerName = "Unavailable",
        string modelName = "Unavailable") =>
        new(null, providerName, modelName, TimeSpan.Zero, false, errorCode, request.CorrelationId, false);

    private async Task<TtsGenerationOutcome> GenerateTtsAsync(
        TextToSpeechRequest request,
        InteractionDecision decision,
        CancellationToken cancellationToken)
    {
        var primary = providers.GetTtsProvider();
        if (primary is null)
        {
            return new TtsGenerationOutcome(
                FailedTtsResponse(request, "TTS_PROVIDER_UNAVAILABLE"), false, null);
        }

        var primaryResponse = await GenerateTtsSafelyAsync(primary, request, decision, cancellationToken)
            .ConfigureAwait(false);
        if (primaryResponse.Success || primary.IsDevelopment || !providers.AllowDevelopmentTtsFallback)
        {
            return new TtsGenerationOutcome(primaryResponse, false, null);
        }

        var fallback = providers.GetDevelopmentTtsProvider();
        if (fallback is null || ReferenceEquals(fallback, primary))
        {
            return new TtsGenerationOutcome(primaryResponse, false, null);
        }

        logger.LogWarning(
            "INTERACTION_TTS_FALLBACK interactionId={InteractionId} primaryProvider={PrimaryProvider} primaryError={PrimaryError} fallbackProvider={FallbackProvider}",
            decision.DecisionId, primary.Name, primaryResponse.ErrorCode, fallback.Name);
        var fallbackResponse = await GenerateTtsSafelyAsync(fallback, request, decision, cancellationToken)
            .ConfigureAwait(false);
        return new TtsGenerationOutcome(fallbackResponse, true, primaryResponse.ErrorCode);
    }

    private async Task<TextToSpeechResult> GenerateTtsSafelyAsync(
        ITextToSpeechProvider provider,
        TextToSpeechRequest request,
        InteractionDecision decision,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.SynthesizeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "INTERACTION_TTS_FAILED interactionId={InteractionId} provider={Provider} errorType={ErrorType}",
                decision.DecisionId, provider.Name, exception.GetType().Name);
            return FailedTtsResponse(
                request, "TTS_PROVIDER_EXCEPTION", provider.Name, provider.AudioFormat, provider.VoiceName);
        }
    }

    private static TextToSpeechResult FailedTtsResponse(
        TextToSpeechRequest request,
        string errorCode,
        string providerName = "Unavailable",
        string audioFormat = "none",
        string? voiceName = null) =>
        new(false, providerName, audioFormat, null, TimeSpan.Zero, errorCode,
            request.CorrelationId, false, voiceName);

    private sealed record AiGenerationOutcome(
        AiInteractionResponse Response,
        bool FallbackUsed,
        string? PrimaryErrorCode);

    private sealed record TtsGenerationOutcome(
        TextToSpeechResult Response,
        bool FallbackUsed,
        string? PrimaryErrorCode);

    private async Task CompleteAsync(InteractionResult result, CancellationToken cancellationToken)
    {
        buffer.Add(result);
        if (result.Status == InteractionStatus.Completed)
        {
            await mediator.Publish(new InteractionCompletedNotification(result), cancellationToken)
                .ConfigureAwait(false);
        }

        await PublishSafelyAsync(result, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishSafelyAsync(InteractionResult result, CancellationToken cancellationToken)
    {
        try
        {
            await eventPublisher.PublishAsync(result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "INTERACTION_PUBLISH_FAILED interactionId={InteractionId} status={Status} errorType={ErrorType}",
                result.InteractionId,
                result.Status,
                exception.GetType().Name);
        }
    }
}
