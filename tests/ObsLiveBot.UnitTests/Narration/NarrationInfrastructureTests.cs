using System.Text.Json;
using System.Threading.Channels;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Narration;
using ObsLiveBot.Domain.Narration;
using ObsLiveBot.Infrastructure.Configuration;
using ObsLiveBot.Infrastructure.Interactions;
using ObsLiveBot.Infrastructure.Obs;
using Microsoft.Extensions.Logging.Abstractions;

namespace ObsLiveBot.UnitTests.Narration;

public sealed class NarrationInfrastructureTests
{
    [Fact]
    public void ArtifactLease_PreventsCleanupUntilEveryLeaseIsReleased()
    {
        var registry = new AudioArtifactLeaseRegistry();
        var artifact = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");

        using var first = registry.Acquire(artifact);
        var second = registry.Acquire(artifact);

        Assert.True(registry.IsLeased(artifact));
        first.Dispose();
        Assert.True(registry.IsLeased(artifact));
        second.Dispose();
        Assert.False(registry.IsLeased(artifact));
    }

    [Fact]
    public void AudioCleanup_DoesNotDeleteLeasedQueuedOrPlayingArtifact()
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), $"tts-cleanup-{Guid.NewGuid():N}");
        var leases = new AudioArtifactLeaseRegistry();
        var store = new TtsAudioStore(
            Options.Create(new InteractionOptions
            {
                Tts = new PiperTtsOptions { OutputDirectory = runtimeDirectory, MaxAgeMinutes = 1 }
            }),
            TimeProvider.System,
            leases);
        var audioPath = store.GetOutputPath(Guid.NewGuid());
        File.WriteAllBytes(audioPath, [1, 2, 3]);
        File.SetLastWriteTimeUtc(audioPath, DateTime.UtcNow.AddHours(-1));

        try
        {
            using (leases.Acquire(audioPath))
            {
                store.Cleanup();
                Assert.True(File.Exists(audioPath));
            }

            store.Cleanup();
            Assert.False(File.Exists(audioPath));
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Fact]
    public void NarrationOptions_DefaultsAreSafeAndValid()
    {
        var interactionOptions = Options.Create(new InteractionOptions());
        var validator = new NarrationOptionsValidator(interactionOptions);

        var result = validator.Validate(Options.DefaultName, new NarrationOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void NarrationOptions_RejectConcurrentPlaybackAboveOne()
    {
        var interactionOptions = Options.Create(new InteractionOptions());
        var validator = new NarrationOptionsValidator(interactionOptions);
        var options = new NarrationOptions { MaxConcurrentPlayback = 2 };

        var result = validator.Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, failure => failure.Contains("MaxConcurrentPlayback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Playback_MapsContainerArtifactToWindowsHostPathAndStartsOnlyNarration()
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), $"narration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runtimeDirectory);
        var interactionId = Guid.NewGuid();
        var artifactPath = Path.Combine(runtimeDirectory, $"{interactionId:N}.wav");
        await File.WriteAllBytesAsync(artifactPath, [1, 2, 3]);
        var obs = new RecordingObsRequestClient();
        var options = new NarrationOptions
        {
            AllowedRuntimeDirectory = runtimeDirectory,
            HostRuntimeDirectory = "D:/OBS-Live/OBS-Live-Bot/data/runtime/tts"
        };
        var service = new ObsAudioPlaybackService(
            obs,
            Options.Create(options),
            Options.Create(new InteractionOptions()),
            TimeProvider.System);

        try
        {
            await service.PlayAsync("GFM StudioOS - Narration",
                new NarrationAudioArtifact(interactionId, artifactPath, "audio/wav",
                    TimeSpan.FromSeconds(2), 22_050, 16, 1), CancellationToken.None);

            var settings = Assert.Single(obs.Requests, request => request.Type == "SetInputSettings");
            var localFile = settings.Data.GetProperty("inputSettings").GetProperty("local_file").GetString();
            Assert.Equal($"D:/OBS-Live/OBS-Live-Bot/data/runtime/tts/{interactionId:N}.wav", localFile);
            Assert.All(obs.Requests, request =>
                Assert.Equal("GFM StudioOS - Narration", request.Data.GetProperty("inputName").GetString()));
            Assert.Contains(obs.Requests, request => request.Type == "TriggerMediaInputAction");
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Playback_RejectsArtifactOutsideAllowedRuntimeDirectory()
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), $"narration-{Guid.NewGuid():N}");
        var obs = new RecordingObsRequestClient();
        var service = new ObsAudioPlaybackService(
            obs,
            Options.Create(new NarrationOptions { AllowedRuntimeDirectory = runtimeDirectory }),
            Options.Create(new InteractionOptions()),
            TimeProvider.System);
        var artifact = new NarrationAudioArtifact(Guid.NewGuid(),
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav"), "audio/wav",
            TimeSpan.FromSeconds(1), 22_050, 16, 1);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PlayAsync("GFM StudioOS - Narration", artifact, CancellationToken.None));

        Assert.Equal("NARRATION_PATH_OUTSIDE_RUNTIME", error.Message);
        Assert.Empty(obs.Requests);
    }

    [Fact]
    public async Task NarrationQueue_IsBoundedAndRejectsOverflowExplicitly()
    {
        using var provider = CreateMediatorProvider();
        var service = CreateNarrationService(provider.GetRequiredService<IMediator>(), maxQueueSize: 2);

        var first = await service.EnqueueAsync(CreateArtifact(), "first", CancellationToken.None);
        var second = await service.EnqueueAsync(CreateArtifact(), "second", CancellationToken.None);
        var overflow = await service.EnqueueAsync(CreateArtifact(), "overflow", CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.False(overflow.Accepted);
        Assert.Equal("NARRATION_QUEUE_FULL", overflow.RejectionCode);
        Assert.Equal(2, service.GetState().QueueLength);
        Assert.Equal(2, service.GetState().MaxQueueSize);
    }

    [Fact]
    public async Task NarrationQueue_RejectsAudioExceedingMaximumDuration()
    {
        using var provider = CreateMediatorProvider();
        var service = CreateNarrationService(provider.GetRequiredService<IMediator>(), maxNarrationSeconds: 1);
        var artifact = CreateArtifact() with { Duration = TimeSpan.FromSeconds(2) };

        var result = await service.EnqueueAsync(artifact, "long-audio", CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("NARRATION_TOO_LONG", result.RejectionCode);
        Assert.Equal(0, service.GetState().QueueLength);
    }

    [Fact]
    public async Task NarrationPlayback_IsFifoAndNeverOverlaps()
    {
        using var provider = CreateMediatorProvider();
        var playback = new BlockingPlaybackService();
        var service = CreateNarrationService(provider.GetRequiredService<IMediator>(), playback: playback);
        await service.StartAsync(CancellationToken.None);
        try
        {
            var firstArtifact = CreateArtifact();
            var secondArtifact = CreateArtifact();
            var first = await service.EnqueueAsync(firstArtifact, "first", CancellationToken.None);
            var second = await service.EnqueueAsync(secondArtifact, "second", CancellationToken.None);
            Assert.True(first.Accepted);
            Assert.True(second.Accepted);

            Assert.Equal(firstArtifact.InteractionId, await playback.NextStartedAsync());
            await playback.WaitUntilPlaybackIsBlockedAsync();
            Assert.Equal(1, playback.ActiveCount);
            Assert.Equal(1, playback.PlayCount);
            Assert.Equal(first.Result.NarrationId, service.GetState().CurrentNarrationId);

            playback.CompleteCurrent();
            Assert.Equal(secondArtifact.InteractionId, await playback.NextStartedAsync());
            await playback.WaitUntilPlaybackIsBlockedAsync();
            Assert.Equal(1, playback.ActiveCount);
            Assert.Equal(2, playback.PlayCount);

            playback.CompleteCurrent();
            await WaitForAsync(() => service.GetState().Completed == 2);
            Assert.Equal(0, playback.ActiveCount);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task NarrationWorker_MarksObsDisconnectFailedWithoutReplay()
    {
        using var provider = CreateMediatorProvider();
        var playback = new DisconnectedPlaybackService();
        var service = CreateNarrationService(provider.GetRequiredService<IMediator>(), playback: playback);
        await service.StartAsync(CancellationToken.None);
        try
        {
            var accepted = await service.EnqueueAsync(CreateArtifact(), "disconnect", CancellationToken.None);
            Assert.True(accepted.Accepted);
            await WaitForAsync(() => service.GetState().Failed == 1);

            var result = Assert.Single(service.GetRecentResults(10));
            Assert.Equal(NarrationStatus.Failed, result.Status);
            Assert.Equal("OBS_UNAVAILABLE", result.ErrorCode);
            Assert.Equal(0, playback.PlayCount);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private sealed class RecordingObsRequestClient : IObsRequestClient
    {
        public bool IsConnected => true;
        public List<(string Type, JsonElement Data)> Requests { get; } = [];

        public Task<JsonElement> SendRequestAsync(
            string requestType,
            object requestData,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = JsonSerializer.SerializeToElement(requestData);
            Requests.Add((requestType, data));
            var response = requestType == "GetMediaInputStatus"
                ? "{\"mediaState\":\"OBS_MEDIA_STATE_PLAYING\"}"
                : "{}";
            return Task.FromResult(JsonDocument.Parse(response).RootElement.Clone());
        }
    }

    private static ServiceProvider CreateMediatorProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(NarrationService).Assembly));
        return services.BuildServiceProvider();
    }

    private static NarrationService CreateNarrationService(IMediator mediator, int maxQueueSize = 5,
        int maxNarrationSeconds = 15, IAudioPlaybackService? playback = null) =>
        new(Options.Create(new NarrationOptions
            {
                MaxQueueSize = maxQueueSize,
                MaxNarrationSeconds = maxNarrationSeconds
            }),
            playback ?? new NoOpPlaybackService(),
            new AlwaysValidArtifactValidator(),
            new AudioArtifactLeaseRegistry(),
            new NoOpNarrationEventPublisher(),
            mediator,
            TimeProvider.System,
            NullLogger<NarrationService>.Instance);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private static NarrationAudioArtifact CreateArtifact() => new(
        Guid.NewGuid(),
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav"),
        "audio/wav",
        TimeSpan.FromMilliseconds(500),
        22_050,
        16,
        1);

    private sealed class AlwaysValidArtifactValidator : INarrationArtifactValidator
    {
        public NarrationArtifactValidation Validate(NarrationAudioArtifact artifact, string allowedRuntimeDirectory) =>
            new(true, null, artifact.Duration, artifact.SampleRate, artifact.BitDepth, artifact.Channels);
    }

    private sealed class NoOpNarrationEventPublisher : INarrationEventPublisher
    {
        public Task PublishAsync(NarrationEvent narrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpPlaybackService : IAudioPlaybackService
    {
        public Task<NarrationPlaybackSnapshot> GetStateAsync(string sourceName, CancellationToken cancellationToken) =>
            Task.FromResult(new NarrationPlaybackSnapshot(false, false, sourceName, null, null,
                "MonitorOff", [], ObsMediaPlaybackState.Unknown, "Degraded", "OBS_UNAVAILABLE"));

        public Task EnsureSourceAsync(string sourceName, string runtimeDirectory, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ConfigureAsync(string sourceName, double volumePercent, bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PlayAsync(string sourceName, NarrationAudioArtifact artifact, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ObsMediaPlaybackState> WaitForCompletionAsync(string sourceName, TimeSpan expectedDuration,
            TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(ObsMediaPlaybackState.Ended);
        public Task StopAndClearAsync(string sourceName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class BlockingPlaybackService : IAudioPlaybackService
    {
        private readonly Channel<Guid> _started = Channel.CreateUnbounded<Guid>();
        private readonly Channel<TaskCompletionSource<ObsMediaPlaybackState>> _waiters =
            Channel.CreateUnbounded<TaskCompletionSource<ObsMediaPlaybackState>>();
        private int _activeCount;
        private int _playCount;

        public int ActiveCount => Volatile.Read(ref _activeCount);
        public int PlayCount => Volatile.Read(ref _playCount);

        public Task<NarrationPlaybackSnapshot> GetStateAsync(string sourceName, CancellationToken cancellationToken) =>
            Task.FromResult(new NarrationPlaybackSnapshot(false, false, sourceName, null, null,
                "MonitorOff", [], ObsMediaPlaybackState.Unknown, "Degraded", "OBS_UNAVAILABLE"));

        public Task EnsureSourceAsync(string sourceName, string runtimeDirectory, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ConfigureAsync(string sourceName, double volumePercent, bool muted, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PlayAsync(string sourceName, NarrationAudioArtifact artifact, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _activeCount);
            Interlocked.Increment(ref _playCount);
            return _started.Writer.WriteAsync(artifact.InteractionId, cancellationToken).AsTask();
        }

        public async Task<ObsMediaPlaybackState> WaitForCompletionAsync(string sourceName, TimeSpan expectedDuration,
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            var waiter = new TaskCompletionSource<ObsMediaPlaybackState>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _waiters.Writer.WriteAsync(waiter, cancellationToken);
            return await waiter.Task.WaitAsync(cancellationToken);
        }

        public Task StopAndClearAsync(string sourceName, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask<Guid> NextStartedAsync() => _started.Reader.ReadAsync();
        public Task WaitUntilPlaybackIsBlockedAsync() => _waiters.Reader.WaitToReadAsync().AsTask();

        public void CompleteCurrent()
        {
            if (!_waiters.Reader.TryRead(out var waiter)) throw new InvalidOperationException("No active playback waiter.");
            Interlocked.Decrement(ref _activeCount);
            waiter.SetResult(ObsMediaPlaybackState.Ended);
        }
    }

    private sealed class DisconnectedPlaybackService : IAudioPlaybackService
    {
        public int PlayCount { get; private set; }

        public Task<NarrationPlaybackSnapshot> GetStateAsync(string sourceName, CancellationToken cancellationToken) =>
            Task.FromResult(new NarrationPlaybackSnapshot(false, false, sourceName, null, null,
                "MonitorOff", [], ObsMediaPlaybackState.Unknown, "Unavailable", "OBS_UNAVAILABLE"));

        public Task EnsureSourceAsync(string sourceName, string runtimeDirectory, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("OBS_UNAVAILABLE"));

        public Task ConfigureAsync(string sourceName, double volumePercent, bool muted, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PlayAsync(string sourceName, NarrationAudioArtifact artifact, CancellationToken cancellationToken)
        {
            PlayCount++;
            return Task.CompletedTask;
        }

        public Task<ObsMediaPlaybackState> WaitForCompletionAsync(string sourceName, TimeSpan expectedDuration,
            TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(ObsMediaPlaybackState.Unknown);

        public Task StopAndClearAsync(string sourceName, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
