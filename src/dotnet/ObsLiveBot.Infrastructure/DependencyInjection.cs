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
            .Configure(options =>
            {
                var providers = configuration.GetSection("providers").Get<StudioOsProviderConfiguration>();
                if (providers is null)
                    return;

                options.Twitch = MapProvider(providers.Twitch, requiresChannel: true, channel: providers.Twitch.Channel);
                options.YouTube = MapProvider(
                    providers.YouTube,
                    requiresChannel: false,
                    channel: providers.YouTube.ChannelId ?? providers.YouTube.Channel);
                options.Kick = MapProvider(providers.Kick, requiresChannel: true, channel: providers.Kick.Channel);
            });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ILiveChatBuffer, LiveChatBuffer>();
        services.AddSingleton<ILiveChatDeduplicator, LiveChatDeduplicator>();
        services.AddSingleton<ILiveChatEventNormalizer, LiveChatEventNormalizer>();
        services.AddSingleton<ILiveChatIngestionPipeline, LiveChatIngestionPipeline>();
        services.AddSingleton<ILiveChatEventPublisher, NoOpLiveChatEventPublisher>();
        services.AddSingleton<ILiveChatReconnectDelay, ProgressiveLiveChatReconnectDelay>();

        services.AddOptions<CredentialHelperClientOptions>()
            .Bind(configuration.GetSection(CredentialHelperClientOptions.SectionName));
        services.AddSingleton<IValidateOptions<CredentialHelperClientOptions>, CredentialHelperClientOptionsValidator>();
        services.AddHttpClient("CredentialHelper", (provider, client) =>
        {
            var helper = provider.GetRequiredService<IOptions<CredentialHelperClientOptions>>().Value;
            client.BaseAddress = new Uri(helper.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(helper.TimeoutSeconds);
        });
        services.AddSingleton<ITwitchTokenStore, SecureHelperTwitchTokenStore>();
        services.AddOptions<TwitchOAuthOptions>()
            .Configure(options =>
            {
                var twitch = configuration.GetSection("providers:twitch");
                options.ClientId = twitch["clientId"];
                options.Channel = twitch["channel"];
                options.Enabled = twitch.GetValue<bool>("officialApiEnabled") &&
                                  twitch.GetValue<bool>("enabled") &&
                                  !string.IsNullOrWhiteSpace(options.ClientId) &&
                                  !string.IsNullOrWhiteSpace(options.Channel);
            });
        services.AddSingleton<IValidateOptions<TwitchOAuthOptions>, TwitchOAuthOptionsValidator>();
        services.AddHttpClient("TwitchOAuth", client =>
        {
            client.BaseAddress = new Uri("https://id.twitch.tv/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient("TwitchApi", client =>
        {
            client.BaseAddress = new Uri("https://api.twitch.tv/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<ITwitchAuthorizationService, TwitchDeviceAuthorizationService>();

        services.AddOptions<SocialStreamNinjaOptions>()
            .Bind(configuration.GetSection(SocialStreamNinjaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SocialStreamNinjaOptions>, SocialStreamNinjaOptionsValidator>();
        services.AddHttpClient("SocialStreamNinja", (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<SocialStreamNinjaOptions>>().Value;
            client.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<SocialStreamNinjaMessageMapper>();
        services.AddSingleton<SocialStreamNinjaLiveChatProvider>();

        services.AddSingleton<TwitchLiveChatProvider>();
        services.AddSingleton<TwitchEventSubLiveChatProvider>();
        services.AddSingleton<YouTubeLiveChatProvider>();
        services.AddSingleton<TikTokLiveChatProvider>();
        services.AddSingleton<ILiveChatProvider>(provider => provider.GetRequiredService<TwitchEventSubLiveChatProvider>());
        services.AddSingleton<ILiveChatProvider>(provider => provider.GetRequiredService<YouTubeLiveChatProvider>());
        services.AddSingleton<ILiveChatProvider>(provider => provider.GetRequiredService<TikTokLiveChatProvider>());
        services.AddSingleton<ILiveChatProvider>(provider => provider.GetRequiredService<SocialStreamNinjaLiveChatProvider>());
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
        services.AddSingleton<InteractionWorkQueue>();
        services.AddSingleton<IInteractionWorkQueue>(provider => provider.GetRequiredService<InteractionWorkQueue>());
        services.AddHostedService<InteractionWorkQueueProcessor>();
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

    private static LiveChatProviderConfiguration MapProvider(
        StudioOsPublicProviderSettings settings,
        bool requiresChannel,
        string? channel) => new()
    {
        Enabled = settings.OfficialApiEnabled &&
                  settings.Enabled &&
                  !string.IsNullOrWhiteSpace(settings.ClientId) &&
                  (!requiresChannel || !string.IsNullOrWhiteSpace(channel)),
        ClientId = settings.ClientId,
        Channel = channel
    };
}
