using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Application.Features.Chat.Ingest;
using ObsLiveBot.Application.Interactions;
using ObsLiveBot.Application.LiveChat;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.UnitTests.ChatResponses;

/// <summary>
/// The loop guard, proven end to end.
///
/// Writing text into a chat means the reply comes back through the very same capture path as a viewer's
/// message. These tests cover the whole cycle rather than each part in isolation: a delivered write is
/// remembered, the echo that returns is recognised, the account behind it is learned, the decision policy
/// refuses it as its own message, and the capability's own gate refuses to answer its own account even if
/// something upstream ever let one through.
/// </summary>
public sealed class ChatResponseLoopGuardTests
{
    [Fact]
    public async Task AWrittenReplyComingBackIsMarkedAsStudioOSOwn()
    {
        var harness = new Harness();
        harness.Ledger.RecordWrite(
            LiveChatProviderType.Twitch, "channel-1", "boa noite", harness.Clock.GetUtcNow());

        var tagged = await harness.Capture("boa noite", "user-1", "echo-1");

        Assert.Equal("true", tagged.Metadata["interaction.generatedByStudioOS"]);
        Assert.Equal("true", tagged.Metadata["interaction.chatResponseEcho"]);
        Assert.Equal("text-echo", tagged.Metadata["interaction.chatResponseGuard"]);
    }

    [Fact]
    public async Task TheAccountBehindTheFirstEchoIsLearnedSoLaterMessagesNeedNoTextMatch()
    {
        var harness = new Harness();
        harness.Ledger.RecordWrite(
            LiveChatProviderType.Twitch, "channel-1", "boa noite", harness.Clock.GetUtcNow());

        await harness.Capture("boa noite", "user-1", "echo-1");

        // A viewer quoting the reply verbatim, or the platform reflowing it, changes the text. The account
        // does not change, so identity alone must be enough from here on.
        var quoted = await harness.Capture("boa noite!", "user-1", "echo-2");
        Assert.Equal("identity", quoted.Metadata["interaction.chatResponseGuard"]);
        Assert.Equal("false", quoted.Metadata["interaction.chatResponseEcho"]);

        // And a completely unrelated line from that account is still recognised as our own.
        var unrelated = await harness.Capture("obrigado pessoal", "user-1", "echo-3");
        Assert.Equal("true", unrelated.Metadata["interaction.generatedByStudioOS"]);
        Assert.Equal("identity", unrelated.Metadata["interaction.chatResponseGuard"]);
    }

    [Fact]
    public async Task AConfiguredAccountIsRecognisedByIdentityAloneWithoutAnyTextMatch()
    {
        var harness = new Harness(identities: ["Twitch:studio-bot"]);

        var tagged = await harness.Capture("uma linha qualquer", "studio-bot", "event-1");

        Assert.Equal("true", tagged.Metadata["interaction.generatedByStudioOS"]);
        Assert.Equal("identity", tagged.Metadata["interaction.chatResponseGuard"]);
        Assert.Equal("false", tagged.Metadata["interaction.chatResponseEcho"]);
        Assert.Equal(0, harness.Ledger.EchoCount);
    }

    [Fact]
    public async Task AnOrdinaryViewerMessageCarriesNoStudioOSMarker()
    {
        var harness = new Harness();
        harness.Ledger.RecordWrite(
            LiveChatProviderType.Twitch, "channel-1", "boa noite", harness.Clock.GetUtcNow());

        var tagged = await harness.Capture("outro texto", "viewer-9", "event-1");

        Assert.False(tagged.Metadata.ContainsKey("interaction.generatedByStudioOS"));
    }

    [Fact]
    public async Task TheSameTextOnAnotherChannelIsNotOurEcho()
    {
        var harness = new Harness();
        harness.Ledger.RecordWrite(
            LiveChatProviderType.Twitch, "channel-1", "boa noite", harness.Clock.GetUtcNow());

        var tagged = await harness.Capture("boa noite", "viewer-9", "event-1", channel: "channel-2");

        Assert.False(tagged.Metadata.ContainsKey("interaction.generatedByStudioOS"));
    }

