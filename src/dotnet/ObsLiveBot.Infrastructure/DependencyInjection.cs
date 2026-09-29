using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.LiveState;
using ObsLiveBot.Application.LiveChat;
using ObsLiveBot.Application.Interactions;
using ObsLiveBot.Infrastructure.Chat;
using ObsLiveBot.Infrastructure.Configuration;
using ObsLiveBot.Infrastructure.Events;
using ObsLiveBot.Infrastructure.Health;
using ObsLiveBot.Infrastructure.Interactions;
using ObsLiveBot.Infrastructure.Obs;
using ObsLiveBot.Application.Narration;

namespace ObsLiveBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddObsInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<ObsWebSocketOptions>, ObsWebSocketOptionsValidator>();
        services.AddSingleton<ObsWebSocketClient>();
        services.AddSingleton<IObsProtocolClient>(provider => provider.GetRequiredService<ObsWebSocketClient>());
        services.AddSingleton<IObsRequestClient>(provider => provider.GetRequiredService<ObsWebSocketClient>());
        services.AddSingleton<IReconnectDelay, ProgressiveReconnectDelay>();
        services.AddSingleton<IDomainEventPublisher, DomainEventPublisher>();
        services.AddSingleton<ILiveEventPublisher, NoOpLiveEventPublisher>();
        services.AddSingleton<ObsLiveStateTracker>();
        services.AddSingleton<IObsLiveStateTracker>(provider => provider.GetRequiredService<ObsLiveStateTracker>());
        services.AddSingleton<IObsLiveStateReader>(provider => provider.GetRequiredService<ObsLiveStateTracker>());
        services.AddSingleton<ObsConnectionManager>();
        services.AddSingleton<IObsClient>(provider => provider.GetRequiredService<ObsConnectionManager>());
        services.AddHostedService(provider => provider.GetRequiredService<ObsConnectionManager>());
        services.AddHealthChecks().AddCheck<ObsHealthCheck>("obs");
        return services;
    }

    public static IServiceCollection AddLiveChatInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<LiveChatOptions>()
            .Bind(configuration.GetSection(LiveChatOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LiveChatOptions>, LiveChatOptionsValidator>();

        services
            .AddOptions<LiveChatProvidersOptions>()
            .Bind(configuration.GetSection(LiveChatProvidersOptions.SectionName))
            .Configure(options =>
            {
                ApplyEnvironment(options.Twitch, "TWITCH");
                ApplyEnvironment(options.YouTube, "YOUTUBE");
                ApplyEnvironment(options.TikTok, "TIKTOK");
            });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ILiveChatBuffer, LiveChatBuffer>();
        services.AddSingleton<ILiveChatDeduplicator, LiveChatDeduplicator>();
        services.AddSingleton<ILiveChatEventNormalizer, LiveChatEventNormalizer>();
        services.AddSingleton<ILiveChatIngestionPipeline, LiveChatIngestionPipeline>();
        services.AddSingleton<ILiveChatEventPublisher, NoOpLiveChatEventPublisher>();
        services.AddSingleton<ILiveChatReconnectDelay, ProgressiveLiveChatReconnectDelay>();

        services.AddSingleton<TwitchLiveChatProvider>();
        services.AddSingleton<YouTubeLiveChatProvider>();
        services.AddSingleton<TikTokLiveChatProvider>();
        services.AddSingleton<ILiveChatProvider>(provider => provider.GetRequiredService<TwitchLiveChatProvider>());
        services.AddSingleton<ILiveChatProvider>(provider => provider.GetRequiredService<YouTubeLiveChatProvider>());
        services.AddSingleton<ILiveChatProvider>(provider => provider.GetRequiredService<TikTokLiveChatProvider>());
        services.AddSingleton<ILiveChatProviderRegistry, LiveChatProviderRegistry>();
        services.AddHostedService<LiveChatProviderHostedService>();
        services.AddHealthChecks().AddCheck<LiveChatHealthCheck>("chat");
        return services;
    }

    public static IServiceCollection AddInteractionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<InteractionOptions>()
            .Bind(configuration.GetSection(InteractionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<InteractionOptions>, InteractionOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IInteractionBuffer, InteractionBuffer>();
        services.AddSingleton<IInteractionCooldownTracker, InteractionCooldownTracker>();
        services.AddSingleton<IInteractionDecisionPolicy, InteractionDecisionPolicy>();
        services.AddSingleton<IInteractionContextBuilder, InteractionContextBuilder>();
        services.AddSingleton<IAiResponseSanitizer, AiResponseSanitizer>();
        services.AddSingleton<IInteractionOrchestrator, InteractionOrchestrator>();
        services.AddSingleton<IAiInteractionProvider, DevelopmentAiInteractionProvider>();
        services.AddHttpClient("Ollama", (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<InteractionOptions>>().Value.Ollama;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<OllamaAiInteractionProvider>(provider => new OllamaAiInteractionProvider(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama"),
            provider.GetRequiredService<IOptions<InteractionOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OllamaAiInteractionProvider>>()));
        services.AddSingleton<IAiInteractionProvider>(provider =>
            provider.GetRequiredService<OllamaAiInteractionProvider>());
        services.AddSingleton<ITextToSpeechProvider, DevelopmentTextToSpeechProvider>();
        services.AddSingleton<ITtsProcessRunner, TtsProcessRunner>();
        services.AddSingleton<TtsAudioStore>();
        services.AddSingleton<ITextToSpeechProvider, PiperTextToSpeechProvider>();
        services.AddSingleton<IInteractionProviderRegistry, InteractionProviderRegistry>();
        services.AddSingleton<IInteractionEventPublisher, NoOpInteractionEventPublisher>();
        services.AddHealthChecks().AddCheck<InteractionHealthCheck>("interactions");
        return services;
    }

    public static IServiceCollection AddNarrationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<NarrationOptions>()
            .Bind(configuration.GetSection(NarrationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<NarrationOptions>, NarrationOptionsValidator>();
        services.AddSingleton<IAudioArtifactLeaseRegistry, AudioArtifactLeaseRegistry>();
        services.AddSingleton<INarrationArtifactValidator, NarrationArtifactValidator>();
        services.AddSingleton<IAudioPlaybackService, ObsAudioPlaybackService>();
        services.AddSingleton<INarrationEventPublisher, NoOpNarrationEventPublisher>();
        services.AddSingleton<NarrationService>();
        services.AddSingleton<INarrationService>(provider => provider.GetRequiredService<NarrationService>());
        services.AddHostedService(provider => provider.GetRequiredService<NarrationService>());
        services.AddHealthChecks().AddCheck<NarrationHealthCheck>("narration");
        return services;
    }

    private static void ApplyEnvironment(LiveChatProviderConfiguration options, string prefix)
    {
        if (bool.TryParse(Environment.GetEnvironmentVariable($"{prefix}_ENABLED"), out var enabled))
        {
            options.Enabled = enabled;
        }

        options.Channel = ReadEnvironment($"{prefix}_CHANNEL", options.Channel);
        options.ClientId = ReadEnvironment($"{prefix}_CLIENT_ID", options.ClientId);
        options.ClientSecret = ReadEnvironment($"{prefix}_CLIENT_SECRET", options.ClientSecret);
        options.AccessToken = ReadEnvironment($"{prefix}_ACCESS_TOKEN", options.AccessToken);
        options.RefreshToken = ReadEnvironment($"{prefix}_REFRESH_TOKEN", options.RefreshToken);
    }

    private static string? ReadEnvironment(string name, string? currentValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? currentValue : value;
    }
}
