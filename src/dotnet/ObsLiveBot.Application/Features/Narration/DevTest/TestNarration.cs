using Ardalis.Result;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Narration;
using ObsLiveBot.Domain.Interactions;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Application.Features.Narration.DevTest;

public sealed record TestNarrationCommand(string Text) : IRequest<Result<NarrationDevTestResponse>>;

public sealed class TestNarrationCommandValidator : AbstractValidator<TestNarrationCommand>
{
    public TestNarrationCommandValidator(IOptions<InteractionOptions> options) =>
        RuleFor(command => command.Text)
            .NotEmpty()
            .MaximumLength(options.Value.Tts.MaxInputCharacters);
}

public sealed class TestNarrationCommandHandler(
    IInteractionProviderRegistry providers,
    INarrationService narration,
    IOptions<InteractionOptions> interactionOptions)
    : IRequestHandler<TestNarrationCommand, Result<NarrationDevTestResponse>>
{
    public async Task<Result<NarrationDevTestResponse>> Handle(
        TestNarrationCommand request,
        CancellationToken cancellationToken)
    {
        var provider = providers.GetTtsProvider();
        if (provider is null || provider.IsDevelopment ||
            !await provider.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            return Result.Success(new NarrationDevTestResponse(false, "REAL_TTS_UNAVAILABLE",
                provider?.Name ?? "Unavailable", null, null));
        }

        var interactionId = Guid.NewGuid();
        var tts = await provider.SynthesizeAsync(new TextToSpeechRequest(
            interactionId,
            request.Text,
            interactionOptions.Value.Tts.Voice,
            interactionOptions.Value.Language,
            Guid.NewGuid().ToString("N")), cancellationToken).ConfigureAwait(false);
        if (!tts.Success || tts.IsSimulated || string.IsNullOrWhiteSpace(tts.AudioPath))
        {
            return Result.Success(new NarrationDevTestResponse(false,
                tts.ErrorCode ?? "REAL_TTS_FAILED", tts.ProviderName,
                tts.AudioDuration?.TotalSeconds, null));
        }

        var artifact = new NarrationAudioArtifact(
            interactionId, tts.AudioPath, tts.AudioFormat,
            tts.AudioDuration ?? TimeSpan.Zero, tts.SampleRate ?? 0,
            tts.BitDepth ?? 0, tts.Channels ?? 0);
        var queued = await narration.EnqueueAsync(artifact, tts.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(new NarrationDevTestResponse(
            queued.Accepted,
            queued.RejectionCode,
            tts.ProviderName,
            artifact.Duration.TotalSeconds,
            NarrationMappings.Map(queued.Result)));
    }
}
