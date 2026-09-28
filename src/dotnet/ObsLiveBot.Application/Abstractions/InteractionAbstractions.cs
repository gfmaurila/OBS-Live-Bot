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
    public bool AllowDevelopmentFallback { get; set; } = true;
    public OllamaInteractionOptions Ollama { get; set; } = new();
    public PiperTtsOptions Tts { get; set; } = new();
    public int ContextMessageLimit { get; set; } = 5;
    public string ReservedCommandPrefix { get; set; } = "!studio";
    public string SystemInstructions { get; set; } =
        "Você é o assistente local do GFM StudioOS em uma transmissão ao vivo. " +
        "Responda em português brasileiro, de forma curta, natural e adequada para fala. " +
        "Não invente ações executadas, não afirme controlar o OBS e não alegue memória que não possui.";
    public string Language { get; set; } = "pt-BR";
    public string? Voice { get; set; }
    public string[] SelfIdentities { get; set; } = [];
}

public sealed class PiperTtsOptions
{
    public string ExecutablePath { get; set; } = "/opt/tts-engine/piper/piper";
    public string ModelPath { get; set; } = "/opt/tts-engine/voices/pt_BR-faber-medium.onnx";
    public string Voice { get; set; } = "pt_BR-faber-medium";
    public string OutputDirectory { get; set; } = "/app/data/runtime/tts";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxInputCharacters { get; set; } = 500;
    public int MaxFiles { get; set; } = 100;
    public int MaxAgeMinutes { get; set; } = 60;
    public int MaxConcurrentRequests { get; set; } = 1;
    public int MaxQueuedRequests { get; set; } = 2;
    public int QueueWaitTimeoutSeconds { get; set; } = 2;
    public bool AllowDevelopmentFallback { get; set; } = true;
}

public sealed class OllamaInteractionOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3:4b-instruct-2507-q4_K_M";
    public int TimeoutSeconds { get; set; } = 45;
    public double Temperature { get; set; } = 0.2;
    public int MaxOutputTokens { get; set; } = 160;
    public int MaxConcurrentRequests { get; set; } = 1;
    public int MaxQueuedRequests { get; set; } = 2;
    public int QueueWaitTimeoutSeconds { get; set; } = 2;
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
    string ModelName { get; }
    bool IsAvailable { get; }
    bool IsDevelopment { get; }
    AiProviderRuntimeSnapshot GetRuntimeState();
    Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken);
    Task<AiInteractionResponse> GenerateAsync(
        AiInteractionRequest request,
        CancellationToken cancellationToken);
}

public interface ITextToSpeechProvider
{
    string Name { get; }
    string VoiceName { get; }
    string AudioFormat { get; }
    bool IsAvailable { get; }
    bool IsDevelopment { get; }
    TtsProviderRuntimeSnapshot GetRuntimeState();
    Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken);
    Task<TextToSpeechResult> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken);
}

public interface IInteractionProviderRegistry
{
    IAiInteractionProvider? GetAiProvider();
    IAiInteractionProvider? GetDevelopmentAiProvider();
    bool AllowDevelopmentFallback { get; }
    ITextToSpeechProvider? GetTtsProvider();
    ITextToSpeechProvider? GetDevelopmentTtsProvider();
    bool AllowDevelopmentTtsFallback { get; }
    Task<IReadOnlyList<InteractionProviderSnapshot>> GetProvidersAsync(CancellationToken cancellationToken);
}

public interface IInteractionBuffer
{
    int Count { get; }
    int Capacity { get; }
    void Add(InteractionResult result);
    IReadOnlyList<InteractionResult> GetRecent(int limit);
    InteractionStateSnapshot GetState(
        bool enabled,
        bool aiAvailable,
        bool ttsAvailable,
        string aiProvider,
        string aiStatus,
        string? aiModel,
        string ttsProvider,
        string ttsStatus,
        string? ttsVoice,
        string? ttsAudioFormat,
        int cooldownEntries,
        int cooldownCapacity);
}

public interface IInteractionEventPublisher
{
    Task PublishAsync(InteractionResult result, CancellationToken cancellationToken);
}
