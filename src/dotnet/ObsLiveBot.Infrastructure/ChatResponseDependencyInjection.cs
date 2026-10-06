using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Infrastructure.Chat;
using ObsLiveBot.Infrastructure.ChatResponses;
using ObsLiveBot.Infrastructure.Configuration;
using ObsLiveBot.Infrastructure.Health;

namespace ObsLiveBot.Infrastructure;

/// <summary>
/// Registers written chat responses.
///
/// The write side is registered as its own composition root rather than as part of live chat. That is
/// deliberate: reading a chat and typing into one are different capabilities, and coupling them would
/// mean a problem on the write side could stop StudioOS from seeing what viewers are saying.
/// </summary>
public static class ChatResponseDependencyInjection
{
    public static IServiceCollection AddChatResponseInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ChatResponseOptions>()
            .Bind(configuration.GetSection(ChatResponseOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ChatResponseOptions>, ChatResponseOptionsValidator>();

        // Separate named client and separate base address wiring. The capture-side client is never
        // reused here, so a stalled or failing write cannot exhaust the capture connection pool.
        services.AddHttpClient("SocialStreamNinjaChatWrite", (provider, client) =>
        {
            var ssn = provider.GetRequiredService<IOptions<SocialStreamNinjaOptions>>().Value;
            client.BaseAddress = new Uri(ssn.Endpoint.TrimEnd('/') + "/", UriKind.Absolute);
            // The writer applies its own per-attempt ceiling, which also covers the inspect/fill/Enter
            // sequence as a whole. A client timeout here would cut the sequence in half.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddSingleton<SocialStreamNinjaChatWriteClient>(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            return new SocialStreamNinjaChatWriteClient(
                factory.CreateClient("SocialStreamNinjaChatWrite"),
                provider.GetRequiredService<ILogger<SocialStreamNinjaChatWriteClient>>());
        });

        // The write path lists its own sources through the write client. The capture-side command client
        // is never referenced from here, so a capture-side failure cannot be mistaken for a write-side
        // one and writing stays available even while reading is degraded.
        services.AddSingleton<IChatWriteSourceLister>(provider =>
            provider.GetRequiredService<SocialStreamNinjaChatWriteClient>());

        services.TryAddSingleton(TimeProvider.System);

        // One adapter per platform that can be written. A platform with no adapter registered is reported
        // as unsupported rather than being attempted through another platform's transport.
        services.AddSingleton<IChatWriteAdapter, TwitchChatWriteAdapter>();
        services.AddSingleton<IChatWriteAdapter, YouTubeChatWriteAdapter>();
        services.AddSingleton<IChatWriteAdapter, KickChatWriteAdapter>();
        services.AddSingleton<ChatWriteAdapterRegistry>();

        services.AddSingleton<ChatWriteTargetResolver>();

        services.AddSingleton<IChatResponseSender, SocialStreamNinjaChatResponseSender>();
        services.AddSingleton<IChatResponseSender, DevelopmentChatResponseSender>();
        services.AddSingleton<IChatResponseSenderRegistry, ChatResponseSenderRegistry>();
        services.AddSingleton<IChatProviderCapabilityProvider, ChatProviderCapabilityService>();

        services.AddSingleton<IChatResponseSettingsStore, ChatResponseSettingsStore>();
        services.AddSingleton<IChatResponseLedger, ChatResponseLedger>();
        services.AddSingleton<IChatResponseSelfIdentityRegistry, ChatResponseSelfIdentityRegistry>();
        services.AddSingleton<ChatResponseCooldownTracker>();
        services.TryAddSingleton<IChatResponseEventPublisher, NoOpChatResponseEventPublisher>();

        services.AddSingleton<ChatResponseGatekeeper>();
        services.AddSingleton<ChatResponseWriter>();
        services.AddSingleton<IChatResponseWriter>(provider => provider.GetRequiredService<ChatResponseWriter>());
        services.AddHostedService(provider => provider.GetRequiredService<ChatResponseWriter>());

        services.AddHealthChecks().AddCheck<ChatResponseHealthCheck>("chatResponses");
        return services;
    }
}