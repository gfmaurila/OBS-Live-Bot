using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Features.Narration.DevTest;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.UnitTests.Interactions;

/// <summary>
/// The isolated dual voice development test. It is the only caller of the development escape hatch, so
/// what matters is that it uses that path, speaks both roles, and leaves the autoplay switch alone.
/// </summary>
public sealed class DualVoiceDevelopmentTestTests
{
    [Fact]
    public async Task TheTestUsesTheDevelopmentPathAndNeverTheAutomaticOne()
    {
        var coordinator = new RouteRecordingCoordinator();
        var result = await Handler(coordinator).Handle(
            new TestDualVoiceNarrationCommand("chat", "reply"), CancellationToken.None);

        Assert.True(result.Value.Accepted);
        Assert.Equal(1, coordinator.DevelopmentCalls);
        Assert.Equal(0, coordinator.AutomaticCalls);
    }

    [Fact]
    public async Task AutoplayStaysOff_WhileTheTestStillSpeaks()
    {
        var coordinator = new RouteRecordingCoordinator();
        var options = new NarrationOptions { Enabled = true, AutoPlayInteractions = false };

        var result = await Handler(coordinator, options).Handle(
            new TestDualVoiceNarrationCommand("chat", "reply"), CancellationToken.None);

        // The point of the escape hatch: an explicit request plays while automatic playback stays off,
        // and the switch keeps the value the operator configured.
        Assert.True(result.Value.Accepted);
        Assert.False(options.AutoPlayInteractions);
        Assert.Single(coordinator.DevelopmentRequests);
    }

    [Fact]
    public async Task BothRolesAreSynthesizedWithTheirOwnConfiguredVoice()
    {
        var tts = new RecordingTts();
        var result = await Handler(new RouteRecordingCoordinator(), tts: tts).Handle(
            new TestDualVoiceNarrationCommand("GFMaurila disse: teste", "resposta"), CancellationToken.None);

        Assert.Equal(
            new[] { "pt_BR-jeff-medium", "pt_BR-faber-medium" },
            tts.Requests.Select(request => request.Voice).ToArray());

        // One interaction, two clips, so the two artifact ids must differ or both would target one file.
        var artifacts = tts.Requests.Select(request => request.EffectiveArtifactId).ToArray();
        Assert.NotEqual(artifacts[0], artifacts[1]);
        Assert.Equal("pt_BR-jeff-medium", result.Value.ChatVoice);
        Assert.Equal("pt_BR-faber-medium", result.Value.AssistantVoice);
    }

    [Fact]
    public async Task TheChatClipIsAlreadyPlayableWhenTheGroupIsSubmitted()
    {
        var coordinator = new RouteRecordingCoordinator();
        var tts = new RecordingTts { AssistantDelay = TimeSpan.FromMilliseconds(150) };

        await Handler(coordinator, tts: tts).Handle(
            new TestDualVoiceNarrationCommand("chat", "reply"), CancellationToken.None);

        // The reply is still being voiced at handover, which is the case ordering has to survive.
        Assert.True(coordinator.ChatReadyAtSubmit);
        Assert.False(coordinator.AssistantReadyAtSubmit);
    }

    [Fact]
    public async Task ASimulatedVoiceIsRefusedBecauseItCannotBeHeard()
    {
        var result = await Handler(new RouteRecordingCoordinator(), tts: new RecordingTts { Simulated = true })
            .Handle(new TestDualVoiceNarrationCommand("chat", "reply"), CancellationToken.None);

        Assert.False(result.Value.Accepted);
        Assert.Equal("CHAT_TTS_FAILED", result.Value.ErrorCode);
    }

    [Fact]
    public async Task AFailedReplyStillLeavesTheChatClipPlaying()
    {
        var coordinator = new RouteRecordingCoordinator();
        var result = await Handler(coordinator, tts: new RecordingTts { FailAssistant = true })
            .Handle(new TestDualVoiceNarrationCommand("chat", "reply"), CancellationToken.None);

        // The group was already admitted, so the chat half is heard; the test reports the reply error
        // rather than pretending the whole playback failed.
        Assert.True(result.Value.Accepted);
        Assert.Equal("TTS_ENGINE_FAILED", result.Value.ErrorCode);
        Assert.Null(result.Value.AssistantAudioDurationSeconds);
        Assert.NotNull(result.Value.ChatAudioDurationSeconds);
        Assert.Single(coordinator.DevelopmentRequests);
    }

    [Fact]
    public async Task NarrationDisabled_RefusesTheTest()
    {
        var coordinator = new RouteRecordingCoordinator();
        var result = await Handler(coordinator, narration: new NarrationOptions { Enabled = false })
            .Handle(new TestDualVoiceNarrationCommand("chat", "reply"), CancellationToken.None);

        Assert.False(result.Value.Accepted);
        Assert.Equal("NARRATION_DISABLED", result.Value.ErrorCode);
        Assert.Equal(0, coordinator.DevelopmentCalls + coordinator.AutomaticCalls);
    }

