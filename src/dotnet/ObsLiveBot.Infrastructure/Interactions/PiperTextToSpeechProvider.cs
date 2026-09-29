using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Infrastructure.Interactions;

public sealed record TtsProcessSpec(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string StandardInput,
    TimeSpan Timeout);

public sealed record TtsProcessExecutionResult(int ExitCode, bool TimedOut, TimeSpan Duration);

public interface ITtsProcessRunner
{
    Task<TtsProcessExecutionResult> RunAsync(TtsProcessSpec spec, CancellationToken cancellationToken);
}

public sealed class TtsProcessRunner : ITtsProcessRunner
{
    public async Task<TtsProcessExecutionResult> RunAsync(
        TtsProcessSpec spec,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false)
        };
        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(spec.ExecutablePath));
        if (!string.IsNullOrWhiteSpace(executableDirectory) && !OperatingSystem.IsWindows())
        {
            startInfo.Environment["LD_LIBRARY_PATH"] = executableDirectory;
        }

        using var process = new Process { StartInfo = startInfo };
        var started = Stopwatch.GetTimestamp();
        if (!process.Start())
        {
            return new TtsProcessExecutionResult(-1, false, Stopwatch.GetElapsedTime(started));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(spec.Timeout);
        try
        {
            var outputDrain = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorDrain = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.StandardInput.WriteLineAsync(spec.StandardInput.AsMemory(), timeout.Token)
                .ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(outputDrain, errorDrain).ConfigureAwait(false);
            return new TtsProcessExecutionResult(
                process.ExitCode,
                false,
                Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillSafely(process);
            return new TtsProcessExecutionResult(-1, true, Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException)
        {
            KillSafely(process);
            throw;
        }
    }

    private static void KillSafely(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

public sealed record TtsAudioMetadata(
    int SampleRate,
    int BitDepth,
    int Channels,
    long DataBytes,
    TimeSpan Duration);

public sealed class TtsAudioStore(
    IOptions<InteractionOptions> options,
    TimeProvider timeProvider,
    IAudioArtifactLeaseRegistry? artifactLeases = null)
{
    private readonly PiperTtsOptions _options = options.Value.Tts;
    private readonly string _root = EnsureTrailingSeparator(
        Path.GetFullPath(options.Value.Tts.OutputDirectory));

    public string Root => _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public string GetOutputPath(Guid interactionId)
    {
        Directory.CreateDirectory(Root);
        var path = Path.GetFullPath(Path.Combine(Root, $"{interactionId:N}.wav"));
        EnsureContained(path);
        return path;
    }

    public void Cleanup(int reserveSlots = 0)
    {
        Directory.CreateDirectory(Root);
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-_options.MaxAgeMinutes);
        var files = new DirectoryInfo(Root)
            .EnumerateFiles("*.wav", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        foreach (var file in files.Where(file =>
                     file.LastWriteTimeUtc < cutoff && artifactLeases?.IsLeased(file.FullName) != true).ToArray())
        {
            Delete(file.FullName);
            files.Remove(file);
        }

        var allowed = Math.Max(0, _options.MaxFiles - reserveSlots);
        while (files.Count > allowed)
        {
            var oldestRemovable = files.FindIndex(file => artifactLeases?.IsLeased(file.FullName) != true);
            if (oldestRemovable < 0) break;
            Delete(files[oldestRemovable].FullName);
            files.RemoveAt(oldestRemovable);
        }
    }

    public NarrationArtifactValidation ValidateArtifact(
        NarrationAudioArtifact artifact,
        string allowedRuntimeDirectory)
    {
        try
        {
            var allowedRoot = EnsureTrailingSeparator(Path.GetFullPath(allowedRuntimeDirectory));
            if (!string.Equals(allowedRoot, _root, PathComparison))
                return new NarrationArtifactValidation(false, "NARRATION_RUNTIME_DIRECTORY_MISMATCH", default, 0, 0, 0);

            var expected = Path.GetFullPath(Path.Combine(Root, $"{artifact.InteractionId:N}.wav"));
            var supplied = Path.GetFullPath(artifact.Path);
            EnsureContained(supplied);
            if (!string.Equals(expected, supplied, PathComparison) ||
                !string.Equals(Path.GetExtension(supplied), ".wav", StringComparison.OrdinalIgnoreCase))
                return new NarrationArtifactValidation(false, "NARRATION_ARTIFACT_UNREGISTERED", default, 0, 0, 0);

            var audio = ValidateWav(supplied);
            if (audio is null)
                return new NarrationArtifactValidation(false, "NARRATION_AUDIO_INVALID", default, 0, 0, 0);

            return new NarrationArtifactValidation(
                true, null, audio.Duration, audio.SampleRate, audio.BitDepth, audio.Channels);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new NarrationArtifactValidation(false, "NARRATION_ARTIFACT_INVALID", default, 0, 0, 0);
        }
    }

    public void Delete(string path)
    {
        var fullPath = Path.GetFullPath(path);
        EnsureContained(fullPath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
    }

    public TtsAudioMetadata? ValidateWav(string path)
    {
        var fullPath = Path.GetFullPath(path);
        EnsureContained(fullPath);
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length < 44) return null;

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: false);
        if (ReadFourCc(reader) != "RIFF") return null;
        _ = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE") return null;

        ushort? format = null;
        ushort? channels = null;
        uint? sampleRate = null;
        ushort? bitDepth = null;
        uint? dataBytes = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunk = ReadFourCc(reader);
            var size = reader.ReadUInt32();
            if (size > stream.Length - stream.Position) return null;
            if (chunk == "fmt " && size >= 16)
            {
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt16();
                bitDepth = reader.ReadUInt16();
                stream.Position += size - 16;
            }
            else if (chunk == "data")
            {
                dataBytes = size;
                stream.Position += size;
            }
            else
            {
                stream.Position += size;
            }

            if ((size & 1) == 1 && stream.Position < stream.Length) stream.Position++;
        }

        if (format != 1 || channels is null or 0 || sampleRate is null or 0 ||
            bitDepth is null or 0 || dataBytes is null or 0) return null;
        var bytesPerSecond = sampleRate.Value * channels.Value * (bitDepth.Value / 8d);
        if (bytesPerSecond <= 0) return null;
        return new TtsAudioMetadata(
            (int)sampleRate.Value,
            bitDepth.Value,
            channels.Value,
            dataBytes.Value,
            TimeSpan.FromSeconds(dataBytes.Value / bytesPerSecond));
    }

    private void EnsureContained(string path)
    {
        if (!path.StartsWith(_root, PathComparison))
            throw new InvalidOperationException("TTS audio path escapes the configured runtime directory.");
    }

    private static string ReadFourCc(BinaryReader reader) =>
        System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

public sealed class PiperTextToSpeechProvider : ITextToSpeechProvider
{
    private readonly PiperTtsOptions _options;
    private readonly ITtsProcessRunner _runner;
    private readonly TtsAudioStore _audioStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PiperTextToSpeechProvider> _logger;
    private readonly SemaphoreSlim _concurrency;
    private int _pendingRequests;
    private int _available;
    private long _requests;
    private long _successes;
    private long _failures;
    private long _timeouts;
    private long _busyRejections;
    private long _totalDurationTicks;
    private DateTimeOffset? _lastSuccessAtUtc;
    private DateTimeOffset? _lastFailureAtUtc;

    public PiperTextToSpeechProvider(
        IOptions<InteractionOptions> options,
        ITtsProcessRunner runner,
        TtsAudioStore audioStore,
        TimeProvider timeProvider,
        ILogger<PiperTextToSpeechProvider> logger)
    {
        _options = options.Value.Tts;
        _runner = runner;
        _audioStore = audioStore;
        _timeProvider = timeProvider;
        _logger = logger;
        _concurrency = new SemaphoreSlim(_options.MaxConcurrentRequests, _options.MaxConcurrentRequests);
        SetAvailability(FilesAvailable());
    }

    public string Name => "Piper";
    public string VoiceName => _options.Voice;
    public string AudioFormat => "audio/wav; codec=pcm_s16le";
    public bool IsAvailable => Volatile.Read(ref _available) == 1;
    public bool IsDevelopment => false;

    public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = FilesAvailable();
        SetAvailability(available);
        return Task.FromResult(available);
    }

    public TtsProviderRuntimeSnapshot GetRuntimeState()
    {
        var successes = Interlocked.Read(ref _successes);
        double? average = successes == 0
            ? null
            : TimeSpan.FromTicks(Interlocked.Read(ref _totalDurationTicks) / successes).TotalMilliseconds;
        return new TtsProviderRuntimeSnapshot(
            IsAvailable, IsAvailable ? "Ready" : "Unavailable", VoiceName, AudioFormat,
            Interlocked.Read(ref _requests), successes, Interlocked.Read(ref _failures),
            Interlocked.Read(ref _timeouts), Interlocked.Read(ref _busyRejections), average,
            _lastSuccessAtUtc, _lastFailureAtUtc);
    }

    public async Task<TextToSpeechResult> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        if (string.IsNullOrWhiteSpace(request.Text)) return Failure(request, "TTS_EMPTY_TEXT");
        if (request.Text.EnumerateRunes().Count() > _options.MaxInputCharacters)
            return Failure(request, "TTS_TEXT_TOO_LONG");
        if (!await CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false))
            return Failure(request, "TTS_UNAVAILABLE");

        var entered = await _concurrency.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        if (!entered)
        {
            if (Interlocked.Increment(ref _pendingRequests) > _options.MaxQueuedRequests)
            {
                Interlocked.Decrement(ref _pendingRequests);
                return Failure(request, "TTS_BUSY", busy: true);
            }

            try
            {
                entered = await _concurrency.WaitAsync(
                    TimeSpan.FromSeconds(_options.QueueWaitTimeoutSeconds), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingRequests);
            }
        }

        if (!entered) return Failure(request, "TTS_BUSY", busy: true);
        string? outputPath = null;
        try
        {
            _audioStore.Cleanup(reserveSlots: 1);
            outputPath = _audioStore.GetOutputPath(request.InteractionId);
            var spec = new TtsProcessSpec(
                _options.ExecutablePath,
                ["--model", _options.ModelPath, "--output_file", outputPath],
                request.Text,
                TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var execution = await _runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
            if (execution.TimedOut)
            {
                Interlocked.Increment(ref _timeouts);
                DeleteSafely(outputPath);
                return Failure(request, "TTS_TIMEOUT", execution.Duration);
            }

            if (execution.ExitCode != 0)
            {
                DeleteSafely(outputPath);
                return Failure(request, "TTS_ENGINE_FAILED", execution.Duration);
            }

            var audio = _audioStore.ValidateWav(outputPath);
            if (audio is null)
            {
                DeleteSafely(outputPath);
                return Failure(request, "TTS_INVALID_AUDIO", execution.Duration);
            }

            SetAvailability(true);
            Interlocked.Increment(ref _successes);
            Interlocked.Add(ref _totalDurationTicks, execution.Duration.Ticks);
            _lastSuccessAtUtc = _timeProvider.GetUtcNow();
            _audioStore.Cleanup();
            return new TextToSpeechResult(
                true, Name, AudioFormat, outputPath, execution.Duration, null,
                request.CorrelationId, false, VoiceName, audio.Duration,
                audio.SampleRate, audio.BitDepth, audio.Channels);
        }
        catch (OperationCanceledException)
        {
            if (outputPath is not null) DeleteSafely(outputPath);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (outputPath is not null) DeleteSafely(outputPath);
            _logger.LogWarning(
                "TTS_PIPER_FAILED provider={Provider} voice={Voice} errorType={ErrorType}",
                Name, VoiceName, exception.GetType().Name);
            return Failure(request, "TTS_RUNTIME_ERROR");
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private bool FilesAvailable() =>
        File.Exists(_options.ExecutablePath) &&
        File.Exists(_options.ModelPath) &&
        File.Exists(_options.ModelPath + ".json");

    private TextToSpeechResult Failure(
        TextToSpeechRequest request,
        string errorCode,
        TimeSpan? duration = null,
        bool busy = false)
    {
        Interlocked.Increment(ref _failures);
        if (busy) Interlocked.Increment(ref _busyRejections);
        _lastFailureAtUtc = _timeProvider.GetUtcNow();
        return new TextToSpeechResult(
            false, Name, AudioFormat, null, duration ?? TimeSpan.Zero, errorCode,
            request.CorrelationId, false, VoiceName);
    }

    private void DeleteSafely(string path)
    {
        try { _audioStore.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
    }

    private void SetAvailability(bool value) => Volatile.Write(ref _available, value ? 1 : 0);
}
