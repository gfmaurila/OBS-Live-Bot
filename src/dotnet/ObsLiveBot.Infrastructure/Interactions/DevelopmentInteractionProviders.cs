using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Infrastructure.Interactions;

public sealed class DevelopmentAiInteractionProvider : IAiInteractionProvider
{
    public string Name => "Development";
    public bool IsAvailable => true;
    public bool IsDevelopment => true;

    public Task<AiInteractionResponse> GenerateAsync(
        AiInteractionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = $"[DEV AI] {request.UserDisplayName}: {request.UserMessage}";
        return Task.FromResult(new AiInteractionResponse(
            text,
            Name,
            "deterministic-development",
            TimeSpan.Zero,
            true,
            null,
            request.CorrelationId,
            true));
    }
}

public sealed class DevelopmentTextToSpeechProvider : ITextToSpeechProvider
{
    public string Name => "Development";
    public bool IsAvailable => true;
    public bool IsDevelopment => true;

    public Task<TextToSpeechResult> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new TextToSpeechResult(
            true,
            Name,
            "development/simulated",
            null,
            TimeSpan.Zero,
            null,
            request.CorrelationId,
            true));
    }
}