    [Fact]
    public async Task RepeatedTestsKeepTheirRelativeOrder()
    {
        var coordinator = new RouteRecordingCoordinator();
        var handler = Handler(coordinator);

        await handler.Handle(new TestDualVoiceNarrationCommand("a", "b"), CancellationToken.None);
        await handler.Handle(new TestDualVoiceNarrationCommand("c", "d"), CancellationToken.None);

        // Distinct, increasing acceptance keys: a second test must not be refused as a duplicate.
        var sequences = coordinator.DevelopmentRequests.Select(request => request.AcceptedSequence).ToArray();
        Assert.Equal(2, sequences.Distinct().Count());
        Assert.True(sequences[0] < sequences[1]);
    }

    private static TestDualVoiceNarrationCommandHandler Handler(
        IDualVoiceNarrationCoordinator coordinator,
        NarrationOptions? narration = null,
        RecordingTts? tts = null)
    {
        var interactions = Options.Create(new InteractionOptions { Language = "pt-BR" });
        var narrationOptions = Options.Create(narration ?? new NarrationOptions
        {
            Enabled = true,
            AutoPlayInteractions = false,
            ChatVoice = new ChatVoiceOptions { VoiceId = "pt_BR-jeff-medium" },
            AssistantVoice = new NarrationVoiceRoleOptions { VoiceId = "pt_BR-faber-medium" }
        });
        return new TestDualVoiceNarrationCommandHandler(
            new SingleProviderRegistry(tts ?? new RecordingTts()),
            coordinator,
            interactions,
            narrationOptions);
    }

    /// <summary>Tells the two submission paths apart, which is the property the escape hatch exists for.</summary>
    private sealed class RouteRecordingCoordinator : IDualVoiceNarrationCoordinator
    {
        private readonly object _gate = new();
        private readonly List<DualVoiceNarrationRequest> _development = [];

        public IReadOnlyList<DualVoiceNarrationRequest> DevelopmentRequests
        {
            get { lock (_gate) return _development.ToArray(); }
        }

        public int DevelopmentCalls { get; private set; }

        public int AutomaticCalls { get; private set; }

        public bool ChatReadyAtSubmit { get; private set; }

        public bool AssistantReadyAtSubmit { get; private set; }

        public Task SubmitAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken)
        {
            AutomaticCalls++;
            return Task.CompletedTask;
        }

        public Task SubmitDevelopmentTestAsync(
            DualVoiceNarrationRequest request,
            CancellationToken cancellationToken)
        {
            DevelopmentCalls++;
            ChatReadyAtSubmit = request.ChatAudio?.IsCompletedSuccessfully ?? false;
            AssistantReadyAtSubmit = request.AssistantAudio?.IsCompletedSuccessfully ?? false;
            lock (_gate) _development.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTts : ITextToSpeechProvider
    {
        private readonly List<TextToSpeechRequest> _requests = [];
        private int _counter;

        public bool Simulated { get; init; }

        public bool FailAssistant { get; init; }

        public TimeSpan AssistantDelay { get; init; }

        public IReadOnlyList<TextToSpeechRequest> Requests
        {
            get { lock (_requests) return _requests.ToArray(); }
        }

        public string Name => "Recording";
        public string VoiceName => "pt_BR-faber-medium";
        public string AudioFormat => "audio/wav";
        public bool IsAvailable => true;
        public bool IsDevelopment => false;
        public TtsProviderRuntimeSnapshot GetRuntimeState() => throw new NotSupportedException();
        public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<TextToSpeechResult> SynthesizeAsync(
            TextToSpeechRequest request,
            CancellationToken cancellationToken)
        {
            var index = ++_counter;
            lock (_requests) _requests.Add(request);

            var isChat = string.Equals(request.Voice, "pt_BR-jeff-medium", StringComparison.Ordinal);
            var failed = !isChat && FailAssistant;
            var result = new TextToSpeechResult(
                !failed, Name, AudioFormat,
                failed ? null : $"clip-{index}.wav",
                TimeSpan.FromMilliseconds(10), failed ? "TTS_ENGINE_FAILED" : null,
                request.CorrelationId, Simulated, request.Voice,
                TimeSpan.FromSeconds(1), 22_050, 16, 1, request.EffectiveArtifactId);

            return !isChat && AssistantDelay > TimeSpan.Zero
                ? Task.Delay(AssistantDelay, cancellationToken).ContinueWith(
                    _ => result, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default)
                : Task.FromResult(result);
        }
    }

    private sealed class SingleProviderRegistry(ITextToSpeechProvider tts) : IInteractionProviderRegistry
    {
        public IAiInteractionProvider GetAiProvider() => throw new NotSupportedException();
        public IAiInteractionProvider? GetDevelopmentAiProvider() => null;
        public bool AllowDevelopmentFallback => false;
        public ITextToSpeechProvider GetTtsProvider() => tts;
        public ITextToSpeechProvider? GetDevelopmentTtsProvider() => null;
        public bool AllowDevelopmentTtsFallback => false;
        public Task<IReadOnlyList<InteractionProviderSnapshot>> GetProvidersAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InteractionProviderSnapshot>>([]);
    }
}