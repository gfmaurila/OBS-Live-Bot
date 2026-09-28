using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class InteractionOptionsValidator : IValidateOptions<InteractionOptions>
{
    public ValidateOptionsResult Validate(string? name, InteractionOptions options)
    {
        var failures = new List<string>();
        if (options.BufferCapacity is < 1 or > 10_000)
            failures.Add("Interactions:BufferCapacity must be between 1 and 10000.");
        if (options.MaxResponseCharacters is < 1 or > 10_000)
            failures.Add("Interactions:MaxResponseCharacters must be between 1 and 10000.");
        if (options.MaxMessageCharacters is < 1 or > 100_000)
            failures.Add("Interactions:MaxMessageCharacters must be between 1 and 100000.");
        if (options.CooldownSeconds is < 0 or > 86_400)
            failures.Add("Interactions:CooldownSeconds must be between 0 and 86400.");
        if (options.CooldownCapacity is < 1 or > 100_000)
            failures.Add("Interactions:CooldownCapacity must be between 1 and 100000.");
        if (!Enum.IsDefined(options.DefaultResponseMode) || options.DefaultResponseMode == InteractionResponseMode.None)
            failures.Add("Interactions:DefaultResponseMode must be Text, Voice, or TextAndVoice.");
        if (!Enum.IsDefined(options.CooldownScope))
            failures.Add("Interactions:CooldownScope is invalid.");
        if (options.ContextMessageLimit is < 0 or > 100)
            failures.Add("Interactions:ContextMessageLimit must be between 0 and 100.");
        if (string.IsNullOrWhiteSpace(options.AiProvider))
            failures.Add("Interactions:AiProvider is required.");
        if (string.IsNullOrWhiteSpace(options.TtsProvider))
            failures.Add("Interactions:TtsProvider is required.");
        if (!string.Equals(options.AiProvider, "Development", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.AiProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
            failures.Add("Interactions:AiProvider must be Development or Ollama.");
        if (!Uri.TryCreate(options.Ollama.BaseUrl, UriKind.Absolute, out var ollamaUri) ||
            ollamaUri.Scheme is not ("http" or "https") ||
            ollamaUri.Host is not ("localhost" or "127.0.0.1" or "::1" or "host.docker.internal"))
            failures.Add("Interactions:Ollama:BaseUrl must target a supported local host.");
        if (string.IsNullOrWhiteSpace(options.Ollama.Model))
            failures.Add("Interactions:Ollama:Model is required.");
        if (options.Ollama.TimeoutSeconds is < 1 or > 300)
            failures.Add("Interactions:Ollama:TimeoutSeconds must be between 1 and 300.");
        if (options.Ollama.Temperature is < 0 or > 2)
            failures.Add("Interactions:Ollama:Temperature must be between 0 and 2.");
        if (options.Ollama.MaxOutputTokens is < 1 or > 2_048)
            failures.Add("Interactions:Ollama:MaxOutputTokens must be between 1 and 2048.");
        if (options.Ollama.MaxConcurrentRequests is < 1 or > 8)
            failures.Add("Interactions:Ollama:MaxConcurrentRequests must be between 1 and 8.");
        if (options.Ollama.MaxQueuedRequests is < 0 or > 100)
            failures.Add("Interactions:Ollama:MaxQueuedRequests must be between 0 and 100.");
        if (options.Ollama.QueueWaitTimeoutSeconds is < 0 or > 60)
            failures.Add("Interactions:Ollama:QueueWaitTimeoutSeconds must be between 0 and 60.");
        if (string.IsNullOrWhiteSpace(options.ReservedCommandPrefix))
            failures.Add("Interactions:ReservedCommandPrefix is required.");
        if (string.IsNullOrWhiteSpace(options.SystemInstructions))
            failures.Add("Interactions:SystemInstructions is required.");
        if (options.SelfIdentities.Any(identity => string.IsNullOrWhiteSpace(identity) || !identity.Contains(':')))
            failures.Add("Interactions:SelfIdentities must use provider-scoped Provider:UserId values.");
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
