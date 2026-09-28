using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Infrastructure.Interactions;

public sealed class DevelopmentAiInteractionProvider : IAiInteractionProvider
{
    private long _requests;
    private long _successes;
    private DateTimeOffset? _lastSuccessAtUtc;

    public string Name => "Development";
    public string ModelName => "deterministic-development";
    public bool IsAvailable => true;
    public bool IsDevelopment => true;

    public AiProviderRuntimeSnapshot GetRuntimeState()
    {
        var successes = Interlocked.Read(ref _successes);
        return new AiProviderRuntimeSnapshot(
            true, "Ready", ModelName, Interlocked.Read(ref _requests), successes,
            0, 0, 0, successes == 0 ? null : 0d, _lastSuccessAtUtc, null);
    }

    public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<AiInteractionResponse> GenerateAsync(
        AiInteractionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _requests);
        var text = $"[DEV AI] {request.UserDisplayName}: {request.UserMessage}";
        Interlocked.Increment(ref _successes);
        _lastSuccessAtUtc = DateTimeOffset.UtcNow;
        return Task.FromResult(new AiInteractionResponse(
            text,
            Name,
            ModelName,
            TimeSpan.Zero,
            true,
            null,
            request.CorrelationId,
            true));
    }
}

public sealed class DevelopmentTextToSpeechProvider : ITextToSpeechProvider
{
    private long _requests;
    private long _successes;
    private DateTimeOffset? _lastSuccessAtUtc;

    public string Name => "Development";
    public string VoiceName => "deterministic-development";
    public string AudioFormat => "development/simulated";
    public bool IsAvailable => true;
    public bool IsDevelopment => true;

    public TtsProviderRuntimeSnapshot GetRuntimeState()
    {
        var successes = Interlocked.Read(ref _successes);
        return new TtsProviderRuntimeSnapshot(
            true, "Ready", VoiceName, AudioFormat, Interlocked.Read(ref _requests), successes,
            0, 0, 0, successes == 0 ? null : 0d, _lastSuccessAtUtc, null);
    }

    public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<TextToSpeechResult> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _requests);
        Interlocked.Increment(ref _successes);
        _lastSuccessAtUtc = DateTimeOffset.UtcNow;
        return Task.FromResult(new TextToSpeechResult(
            true,
            Name,
            "development/simulated",
            null,
            TimeSpan.Zero,
            null,
            request.CorrelationId,
            true,
            VoiceName));
    }
}
