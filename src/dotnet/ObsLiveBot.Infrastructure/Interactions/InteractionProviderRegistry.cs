using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Infrastructure.Interactions;

public sealed class InteractionProviderRegistry(
    IEnumerable<IAiInteractionProvider> aiProviders,
    IEnumerable<ITextToSpeechProvider> ttsProviders,
    IOptions<InteractionOptions> options) : IInteractionProviderRegistry
{
    private readonly IReadOnlyList<IAiInteractionProvider> _aiProviders = aiProviders.ToArray();
    private readonly IReadOnlyList<ITextToSpeechProvider> _ttsProviders = ttsProviders.ToArray();
    private readonly InteractionOptions _options = options.Value;

    public IAiInteractionProvider? GetAiProvider() =>
        _aiProviders.FirstOrDefault(provider =>
            string.Equals(provider.Name, _options.AiProvider, StringComparison.OrdinalIgnoreCase));

    public ITextToSpeechProvider? GetTtsProvider() =>
        _ttsProviders.FirstOrDefault(provider =>
            string.Equals(provider.Name, _options.TtsProvider, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<InteractionProviderSnapshot> GetProviders()
    {
        var ai = _aiProviders.Select(provider => new InteractionProviderSnapshot(
            "AI",
            provider.Name,
            string.Equals(provider.Name, _options.AiProvider, StringComparison.OrdinalIgnoreCase),
            provider.IsAvailable,
            provider.IsDevelopment,
            provider.IsAvailable ? "Ready" : "Unavailable"));
        var tts = _ttsProviders.Select(provider => new InteractionProviderSnapshot(
            "TTS",
            provider.Name,
            string.Equals(provider.Name, _options.TtsProvider, StringComparison.OrdinalIgnoreCase),
            provider.IsAvailable,
            provider.IsDevelopment,
            provider.IsAvailable ? "Ready" : "Unavailable"));
        return ai.Concat(tts).ToArray();
    }
}
