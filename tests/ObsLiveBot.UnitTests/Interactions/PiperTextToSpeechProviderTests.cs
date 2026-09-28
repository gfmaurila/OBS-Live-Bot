using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;
using ObsLiveBot.Infrastructure.Configuration;
using ObsLiveBot.Infrastructure.Health;
using ObsLiveBot.Infrastructure.Interactions;

namespace ObsLiveBot.UnitTests.Interactions;

public sealed class PiperTextToSpeechProviderTests
{
    [Fact]
    public async Task SuccessfulSynthesis_MapsValidatedPcmWavAndSafeFilename()
    {
        using var context = Context((spec, _) =>
        {
            WriteValidWav(OutputPath(spec));
            return Task.FromResult(new TtsProcessExecutionResult(0, false, TimeSpan.FromMilliseconds(25)));
        });
        var interactionId = Guid.NewGuid();

        var result = await context.Provider.SynthesizeAsync(Request(interactionId), default);

        Assert.True(result.Success);
        Assert.Equal("Piper", result.ProviderName);
        Assert.Equal("pt_BR-faber-medium", result.VoiceName);
        Assert.Equal("audio/wav; codec=pcm_s16le", result.AudioFormat);
        Assert.Equal($"{interactionId:N}.wav", Path.GetFileName(result.AudioPath));
        Assert.Equal(22_050, result.SampleRate);
        Assert.Equal(16, result.BitDepth);
        Assert.Equal(1, result.Channels);
        Assert.True(result.AudioDuration > TimeSpan.Zero);
        Assert.True(new FileInfo(result.AudioPath!).Length > 44);
        Assert.False(result.IsSimulated);
    }

    [Fact]
    public async Task EmptyAndOversizedInput_AreRejectedWithoutProcess()
    {
        using var context = Context((_, _) => throw new InvalidOperationException("must not run"),
            options => options.Tts.MaxInputCharacters = 3);

        var empty = await context.Provider.SynthesizeAsync(Request(text: " "), default);
        var oversized = await context.Provider.SynthesizeAsync(Request(text: "quatro"), default);

        Assert.Equal("TTS_EMPTY_TEXT", empty.ErrorCode);
        Assert.Equal("TTS_TEXT_TOO_LONG", oversized.ErrorCode);
        Assert.Empty(context.Runner.Specs);
    }

    [Theory]
    [InlineData("Olá, ação e coração.")]
    [InlineData("Teste com emoji 🔥🚚")]
    public async Task UnicodeAndEmoji_ArePassedThroughStandardInput(string text)
    {
        using var context = Context((spec, _) =>
        {
            WriteValidWav(OutputPath(spec));
            return Task.FromResult(new TtsProcessExecutionResult(0, false, TimeSpan.Zero));
        });

        var result = await context.Provider.SynthesizeAsync(Request(text: text), default);

        Assert.True(result.Success);
        Assert.Equal(text, Assert.Single(context.Runner.Specs).StandardInput);
    }

    [Fact]
    public async Task UntrustedText_IsNeverPlacedInProcessArguments()
    {
        const string untrusted = "olá & whoami | Remove-Item -Recurse C:\\temp";
        using var context = Context((spec, _) =>
        {
            WriteValidWav(OutputPath(spec));
            return Task.FromResult(new TtsProcessExecutionResult(0, false, TimeSpan.Zero));
        });

        await context.Provider.SynthesizeAsync(Request(text: untrusted), default);
        var spec = Assert.Single(context.Runner.Specs);

        Assert.Equal(untrusted, spec.StandardInput);
        Assert.DoesNotContain(spec.Arguments, argument => argument.Contains(untrusted, StringComparison.Ordinal));
        Assert.Equal(["--model", context.Options.Tts.ModelPath, "--output_file", OutputPath(spec)], spec.Arguments);
    }

