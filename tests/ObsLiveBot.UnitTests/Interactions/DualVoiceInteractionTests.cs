using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Interactions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.UnitTests.Interactions;

/// <summary>
/// End-to-end behavior of the interaction pipeline now that it produces two voices. The unit under
/// test is the orchestrator: what it synthesizes, what it hands to the narrator, and what it records.
/// </summary>
public sealed class DualVoiceInteractionTests
{
    [Fact]
    public async Task VoiceMode_SynthesizesTheChatClipAndTheAssistantClip()
    {
        var tts = new RecordingTtsProvider();
        var coordinator = new RecordingDualVoiceCoordinator();
        var result = await Run(
            tts: tts, coordinator: coordinator, mode: InteractionResponseMode.Voice, message: "!studio olá");

        // Two syntheses: the chat clip, then the assistant clip.
        Assert.Equal(2, tts.Requests.Count);
        Assert.Contains(tts.Requests, request => request.Voice == "pt_BR-jeff-medium");
        Assert.Contains(tts.Requests, request => request.Voice == "pt_BR-faber-medium");
        Assert.Equal(InteractionStatus.Completed, result.Status);
    }

    [Fact]
    public async Task TheTwoRolesUseDifferentArtifactIdsSoTheyAreDistinctFiles()
    {
        var tts = new RecordingTtsProvider();
        var result = await Run(tts: tts, mode: InteractionResponseMode.Voice, message: "!studio olá");

        var chat = tts.Requests.Single(request => request.Voice == "pt_BR-jeff-medium");
        var assistant = tts.Requests.Single(request => request.Voice == "pt_BR-faber-medium");

        // Same interaction, two roles: the ids must differ or both clips would target one WAV file.
        Assert.NotEqual(Guid.Empty, chat.EffectiveArtifactId);
        Assert.NotEqual(Guid.Empty, assistant.EffectiveArtifactId);
        Assert.NotEqual(chat.EffectiveArtifactId, assistant.EffectiveArtifactId);
        Assert.NotEqual(result.InteractionId, chat.EffectiveArtifactId);
        Assert.Equal(chat.EffectiveArtifactId, result.ChatArtifactId);
    }

    [Fact]
    public async Task ChatClip_UsesTheTriggerStrippedMessageAndTheAssistantClip_UsesTheModelReply()
    {
        var tts = new RecordingTtsProvider();
        await Run(tts: tts, mode: InteractionResponseMode.Voice, message: "!studio bom dia");

        var chat = tts.Requests.Single(request => request.Voice == "pt_BR-jeff-medium");
        Assert.Equal("maria disse: bom dia", chat.Text);
        var assistant = tts.Requests.Single(request => request.Voice == "pt_BR-faber-medium");
        Assert.Equal("RESPOSTA", assistant.Text);
    }

    [Fact]
    public async Task OneGroupIsSubmittedPerInteraction_CarryingBothClips()
    {
        var coordinator = new RecordingDualVoiceCoordinator();
        var tts = new RecordingTtsProvider();
        await Run(tts: tts, coordinator: coordinator, mode: InteractionResponseMode.Voice, message: "!studio olá");

        var group = Assert.Single(coordinator.Requests);
        Assert.NotNull(group.ChatAudio);
        Assert.NotNull(group.AssistantAudio);
        Assert.Equal("corr", group.CorrelationId);
    }

    [Fact]
    public async Task AutoplayOff_StillAnswersButNothingReachesTheNarrator()
    {
        var tts = new RecordingTtsProvider();
        var coordinator = new RecordingDualVoiceCoordinator();
        var result = await Run(
            tts: tts, coordinator: coordinator, autoPlay: false,
            mode: InteractionResponseMode.Voice, message: "!studio olá");

        Assert.Equal(InteractionStatus.Completed, result.Status);
        Assert.Equal("RESPOSTA", result.ResponseText);

        // The assistant reply keeps its pre-existing behavior, but the chat clip is narration-only so
        // it is not produced at all, and nothing is handed to the narrator: no clip can be heard.
        var assistant = Assert.Single(tts.Requests);
        Assert.Equal("pt_BR-faber-medium", assistant.Voice);
        Assert.Empty(coordinator.Requests);
    }

    [Fact]
    public async Task TextOnlyMode_ProducesNoAudioAndNoGroup()
    {
        var tts = new RecordingTtsProvider();
        var coordinator = new RecordingDualVoiceCoordinator();
        await Run(tts: tts, coordinator: coordinator, mode: InteractionResponseMode.Text, message: "!studio olá");

        Assert.Empty(tts.Requests);
        Assert.Empty(coordinator.Requests);
    }

