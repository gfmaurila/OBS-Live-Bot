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

    public IAiInteractionProvider? GetDevelopmentAiProvider() =>
        _aiProviders.FirstOrDefault(provider => provider.IsDevelopment);

    public bool AllowDevelopmentFallback => _options.AllowDevelopmentFallback;

    public ITextToSpeechProvider? GetTtsProvider() =>
        _ttsProviders.FirstOrDefault(provider =>
            string.Equals(provider.Name, _options.TtsProvider, StringComparison.OrdinalIgnoreCase));

    public ITextToSpeechProvider? GetDevelopmentTtsProvider() =>
        _ttsProviders.FirstOrDefault(provider => provider.IsDevelopment);

    public bool AllowDevelopmentTtsFallback => _options.Tts.AllowDevelopmentFallback;

    public async Task<IReadOnlyList<InteractionProviderSnapshot>> GetProvidersAsync(
        CancellationToken cancellationToken)
    {
        var ai = new List<InteractionProviderSnapshot>(_aiProviders.Count);
        foreach (var provider in _aiProviders)
        {
            var selected = string.Equals(provider.Name, _options.AiProvider, StringComparison.OrdinalIgnoreCase);
            if (selected)
            {
                await provider.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            }

            var state = provider.GetRuntimeState();
            ai.Add(new InteractionProviderSnapshot(
                "AI",
                provider.Name,
                selected,
                state.Available,
                provider.IsDevelopment,
                state.Status,
                state.Model,
                state.Requests,
                state.Successes,
                state.Failures,
                state.Timeouts,
                state.BusyRejections,
                state.AverageDurationMilliseconds,
                state.LastSuccessAtUtc,
                state.LastFailureAtUtc,
                null,
                null));
        }

        var tts = new List<InteractionProviderSnapshot>(_ttsProviders.Count);
        foreach (var provider in _ttsProviders)
        {
            var selected = string.Equals(provider.Name, _options.TtsProvider, StringComparison.OrdinalIgnoreCase);
            if (selected)
            {
                await provider.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            }

            var state = provider.GetRuntimeState();
            tts.Add(new InteractionProviderSnapshot(
                "TTS", provider.Name, selected, state.Available, provider.IsDevelopment,
                state.Status, null, state.Requests, state.Successes, state.Failures,
                state.Timeouts, state.BusyRejections, state.AverageDurationMilliseconds,
                state.LastSuccessAtUtc, state.LastFailureAtUtc, state.Voice, state.AudioFormat));
        }
        return ai.Concat(tts).ToArray();
    }
}