    [Fact]
    public async Task Timeout_ReturnsControlledFailureAndMetrics()
    {
        using var context = Context((_, _) => Task.FromResult(
            new TtsProcessExecutionResult(-1, true, TimeSpan.FromSeconds(1))));

        var result = await context.Provider.SynthesizeAsync(Request(), default);

        Assert.Equal("TTS_TIMEOUT", result.ErrorCode);
        Assert.Equal(1, context.Provider.GetRuntimeState().Timeouts);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var context = Context(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new TtsProcessExecutionResult(0, false, TimeSpan.Zero);
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Provider.SynthesizeAsync(Request(), cancellation.Token));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MissingEngineOrModel_IsUnavailable(bool removeEngine, bool removeModel)
    {
        using var context = Context((_, _) => throw new InvalidOperationException("must not run"));
        if (removeEngine) File.Delete(context.Options.Tts.ExecutablePath);
        if (removeModel) File.Delete(context.Options.Tts.ModelPath);

        var result = await context.Provider.SynthesizeAsync(Request(), default);

        Assert.Equal("TTS_UNAVAILABLE", result.ErrorCode);
        Assert.False(await context.Provider.CheckAvailabilityAsync(default));
    }

    [Fact]
    public async Task InvalidAudio_IsRejectedAndDeleted()
    {
        using var context = Context((spec, _) =>
        {
            File.WriteAllBytes(OutputPath(spec), [1, 2, 3, 4]);
            return Task.FromResult(new TtsProcessExecutionResult(0, false, TimeSpan.Zero));
        });

        var result = await context.Provider.SynthesizeAsync(Request(), default);

        Assert.Equal("TTS_INVALID_AUDIO", result.ErrorCode);
        Assert.Null(result.AudioPath);
        Assert.Empty(Directory.GetFiles(context.Options.Tts.OutputDirectory, "*.wav"));
    }

    [Fact]
    public async Task ConcurrencyLimit_RejectsUnboundedOverload()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = Context(async (spec, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            WriteValidWav(OutputPath(spec));
            return new TtsProcessExecutionResult(0, false, TimeSpan.Zero);
        }, options =>
        {
            options.Tts.MaxConcurrentRequests = 1;
            options.Tts.MaxQueuedRequests = 0;
        });

        var first = context.Provider.SynthesizeAsync(Request(), default);
        await entered.Task;
        var rejected = await context.Provider.SynthesizeAsync(Request(), default);
        release.TrySetResult();
        var completed = await first;

        Assert.True(completed.Success);
        Assert.Equal("TTS_BUSY", rejected.ErrorCode);
        Assert.Equal(1, context.Provider.GetRuntimeState().BusyRejections);
    }