    [Fact]
    public async Task DisabledChatVoice_LeavesTheAssistantClipAlone()
    {
        var tts = new RecordingTtsProvider();
        var coordinator = new RecordingDualVoiceCoordinator();
        await Run(
            tts: tts, coordinator: coordinator, mode: InteractionResponseMode.Voice, message: "!studio olá",
            configureNarration: narration => narration.ChatVoice.Enabled = false);

        var assistant = Assert.Single(tts.Requests);
        Assert.Equal("pt_BR-faber-medium", assistant.Voice);
        var group = Assert.Single(coordinator.Requests);
        Assert.Null(group.ChatAudio);
        Assert.NotNull(group.AssistantAudio);
    }

    [Fact]
    public async Task ChatClipIsSynthesizedBeforeTheModelIsCalled()
    {
        var timeline = new List<string>();
        var tts = new RecordingTtsProvider(timeline);
        await Run(
            ai: new RecordingAiProvider(timeline), tts: tts,
            mode: InteractionResponseMode.Voice, message: "!studio olá");

        var chat = tts.Requests.Single(request => request.Voice == "pt_BR-jeff-medium");
        var chatIndex = timeline.FindIndex(entry => entry.StartsWith("tts:", StringComparison.Ordinal) &&
                                                      entry.EndsWith(chat.Text, StringComparison.Ordinal));
        var aiIndex = timeline.FindIndex(entry => entry == "ai:start");

        // The chat clip must not wait for the model, otherwise the viewer's name is heard late.
        Assert.True(chatIndex >= 0, "the chat synthesis was never recorded");
        Assert.True(aiIndex >= 0, "the model was never called");
        Assert.True(chatIndex < aiIndex,
            $"chat synthesis at {chatIndex} should precede the model call at {aiIndex}: {string.Join(", ", timeline)}");
    }

    [Fact]
    public async Task ModelFailure_StillSubmitsTheChatClipOnItsOwn()
    {
        var tts = new RecordingTtsProvider();
        var coordinator = new RecordingDualVoiceCoordinator();        var result = await Run(
            ai: new FailingAiProvider(), tts: tts, coordinator: coordinator,
            mode: InteractionResponseMode.Voice, message: "!studio olá");

        Assert.Equal(InteractionStatus.Failed, result.Status);
        var group = Assert.Single(coordinator.Requests);
        Assert.NotNull(group.ChatAudio);
        // The slot is still reserved and still closed. The promise settles with "no assistant clip" so
        // the coordinator releases whatever was accepted after this one, instead of waiting out the
        // admission timeout waiting for a reply that is never coming.
        Assert.NotNull(group.AssistantAudio);
        Assert.Null(await group.AssistantAudio!);
    }

    [Fact]
    public async Task ChatSynthesisFailure_DoesNotStopTheAssistantClip()
    {
        var tts = new RecordingTtsProvider { FailVoice = "pt_BR-jeff-medium" };
        var coordinator = new RecordingDualVoiceCoordinator();
        var result = await Run(
            tts: tts, coordinator: coordinator, mode: InteractionResponseMode.Voice, message: "!studio olá");

        Assert.Equal(InteractionStatus.Completed, result.Status);
        var group = Assert.Single(coordinator.Requests);
        Assert.NotNull(group.ChatAudio);
        Assert.False((await group.ChatAudio!)!.Success);
        Assert.True((await group.AssistantAudio!)!.Success);
    }

    [Fact]
    public async Task AssistantSynthesisFailure_StillSubmitsTheChatClip()
    {
        var tts = new RecordingTtsProvider { FailVoice = "pt_BR-faber-medium" };
        var coordinator = new RecordingDualVoiceCoordinator();
        var result = await Run(
            tts: tts, coordinator: coordinator, mode: InteractionResponseMode.Voice, message: "!studio olá");

        // The reply could not be voiced, but the group still carries both halves: the coordinator
        // skips the role whose audio never appears and speaks the chat clip.
        Assert.Equal(InteractionStatus.Failed, result.Status);
        var group = Assert.Single(coordinator.Requests);
        Assert.NotNull(group.ChatAudio);
        Assert.True((await group.ChatAudio!)!.Success);
        Assert.NotNull(group.AssistantAudio);
        Assert.False((await group.AssistantAudio!)!.Success);
    }