    [Fact]
    public async Task TheSameTextOnAnotherPlatformIsNotOurEcho()
    {
        var harness = new Harness();
        harness.Ledger.RecordWrite(
            LiveChatProviderType.Twitch, "channel-1", "boa noite", harness.Clock.GetUtcNow());

        var tagged = await harness.Capture(
            "boa noite", "viewer-9", "event-1", provider: LiveChatProviderType.YouTube);

        Assert.False(tagged.Metadata.ContainsKey("interaction.generatedByStudioOS"));
    }

    [Fact]
    public async Task TheMarkedEchoIsDecidedAsOurOwnMessageAndNeverReachesTheModel()
    {
        var harness = new Harness();
        harness.Ledger.RecordWrite(
            LiveChatProviderType.Twitch, "channel-1", "boa noite", harness.Clock.GetUtcNow());
        var tagged = await harness.Capture("boa noite", "user-1", "echo-1");

        var decision = harness.Decisions.Decide(tagged, requestedMode: null, sequence: 1);

        Assert.Equal(InteractionDecisionType.Ignore, decision.DecisionType);
        Assert.Equal("SelfMessage", decision.Reason);
        Assert.Equal(InteractionResponseMode.None, decision.RequestedResponseMode);
    }

    [Fact]
    public async Task TheGateRefusesToAnswerOurOwnAccountEvenIfTheDecisionPolicyLetItThrough()
    {
        var harness = new Harness();
        harness.Ledger.RecordWrite(
            LiveChatProviderType.Twitch, "channel-1", Trigger, harness.Clock.GetUtcNow());
        var tagged = await harness.Capture(Trigger, "user-1", "echo-1");

        // The untagged event, as if a future change stopped the capture-side tagging. This text carries the
        // reserved trigger, so the decision policy alone would answer it - which is exactly why the
        // written-reply gate has to refuse our own account on its own authority.
        var untagged = tagged with
        {
            Metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        };

        Assert.Equal(
            InteractionDecisionType.Respond,
            harness.Decisions.Decide(untagged, requestedMode: null, sequence: 1).DecisionType);
        Assert.Equal(
            "STUDIOOS_OWN_MESSAGE",
            harness.CaptureGate.Admit(new ChatResponseCandidate(
                LiveChatProviderType.Twitch, "channel-1", "user-1", Trigger)).Reason);
    }

    private const string Trigger = "!studio alô";

    [Fact]
    public async Task AThrowingLedgerNeverStopsCaptureFromPublishingTheMessage()
    {
        var harness = new Harness(ledger: new ThrowingLedger());

        var tagged = await harness.Capture("boa noite", "viewer-9", "event-1");

        // A loop guard that throws would take capture down, which is worse than the loop it prevents.
        Assert.Equal("boa noite", tagged.Message);
        Assert.False(tagged.Metadata.ContainsKey("interaction.generatedByStudioOS"));
    }

    [Fact]
    public async Task AThrowingIdentityRegistryNeverStopsCaptureFromPublishingTheMessage()
    {
        var harness = new Harness(selfIdentities: new ThrowingSelfIdentities());

        var tagged = await harness.Capture("boa noite", "viewer-9", "event-1");

        Assert.Equal("boa noite", tagged.Message);
        Assert.False(tagged.Metadata.ContainsKey("interaction.generatedByStudioOS"));
    }

    [Fact]
    public async Task AMarkerWithoutTheLedgerInstalledIsNotInvented()
    {
        // Capture must behave identically when written replies are not composed in at all, so the loop
        // guard can never be the reason a message fails to appear.
        var harness = new Harness();
        var untagged = await harness.CaptureWithoutGuard("boa noite", "viewer-9", "event-1");

        Assert.False(untagged.Metadata.ContainsKey("interaction.generatedByStudioOS"));
        Assert.Equal("boa noite", untagged.Message);
    }

    private sealed class Harness
    {
        private readonly IChatResponseSelfIdentityRegistry _identities;

