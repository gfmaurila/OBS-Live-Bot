using System.Runtime.CompilerServices;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.LiveChat;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.Chat;
using ObsLiveBot.Infrastructure.Configuration;
using ObsLiveBot.Infrastructure.Health;

namespace ObsLiveBot.UnitTests.Chat;

public sealed class LiveChatProviderTests
{
    [Fact]
    public void Registry_ProvidesTypedLookupAndEnabledProviders()
    {
        var twitch = new FakeProvider(LiveChatProviderType.Twitch, true, LiveChatProviderState.Disconnected);
        var youtube = new FakeProvider(LiveChatProviderType.YouTube, false, LiveChatProviderState.Disabled);
        var registry = new LiveChatProviderRegistry([twitch, youtube]);

        Assert.Same(twitch, registry.Find(LiveChatProviderType.Twitch));
        Assert.Null(registry.Find(LiveChatProviderType.TikTok));
        Assert.Single(registry.GetEnabledProviders());
        Assert.Equal(2, registry.GetProviders().Count);
    }

    [Fact]
    public async Task DisabledProvider_RemainsDisabled()
    {
        var provider = Twitch(new LiveChatProviderConfiguration { Enabled = false });

        await provider.ConnectAsync(CancellationToken.None);

        Assert.Equal(LiveChatProviderState.Disabled, provider.Snapshot.State);
        Assert.False(provider.Snapshot.Enabled);
    }

    [Fact]
    public async Task EnabledProviderWithoutCredentials_IsNotConfigured()
    {
        var provider = Twitch(new LiveChatProviderConfiguration { Enabled = true, Channel = "channel" });

        await provider.ConnectAsync(CancellationToken.None);

        Assert.Equal(LiveChatProviderState.NotConfigured, provider.Snapshot.State);
    }

    [Fact]
    public async Task HostedService_StartsEnabledProviderAndShutsDownGracefully()
    {
        var provider = new FakeProvider(LiveChatProviderType.Twitch, true, LiveChatProviderState.Connected, waitUntilDisconnect: true);
        var service = HostedService([provider]);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => provider.ConnectCalls == 1);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, provider.ConnectCalls);
        Assert.Equal(1, provider.DisconnectCalls);
    }

    [Fact]
    public async Task ProviderFailure_IsIsolatedAndReconnects()
    {
        var failing = new FakeProvider(
            LiveChatProviderType.Twitch,
            true,
            LiveChatProviderState.NotConfigured,
            failuresBeforeSuccess: 1);
        var healthy = new FakeProvider(LiveChatProviderType.YouTube, true, LiveChatProviderState.NotConfigured);
        var service = HostedService([failing, healthy]);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => failing.ConnectCalls >= 2 && healthy.ConnectCalls >= 1);
        await service.StopAsync(CancellationToken.None);

        Assert.True(failing.ConnectCalls >= 2);
        Assert.Equal(1, healthy.ConnectCalls);
    }

    [Fact]
    public void ReconnectPolicy_IsProgressiveAndCapped()
    {
        var policy = new ProgressiveLiveChatReconnectDelay();

        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(5));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(500));
    }

    [Fact]
    public async Task RateLimitedProvider_DegradesChatHealthOnly()
    {
        var provider = new FakeProvider(LiveChatProviderType.Twitch, true, LiveChatProviderState.RateLimited);
        var registry = new LiveChatProviderRegistry([provider]);
        var check = new LiveChatHealthCheck(registry);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Theory]
    [InlineData(LiveChatProviderState.Disabled)]
    [InlineData(LiveChatProviderState.NotConfigured)]
    [InlineData(LiveChatProviderState.Connected)]
    public async Task DisabledNotConfiguredAndConnectedProviders_AreHealthy(LiveChatProviderState state)
    {
        var enabled = state != LiveChatProviderState.Disabled;
        var registry = new LiveChatProviderRegistry([new FakeProvider(LiveChatProviderType.Twitch, enabled, state)]);

        var result = await new LiveChatHealthCheck(registry).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static TwitchLiveChatProvider Twitch(LiveChatProviderConfiguration configuration) =>
        new(
            Options.Create(new LiveChatProvidersOptions { Twitch = configuration }),
            new NullSender(),
            NullLogger<TwitchLiveChatProvider>.Instance);

    private static LiveChatProviderHostedService HostedService(IEnumerable<ILiveChatProvider> providers) =>
        new(
            new LiveChatProviderRegistry(providers),
            new ImmediateDelay(),
            NullLogger<LiveChatProviderHostedService>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class ImmediateDelay : ILiveChatReconnectDelay
    {
        public TimeSpan GetDelay(int attempt) => TimeSpan.Zero;
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeProvider : ILiveChatProvider
    {
        private readonly int _failuresBeforeSuccess;
        private readonly bool _waitUntilDisconnect;
        private readonly LiveChatProviderState _successState;
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeProvider(
            LiveChatProviderType provider,
            bool enabled,
            LiveChatProviderState state,
            int failuresBeforeSuccess = 0,
            bool waitUntilDisconnect = false)
        {
            Provider = provider;
            Snapshot = new LiveChatProviderSnapshot(provider, enabled, state, null, null, null, null);
            _successState = state;
            _failuresBeforeSuccess = failuresBeforeSuccess;
            _waitUntilDisconnect = waitUntilDisconnect;
        }

        public LiveChatProviderType Provider { get; }
        public LiveChatProviderSnapshot Snapshot { get; private set; }
        public TimeSpan? RetryAfter => Snapshot.State == LiveChatProviderState.RateLimited ? TimeSpan.Zero : null;
        public Task Completion => _waitUntilDisconnect ? _completion.Task : Task.CompletedTask;
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }

        public void SetLifecycleState(LiveChatProviderState state, string? error = null) =>
            Snapshot = Snapshot with { State = state, Error = error };

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectCalls++;
            if (ConnectCalls <= _failuresBeforeSuccess)
            {
                throw new IOException("provider unavailable");
            }

            Snapshot = Snapshot with { State = _successState, Error = null };
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCalls++;
            _completion.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class NullSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<object?> CreateStream(
            object request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