    [Fact]
    public async Task NarratorFailing_DoesNotFailTheInteraction()
    {
        var coordinator = new FailingDualVoiceCoordinator();
        var result = await Run(
            tts: new RecordingTtsProvider(), coordinator: coordinator,
            mode: InteractionResponseMode.Voice, message: "!studio olá");

        Assert.Equal(InteractionStatus.Completed, result.Status);
        Assert.Equal(1, coordinator.Attempts);
    }

    [Fact]
    public async Task NarratorThatNeverCompletes_DoesNotBlockTheInteraction()
    {
        var coordinator = new BlockingDualVoiceCoordinator();
        var result = await Run(
            tts: new RecordingTtsProvider(), coordinator: coordinator,
            mode: InteractionResponseMode.Voice, message: "!studio olá");

        Assert.Equal(InteractionStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Result_RecordsTheChatRoleAlongsideTheAssistantRole()
    {
        var result = await Run(tts: new RecordingTtsProvider(), mode: InteractionResponseMode.Voice,
            message: "!studio bom dia");

        Assert.True(result.ChatTtsSuccess);
        Assert.Equal("pt_BR-jeff-medium", result.ChatTtsVoice);
        Assert.Equal("maria disse: bom dia", result.ChatSpeechText);
        Assert.Equal("pt_BR-faber-medium", result.TtsVoice);
        Assert.Equal("chat-1.wav", Path.GetFileName(result.ChatAudioPath));
        Assert.Equal("assistant-2.wav", Path.GetFileName(result.AudioPath));
    }

    [Fact]
    public async Task Result_RecordsMeasuredLatencyForBothBranches()
    {
        var result = await Run(tts: new RecordingTtsProvider(), mode: InteractionResponseMode.Voice,
            message: "!studio olá");

        Assert.NotNull(result.AudioLatency);
        Assert.NotNull(result.AudioLatency!.ChatTtsMs);
        Assert.NotNull(result.AudioLatency.AiMs);
        Assert.NotNull(result.AudioLatency.AssistantTtsMs);
        Assert.NotNull(result.AudioLatency.ChatTtsStartedAfterAcceptedMs);
        Assert.NotNull(result.AudioLatency.AiStartedAfterAcceptedMs);
        Assert.NotNull(result.AudioLatency.TotalMs);
    }

    [Fact]
    public async Task LatencyShowsTheChatBranchDidNotWaitForTheModel()
    {
        var ai = new SlowAiProvider();
        var result = await Run(
            ai: ai, tts: new RecordingTtsProvider(), mode: InteractionResponseMode.Voice, message: "!studio olá");

        var latency = result.AudioLatency!;
        // The chat synthesis is reported separately from the model, so the two are not summed.
        Assert.True(latency.ChatTtsMs < latency.AiMs,
            $"chat {latency.ChatTtsMs}ms should be far below ai {latency.AiMs}ms");
    }

    [Fact]
    public async Task IgnoredMessages_ProduceNoAudioAndNoGroup()
    {
        var tts = new RecordingTtsProvider();
        var coordinator = new RecordingDualVoiceCoordinator();
        // No explicit mode: the trigger rules decide, and this message has no trigger.
        var result = await Run(
            tts: tts, coordinator: coordinator, mode: null, message: "oi tudo bem");

        Assert.Equal(InteractionStatus.Ignored, result.Status);
        Assert.Empty(tts.Requests);
        Assert.Empty(coordinator.Requests);
    }

    [Fact]
    public async Task DuplicateProviderEvents_StillRunTheDualVoicePipelineOnlyOnce()
    {
        var tts = new RecordingTtsProvider();
        var coordinator = new RecordingDualVoiceCoordinator();
        // One orchestrator instance, so the dedupe window is the one the pipeline actually keeps.
        var orchestrator = Orchestrator(null, tts, coordinator, autoPlay: true, configureNarration: null);
        var chatEvent = Event("!studio olá");

        var first = await Process(chatEvent, orchestrator, InteractionResponseMode.Voice);
        var second = await Process(chatEvent, orchestrator, InteractionResponseMode.Voice);

        Assert.Equal(InteractionStatus.Completed, first.Status);
        Assert.Equal(InteractionStatus.Ignored, second.Status);
        Assert.Single(coordinator.Requests);
        Assert.Equal(2, tts.Requests.Count);
    }

    [Fact]
    public async Task NoSecretOrRawUrlAppearsInTheSpokenChatText()
    {
        var tts = new RecordingTtsProvider();
        await Run(tts: tts, mode: InteractionResponseMode.Voice,
            message: "!studio veja https://exemplo.com/segredo?token=abc");

        var chat = tts.Requests.Single(request => request.Voice == "pt_BR-jeff-medium");
        Assert.DoesNotContain("token", chat.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("link", chat.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheGroupIsSubmittedWhileTheReplyIsStillBeingSynthesized()
    {
        // The chat clip is quick, the reply takes a while: this is the case the whole design exists for.
        var tts = new RecordingTtsProvider { AssistantDelay = TimeSpan.FromMilliseconds(150) };
        var coordinator = new SnapshottingDualVoiceCoordinator();
        await Run(tts: tts, coordinator: coordinator, mode: InteractionResponseMode.Voice,
            message: "!studio olá");

        Assert.True(coordinator.ChatWasReadyAtSubmit,
            "the chat clip should already be playable when the group is handed over");
        Assert.False(coordinator.AssistantWasReadyAtSubmit,
            "the reply must still be in flight, otherwise the chat clip could not start playing first");
    }

    private static Task<InteractionResult> Run(
        RecordingAiProvider? ai = null,
        RecordingTtsProvider? tts = null,
        IDualVoiceNarrationCoordinator? coordinator = null,
        InteractionResponseMode? mode = InteractionResponseMode.Voice,
        string message = "!studio olá",
        bool autoPlay = true,
        Action<NarrationOptions>? configureNarration = null) =>
        Process(
            Event(message),
            Orchestrator(ai, tts ?? new RecordingTtsProvider(),
                coordinator ?? new RecordingDualVoiceCoordinator(), autoPlay, configureNarration),
            mode);

    /// <summary>
    /// Builds a real orchestrator so the tests exercise the production wiring rather than a stub.
    /// </summary>
    private static InteractionOrchestrator Orchestrator(
        RecordingAiProvider? ai,
        RecordingTtsProvider tts,
        IDualVoiceNarrationCoordinator coordinator,
        bool autoPlay,
        Action<NarrationOptions>? configureNarration)
    {
        var interactions = Options.Create(new InteractionOptions());
        var narration = new NarrationOptions
        {
            Enabled = true,
            AutoPlayInteractions = autoPlay,
            ChatVoice = new ChatVoiceOptions { Enabled = true, VoiceId = "pt_BR-jeff-medium" },
            AssistantVoice = new NarrationVoiceRoleOptions { Enabled = true, VoiceId = "pt_BR-faber-medium" }
        };
        configureNarration?.Invoke(narration);
        var narrationOptions = Options.Create(narration);
        var buffer = new Application.Interactions.InteractionBuffer(interactions);
        return new InteractionOrchestrator(
            new InteractionDecisionPolicy(interactions, TimeProvider.System),
            new InteractionCooldownTracker(interactions, TimeProvider.System),
            new Application.Interactions.InteractionContextBuilder(interactions, buffer),
            new ChatSpeechBuilder(interactions, narrationOptions),
            coordinator,
            new SingleProviderRegistry(ai ?? new RecordingAiProvider(), tts),
            new AiResponseSanitizer(interactions),
            buffer,
            new NoOpEventPublisher(),
            new NoOpPublisher(),
            interactions,
            narrationOptions,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InteractionOrchestrator>.Instance);
    }

    private static Task<InteractionResult> Process(
        LiveChatEvent chatEvent,
        InteractionOrchestrator orchestrator,
        InteractionResponseMode? mode) =>
        orchestrator.ProcessAsync(chatEvent, mode, CancellationToken.None);

    private static LiveChatEvent Event(string message) =>
        new(Guid.NewGuid(), LiveChatEventType.Message, LiveChatProviderType.Twitch, "e1", "canal", "Canal",
            new LiveChatUser(LiveChatProviderType.Twitch, "u1", "maria", "maria", false, false, false, false, false, []),
            message, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, "corr",
            new Dictionary<string, string?>());

    private sealed class RecordingTtsProvider(List<string>? timeline = null) : ITextToSpeechProvider
    {
        private readonly List<string> _timeline = timeline ?? [];
        private readonly List<TextToSpeechRequest> _requests = [];
        private readonly object _gate = new();
        private int _counter;

        public string? FailVoice { get; set; }

        /// <summary>Artificial delay for the reply synthesis, to model a slow assistant voice.</summary>
        public TimeSpan AssistantDelay { get; set; }

        public IReadOnlyList<TextToSpeechRequest> Requests
        {
            get { lock (_gate) return _requests.ToArray(); }
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
            int index;
            lock (_gate)
            {
                _requests.Add(request);
                _timeline.Add($"tts:{request.Voice}:{request.Text}");
                index = ++_counter;
            }

            if (string.Equals(FailVoice, request.Voice, StringComparison.Ordinal))
            {
                return Task.FromResult(new TextToSpeechResult(
                    false, Name, AudioFormat, null, TimeSpan.Zero, "TTS_ENGINE_FAILED",
                    request.CorrelationId, false, request.Voice, null, null, null, null, request.EffectiveArtifactId));
            }

            var isChat = string.Equals(request.Voice, "pt_BR-jeff-medium", StringComparison.Ordinal);
            var result = new TextToSpeechResult(
                true, Name, AudioFormat,
                isChat ? $"chat-{index}.wav" : $"assistant-{index}.wav",
                TimeSpan.FromMilliseconds(10), null, request.CorrelationId, false, request.Voice,
                TimeSpan.FromSeconds(1), 22_050, 16, 1, request.EffectiveArtifactId);
            return isChat || AssistantDelay <= TimeSpan.Zero
                ? Task.FromResult(result)
                : Task.Delay(AssistantDelay, cancellationToken).ContinueWith(
                    _ => result, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>Records which halves of the group were already playable at the moment of submission.</summary>
    private sealed class SnapshottingDualVoiceCoordinator : IDualVoiceNarrationCoordinator
    {
        public bool ChatWasReadyAtSubmit { get; private set; }
        public bool AssistantWasReadyAtSubmit { get; private set; }

        public Task SubmitAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken)
        {
            ChatWasReadyAtSubmit = request.ChatAudio?.IsCompletedSuccessfully ?? false;
            AssistantWasReadyAtSubmit = request.AssistantAudio?.IsCompletedSuccessfully ?? false;
            return Task.CompletedTask;
        }
    }

    private class RecordingAiProvider(List<string>? timeline = null) : IAiInteractionProvider
    {
        protected readonly List<string> Timeline = timeline ?? [];

        public string Name => "Recording";
        public string ModelName => "recording";
        public bool IsAvailable => true;
        public bool IsDevelopment => false;
        public AiProviderRuntimeSnapshot GetRuntimeState() => throw new NotSupportedException();
        public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public virtual Task<AiInteractionResponse> GenerateAsync(
            AiInteractionRequest request,
            CancellationToken cancellationToken)
        {
            Timeline.Add("ai:start");
            return Task.FromResult(new AiInteractionResponse(
                "RESPOSTA", Name, ModelName, TimeSpan.FromMilliseconds(20), true, null,
                request.CorrelationId, false));
        }
    }

    private sealed class FailingAiProvider : RecordingAiProvider
    {
        public override Task<AiInteractionResponse> GenerateAsync(
            AiInteractionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiInteractionResponse(
                null, Name, ModelName, TimeSpan.FromMilliseconds(5), false, "AI_PROVIDER_FAILED",
                request.CorrelationId, false));
    }

    private sealed class SlowAiProvider : RecordingAiProvider
    {
        public override async Task<AiInteractionResponse> GenerateAsync(
            AiInteractionRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(300, cancellationToken);
            return await base.GenerateAsync(request, cancellationToken);
        }
    }

    private sealed class SingleProviderRegistry(
        IAiInteractionProvider ai,
        ITextToSpeechProvider tts) : IInteractionProviderRegistry
    {
        public IAiInteractionProvider GetAiProvider() => ai;
        public IAiInteractionProvider? GetDevelopmentAiProvider() => null;
        public bool AllowDevelopmentFallback => false;
        public ITextToSpeechProvider GetTtsProvider() => tts;
        public ITextToSpeechProvider? GetDevelopmentTtsProvider() => null;
        public bool AllowDevelopmentTtsFallback => false;
        public Task<IReadOnlyList<InteractionProviderSnapshot>> GetProvidersAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InteractionProviderSnapshot>>([]);
    }

    private sealed class NoOpEventPublisher : IInteractionEventPublisher
    {
        public Task PublishAsync(InteractionResult result, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoOpPublisher : MediatR.IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : MediatR.INotification => Task.CompletedTask;
    }
}
