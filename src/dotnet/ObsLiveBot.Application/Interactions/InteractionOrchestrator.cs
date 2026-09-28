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

        var aiProvider = providers.GetAiProvider();
        if (aiProvider is null || !aiProvider.IsAvailable)
        {
            return await FailAsync(decision, "AI_PROVIDER_UNAVAILABLE", cancellationToken)
                .ConfigureAwait(false);
        }

        AiInteractionResponse aiResponse;
        try
        {
            aiResponse = await aiProvider.GenerateAsync(
                contextBuilder.Build(chatEvent, decision), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "INTERACTION_AI_FAILED interactionId={InteractionId} provider={Provider} errorType={ErrorType}",
                decision.DecisionId,
                aiProvider.Name,
                exception.GetType().Name);
            return await FailAsync(
                decision,
                "AI_PROVIDER_EXCEPTION",
                cancellationToken,
                aiProvider.Name).ConfigureAwait(false);
        }

        if (!aiResponse.Success)
        {
            return await FailAsync(
                decision,
                aiResponse.ErrorCode ?? "AI_PROVIDER_FAILED",
                cancellationToken,
                aiResponse.ProviderName,
                aiResponse.ModelName,
                false,
                aiResponse.Duration).ConfigureAwait(false);
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
                aiResponse.Duration).ConfigureAwait(false);
        }

        TextToSpeechResult? ttsResult = null;
        if (decision.RequestedResponseMode is InteractionResponseMode.Voice or InteractionResponseMode.TextAndVoice)
        {
            var ttsProvider = providers.GetTtsProvider();
            if (ttsProvider is null || !ttsProvider.IsAvailable)
            {
                return await FailAsync(
                    decision,
                    "TTS_PROVIDER_UNAVAILABLE",
                    cancellationToken,
                    aiResponse.ProviderName,
                    aiResponse.ModelName,
                    true,
                    aiResponse.Duration,
                    responseText: sanitized.Text).ConfigureAwait(false);
            }

            try
            {
                ttsResult = await ttsProvider.SynthesizeAsync(
                    new TextToSpeechRequest(
                        decision.DecisionId,
                        sanitized.Text!,
                        options.Value.Voice,
                        options.Value.Language,
                        decision.CorrelationId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    "INTERACTION_TTS_FAILED interactionId={InteractionId} provider={Provider} errorType={ErrorType}",
                    decision.DecisionId,
                    ttsProvider.Name,
                    exception.GetType().Name);
                return await FailAsync(
                    decision,
                    "TTS_PROVIDER_EXCEPTION",
                    cancellationToken,
                    aiResponse.ProviderName,
                    aiResponse.ModelName,
                    true,
                    aiResponse.Duration,
                    ttsProvider.Name,
                    responseText: sanitized.Text).ConfigureAwait(false);
            }

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
                    ttsResult.AudioPath).ConfigureAwait(false);
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
            ttsResult?.ProviderName,
            ttsResult?.Success,
            ttsResult?.AudioFormat,
            ttsResult?.AudioPath,
            ttsResult?.Duration,
            null,
            decision.CreatedAtUtc,
            timeProvider.GetUtcNow(),
            decision.Sequence,
            decision.CorrelationId);
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
        string? audioPath = null) =>
        new(
            decision.DecisionId,
            decision,
            status,
            responseText,
            aiProvider,
            aiModel,
            aiSuccess,
            aiDuration,
            ttsProvider,
            ttsSuccess,
            audioFormat,
            audioPath,
            ttsDuration,
            errorCode,
            decision.CreatedAtUtc,
            timeProvider.GetUtcNow(),
            decision.Sequence,
            decision.CorrelationId);

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
        string? audioPath = null)
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
            audioPath);
        buffer.Add(failed);
        await mediator.Publish(new InteractionFailedNotification(failed), cancellationToken)
            .ConfigureAwait(false);
        await PublishSafelyAsync(failed, cancellationToken).ConfigureAwait(false);
        return failed;
    }

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
