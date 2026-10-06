using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.UnitTests.ChatResponses;

public sealed class ChatResponseWriterTests
{
    [Fact]
    public async Task EnqueueThenDrainDeliversThroughTheSelectedSender()
    {
        await using var harness = await WriterHarness.StartedAsync();

        var accepted = await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        var state = await harness.WaitForAsync(counter => counter.Sent == 1);

        Assert.True(accepted.Accepted);
        Assert.Equal(ChatResponseStatus.Queued, accepted.Status);
        Assert.Equal(1, harness.Sender.RequestCount);
        Assert.Equal(1, state.Queued);
    }

    [Fact]
    public async Task SuccessfulWriteRecordsTheTextSoItsEchoCanBeRecognized()
    {
        await using var harness = await WriterHarness.StartedAsync();

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Sent == 1);

        Assert.True(harness.Ledger.WasWritten(
            LiveChatProviderType.Twitch, "channel", "boa noite", harness.Time.GetUtcNow()));
    }

    [Fact]
    public async Task SimulatedSendIsMarkedAsSimulatedInTheResult()
    {
        await using var harness = await WriterHarness.StartedAsync();

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Sent == 1);

        Assert.True(harness.Writer.GetRecent(5).Single().Simulated);
    }

    [Fact]
    public async Task FailedWriteIsRecordedAndDoesNotRecordTheEchoText()
    {
        // Recording the text of a message the platform never accepted would suppress the echo of a
        // reply that never existed, and could hide a real viewer message carrying the same words.
        await using var harness = await WriterHarness.StartedAsync(senderOptions: sender => sender.Success = false);

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        var state = await harness.WaitForAsync(counter => counter.Failed == 1);

        Assert.Equal(0, state.Sent);
        Assert.False(harness.Ledger.WasWritten(
            LiveChatProviderType.Twitch, "channel", "boa noite", harness.Time.GetUtcNow()));
        Assert.Equal("CHAT_SEND_FAILED", harness.Writer.GetRecent(5).Single().Reason);
    }

    [Fact]
    public async Task SenderExceptionIsIsolatedIntoARecordedFailure()
    {
        await using var harness = await WriterHarness.StartedAsync(senderOptions: sender => sender.Behaviour =
            (_, _) => throw new InvalidOperationException("transport exploded"));

        var accepted = await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Failed == 1);

        Assert.True(accepted.Accepted);
        var result = harness.Writer.GetRecent(5).Single();
        Assert.Equal(ChatResponseStatus.Failed, result.Status);
        Assert.Equal("CHAT_SEND_EXCEPTION", result.Reason);
    }

    [Fact]
    public async Task SendThatOutlivesItsCeilingIsFailedRatherThanLeftHanging()
    {
        await using var harness = await WriterHarness.StartedAsync(
            options: options => options.CommandTimeoutSeconds = 1,
            senderOptions: sender => sender.Behaviour = async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromMinutes(5), token);
                return new ChatResponseSendResult(true, null, TimeSpan.Zero, false);
            });

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Failed == 1);

        var result = harness.Writer.GetRecent(5).Single();
        Assert.Equal(ChatResponseStatus.Failed, result.Status);
        Assert.Equal("CHAT_WRITE_TIMEOUT", result.Reason);
    }

    [Fact]
    public async Task MissingSenderIsRecordedInsteadOfCrashingTheLoop()
    {
        await using var harness = await WriterHarness.StartedAsync();
        harness.Registry.SelectedName = "gone";

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Failed == 1);

        Assert.Equal("CHAT_SENDER_NOT_SELECTED", harness.Writer.GetRecent(5).Single().Reason);
    }

    [Fact]
    public async Task FullQueueRefusesTheNewestReplyAndKeepsTheAcceptedOnes()
    {
        // The reader is parked inside the sender, so the first reply has left the queue and the second
        // fills it. Only the third has nowhere to go, which is the property being asserted: already
        // accepted replies are never dropped to make room for a newer one.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await WriterHarness.StartedAsync(
            options: options => options.MaxQueueSize = 1,
            senderOptions: sender => sender.Behaviour = async (_, token) =>
            {
                await gate.Task.WaitAsync(token);
                return new ChatResponseSendResult(true, null, TimeSpan.Zero, false);
            });

        var first = await harness.Writer.EnqueueAsync(harness.Intent(text: "one"), CancellationToken.None);
        await harness.SenderEntered.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await harness.Writer.EnqueueAsync(harness.Intent(text: "two"), CancellationToken.None);
        var third = await harness.Writer.EnqueueAsync(harness.Intent(text: "three"), CancellationToken.None);

        gate.SetResult();
        await harness.WaitForAsync(counter => counter.Sent == 2);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.False(third.Accepted);
        Assert.Equal("CHAT_QUEUE_FULL", third.Reason);
        Assert.Equal(ChatResponseStatus.Skipped, third.Status);
        Assert.Equal(1, harness.Writer.GetState().Rejected);
        Assert.Equal(2, harness.Sender.RequestCount);
        Assert.DoesNotContain(harness.Writer.GetRecent(10), result => result.CharacterCount == "three".Length);
    }

    [Fact]
    public async Task RefusedReplyReleasesItsIdempotencyKeySoAnExplicitRetryStillWorks()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await WriterHarness.StartedAsync(
            options: options => options.MaxQueueSize = 1,
            senderOptions: sender => sender.Behaviour = async (_, token) =>
            {
                await gate.Task.WaitAsync(token);
                return new ChatResponseSendResult(true, null, TimeSpan.Zero, false);
            });

        var intent = harness.Intent(text: "one");
        await harness.Writer.EnqueueAsync(intent, CancellationToken.None);
        await harness.SenderEntered.WaitAsync(TimeSpan.FromSeconds(10));
        await harness.Writer.EnqueueAsync(harness.Intent(text: "two"), CancellationToken.None);
        var refused = await harness.Writer.EnqueueAsync(harness.Intent(text: "three"), CancellationToken.None);
        gate.SetResult();
        await harness.WaitForAsync(counter => counter.Sent == 2);

        Assert.False(refused.Accepted);
        Assert.True(harness.Ledger.TryReserve(intent.IdempotencyKey, harness.Time.GetUtcNow()));
    }

    [Fact]
    public async Task HistoryIsBoundedAndOrderedNewestFirst()
    {
        await using var harness = await WriterHarness.StartedAsync(options: options => options.HistoryCapacity = 2);

        for (var index = 0; index < 4; index++)
        {
            await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
            await harness.WaitForAsync(counter => counter.Sent == index + 1);
        }

        var state = harness.Writer.GetState();
        var recent = harness.Writer.GetRecent(50);
        Assert.Equal(2, state.HistorySize);
        Assert.Equal(2, recent.Count);
        Assert.True(recent[0].Sequence > recent[1].Sequence);
    }

    [Fact]
    public async Task GetRecentIsClampedToAtLeastOneResultAndAtMostTheHistoryLimit()
    {
        await using var harness = await WriterHarness.StartedAsync();

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Sent == 1);

        Assert.Single(harness.Writer.GetRecent(0));
        Assert.Single(harness.Writer.GetRecent(-10));
        Assert.Single(harness.Writer.GetRecent(100_000));
    }

    [Fact]
    public async Task QueuedAndCompletedNotificationsAreBothEmitted()
    {
        var notifications = new CollectingPublisher();
        await using var harness = await WriterHarness.StartedAsync(publisher: notifications);

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Sent == 1);

        Assert.Contains(notifications.Notifications, item => item is ChatResponseQueuedNotification);
        Assert.Contains(notifications.Notifications,
            item => item is ChatResponseCompletedNotification { Status: ChatResponseStatus.Sent });
    }

    [Fact]
    public async Task FailingNotificationDoesNotBreakTheWrite()
    {
        await using var harness = await WriterHarness.StartedAsync(publisher: new ThrowingPublisher());

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Sent == 1);

        Assert.Equal(1, harness.Writer.GetState().Sent);
    }

    [Fact]
    public async Task FailingEventPublisherDoesNotBreakTheWrite()
    {
        await using var harness = await WriterHarness.StartedAsync(events: new ThrowingEventPublisher());

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Sent == 1);

        Assert.Equal(1, harness.Writer.GetState().Sent);
    }

    [Fact]
    public async Task TerminalStateIsPublishedAsADomainEvent()
    {
        var events = new StubChatResponseEventPublisher();
        await using var harness = await WriterHarness.StartedAsync(events: events);

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(counter => counter.Sent == 1);

        var published = Assert.Single(events.Events);
        Assert.Equal("Sent", published.EventType);
        Assert.Equal(LiveChatProviderType.Twitch, published.Provider);
        Assert.Equal("channel", published.ChannelId);
    }

    [Fact]
    public async Task DirectExecutionReportsTheOutcomeInline()
    {
        await using var harness = await WriterHarness.StartedAsync();

        var outcome = await harness.Writer.ExecuteDirectAsync(harness.Intent(), CancellationToken.None);

        Assert.True(outcome.Accepted);
        Assert.Equal(ChatResponseStatus.Sent, outcome.Status);
        Assert.NotNull(outcome.Result);
        Assert.Equal(1, harness.Sender.RequestCount);
    }

    [Fact]
    public async Task DirectExecutionRecordsAFailureWithoutThrowing()
    {
        await using var harness = await WriterHarness.StartedAsync(senderOptions: sender => sender.Success = false);

        var outcome = await harness.Writer.ExecuteDirectAsync(harness.Intent(), CancellationToken.None);

        Assert.False(outcome.Accepted);
        Assert.Equal(ChatResponseStatus.Failed, outcome.Status);
    }

    [Fact]
    public async Task DirectExecutionRejectsANullIntent()
    {
        await using var harness = await WriterHarness.StartedAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Writer.ExecuteDirectAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task HostShutdownDuringAWriteIsCancelledAndReleasesTheIdempotencyKey()
    {
        await using var harness = await WriterHarness.StartedAsync(senderOptions: sender => sender.Behaviour =
            (_, token) => Task.FromCanceled<ChatResponseSendResult>(token));
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();

        var outcome = await harness.Writer.ExecuteDirectAsync(harness.Intent(), shutdown.Token);

        Assert.Equal(ChatResponseStatus.Cancelled, outcome.Status);
        Assert.Equal("CHAT_WRITE_CANCELLED", outcome.Reason);
        // Released so a reply is not lost forever just because the process was stopping.
        Assert.Equal(0, harness.Ledger.IdempotencyCount);
    }

    [Fact]
    public void StateReportsDisabledAndTheSelectedSenderWhenNotEnabled()
    {
        var harness = WriterHarness.Create(options: options => options.Enabled = false);

        var state = harness.Writer.GetState();

        Assert.False(state.Enabled);
        Assert.Equal("Disabled", state.Status);
        Assert.Equal("Stub", state.SelectedSender);
    }

    [Fact]
    public void StateReportsNoSenderWhenTheSelectionIsUnknown()
    {
        var harness = WriterHarness.Create();
        harness.Registry.SelectedName = "gone";

        Assert.Equal("NoSenderSelected", harness.Writer.GetState().Status);
    }

    [Fact]
    public void StateReportsAnUnavailableSender()
    {
        var harness = WriterHarness.Create();
        harness.Sender.IsAvailable = false;

        var state = harness.Writer.GetState();

        Assert.Equal("SenderUnavailable", state.Status);
        Assert.False(state.SenderAvailable);
    }

    [Fact]
    public void StateReportsReadyWhenEnabledWithAnAvailableSender()
    {
        Assert.Equal("Ready", WriterHarness.Create().Writer.GetState().Status);
    }

    [Fact]
    public void StateExposesTheBoundedCapacitiesThatMakeTheSubsystemBounded()
    {
        var harness = WriterHarness.Create(options =>
        {
            options.HistoryCapacity = 7;
            options.CooldownCapacity = 11;
            options.IdempotencyCapacity = 13;
            options.EchoCapacity = 17;
            options.MaxQueueSize = 3;
        });

        var state = harness.Writer.GetState();

        Assert.Equal(7, state.HistoryCapacity);
        Assert.Equal(11, state.CooldownCapacity);
        Assert.Equal(13, state.IdempotencyCapacity);
        Assert.Equal(17, state.EchoCapacity);
        Assert.Equal(3, state.QueueCapacity);
    }

    [Fact]
    public async Task HostShutdownCancelsQueuedRepliesAndReleasesTheirIdempotencyKeys()
    {
        // An accepted reply must never vanish silently at shutdown. The reader may still pull a buffered
        // reply after cancellation, but that send can only fail fast, so every accepted reply ends up
        // recorded as cancelled with its key released - never lost, never marked sent.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await WriterHarness.StartedAsync(senderOptions: sender => sender.Behaviour =
            async (_, token) =>
            {
                await gate.Task.WaitAsync(token);
                return new ChatResponseSendResult(true, null, TimeSpan.Zero, false);
            });

        var inFlight = harness.Intent(text: "in flight");
        await harness.Writer.EnqueueAsync(inFlight, CancellationToken.None);
        await harness.SenderEntered.WaitAsync(TimeSpan.FromSeconds(10));
        var queued = harness.Intent(text: "still queued");
        await harness.Writer.EnqueueAsync(queued, CancellationToken.None);

        // Shutdown is observed asynchronously, so the assertion waits for both replies to be recorded
        // rather than assuming StopAsync has already done it.
        await harness.DisposeAsync();
        await harness.WaitUntilAsync(
            () => harness.Writer.GetRecent(10).Count(result => result.Status == ChatResponseStatus.Cancelled) == 2);

        var recorded = harness.Writer.GetRecent(10);
        Assert.All(recorded, result => Assert.Equal("CHAT_WRITE_CANCELLED", result.Reason));
        Assert.DoesNotContain(recorded, result => result.Status == ChatResponseStatus.Sent);
        // Released so neither reply is treated as a duplicate after the restart.
        Assert.True(harness.Ledger.TryReserve(inFlight.IdempotencyKey, harness.Time.GetUtcNow()));
        Assert.True(harness.Ledger.TryReserve(queued.IdempotencyKey, harness.Time.GetUtcNow()));
        Assert.False(harness.Ledger.WasWritten(
            LiveChatProviderType.Twitch, "channel", queued.Text, harness.Time.GetUtcNow()));
    }

    [Fact]
    public async Task ReplyArrivingAfterShutdownIsReportedAsStoppedRatherThanAsAFullQueue()
    {
        await using var harness = WriterHarness.Create();
        await harness.Writer.StartAsync(CancellationToken.None);
        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.WaitForAsync(state => state.Sent == 1);
        await harness.DisposeAsync();

        var outcome = await harness.WaitForEnqueueAsync(
            result => result.Reason == "CHAT_WRITER_STOPPED");

        Assert.False(outcome.Accepted);
        Assert.Equal(ChatResponseStatus.Skipped, outcome.Status);
    }

    [Fact]
    public async Task ReplyArrivingWhileTheQueueIsFullIsReportedAsFull()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await WriterHarness.StartedAsync(
            options: options => options.MaxQueueSize = 1,
            senderOptions: sender => sender.Behaviour = async (_, token) =>
            {
                await gate.Task.WaitAsync(token);
                return new ChatResponseSendResult(true, null, TimeSpan.Zero, false);
            });

        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        await harness.SenderEntered.WaitAsync(TimeSpan.FromSeconds(10));
        await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        var refused = await harness.Writer.EnqueueAsync(harness.Intent(), CancellationToken.None);
        gate.SetResult();

        Assert.Equal("CHAT_QUEUE_FULL", refused.Reason);
    }

    private sealed class ThrowingPublisher : MediatR.IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("notification failed");

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : MediatR.INotification =>
            throw new InvalidOperationException("notification failed");
    }

    private sealed class ThrowingEventPublisher : IChatResponseEventPublisher
    {
        public Task PublishAsync(ChatResponseEvent chatResponseEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("event publication failed");
    }
}

