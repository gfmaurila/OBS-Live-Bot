using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Narration;
using ObsLiveBot.Domain.Interactions;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.UnitTests.Interactions;

public sealed class DualVoiceNarrationTests
{
    [Fact]
    public async Task ChatRole_IsAdmittedBeforeTheAssistantRole()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "corr", Chat("chat.wav"), Assistant("assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);

        await WaitForAsync(() => narration.Enqueued.Count == 2);

        Assert.Equal(
            new[] { NarrationVoiceRole.Chat, NarrationVoiceRole.Assistant },
            narration.Enqueued.Select(item => item.Role).ToArray());
        Assert.Equal(
            new[] { NarrationOrder.Chat, NarrationOrder.Assistant },
            narration.Enqueued.Select(item => item.Order).ToArray());
    }

    [Fact]
    public async Task ChatAudioWaitsForTheAiReply_AndStillPlaysFirst()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        var aiGate = PendingAssistant();

        // The chat clip is ready immediately; the assistant clip is not ready until the model answers.
        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "corr", Chat("chat.wav"), aiGate.Task, TimeSpan.Zero),
            CancellationToken.None);

        await WaitForAsync(() => narration.Enqueued.Count == 1);
        Assert.Equal(NarrationVoiceRole.Chat, narration.Enqueued[0].Role);

        aiGate.SetResult(Audio("assistant.wav", "assistant"));
        await WaitForAsync(() => narration.Enqueued.Count == 2);
        Assert.Equal(
            new[] { NarrationVoiceRole.Chat, NarrationVoiceRole.Assistant },
            narration.Enqueued.Select(item => item.Role).ToArray());
    }

    /// <summary>
    /// The headline guarantee. A is accepted first, B's reply is ready first, and playback must still
    /// be A chat, A assistant, B chat, B assistant. Ordering is by acceptance, not by readiness.
    /// </summary>
    [Fact]
    public async Task InteractionAcceptedFirst_PlaysFirst_EvenWhenTheLaterReplyIsReadyFirst()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        var aReply = PendingAssistant();

        // A is accepted first. Its model call is still running, so its reply is not ready.
        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "A", Chat("a-chat.wav"), aReply.Task, TimeSpan.Zero),
            CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 1);

        // B is accepted second but is ready immediately, so a coordinator that ordered by readiness
        // would put B's chat clip in the queue right now.
        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 2, "B", Chat("b-chat.wav"), Assistant("b-assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);

        await Task.Delay(150);
        Assert.Equal(new[] { "A" }, narration.Enqueued.Select(item => item.CorrelationId).Distinct().ToArray());
        Assert.Equal(NarrationVoiceRole.Chat, narration.Enqueued[0].Role);

        // Only now does A's reply land, and the whole of A is spoken before B is let in.
        aReply.SetResult(Audio("a-assistant.wav", "assistant"));
        await WaitForAsync(() => narration.Enqueued.Count == 4);

        Assert.Equal(
            new[] { "A", "A", "B", "B" },
            narration.Enqueued.Select(item => item.CorrelationId).ToArray());
        Assert.Equal(
            new[] { NarrationVoiceRole.Chat, NarrationVoiceRole.Assistant,
                    NarrationVoiceRole.Chat, NarrationVoiceRole.Assistant },
            narration.Enqueued.Select(item => item.Role).ToArray());
    }

    [Fact]
    public async Task AdmissionFollowsTheAcceptanceKey_NotTheArrivalOfTheSubmissions()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);

        // B reaches the coordinator first, yet A was accepted first and must still be spoken first.
        var b = new DualVoiceNarrationRequest(
            Guid.NewGuid(), 2, "B", Chat("b-chat.wav"), Assistant("b-assistant.wav"), TimeSpan.Zero);
        var a = new DualVoiceNarrationRequest(
            Guid.NewGuid(), 1, "A", Chat("a-chat.wav"), Assistant("a-assistant.wav"), TimeSpan.Zero);

        await coordinator.SubmitAsync(b, CancellationToken.None);
        await coordinator.SubmitAsync(a, CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 4);

        Assert.Equal(new[] { "A", "A", "B", "B" }, narration.Enqueued.Select(item => item.CorrelationId).ToArray());
        Assert.Equal(
            new[] { NarrationVoiceRole.Chat, NarrationVoiceRole.Assistant,
                    NarrationVoiceRole.Chat, NarrationVoiceRole.Assistant },
            narration.Enqueued.Select(item => item.Role).ToArray());
    }

    [Fact]
    public async Task FailedAiCall_ClosesTheSlotAndLetsTheNextInteractionThrough()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        var aReply = PendingAssistant();

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "A", Chat("a-chat.wav"), aReply.Task, TimeSpan.Zero),
            CancellationToken.None);
        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 2, "B", Chat("b-chat.wav"), Assistant("b-assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 1);

        // The model failed, so the pipeline resolves the promise with "no assistant clip".
        aReply.SetResult(null);
        await WaitForAsync(() => narration.Enqueued.Count == 3);

        // A's chat is still heard, A's slot is closed, and B is not blocked behind it.
        Assert.Equal(
            new[] { "A", "B", "B" },
            narration.Enqueued.Select(item => item.CorrelationId).ToArray());
        Assert.Equal(
            new[] { NarrationVoiceRole.Chat, NarrationVoiceRole.Chat, NarrationVoiceRole.Assistant },
            narration.Enqueued.Select(item => item.Role).ToArray());
    }

    [Fact]
    public async Task FailedAssistantSynthesis_ClosesTheSlotAndLetsTheNextInteractionThrough()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "A", Chat("a-chat.wav"), Failed("TTS_ENGINE_FAILED"), TimeSpan.Zero),
            CancellationToken.None);
        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 2, "B", Chat("b-chat.wav"), Assistant("b-assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 3);

        // A's reply could not be voiced: A's chat plays, A's slot still closes, B proceeds in order.
        Assert.Equal(
            new[] { "A", "B", "B" },
            narration.Enqueued.Select(item => item.CorrelationId).ToArray());
        Assert.Equal(
            new[] { NarrationVoiceRole.Chat, NarrationVoiceRole.Chat, NarrationVoiceRole.Assistant },
            narration.Enqueued.Select(item => item.Role).ToArray());
    }

    [Fact]
    public async Task ReplyThatNeverArrives_ReleasesTheSlotAfterTheAdmissionTimeout()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration, admissionTimeoutSeconds: 1);
        var neverArrives = PendingAssistant();

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "stuck", Chat("stuck-chat.wav"), neverArrives.Task, TimeSpan.Zero),
            CancellationToken.None);
        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 2, "next", Chat("next-chat.wav"), Assistant("next-assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);

        // A hung model call must not hold the playback order for good.
        await WaitForAsync(() => narration.Enqueued.Count == 3, timeoutMilliseconds: 10_000);
        Assert.Equal(
            new[] { "stuck", "next", "next" },
            narration.Enqueued.Select(item => item.CorrelationId).ToArray());

        // The abandoned interaction can never sneak its clip in behind the ones already released.
        neverArrives.SetResult(Audio("late-assistant.wav", "assistant"));
        await Task.Delay(150);
        Assert.Equal(3, narration.Enqueued.Count);
        Assert.DoesNotContain(narration.Enqueued, item => item.Artifact.Path.Contains("late-assistant"));
    }

    [Fact]
    public async Task GroupSequence_IncreasesInPlaybackOrder()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);

        for (var index = 0; index < 3; index++) await SubmitAsync(coordinator, $"G{index}", index + 1);
        await WaitForAsync(() => narration.Enqueued.Count == 6);

        var sequences = narration.Enqueued.Select(item => item.GroupSequence).Distinct().ToArray();
        Assert.Equal(3, sequences.Length);
        Assert.Equal(sequences.Order(), sequences);
        Assert.Equal(sequences.Distinct().Count(), sequences.Length);
    }

    [Fact]
    public async Task CorrelationIdAndInteractionId_AreCarriedOnEveryItem()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        var interactionId = Guid.NewGuid();

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                interactionId, 1, "corr-123", Chat("chat.wav"), Assistant("assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 2);

        Assert.All(narration.Enqueued, item =>
        {
            Assert.Equal(interactionId, item.Artifact.InteractionId);
            Assert.Equal("corr-123", item.CorrelationId);
        });
    }

    [Fact]
    public async Task ChatAndAssistantArtifacts_HaveDistinctFileNames()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        var chat = Guid.NewGuid();
        var assistant = Guid.NewGuid();

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "corr",
                Task.FromResult<TextToSpeechResult?>(Audio("chat.wav", "chat", chat)),
                Assistant("assistant.wav", assistant), TimeSpan.Zero),
            CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 2);

        Assert.Equal(chat, narration.Enqueued[0].Artifact.ArtifactId);
        Assert.Equal(assistant, narration.Enqueued[1].Artifact.ArtifactId);
        Assert.NotEqual(narration.Enqueued[0].Artifact.EffectiveArtifactId,
            narration.Enqueued[1].Artifact.EffectiveArtifactId);
    }

    [Fact]
    public async Task PerRoleVolume_IsAppliedIndependently()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration,
            configureChat: chat => chat.Volume = 40,
            configureAssistant: assistant => assistant.Volume = 90);

        await SubmitAsync(coordinator, "A", 1);
        await WaitForAsync(() => narration.Enqueued.Count == 2);

        Assert.Equal(40, narration.Enqueued[0].RequestedVolume);
        Assert.Equal(90, narration.Enqueued[1].RequestedVolume);
    }

    [Fact]
    public async Task RoleVolumeLeftUnset_FallsBackToTheNarrationWideVolume()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration, configureChat: chat => chat.Volume = 40);

        await SubmitAsync(coordinator, "A", 1);
        await WaitForAsync(() => narration.Enqueued.Count == 2);

        Assert.Equal(40, narration.Enqueued[0].RequestedVolume);
        Assert.Null(narration.Enqueued[1].RequestedVolume);
    }

    [Fact]
    public async Task DisablingOneRole_LeavesTheOtherIntact()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration, configureChat: chat => chat.Enabled = false);

        await SubmitAsync(coordinator, "A", 1);
        await WaitForAsync(() => narration.Enqueued.Count == 1);

        Assert.Equal(NarrationVoiceRole.Assistant, narration.Enqueued[0].Role);
    }

    [Fact]
    public async Task FailedChatSynthesis_StillQueuesTheAssistantReply()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "corr", Failed("TTS_BUSY"), Assistant("assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 1);

        Assert.Equal(NarrationVoiceRole.Assistant, narration.Enqueued[0].Role);
    }

    [Fact]
    public async Task FailedAssistantSynthesis_StillQueuesTheChatMessage()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "corr", Chat("chat.wav"), Failed("TTS_ENGINE_FAILED"), TimeSpan.Zero),
            CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 1);

        Assert.Equal(NarrationVoiceRole.Chat, narration.Enqueued[0].Role);
    }

    [Fact]
    public async Task BothRolesFailing_QueuesNothingAndDoesNotStallTheNextGroup()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "bad", Failed("TTS_BUSY"), Failed("TTS_BUSY"), TimeSpan.Zero),
            CancellationToken.None);
        await SubmitAsync(coordinator, "good", 2);
        await WaitForAsync(() => narration.Enqueued.Count == 2);

        Assert.All(narration.Enqueued, item => Assert.Equal("good", item.CorrelationId));
    }

    [Fact]
    public async Task RoleSynthesisThatThrows_IsTreatedAsMissingAudio()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        var boom = PendingAssistant();
        boom.SetException(new InvalidOperationException("engine exploded"));

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "corr", boom.Task, Assistant("assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 1);

        Assert.Equal(NarrationVoiceRole.Assistant, narration.Enqueued[0].Role);
    }

    [Fact]
    public async Task NarratorRejectingAnItem_DoesNotStallLaterGroups()
    {
        var narration = new RecordingNarrationService { RejectCorrelationId = "first" };
        var coordinator = Coordinator(narration);

        await SubmitAsync(coordinator, "first", 1);
        await SubmitAsync(coordinator, "second", 2);
        await WaitForAsync(() => narration.Enqueued.Count == 2);

        Assert.Equal(new[] { "second" }, narration.Enqueued.Select(item => item.CorrelationId).Distinct().ToArray());
    }

    [Fact]
    public async Task AutoplayOff_SubmitsNothingToTheNarrator()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration, autoPlay: false);

        await SubmitAsync(coordinator, "A", 1);
        await Task.Delay(80);

        Assert.Empty(narration.Enqueued);
    }

    [Fact]
    public async Task ReorderingTheSameInteractionTwice_IsRejectedAsADuplicate()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        var request = new DualVoiceNarrationRequest(
            Guid.NewGuid(), 7, "same", Chat("chat.wav"), Assistant("assistant.wav"), TimeSpan.Zero);

        await coordinator.SubmitAsync(request, CancellationToken.None);
        await coordinator.SubmitAsync(request, CancellationToken.None);
        await WaitForAsync(() => narration.Enqueued.Count == 2);
        await Task.Delay(100);

        // A retried submission must not speak the same interaction twice.
        Assert.Equal(2, narration.Enqueued.Count);
    }

    [Fact]
    public async Task SubmissionBeyondTheAdmissionLimit_IsRejectedRatherThanBuffered()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration, admissionTimeoutSeconds: 300);
        // Hold every assistant clip so no group can complete and free an admission slot.
        var blockers = new List<TaskCompletionSource<TextToSpeechResult?>>();
        for (var index = 0; index < DualVoiceNarrationCoordinator.MaxPendingGroups + 4; index++)
        {
            var gate = PendingAssistant();
            blockers.Add(gate);
            await coordinator.SubmitAsync(
                new DualVoiceNarrationRequest(
                    Guid.NewGuid(), index + 1, $"corr{index}", Chat("chat.wav"), gate.Task, TimeSpan.Zero),
                CancellationToken.None);
        }

        await Task.Delay(200);
        // Bounded: at most one chat clip per admitted group reached the queue.
        Assert.True(narration.Enqueued.Count <= DualVoiceNarrationCoordinator.MaxPendingGroups,
            $"enqueued {narration.Enqueued.Count}");

        foreach (var gate in blockers) gate.TrySetCanceled();
    }

    [Fact]
    public async Task PlaybackStaysSerial_EachItemCarriesASingleNarrationId()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);

        for (var index = 0; index < 4; index++) await SubmitAsync(coordinator, $"G{index}", index + 1);
        await WaitForAsync(() => narration.Enqueued.Count == 8);

        // One queue entry per item, each with its own narration id: the single-reader queue is what
        // keeps two voices from overlapping.
        Assert.Equal(8, narration.Enqueued.Select(item => item.NarrationId).Distinct().Count());
        Assert.Equal(4, narration.Enqueued.Count(item => item.Order == NarrationOrder.Chat));
        Assert.Equal(4, narration.Enqueued.Count(item => item.Order == NarrationOrder.Assistant));
    }

    private static async Task SubmitAsync(IDualVoiceNarrationCoordinator coordinator, string group, long acceptedSequence)
    {
        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), acceptedSequence, group,
                Chat($"{group}-chat.wav"), Assistant($"{group}-assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);
    }

    private static TaskCompletionSource<TextToSpeechResult?> PendingAssistant() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static DualVoiceNarrationCoordinator Coordinator(
        INarrationService narration,
        bool autoPlay = true,
        int admissionTimeoutSeconds = 120,
        Action<ChatVoiceOptions>? configureChat = null,
        Action<NarrationVoiceRoleOptions>? configureAssistant = null)
    {
        var options = new NarrationOptions
        {
            Enabled = true,
            AutoPlayInteractions = autoPlay,
            GroupAdmissionTimeoutSeconds = admissionTimeoutSeconds
        };
        configureChat?.Invoke(options.ChatVoice);
        configureAssistant?.Invoke(options.AssistantVoice);
        return new DualVoiceNarrationCoordinator(
            narration, Options.Create(options),
            NullLogger<DualVoiceNarrationCoordinator>.Instance);
    }

    private static Task<TextToSpeechResult?> Chat(string path) =>
        Task.FromResult<TextToSpeechResult?>(Audio(path, "chat"));

    private static Task<TextToSpeechResult?> Assistant(string path, Guid? artifactId = null) =>
        Task.FromResult<TextToSpeechResult?>(Audio(path, "assistant", artifactId));

    private static Task<TextToSpeechResult?> Failed(string errorCode) =>
        Task.FromResult<TextToSpeechResult?>(FailedAudio(errorCode));

    private static TextToSpeechResult Audio(string path, string voice, Guid? artifactId = null) =>
        new(true, "Piper", "audio/wav", path, TimeSpan.FromSeconds(1), null,
            "corr", false, voice, TimeSpan.FromSeconds(1), 22_050, 16, 1, artifactId ?? Guid.NewGuid());

    private static TextToSpeechResult FailedAudio(string errorCode) =>
        new(false, "Piper", "audio/wav", null, TimeSpan.Zero, errorCode, "corr", false, "pt_BR-jeff-medium");

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 5_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(5);
        }

        Assert.True(condition(), "condition was not met before the timeout");
    }

    private sealed class RecordingNarrationService : INarrationService
    {
        private readonly object _gate = new();
        private readonly List<Enqueued> _enqueued = [];

        /// <summary>When set, items carrying this correlation id are refused by the narrator.</summary>
        public string? RejectCorrelationId { get; set; }

        public IReadOnlyList<Enqueued> Enqueued
        {
            get { lock (_gate) return _enqueued.ToArray(); }
        }

        public Task<NarrationEnqueueResult> EnqueueAsync(
            NarrationAudioArtifact artifact,
            string correlationId,
            CancellationToken cancellationToken) =>
            EnqueueAsync(artifact, correlationId, new NarrationVoiceRoleOptions(), 0, 0, cancellationToken);

        public Task<NarrationEnqueueResult> EnqueueAsync(
            NarrationAudioArtifact artifact,
            string correlationId,
            NarrationVoiceRoleOptions role,
            int orderWithinInteraction,
            long groupSequence,
            CancellationToken cancellationToken)
        {
            var rejected = RejectCorrelationId is not null &&
                           string.Equals(RejectCorrelationId, correlationId, StringComparison.Ordinal);
            var result = new NarrationResult(
                Guid.NewGuid(), artifact.InteractionId,
                rejected ? NarrationStatus.Failed : NarrationStatus.Queued,
                rejected ? "NARRATION_QUEUE_FULL" : null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, correlationId,
                null, artifact.VoiceRole, orderWithinInteraction, groupSequence, role.VoiceId);
            lock (_gate)
            {
                if (!rejected) _enqueued.Add(new Enqueued(
                    artifact, artifact.VoiceRole, orderWithinInteraction, groupSequence,
                    role.Volume, correlationId, result.NarrationId));
            }

            return Task.FromResult(new NarrationEnqueueResult(
                !rejected, result, rejected ? "NARRATION_QUEUE_FULL" : null));
        }

        public NarrationStateSnapshot GetState() => throw new NotSupportedException();
        public IReadOnlyList<NarrationEvent> GetRecentEvents(int limit) => [];
        public IReadOnlyList<NarrationResult> GetRecentResults(int limit) => [];
        public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<NarrationStateSnapshot> SetVolumeAsync(double volume, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<NarrationPlaybackSnapshot> RefreshPlaybackStateAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed record Enqueued(
        NarrationAudioArtifact Artifact,
        NarrationVoiceRole Role,
        int Order,
        long GroupSequence,
        double? RequestedVolume,
        string CorrelationId,
        Guid NarrationId);
}
