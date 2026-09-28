using Microsoft.Extensions.Logging.Abstractions;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Obs;
using ObsLiveBot.Infrastructure.Obs;

namespace ObsLiveBot.UnitTests.Obs;

public sealed class ObsConnectionManagerTests
{
    [Fact]
    public async Task SuccessfulConnection_ReadsInitialRuntimeState()
    {
        var protocol = new FakeProtocolClient();
        var manager = CreateManager(protocol);

        await manager.ConnectAsync(CancellationToken.None);

        Assert.Equal(ObsConnectionState.Connected, manager.ConnectionState);
        Assert.Equal("32.1.2", manager.RuntimeState.ObsVersion);
        Assert.Equal("5.6.3", manager.RuntimeState.WebSocketVersion);
        Assert.Equal("Program", manager.RuntimeState.CurrentProgramScene);
        Assert.False(manager.RuntimeState.IsStreaming);
        Assert.False(manager.RuntimeState.IsRecording);
    }

    [Fact]
    public async Task Disconnect_UpdatesState()
    {
        var protocol = new FakeProtocolClient();
        var manager = CreateManager(protocol);
        await manager.ConnectAsync(CancellationToken.None);

        await manager.DisconnectAsync(CancellationToken.None);

        Assert.Equal(ObsConnectionState.Disconnected, manager.ConnectionState);
        Assert.False(protocol.IsConnected);
    }

    [Fact]
    public async Task AuthenticationFailure_UsesAuthenticationFailedState()
    {
        var protocol = new FakeProtocolClient { AuthenticationFailure = true };
        var manager = CreateManager(protocol);

        await Assert.ThrowsAsync<ObsAuthenticationException>(
            () => manager.ConnectAsync(CancellationToken.None));

        Assert.Equal(ObsConnectionState.AuthenticationFailed, manager.ConnectionState);
    }

    [Fact]
    public async Task OfflineObs_ReconnectsWithoutCrashing()
    {
        var protocol = new FakeProtocolClient { OfflineFailuresRemaining = 1 };
        var delay = new ImmediateReconnectDelay();
        var manager = CreateManager(protocol, delay);

        await manager.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => protocol.ConnectAttempts >= 2, CancellationToken.None);

        Assert.Equal(ObsConnectionState.Connected, manager.ConnectionState);
        Assert.True(delay.Attempts.Count >= 1);
        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CancellationToken_CancelsConnection()
    {
        var protocol = new FakeProtocolClient { WaitForCancellation = true };
        var manager = CreateManager(protocol);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ConnectAsync(cancellation.Token));
    }

    [Fact]
    public void Backoff_IsProgressiveAndCapped()
    {
        var delay = new ProgressiveReconnectDelay();

        Assert.Equal(TimeSpan.FromSeconds(1), delay.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), delay.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(5), delay.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(10), delay.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(30), delay.GetDelay(5));
        Assert.Equal(TimeSpan.FromSeconds(30), delay.GetDelay(500));
    }

    private static ObsConnectionManager CreateManager(
        FakeProtocolClient protocol,
        IReconnectDelay? reconnectDelay = null) =>
        new(
            protocol,
            reconnectDelay ?? new ProgressiveReconnectDelay(),
            new FakeEventPublisher(),
            TimeProvider.System,
            NullLogger<ObsConnectionManager>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(10, cancellationToken);
        }

        Assert.True(condition());
    }

    private sealed class FakeEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ImmediateReconnectDelay : IReconnectDelay
    {
        public List<TimeSpan> Attempts { get; } = [];

        public TimeSpan GetDelay(int attempt) => TimeSpan.FromSeconds(Math.Min(attempt, 30));

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Attempts.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProtocolClient : IObsProtocolClient
    {
        private TaskCompletionSource _completion = NewCompletion();

        public bool AuthenticationFailure { get; init; }

        public int OfflineFailuresRemaining { get; set; }

        public bool WaitForCancellation { get; init; }

        public int ConnectAttempts { get; private set; }

        public bool IsConnected { get; private set; }

        public Task Completion => _completion.Task;

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectAttempts++;
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (AuthenticationFailure)
            {
                throw new ObsAuthenticationException("Authentication rejected.");
            }

            if (OfflineFailuresRemaining-- > 0)
            {
                throw new IOException("OBS is offline.");
            }

            _completion = NewCompletion();
            IsConnected = true;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            _completion.TrySetResult();
            return Task.CompletedTask;
        }

        public Task<ObsVersionInfo> GetVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ObsVersionInfo("32.1.2", "5.6.3"));

        public Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken) =>
            Task.FromResult("Program");

        public Task<bool> GetStreamStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ObsRecordStatus(false, false));

        private static TaskCompletionSource NewCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
