using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Application.Abstractions;

public sealed class NarrationOptions
{
    public const string SectionName = "Narration";
    public bool Enabled { get; set; } = true;
    public bool AutoPlayInteractions { get; set; }
    public string SourceName { get; set; } = "GFM StudioOS - Narration";
    public int MaxQueueSize { get; set; } = 5;
    public int MaxConcurrentPlayback { get; set; } = 1;
    public int MaxNarrationSeconds { get; set; } = 15;
    public int PlaybackTimeoutSeconds { get; set; } = 25;
    public int StartTimeoutSeconds { get; set; } = 5;
    public int PollIntervalMilliseconds { get; set; } = 100;
    public double DefaultVolume { get; set; } = 70;
    public double MinimumVolume { get; set; } = 0;
    public double MaximumVolume { get; set; } = 100;
    public string MonitoringMode { get; set; } = "MonitorOff";
    public string AllowedRuntimeDirectory { get; set; } = "/app/data/runtime/tts";
    public string HostRuntimeDirectory { get; set; } = "D:/OBS-Live/OBS-Live-Bot/data/runtime/tts";
    public int EventBufferCapacity { get; set; } = 100;
}

public sealed class NarrationPlaybackException(string errorCode, Exception? innerException = null)
    : Exception(errorCode, innerException)
{
    public string ErrorCode { get; } = errorCode;
}

public interface INarrationService
{
    NarrationStateSnapshot GetState();
    IReadOnlyList<NarrationEvent> GetRecentEvents(int limit);
    IReadOnlyList<NarrationResult> GetRecentResults(int limit);
    Task<NarrationEnqueueResult> EnqueueAsync(
        NarrationAudioArtifact artifact,
        string correlationId,
        CancellationToken cancellationToken);
    Task SetMutedAsync(bool muted, CancellationToken cancellationToken);
    Task<NarrationStateSnapshot> SetVolumeAsync(double volume, CancellationToken cancellationToken);
    Task<NarrationPlaybackSnapshot> RefreshPlaybackStateAsync(CancellationToken cancellationToken);
}

public interface IAudioPlaybackService
{
    Task<NarrationPlaybackSnapshot> GetStateAsync(string sourceName, CancellationToken cancellationToken);
    Task EnsureSourceAsync(string sourceName, string runtimeDirectory, CancellationToken cancellationToken);
    Task ConfigureAsync(string sourceName, double volumePercent, bool muted, CancellationToken cancellationToken);
    Task PlayAsync(string sourceName, NarrationAudioArtifact artifact, CancellationToken cancellationToken);
    Task<ObsMediaPlaybackState> WaitForCompletionAsync(
        string sourceName,
        TimeSpan expectedDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken);
    Task StopAndClearAsync(string sourceName, CancellationToken cancellationToken);
}

public interface INarrationArtifactValidator
{
    NarrationArtifactValidation Validate(NarrationAudioArtifact artifact, string allowedRuntimeDirectory);
}

public interface IAudioArtifactLeaseRegistry
{
    IDisposable Acquire(string path);
    bool IsLeased(string path);
}

public interface INarrationEventPublisher
{
    Task PublishAsync(NarrationEvent narrationEvent, CancellationToken cancellationToken);
}
