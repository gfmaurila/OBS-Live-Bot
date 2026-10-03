using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Narration;
using ObsLiveBot.Domain.Interactions;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.UnitTests.Interactions;

/// <summary>
/// Concurrency and resource-accounting behavior of the admission coordinator.
///
/// These cover the properties that cannot be shown by a single ordered submission: that the cap on
/// admission slots holds under simultaneous submissions, that every admitted group gives its slot
/// back exactly once, and that no failure mode on the way in or out leaves a slot behind to stall the
/// interactions accepted after it.
/// </summary>
public sealed class DualVoiceNarrationConcurrencyTests
{
    /// <summary>
    /// The cap is enforced on slots, not on a buffer, so a burst far larger than the cap must admit
    /// exactly the cap. The rest are refused instead of queued, which is what keeps the reorder window
    /// from becoming an unbounded backlog of accepted messages.
    /// </summary>
    [Fact]
    public async Task SimultaneousSubmissions_AdmitExactlyTheCap()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration, admissionTimeoutSeconds: 300);
        const int attempts = DualVoiceNarrationCoordinator.MaxPendingGroups * 5;

        var gates = await SubmitBlockedBatchAsync(coordinator, attempts, round: 0);

        // Every group holds its reply back until the whole burst has been offered, so nothing can
        // finish early and free a slot behind the cap's back.
        foreach (var gate in gates) gate.TrySetResult(Audio("released.wav", "assistant"));

        // Only the admitted groups ever reach the queue, and each of them contributes both of its
        // roles, so the admitted count is observable as half the enqueued items.
        await WaitForAsync(() =>
            narration.Enqueued.Count == DualVoiceNarrationCoordinator.MaxPendingGroups * 2);

        Assert.Equal(DualVoiceNarrationCoordinator.MaxPendingGroups,
            narration.Enqueued.Select(item => item.CorrelationId).Distinct().Count());
    }

    /// <summary>
    /// A slot is taken on admission and given back when the group is done. If any path leaked a slot or
    /// released one twice, the capacity available to the next batch would drift and this fails.
    /// </summary>
    [Fact]
    public async Task EveryAdmittedGroupGivesItsSlotBack_SoFullCapacityReturns()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration, admissionTimeoutSeconds: 300);
        const int perRound = DualVoiceNarrationCoordinator.MaxPendingGroups;

        for (var round = 0; round < 3; round++)
        {
            var expected = (round + 1) * perRound * 2;
            var gates = await SubmitBlockedBatchAsync(coordinator, perRound + 4, round);
            foreach (var gate in gates) gate.TrySetResult(Audio("released.wav", "assistant"));

            await WaitForAsync(() => narration.Enqueued.Count == expected);
            // The last enqueue happens inside the group, so the slot is released just after the count
            // is reached. The short settle keeps the next round from racing that release.
            await Task.Delay(150);
        }

        // Every round admitted the full cap, so no slot leaked and none was released twice.
        Assert.Equal(3 * perRound, narration.Enqueued.Select(item => item.CorrelationId).Distinct().Count());
    }

    /// <summary>
    /// Two groups may never interleave. Each group's chat clip is enqueued and awaited before its
    /// assistant clip, and the drainer finishes a group before starting the next one, so the queue is
    /// always a run of contiguous pairs.
    /// </summary>
    [Fact]
    public async Task SimultaneousSubmissions_NeverInterleaveTwoGroups()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        const int groups = 6;

        var next = 0;
        await Task.WhenAll(Enumerable.Range(0, groups).Select(_ => Task.Run(async () =>
        {
            // Each caller takes its acceptance key and submits it, which is how the interaction
            // pipeline behaves: the key and the submission are the same fact.
            var index = Interlocked.Increment(ref next) - 1;
            await coordinator.SubmitAsync(
                new DualVoiceNarrationRequest(
                    Guid.NewGuid(), index + 1, $"G{index}",
                    Chat($"g{index}-chat.wav"), Assistant($"g{index}-assistant.wav"), TimeSpan.Zero),
                CancellationToken.None).ConfigureAwait(false);
        })));

        await WaitForAsync(() => narration.Enqueued.Count == groups * 2);
        var items = narration.Enqueued;

        for (var index = 0; index < items.Count; index += 2)
        {
            Assert.Equal(NarrationVoiceRole.Chat, items[index].Role);
            Assert.Equal(NarrationVoiceRole.Assistant, items[index + 1].Role);
            Assert.Equal(items[index].CorrelationId, items[index + 1].CorrelationId);
        }

        // The first group to arrive is admitted immediately, and the rest follow in ascending
        // acceptance order. A group that had not been submitted yet cannot be waited for.
        var admitted = items.Select(item => item.GroupSequence).Distinct().ToArray();
        Assert.Equal(groups, admitted.Length);
        Assert.True(admitted.Skip(1).SequenceEqual(admitted.Skip(1).Order()),
            $"expected ascending acceptance order, got {string.Join(", ", admitted)}");
    }

    /// <summary>
    /// A narrator that throws rather than refusing must cost the group its slot and nothing else.
    /// </summary>
    [Fact]
    public async Task NarratorThrowing_ReleasesTheSlotAndDoesNotStallTheNextGroup()
    {
        var narration = new RecordingNarrationService { ThrowCorrelationId = "A" };
        var coordinator = Coordinator(narration);

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "A", Chat("a-chat.wav"), Assistant("a-assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);
        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 2, "B", Chat("b-chat.wav"), Assistant("b-assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);

        await WaitForAsync(() => narration.Enqueued.Count == 2);

        // A lost its whole group and gave the slot back; B was not held behind it.
        Assert.Equal(new[] { "B", "B" }, narration.Enqueued.Select(item => item.CorrelationId).ToArray());
    }

    /// <summary>
    /// A canceled reply is a missing reply, not a broken group: the slot still closes.
    /// </summary>
    [Fact]
    public async Task ACanceledReplyPromise_ReleasesTheSlotWithoutQueuingThatRole()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        var canceled = new TaskCompletionSource<TextToSpeechResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        canceled.TrySetCanceled();

        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 1, "A", Chat("a-chat.wav"), canceled.Task, TimeSpan.Zero),
            CancellationToken.None);
        await coordinator.SubmitAsync(
            new DualVoiceNarrationRequest(
                Guid.NewGuid(), 2, "B", Chat("b-chat.wav"), Assistant("b-assistant.wav"), TimeSpan.Zero),
            CancellationToken.None);

        await WaitForAsync(() => narration.Enqueued.Count == 3);

        Assert.Equal(new[] { "A", "B", "B" }, narration.Enqueued.Select(item => item.CorrelationId).ToArray());
    }

    /// <summary>
    /// A burst where some replies fail must still account for every group exactly once: the chat clip
    /// of every admitted interaction is spoken, and the assistant clip only where synthesis worked.
    /// </summary>
    [Fact]
    public async Task SimultaneousSubmissions_WithFailedReplies_AccountForEveryGroupOnce()
    {
        var narration = new RecordingNarrationService();
        var coordinator = Coordinator(narration);
        // Exactly the cap, so every group is admitted regardless of the order they arrive in.
        const int groups = DualVoiceNarrationCoordinator.MaxPendingGroups;

        var next = 0;
        await Task.WhenAll(Enumerable.Range(0, groups).Select(_ => Task.Run(async () =>
        {
            var index = Interlocked.Increment(ref next) - 1;
            var assistant = index % 4 == 0
                ? Failed("TTS_ENGINE_FAILED")
                : Assistant($"g{index}-assistant.wav");
            await coordinator.SubmitAsync(
                new DualVoiceNarrationRequest(
                    Guid.NewGuid(), index + 1, $"G{index}",
                    Chat($"g{index}-chat.wav"), assistant, TimeSpan.Zero),
                CancellationToken.None).ConfigureAwait(false);
        })));

        await WaitForAsync(() => narration.Enqueued.Count == groups + groups - groups / 4);

        Assert.Equal(groups, narration.Enqueued.Count(item => item.Role == NarrationVoiceRole.Chat));
        Assert.Equal(groups - groups / 4, narration.Enqueued.Count(item => item.Role == NarrationVoiceRole.Assistant));
        Assert.Equal(groups, narration.Enqueued.Select(item => item.CorrelationId).Distinct().Count());
    }

    private static async Task<TaskCompletionSource<TextToSpeechResult?>[]> SubmitBlockedBatchAsync(
        IDualVoiceNarrationCoordinator coordinator,
        int count,
        int round)
    {
        var gates = new TaskCompletionSource<TextToSpeechResult?>[count];
        var next = 0;
        await Task.WhenAll(Enumerable.Range(0, count).Select(_ => Task.Run(async () =>
        {
            var index = Interlocked.Increment(ref next) - 1;
            var gate = new TaskCompletionSource<TextToSpeechResult?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            gates[index] = gate;
            await coordinator.SubmitAsync(
                new DualVoiceNarrationRequest(
                    Guid.NewGuid(), index + 1, $"r{round}g{index}",
                    Chat($"r{round}g{index}-chat.wav"), gate.Task, TimeSpan.Zero),
                CancellationToken.None).ConfigureAwait(false);
        })));

        return gates;
    }

    private static DualVoiceNarrationCoordinator Coordinator(
        INarrationService narration,
        int admissionTimeoutSeconds = 120) =>
        new(
            narration,
            Options.Create(new NarrationOptions
            {
                Enabled = true,
                AutoPlayInteractions = true,
                GroupAdmissionTimeoutSeconds = admissionTimeoutSeconds
            }),
            NullLogger<DualVoiceNarrationCoordinator>.Instance);

    private static Task<TextToSpeechResult?> Chat(string path) =>
        Task.FromResult<TextToSpeechResult?>(Audio(path, "chat"));

    private static Task<TextToSpeechResult?> Assistant(string path) =>
        Task.FromResult<TextToSpeechResult?>(Audio(path, "assistant"));

    private static Task<TextToSpeechResult?> Failed(string errorCode) =>
        Task.FromResult<TextToSpeechResult?>(
            new TextToSpeechResult(false, "Piper", "audio/wav", null, TimeSpan.Zero, errorCode,
                "corr", false, "pt_BR-jeff-medium"));

    private static TextToSpeechResult Audio(string path, string voice) =>
        new(true, "Piper", "audio/wav", path, TimeSpan.FromSeconds(1), null,
            "corr", false, voice, TimeSpan.FromSeconds(1), 22_050, 16, 1, Guid.NewGuid());

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 10_000)
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

        /// <summary>When set, an item carrying this correlation id makes the narrator fail hard.</summary>
        public string? ThrowCorrelationId { get; init; }

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
            if (string.Equals(ThrowCorrelationId, correlationId, StringComparison.Ordinal))
                throw new InvalidOperationException("narrator unavailable");

            lock (_gate)
            {
                _enqueued.Add(new Enqueued(artifact, artifact.VoiceRole, groupSequence, correlationId));
            }

            var result = new NarrationResult(
                Guid.NewGuid(), artifact.InteractionId, NarrationStatus.Queued, null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, correlationId,
                null, artifact.VoiceRole, orderWithinInteraction, groupSequence, role.VoiceId);
            return Task.FromResult(new NarrationEnqueueResult(true, result, null));
        }

        public NarrationStateSnapshot GetState() => throw new NotSupportedException();
        public IReadOnlyList<NarrationEvent> GetRecentEvents(int limit) => [];
        public IReadOnlyList<NarrationResult> GetRecentResults(int limit) => [];
        public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<NarrationStateSnapshot> SetVolumeAsync(double volume, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<NarrationPlaybackSnapshot> RefreshPlaybackStateAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record Enqueued(
        NarrationAudioArtifact Artifact,
        NarrationVoiceRole Role,
        long GroupSequence,
        string CorrelationId);
}