        public Harness(
            IChatResponseLedger? ledger = null,
            IChatResponseSelfIdentityRegistry? selfIdentities = null,
            IReadOnlyList<string>? identities = null)
        {
            Clock = new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
            Ledger = ledger ?? ChatResponseTestFactory.Ledger();
            _identities = selfIdentities ?? ChatResponseTestFactory.SelfIdentities(
                options => options.SelfActorIdentities = identities?.ToArray() ?? []);

            Pipeline = new LiveChatIngestionPipeline(
                new ProviderLiveChatEventValidator(Options.Create(new LiveChatOptions())),
                new LiveChatEventNormalizer(Clock),
                new LiveChatDeduplicator(Options.Create(new LiveChatOptions())),
                new NullMediator(),
                Ledger,
                _identities,
                Clock);

            // The same registry the capture path learned an identity into, so the gate is shown refusing
            // what capture really learned rather than a freshly built, empty copy of it.
            CaptureGate = new ChatResponseGatekeeper(
                Options.Create(ChatResponseTestFactory.Options()),
                new StubChatResponseSettingsStore(true),
                new StubChatResponseSenderRegistry([new StubChatResponseSender { Name = "Stub" }]),
                Ledger,
                _identities,
                ChatResponseTestFactory.Cooldowns(),
                Clock,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatResponseGatekeeper>.Instance);
        }

        public FixedTimeProvider Clock { get; }

        public IChatResponseLedger Ledger { get; }

        public LiveChatIngestionPipeline Pipeline { get; }

        public ChatResponseGatekeeper CaptureGate { get; }

        public InteractionDecisionPolicy Decisions { get; } = new(
            Options.Create(new InteractionOptions { Enabled = true, AutoPlayInteractions = true }),
            TimeProvider.System);

        public Task<LiveChatEvent> Capture(
            string message,
            string userId,
            string providerEventId,
            LiveChatProviderType provider = LiveChatProviderType.Twitch,
            string channel = "channel-1") =>
            Ingest(Pipeline, new ProviderLiveChatEvent(
                provider,
                LiveChatEventType.Message,
                providerEventId,
                channel,
                "Channel",
                new LiveChatUser(provider, userId, userId, userId, false, false, false, false, false, []),
                message,
                Clock.GetUtcNow(),
                null,
                new Dictionary<string, string?>()));

        /// <summary>Capture with the loop guard not composed in at all, as before written replies existed.</summary>
        public Task<LiveChatEvent> CaptureWithoutGuard(
            string message,
            string userId,
            string providerEventId)
        {
            var options = Options.Create(new LiveChatOptions());
            return Ingest(new LiveChatIngestionPipeline(
                new ProviderLiveChatEventValidator(options),
                new LiveChatEventNormalizer(Clock),
                new LiveChatDeduplicator(options),
                new NullMediator()),
                new ProviderLiveChatEvent(
                    LiveChatProviderType.Twitch,
                    LiveChatEventType.Message,
                    providerEventId,
                    "channel-1",
                    "Channel",
                    new LiveChatUser(
                        LiveChatProviderType.Twitch, userId, userId, userId,
                        false, false, false, false, false, []),
                    message,
                    Clock.GetUtcNow(),
                    null,
                    new Dictionary<string, string?>()));
        }

        private static async Task<LiveChatEvent> Ingest(
            LiveChatIngestionPipeline pipeline,
            ProviderLiveChatEvent chatEvent)
        {
            var result = await pipeline.IngestAsync(chatEvent, CancellationToken.None);

            Assert.True(result.Accepted);
            return result.Event!;
        }
    }

    private sealed class NullMediator : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;
    }

    private sealed class ThrowingLedger : IChatResponseLedger
    {
        public int IdempotencyCount => 0;

        public int IdempotencyCapacity => 0;

        public bool TryReserve(string idempotencyKey, DateTimeOffset now) => throw new IOException("ledger down");

        public void Forget(string idempotencyKey) => throw new IOException("ledger down");

        public bool WasWritten(
            LiveChatProviderType provider,
            string channelId,
            string text,
            DateTimeOffset now) => throw new IOException("ledger down");

        public void RecordWrite(
            LiveChatProviderType provider,
            string channelId,
            string text,
            DateTimeOffset now) => throw new IOException("ledger down");

        public int EchoCount => 0;

        public int EchoCapacity => 0;
    }

    private sealed class ThrowingSelfIdentities : IChatResponseSelfIdentityRegistry
    {
        public int Count => 0;

        public int Capacity => 0;

        public bool IsStudioOsActor(LiveChatProviderType provider, string? userId, string? username = null) =>
            throw new IOException("registry down");

        public bool Learn(LiveChatProviderType provider, string? userId, string? username) =>
            throw new IOException("registry down");

        public IReadOnlyList<string> GetIdentities() => throw new IOException("registry down");
    }
}
