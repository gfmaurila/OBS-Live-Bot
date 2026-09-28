using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Interactions;

public sealed class InteractionOrchestrator(
    IInteractionDecisionPolicy decisionPolicy,
    IInteractionCooldownTracker cooldown,
    IInteractionContextBuilder contextBuilder,
    IInteractionProviderRegistry providers,
    IAiResponseSanitizer sanitizer,
    IInteractionBuffer buffer,
    IInteractionEventPublisher eventPublisher,
    IPublisher mediator,
    IOptions<InteractionOptions> options,
    TimeProvider timeProvider,
    ILogger<InteractionOrchestrator> logger) : IInteractionOrchestrator
{
    private long _sequence;

    public async Task<InteractionResult> ProcessAsync(
        LiveChatEvent chatEvent,
        InteractionResponseMode? requestedMode,
        CancellationToken cancellationToken)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var decision = decisionPolicy.Decide(chatEvent, requestedMode, sequence);
        if (decision.DecisionType == InteractionDecisionType.Respond &&
            !cooldown.TryAcquire(decision.Provider, decision.ChannelId, decision.UserId))
        {
            decision = decision with
            {
                DecisionType = InteractionDecisionType.Ignore,
                Reason = "CooldownActive",
                RequestedResponseMode = InteractionResponseMode.None
            };
        }

        await mediator.Publish(new InteractionDecidedNotification(decision), cancellationToken)
            .ConfigureAwait(false);

        if (decision.DecisionType == InteractionDecisionType.Ignore)
        {
            var ignored = CreateResult(decision, InteractionStatus.Ignored);
            await CompleteAsync(ignored, cancellationToken).ConfigureAwait(false);
            return ignored;
        }

        var aiOutcome = await GenerateAiAsync(
            contextBuilder.Build(chatEvent, decision), decision, cancellationToken).ConfigureAwait(false);
        var aiResponse = aiOutcome.Response;

        if (!aiResponse.Success)
        {
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

        TextToSpeechResult? ttsResult = null;
        var ttsFallbackUsed = false;
        string? primaryTtsErrorCode = null;
        if (decision.RequestedResponseMode is InteractionResponseMode.Voice or InteractionResponseMode.TextAndVoice)
        {
            var ttsOutcome = await GenerateTtsAsync(
                new TextToSpeechRequest(
                    decision.DecisionId,
                    sanitized.Text!,
                    options.Value.Voice ?? options.Value.Tts.Voice,
                    options.Value.Language,
                    decision.CorrelationId),
                decision,
                cancellationToken).ConfigureAwait(false);
            ttsResult = ttsOutcome.Response;
            ttsFallbackUsed = ttsOutcome.FallbackUsed;
            primaryTtsErrorCode = ttsOutcome.PrimaryErrorCode;

            if (!ttsResult.Success)
            {
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
            timeProvider.GetUtcNow(),
            decision.Sequence,
            decision.CorrelationId,
            ttsFallbackUsed,
            primaryTtsErrorCode,
            ttsResult?.IsSimulated,
            ttsResult?.VoiceName,
            ttsResult?.AudioDuration,
            ttsResult?.SampleRate,
            ttsResult?.BitDepth,
            ttsResult?.Channels);
        await CompleteAsync(completed, cancellationToken).ConfigureAwait(false);
        return completed;
    }

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
