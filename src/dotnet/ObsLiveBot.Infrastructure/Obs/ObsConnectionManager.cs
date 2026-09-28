using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Infrastructure.Obs;

public sealed class ObsConnectionManager(
    IObsProtocolClient protocolClient,
    IReconnectDelay reconnectDelay,
    IDomainEventPublisher eventPublisher,
    IObsLiveStateTracker liveStateTracker,
    TimeProvider timeProvider,
    ILogger<ObsConnectionManager> logger) : BackgroundService, IObsClient
{
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private ObsRuntimeState _runtimeState = ObsRuntimeState.Initial;

    public ObsConnectionState ConnectionState => RuntimeState.ConnectionState;

    public ObsRuntimeState RuntimeState
    {
        get
        {
            lock (_stateLock)
            {
                return _runtimeState;
            }
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (protocolClient.IsConnected)
            {
                return;
            }

            UpdateConnectionState(ObsConnectionState.Connecting);
            logger.LogInformation("OBS_CONNECTING");
            await protocolClient.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await RefreshRuntimeStateAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("OBS_CONNECTED");
            await eventPublisher.PublishAsync(
                new ObsConnected(timeProvider.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ObsAuthenticationException)
        {
            UpdateConnectionState(ObsConnectionState.AuthenticationFailed);
            logger.LogError("OBS_AUTH_FAILED");
            await eventPublisher.PublishAsync(
                new ObsConnectionFailed(timeProvider.GetUtcNow(), "authentication_failed"),
                cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await protocolClient.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            UpdateConnectionState(ObsConnectionState.Disconnected);
            await liveStateTracker.MarkStaleAsync(ObsConnectionState.Disconnected, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("OBS_DISCONNECTED");
            await eventPublisher.PublishAsync(
                new ObsDisconnected(timeProvider.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public Task<ObsVersionInfo> GetVersionAsync(CancellationToken cancellationToken = default) =>
        protocolClient.GetVersionAsync(cancellationToken);

    public Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken = default) =>
        protocolClient.GetCurrentProgramSceneAsync(cancellationToken);

    public Task<bool> GetStreamStatusAsync(CancellationToken cancellationToken = default) =>
        protocolClient.GetStreamStatusAsync(cancellationToken);

    public Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken = default) =>
        protocolClient.GetRecordStatusAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var authenticationFailed = false;
            try
            {
                if (!protocolClient.IsConnected)
                {
                    if (attempt > 0)
                    {
                        UpdateConnectionState(ObsConnectionState.Reconnecting);
                    }

                    await ConnectAsync(stoppingToken).ConfigureAwait(false);
                    attempt = 0;
                }

                await ProcessEventsUntilDisconnectedAsync(stoppingToken).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                UpdateConnectionState(ObsConnectionState.Disconnected);
                await liveStateTracker.MarkStaleAsync(ObsConnectionState.Disconnected, stoppingToken).ConfigureAwait(false);
                logger.LogWarning("OBS_DISCONNECTED");
                await eventPublisher.PublishAsync(
                    new ObsDisconnected(timeProvider.GetUtcNow()),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObsAuthenticationException)
            {
                // ConnectAsync already emitted the safe authentication event and log code.
                authenticationFailed = true;
            }
            catch (Exception exception)
            {
                UpdateConnectionState(ObsConnectionState.Reconnecting);
                await liveStateTracker.MarkStaleAsync(ObsConnectionState.Reconnecting, stoppingToken).ConfigureAwait(false);
                logger.LogWarning("OBS_CONNECTION_FAILED errorType={ErrorType}", exception.GetType().Name);
                await eventPublisher.PublishAsync(
                    new ObsConnectionFailed(timeProvider.GetUtcNow(), "connection_failed"),
                    stoppingToken).ConfigureAwait(false);
            }

            attempt++;
            var delay = reconnectDelay.GetDelay(attempt);
            if (!authenticationFailed)
            {
                UpdateConnectionState(ObsConnectionState.Reconnecting);
                await liveStateTracker.MarkStaleAsync(ObsConnectionState.Reconnecting, stoppingToken).ConfigureAwait(false);
            }
            logger.LogInformation(
                "OBS_RECONNECTING attempt={Attempt} delaySeconds={DelaySeconds}",
                attempt,
                delay.TotalSeconds);
            await eventPublisher.PublishAsync(
                new ObsReconnecting(timeProvider.GetUtcNow(), attempt, delay),
                stoppingToken).ConfigureAwait(false);

            try
            {
                await reconnectDelay.WaitAsync(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await protocolClient.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        UpdateConnectionState(ObsConnectionState.Disconnected);
        await liveStateTracker.MarkStaleAsync(ObsConnectionState.Disconnected, cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshRuntimeStateAsync(CancellationToken cancellationToken)
    {
        var version = await protocolClient.GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var scene = await protocolClient.GetCurrentProgramSceneAsync(cancellationToken).ConfigureAwait(false);
        var streaming = await protocolClient.GetStreamStatusAsync(cancellationToken).ConfigureAwait(false);
        var recording = await protocolClient.GetRecordStatusAsync(cancellationToken).ConfigureAwait(false);
        var sceneCollection = await protocolClient.GetCurrentSceneCollectionAsync(cancellationToken).ConfigureAwait(false);
        var profile = await protocolClient.GetCurrentProfileAsync(cancellationToken).ConfigureAwait(false);
        var replayBuffer = await protocolClient.GetReplayBufferStatusAsync(cancellationToken).ConfigureAwait(false);
        var virtualCamera = await protocolClient.GetVirtualCameraStatusAsync(cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            _runtimeState = new ObsRuntimeState(
                ObsConnectionState.Connected,
                version.ObsVersion,
                version.WebSocketVersion,
                scene,
                streaming,
                recording.IsRecording,
                recording.IsPaused,
                timeProvider.GetUtcNow());
        }

        await liveStateTracker.SynchronizeAsync(
            new ObsStateSnapshot(
                version.ObsVersion,
                version.WebSocketVersion,
                scene,
                sceneCollection,
                profile,
                streaming,
                recording.IsRecording,
                recording.IsPaused,
                replayBuffer,
                virtualCamera),
            Guid.NewGuid(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessEventsUntilDisconnectedAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && protocolClient.IsConnected)
        {
            while (protocolClient.Events.TryRead(out var message))
            {
                try
                {
                    await liveStateTracker.ProcessAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning("OBS_STATE_EVENT_FAILED eventType={EventType} errorType={ErrorType}", message.EventType, exception.GetType().Name);
                }
            }

            var eventAvailable = protocolClient.Events.WaitToReadAsync(cancellationToken).AsTask();
            var completed = await Task.WhenAny(eventAvailable, protocolClient.Completion).ConfigureAwait(false);
            if (completed == protocolClient.Completion) return;
            if (!await eventAvailable.ConfigureAwait(false)) return;
        }
    }

    private void UpdateConnectionState(ObsConnectionState state)
    {
        lock (_stateLock)
        {
            _runtimeState = _runtimeState with
            {
                ConnectionState = state,
                LastUpdatedUtc = timeProvider.GetUtcNow()
            };
        }
    }
}
