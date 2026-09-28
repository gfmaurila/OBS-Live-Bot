using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Infrastructure.Configuration;
using ObsLiveBot.Infrastructure.Events;
using ObsLiveBot.Infrastructure.Health;
using ObsLiveBot.Infrastructure.Obs;

namespace ObsLiveBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddObsInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<ObsWebSocketOptions>, ObsWebSocketOptionsValidator>();
        services.AddSingleton<IObsProtocolClient, ObsWebSocketClient>();
        services.AddSingleton<IReconnectDelay, ProgressiveReconnectDelay>();
        services.AddSingleton<IDomainEventPublisher, DomainEventPublisher>();
        services.AddSingleton<ObsConnectionManager>();
        services.AddSingleton<IObsClient>(provider => provider.GetRequiredService<ObsConnectionManager>());
        services.AddHostedService(provider => provider.GetRequiredService<ObsConnectionManager>());
        services.AddHealthChecks().AddCheck<ObsHealthCheck>("obs");
        return services;
    }
}