/// <summary>
/// Builds a real <see cref="ChatResponseWriter"/> with a real bounded queue and, optionally, runs it as a
/// hosted service so the drain behaviour under test is the one production uses.
/// </summary>
internal sealed class WriterHarness : IAsyncDisposable
{
    private WriterHarness(
        ChatResponseWriter writer,
        StubChatResponseSender sender,
        StubChatResponseSenderRegistry registry,
        ChatResponseLedger ledger,
        FixedTimeProvider time,
        MediatR.IPublisher publisher,
        Task senderEntered)
    {
        Writer = writer;
        Sender = sender;
        Registry = registry;
        Ledger = ledger;
        Time = time;
        Publisher = publisher;
        SenderEntered = senderEntered;
    }

    public ChatResponseWriter Writer { get; }

    public StubChatResponseSender Sender { get; }

    public StubChatResponseSenderRegistry Registry { get; }

    public ChatResponseLedger Ledger { get; }

    public FixedTimeProvider Time { get; }

    public MediatR.IPublisher Publisher { get; }

    /// <summary>Completes the first time a reply reaches the sender, so queue tests need no sleep.</summary>
    public Task SenderEntered { get; }

    public ChatResponseIntent Intent(string text = "boa noite") =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"message-{Guid.NewGuid():N}",
            LiveChatProviderType.Twitch,
            "channel",
            "user",
            "viewer",
            text,
            $"key-{Guid.NewGuid():N}",
            "correlation",
            Time.GetUtcNow());

    /// <summary>
    /// Waits for the writer to reach a state.
    ///
    /// The tests assert on recorded counters rather than on a "queue drained" signal, because a reply can
    /// be read from the queue and still be inside the sender. The recorded counters are also the
    /// production-visible truth, so the wait and the assertion rest on one fact.
    /// </summary>
    public async Task<ChatResponseStateSnapshot> WaitForAsync(
        Func<ChatResponseStateSnapshot, bool> predicate,
        int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var state = Writer.GetState();
            if (predicate(state)) return state;
            await Task.Delay(10);
        }

        throw new TimeoutException(
            $"Writer did not reach the expected state. Last state: {Writer.GetState()}");
    }

    /// <summary>
    /// Waits for an arbitrary condition. Used for effects that outlive the call that causes them, such as
    /// a reader loop noticing that its host is shutting down.
    /// </summary>
    public async Task WaitUntilAsync(Func<bool> condition, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was never met. Last state: " + Writer.GetState());
    }

    /// <summary>
    /// Offers a reply until the writer reports the wanted reason. Refusals are idempotent, so repeated
    /// offers are harmless, which is what makes this safe for an asynchronous shutdown.
    /// </summary>
    public async Task<ChatResponseEnqueueResult> WaitForEnqueueAsync(
        Func<ChatResponseEnqueueResult, bool> predicate,
        int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        ChatResponseEnqueueResult last;
        do
        {
            last = await Writer.EnqueueAsync(Intent(), CancellationToken.None);
            if (predicate(last)) return last;
            await Task.Delay(10);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Writer never reported the expected enqueue reason. Last: {last.Reason}");
    }

    public async ValueTask DisposeAsync() => await Writer.StopAsync(CancellationToken.None);

    public static WriterHarness Create(
        Action<ChatResponseOptions>? options = null,
        Action<StubChatResponseSender>? senderOptions = null,
        IChatResponseEventPublisher? events = null,
        MediatR.IPublisher? publisher = null)
    {
        var settings = ChatResponseTestFactory.Options(options);
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var ledger = new ChatResponseLedger(Options.Create(settings));
        var cooldowns = new ChatResponseCooldownTracker(Options.Create(settings));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new StubChatResponseSender { Name = "Stub", Entered = entered };
        senderOptions?.Invoke(sender);
        var registry = new StubChatResponseSenderRegistry([sender]);
        MediatR.IPublisher notifications = publisher ?? new CollectingPublisher();
        var writer = new ChatResponseWriter(
            Options.Create(settings),
            new StubChatResponseSettingsStore(settings.Enabled),
            registry,
            ledger,
            cooldowns,
            events ?? new StubChatResponseEventPublisher(),
            notifications,
            time,
            NullLogger<ChatResponseWriter>.Instance);
        return new WriterHarness(
            writer, sender, registry, ledger, time, notifications, entered.Task);
    }

    /// <summary>
    /// Builds the writer and starts its reader loop. The returned harness must be awaited-disposed, which
    /// is what stops the loop - stopping it here would drain nothing.
    /// </summary>
    public static async Task<WriterHarness> StartedAsync(
        Action<ChatResponseOptions>? options = null,
        Action<StubChatResponseSender>? senderOptions = null,
        IChatResponseEventPublisher? events = null,
        MediatR.IPublisher? publisher = null)
    {
        var harness = Create(options, senderOptions, events, publisher);
        await harness.Writer.StartAsync(CancellationToken.None);
        return harness;
    }
}
