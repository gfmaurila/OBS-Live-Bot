using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.Chat;

public sealed class LiveChatProviderHostedService(
    ILiveChatProviderRegistry registry,
    ILiveChatReconnectDelay reconnectDelay,
    ILogger<LiveChatProviderHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var providerTasks = registry.GetEnabledProviders()
            .Select(provider => RunProviderAsync(provider, stoppingToken));
        return Task.WhenAll(providerTasks);
    }

    private async Task RunProviderAsync(ILiveChatProvider provider, CancellationToken cancellationToken)
    {
        var attempt = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    provider.SetLifecycleState(
                        attempt == 0 ? LiveChatProviderState.Connecting : LiveChatProviderState.Reconnecting);
                    await provider.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    var state = provider.Snapshot.State;
                    if (state is LiveChatProviderState.Disabled or LiveChatProviderState.NotConfigured or
                        LiveChatProviderState.AuthenticationFailed)
                    {
                        return;
                    }

                    if (state == LiveChatProviderState.AuthenticationRequired &&
                        provider is ILiveChatProviderReconnectSignal reconnectSignal)
                    {
                        await reconnectSignal.WaitForReconnectSignalAsync(cancellationToken).ConfigureAwait(false);
                        attempt = 0;
                        continue;
                    }

                    if (state == LiveChatProviderState.RateLimited && provider.RetryAfter is { } retryAfter)
                    {
                        await reconnectDelay.WaitAsync(retryAfter, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (state == LiveChatProviderState.Connected)
                    {
                        attempt = 0;
                        await provider.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    provider.SetLifecycleState(LiveChatProviderState.Faulted, "provider_connection_failed");
                    logger.LogWarning(
                        "LIVE_CHAT_PROVIDER_FAILED provider={Provider} errorType={ErrorType}",
                        provider.Provider,
                        exception.GetType().Name);
                }

                attempt++;
                provider.SetLifecycleState(LiveChatProviderState.Reconnecting);
                var delay = reconnectDelay.GetDelay(attempt);
                logger.LogInformation(
                    "LIVE_CHAT_PROVIDER_RECONNECTING provider={Provider} attempt={Attempt} delaySeconds={DelaySeconds}",
                    provider.Provider,
                    attempt,
                    delay.TotalSeconds);
                await reconnectDelay.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await provider.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "LIVE_CHAT_PROVIDER_DISCONNECT_FAILED provider={Provider} errorType={ErrorType}",
                    provider.Provider,
                    exception.GetType().Name);
            }
        }
    }
}

public sealed class ProgressiveLiveChatReconnectDelay : ILiveChatReconnectDelay
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public TimeSpan GetDelay(int attempt) => Delays[Math.Clamp(attempt - 1, 0, Delays.Length - 1)];

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
