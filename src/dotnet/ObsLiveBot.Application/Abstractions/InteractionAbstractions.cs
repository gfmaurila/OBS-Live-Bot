using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Abstractions;

public sealed class InteractionOptions
{
    public const string SectionName = "Interactions";
    public bool Enabled { get; set; } = true;
    public int BufferCapacity { get; set; } = 100;
    public int MaxResponseCharacters { get; set; } = 500;
    public int MaxMessageCharacters { get; set; } = 4_000;
    public InteractionResponseMode DefaultResponseMode { get; set; } = InteractionResponseMode.Text;
    public int CooldownSeconds { get; set; } = 5;
    public int CooldownCapacity { get; set; } = 1_000;
    public InteractionCooldownScope CooldownScope { get; set; } = InteractionCooldownScope.User;
    public string AiProvider { get; set; } = "Development";
    public string TtsProvider { get; set; } = "Development";
    public int ContextMessageLimit { get; set; } = 5;
    public string ReservedCommandPrefix { get; set; } = "!studio";
    public string SystemInstructions { get; set; } =
        "You are the local GFM StudioOS development interaction provider. Keep responses concise.";
    public string Language { get; set; } = "pt-BR";
    public string? Voice { get; set; }
    public string[] SelfIdentities { get; set; } = [];
}

public interface IInteractionOrchestrator
{
    Task<InteractionResult> ProcessAsync(
        LiveChatEvent chatEvent,
        InteractionResponseMode? requestedMode,
        CancellationToken cancellationToken);
}

public interface IInteractionDecisionPolicy
{
    InteractionDecision Decide(
        LiveChatEvent chatEvent,
        InteractionResponseMode? requestedMode,
        long sequence);
}

public interface IInteractionCooldownTracker
{
    int Count { get; }
    int Capacity { get; }
    bool TryAcquire(LiveChatProviderType provider, string channelId, string userId);
}

public interface IInteractionContextBuilder
{
    AiInteractionRequest Build(LiveChatEvent chatEvent, InteractionDecision decision);
}

public sealed record AiResponseSanitizationResult(bool Success, string? Text, string? ErrorCode);

public interface IAiResponseSanitizer
{
    AiResponseSanitizationResult Sanitize(string? text);
}

public interface IAiInteractionProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    bool IsDevelopment { get; }
    Task<AiInteractionResponse> GenerateAsync(
        AiInteractionRequest request,
        CancellationToken cancellationToken);
}

public interface ITextToSpeechProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    bool IsDevelopment { get; }
    Task<TextToSpeechResult> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken);
}

public interface IInteractionProviderRegistry
{
    IAiInteractionProvider? GetAiProvider();
    ITextToSpeechProvider? GetTtsProvider();
    IReadOnlyList<InteractionProviderSnapshot> GetProviders();
}

public interface IInteractionBuffer
{
    int Count { get; }
    int Capacity { get; }
    void Add(InteractionResult result);
    IReadOnlyList<InteractionResult> GetRecent(int limit);
    InteractionStateSnapshot GetState(
        bool enabled,
        bool providersAvailable,
        int cooldownEntries,
        int cooldownCapacity);
}

public interface IInteractionEventPublisher
{
    Task PublishAsync(InteractionResult result, CancellationToken cancellationToken);
}