    [Fact]
    public void Cleanup_RemovesExpiredAndOldestFilesButNotNestedOrOutsideFiles()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "tts");
        var options = CreateOptions(root);
        options.Tts.MaxFiles = 2;
        options.Tts.MaxAgeMinutes = 60;
        var store = new TtsAudioStore(Options.Create(options), TimeProvider.System);
        Directory.CreateDirectory(root);
        var expired = Path.Combine(root, "expired.wav");
        var old = Path.Combine(root, "old.wav");
        var current = Path.Combine(root, "current.wav");
        File.WriteAllBytes(expired, [1]);
        File.WriteAllBytes(old, [1]);
        File.WriteAllBytes(current, [1]);
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-20));
        File.SetLastWriteTimeUtc(current, DateTime.UtcNow.AddMinutes(-10));
        var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));
        var nestedFile = Path.Combine(nested.FullName, "nested.wav");
        File.WriteAllBytes(nestedFile, [1]);
        var outside = Path.Combine(temp.Path, "outside.wav");
        File.WriteAllBytes(outside, [1]);

        store.Cleanup(reserveSlots: 1);

        Assert.False(File.Exists(expired));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(nestedFile));
        Assert.True(File.Exists(outside));
        Assert.Throws<InvalidOperationException>(() => store.Delete(outside));
    }

    [Fact]
    public async Task Metrics_RecordSuccessFailureAndDuration()
    {
        var calls = 0;
        using var context = Context((spec, _) =>
        {
            calls++;
            if (calls == 1) WriteValidWav(OutputPath(spec));
            return Task.FromResult(new TtsProcessExecutionResult(
                calls == 1 ? 0 : 7, false, TimeSpan.FromMilliseconds(20)));
        });

        await context.Provider.SynthesizeAsync(Request(), default);
        await context.Provider.SynthesizeAsync(Request(), default);
        var state = context.Provider.GetRuntimeState();

        Assert.Equal(2, state.Requests);
        Assert.Equal(1, state.Successes);
        Assert.Equal(1, state.Failures);
        Assert.Equal(20, state.AverageDurationMilliseconds);
        Assert.NotNull(state.LastSuccessAtUtc);
        Assert.NotNull(state.LastFailureAtUtc);
    }

    [Fact]
    public void ConfigurationValidation_RejectsInvalidPiperLimits()
    {
        var options = CreateOptions(Path.GetTempPath());
        options.Tts.TimeoutSeconds = 0;
        options.Tts.MaxFiles = 0;
        options.Tts.MaxConcurrentRequests = 0;

        var result = new InteractionOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Tts:TimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("Tts:MaxFiles", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("Tts:MaxConcurrentRequests", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Registry_PreservesDevelopmentAndExposesPiperVoiceAndFormat()
    {
        using var context = Context((_, _) => throw new InvalidOperationException("not called"));
        var options = Options.Create(context.Options);
        var registry = new InteractionProviderRegistry(
            [new DevelopmentAiInteractionProvider()],
            [new DevelopmentTextToSpeechProvider(), context.Provider],
            options);

        var providers = await registry.GetProvidersAsync(default);

        Assert.Equal("Piper", registry.GetTtsProvider()!.Name);
        Assert.Equal("Development", registry.GetDevelopmentTtsProvider()!.Name);
        Assert.Contains(providers, item =>
            item.Name == "Piper" && item.Selected && item.Available &&
            item.Voice == "pt_BR-faber-medium" && item.AudioFormat!.StartsWith("audio/wav"));
    }

    [Theory]
    [InlineData(true, HealthStatus.Healthy)]
    [InlineData(false, HealthStatus.Degraded)]
    public async Task Health_ReflectsPiperAvailability(bool available, HealthStatus expected)
    {
        using var context = Context((_, _) => throw new InvalidOperationException("not called"));
        if (!available) File.Delete(context.Options.Tts.ModelPath);
        var options = Options.Create(context.Options);
        var registry = new InteractionProviderRegistry(
            [new DevelopmentAiInteractionProvider()],
            [new DevelopmentTextToSpeechProvider(), context.Provider],
            options);
        var health = new InteractionHealthCheck(registry, options);

        var result = await health.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status);
    }

    private static TestContext Context(
        Func<TtsProcessSpec, CancellationToken, Task<TtsProcessExecutionResult>> callback,
        Action<InteractionOptions>? configure = null)
    {
        var temp = new TempDirectory();
        var options = CreateOptions(Path.Combine(temp.Path, "audio"));
        configure?.Invoke(options);
        Directory.CreateDirectory(Path.GetDirectoryName(options.Tts.ExecutablePath)!);
        File.WriteAllBytes(options.Tts.ExecutablePath, [1]);
        Directory.CreateDirectory(Path.GetDirectoryName(options.Tts.ModelPath)!);
        File.WriteAllBytes(options.Tts.ModelPath, [1]);
        File.WriteAllText(options.Tts.ModelPath + ".json", "{}");
        var wrapped = Options.Create(options);
        var runner = new FakeRunner(callback);
        var store = new TtsAudioStore(wrapped, TimeProvider.System);
        var provider = new PiperTextToSpeechProvider(
            wrapped, runner, store, TimeProvider.System,
            NullLogger<PiperTextToSpeechProvider>.Instance);
        return new TestContext(temp, options, runner, provider);
    }

    private static InteractionOptions CreateOptions(string outputDirectory)
    {
        var root = Path.GetDirectoryName(outputDirectory) ?? outputDirectory;
        return new InteractionOptions
        {
            AiProvider = "Development",
            TtsProvider = "Piper",
            Tts = new PiperTtsOptions
            {
                ExecutablePath = Path.Combine(root, "engine", "piper"),
                ModelPath = Path.Combine(root, "models", "pt_BR-faber-medium.onnx"),
                OutputDirectory = outputDirectory,
                Voice = "pt_BR-faber-medium",
                TimeoutSeconds = 5,
                MaxInputCharacters = 500,
                MaxFiles = 100,
                MaxAgeMinutes = 60,
                MaxConcurrentRequests = 1,
                MaxQueuedRequests = 2,
                QueueWaitTimeoutSeconds = 1,
                AllowDevelopmentFallback = true
            }
        };
    }

    private static TextToSpeechRequest Request(Guid? interactionId = null, string text = "Olá StudioOS") =>
        new(interactionId ?? Guid.NewGuid(), text, "pt_BR-faber-medium", "pt-BR", Guid.NewGuid().ToString("N"));

    private static string OutputPath(TtsProcessSpec spec) => spec.Arguments[3];

    private static void WriteValidWav(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const int sampleRate = 22_050;
        const short channels = 1;
        const short bitDepth = 16;
        const int dataLength = 4_410;
        using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitDepth / 8);
        writer.Write((short)(channels * bitDepth / 8));
        writer.Write(bitDepth);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
    }

    private sealed class FakeRunner(
        Func<TtsProcessSpec, CancellationToken, Task<TtsProcessExecutionResult>> callback)
        : ITtsProcessRunner
    {
        public List<TtsProcessSpec> Specs { get; } = [];
        public Task<TtsProcessExecutionResult> RunAsync(
            TtsProcessSpec spec,
            CancellationToken cancellationToken)
        {
            lock (Specs) Specs.Add(spec);
            return callback(spec, cancellationToken);
        }
    }

    private sealed record TestContext(
        TempDirectory Temp,
        InteractionOptions Options,
        FakeRunner Runner,
        PiperTextToSpeechProvider Provider) : IDisposable
    {
        public void Dispose() => Temp.Dispose();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"obs-live-bot-tts-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
