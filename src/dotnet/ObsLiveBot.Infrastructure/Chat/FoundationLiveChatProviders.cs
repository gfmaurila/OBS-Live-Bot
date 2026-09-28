using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Features.Chat.Ingest;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Infrastructure.Chat;

public abstract class FoundationLiveChatProvider : ILiveChatProvider
{
    private readonly object _gate = new();
    private readonly ISender _sender;
    private readonly ILogger _logger;
    private readonly LiveChatProviderConfiguration _configuration;
    private LiveChatProviderSnapshot _snapshot;

    protected FoundationLiveChatProvider(
        LiveChatProviderType provider,
        LiveChatProviderConfiguration configuration,
        ISender sender,
        ILogger logger)
    {
        Provider = provider;
        _configuration = configuration;
        _sender = sender;
        _logger = logger;
        _snapshot = new LiveChatProviderSnapshot(
            provider,
            configuration.Enabled,
            configuration.Enabled ? LiveChatProviderState.NotConfigured : LiveChatProviderState.Disabled,
            configuration.Channel,
            null,
            null,
            null);
    }

    public LiveChatProviderType Provider { get; }

    public LiveChatProviderSnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public TimeSpan? RetryAfter => null;

    public Task Completion => Task.CompletedTask;

    public void SetLifecycleState(LiveChatProviderState state, string? error = null) =>
        UpdateState(state, error);

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = !_configuration.Enabled
            ? LiveChatProviderState.Disabled
            : HasRequiredConfiguration(_configuration)
                ? LiveChatProviderState.Disconnected
                : LiveChatProviderState.NotConfigured;
        UpdateState(state, null);
        _logger.LogInformation(
            "LIVE_CHAT_PROVIDER_STATE provider={Provider} state={ProviderState}",
            Provider,
            state);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateState(_configuration.Enabled ? LiveChatProviderState.Disconnected : LiveChatProviderState.Disabled, null);
        return Task.CompletedTask;
    }

    protected abstract bool HasRequiredConfiguration(LiveChatProviderConfiguration configuration);

    protected async Task<LiveChatIngestionResult> IngestAsync(
        ProviderLiveChatEvent providerEvent,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new IngestLiveChatEventCommand(providerEvent),
            cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Value.Accepted)
        {
            lock (_gate)
            {
                _snapshot = _snapshot with { LastEventAtUtc = result.Value.Event?.ReceivedAtUtc };
            }
        }

        return result.Value;
    }

    protected void UpdateState(LiveChatProviderState state, string? error)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                State = state,
                Error = error,
                LastConnectedAtUtc = state == LiveChatProviderState.Connected
                    ? DateTimeOffset.UtcNow
                    : _snapshot.LastConnectedAtUtc
            };
        }
    }
}

public sealed class TwitchLiveChatProvider(
    IOptions<LiveChatProvidersOptions> options,
    ISender sender,
    ILogger<TwitchLiveChatProvider> logger)
    : FoundationLiveChatProvider(LiveChatProviderType.Twitch, options.Value.Twitch, sender, logger)
{
    protected override bool HasRequiredConfiguration(LiveChatProviderConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.Channel) &&
        !string.IsNullOrWhiteSpace(configuration.ClientId) &&
        !string.IsNullOrWhiteSpace(configuration.AccessToken);
}

public sealed class YouTubeLiveChatProvider(
    IOptions<LiveChatProvidersOptions> options,
    ISender sender,
    ILogger<YouTubeLiveChatProvider> logger)
    : FoundationLiveChatProvider(LiveChatProviderType.YouTube, options.Value.YouTube, sender, logger)
{
    protected override bool HasRequiredConfiguration(LiveChatProviderConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.Channel) &&
        !string.IsNullOrWhiteSpace(configuration.ClientId) &&
        !string.IsNullOrWhiteSpace(configuration.RefreshToken);
}

public sealed class TikTokLiveChatProvider(
    IOptions<LiveChatProvidersOptions> options,
    ISender sender,
    ILogger<TikTokLiveChatProvider> logger)
    : FoundationLiveChatProvider(LiveChatProviderType.TikTok, options.Value.TikTok, sender, logger)
{
    protected override bool HasRequiredConfiguration(LiveChatProviderConfiguration configuration) => false;
}
