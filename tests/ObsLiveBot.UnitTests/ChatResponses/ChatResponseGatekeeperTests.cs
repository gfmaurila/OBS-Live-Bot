using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.UnitTests.ChatResponses;

public sealed class ChatResponseGatekeeperTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DisabledCapabilityRefusesEverything()
    {
        var fixture = Harness(options => options.Enabled = false);

        var admission = fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate());

        Assert.False(admission.Allowed);
        Assert.Equal("WRITTEN_RESPONSES_DISABLED", admission.Reason);
        Assert.Null(admission.Intent);
    }

    [Fact]
    public void MissingSenderIsReportedRatherThanCrashing()
    {
        var fixture = Harness();
        fixture.Registry.SelectedName = "does-not-exist";

        Assert.Equal("CHAT_SENDER_NOT_SELECTED", fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate()).Reason);
    }

    [Fact]
    public void UnavailableSenderIsRefusedSoNoReplyIsAttemptedAgainstIt()
    {
        var fixture = Harness();
        fixture.Sender.IsAvailable = false;

        Assert.Equal("CHAT_SENDER_UNAVAILABLE", fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate()).Reason);
    }

    [Fact]
    public void DevelopmentSenderIsRefusedWhenTheOperatorForbadeIt()
    {
        var fixture = Harness(
            options => options.AllowDevelopmentSender = false,
            new StubChatResponseSender { Name = "Stub", IsDevelopment = true });

        Assert.Equal("DEVELOPMENT_SENDER_NOT_ALLOWED", fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate()).Reason);
    }

    [Fact]
    public void ProviderTheSenderCannotWriteIsRefused()
    {
        var fixture = Harness();

        var admission = fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(provider: LiveChatProviderType.TikTok));

        Assert.Equal("PROVIDER_NOT_SUPPORTED", admission.Reason);
    }

    [Fact]
    public void AllowedProvidersListNarrowsTheSelectedSendersOwnList()
    {
        var fixture = Harness(options => options.AllowedProviders = [LiveChatProviderType.Twitch]);

        Assert.Equal("PROVIDER_NOT_ALLOWED", fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(provider: LiveChatProviderType.YouTube)).Reason);
        Assert.True(fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(provider: LiveChatProviderType.Twitch)).Allowed);
    }

    [Fact]
    public void EmptyAllowedProvidersListMeansNoExtraRestriction()
    {
        var fixture = Harness(options => options.AllowedProviders = []);

        Assert.True(fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate()).Allowed);
    }

    [Fact]
    public void MissingChannelIsRefused()
    {
        var fixture = Harness();

        Assert.Equal("MISSING_CHANNEL", fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(channel: "   ")).Reason);
    }

    [Fact]
    public void ChannelIsTrimmedIntoTheIntent()
    {
        var fixture = Harness();

        var admission = fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate(channel: "  channel  "));

        Assert.True(admission.Allowed);
        Assert.Equal("channel", admission.Intent!.ChannelId);
    }

    [Fact]
    public void EmptyReplyTextIsRefused()
    {
        var fixture = Harness();

        Assert.Equal("EMPTY_TEXT", fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(text: "   \n ")).Reason);
    }

    [Fact]
    public void ReplyTextIsCollapsedAndPrefixedIntoTheIntent()
    {
        var fixture = Harness(options => options.MessagePrefix = "[bot]");

        var admission = fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate(text: "  boa   noite \n"));

        Assert.True(admission.Allowed);
        Assert.Equal("[bot] boa noite", admission.Intent!.Text);
    }

    [Fact]
    public void OverlongReplyIsTruncatedAndReported()
    {
        var fixture = Harness(options => options.MaxCharacters = 5);

        var admission = fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(text: "abcdefghij"));

        Assert.True(admission.Allowed);
        Assert.Equal("abcde", admission.Intent!.Text);
        Assert.True(admission.Truncated);
        Assert.Equal(5, admission.CharacterCount);
    }

    [Fact]
    public void DuplicateTextInsideTheEchoWindowIsRefused()
    {
        var fixture = Harness();
        Assert.True(fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate()).Allowed);
        fixture.Ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "boa noite", Now);

        var second = fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate());

        Assert.Equal("DUPLICATE_TEXT", second.Reason);
    }

    [Fact]
    public void DuplicateTextIsRefusedEvenWhenTheSpacingDiffers()
    {
        var fixture = Harness();
        fixture.Ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "boa noite", Now);

        Assert.Equal("DUPLICATE_TEXT", fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(text: "boa   noite")).Reason);
    }

    [Fact]
    public void DuplicateTextIsScopedToItsOwnChannel()
    {
        // Two channels are two rooms. Suppressing a reply because an identical line exists in another
        // room would silently drop replies a viewer is waiting for.
        var fixture = Harness();
        fixture.Ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "boa noite", Now);

        Assert.True(fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(channel: "other")).Allowed);
    }

    [Fact]
    public void GlobalCooldownIsReported()
    {
        var fixture = Harness(options => options.GlobalCooldownSeconds = 30);
        Assert.True(fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate(text: "first")).Allowed);

        var refused = fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate(text: "second"));

        Assert.Equal("GLOBAL_COOLDOWN", refused.Reason);
    }

    [Fact]
    public void UserCooldownIsReported()
    {
        var fixture = Harness(options =>
        {
            options.GlobalCooldownSeconds = 1;
            options.UserCooldownSeconds = 300;
        });
        Assert.True(fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate(text: "first")).Allowed);

        fixture.Time.Advance(TimeSpan.FromSeconds(5));
        var refused = fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate(text: "second"));

        Assert.Equal("USER_COOLDOWN", refused.Reason);
    }

    [Fact]
    public void RefusedByCooldownDoesNotConsumeTheIdempotencyKey()
    {
        // A transient refusal must not permanently burn the identity of a reply the operator still wants.
        var fixture = Harness(options => options.GlobalCooldownSeconds = 30);
        Assert.True(fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate()).Allowed);

        fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate(text: "second"));

        Assert.Equal(1, fixture.Ledger.IdempotencyCount);
    }

    [Fact]
    public void SameInteractionIsNeverAdmittedTwice()
    {
        var fixture = Harness();
        var interactionId = Guid.NewGuid();

        Assert.True(fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(interactionId: interactionId)).Allowed);

        var replay = fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(text: "different text", interactionId: interactionId));

        Assert.Equal("DUPLICATE_WRITE", replay.Reason);
    }

    [Fact]
    public void DifferentInteractionsProduceDifferentIdempotencyKeys()
    {
        var fixture = Harness();

        var first = fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate(interactionId: Guid.NewGuid()));
        var second = fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate(interactionId: Guid.NewGuid()));

        Assert.True(first.Allowed);
        Assert.True(second.Allowed);
        Assert.NotEqual(first.Intent!.IdempotencyKey, second.Intent!.IdempotencyKey);
    }

    [Fact]
    public void AdmittedIntentCarriesInteractionAndCorrelationIdentity()
    {
        var fixture = Harness();
        var interactionId = Guid.NewGuid();

        var admission = fixture.Gatekeeper.Admit(
            ChatResponseTestFactory.Candidate(interactionId: interactionId));

        Assert.Equal(interactionId, admission.Intent!.InteractionId);
        Assert.Equal(interactionId, admission.Intent.ChatResponseId);
        Assert.Equal("correlation", admission.Intent.CorrelationId);
        Assert.Equal(Now, admission.Intent.CreatedAtUtc);
    }

    [Fact]
    public void RequestWithoutAnInteractionStillGetsItsOwnIdentity()
    {
        var fixture = Harness();

        var admission = fixture.Gatekeeper.Admit(ChatResponseTestFactory.Candidate());

        Assert.NotEqual(Guid.Empty, admission.Intent!.ChatResponseId);
        Assert.Null(admission.Intent.InteractionId);
    }

    [Fact]
    public void GatekeeperNeverThrowsOnAnEmptyCandidate()
    {
        var fixture = Harness();

        var admission = fixture.Gatekeeper.Admit(new ChatResponseCandidate(
            LiveChatProviderType.Twitch, null, null, null));

        Assert.False(admission.Allowed);
    }

    [Theory]
    [InlineData("respond", "completed", "text", true)]
    [InlineData("respond", "failed", "text", false)]
    [InlineData("respond", "completed", null, false)]
    [InlineData("respond", "completed", "   ", false)]
    [InlineData("ignore", "completed", "text", false)]
    public void EligibilityMatchesTheCasesWhereAWrittenReplyIsMeaningful(
        string decision,
        string status,
        string? text,
        bool expected)
    {
        var result = ChatResponseTestFactory.Interaction(
            text,
            string.Equals(decision, "respond", StringComparison.Ordinal)
                ? InteractionDecisionType.Respond
                : InteractionDecisionType.Ignore,
            string.Equals(status, "completed", StringComparison.Ordinal)
                ? InteractionStatus.Completed
                : InteractionStatus.Failed);

        Assert.Equal(expected, ChatResponseGatekeeper.IsEligible(result));
    }

    [Fact]
    public void EligibilityRejectsAnInteractionThatCarriedAnErrorCode()
    {
        Assert.False(ChatResponseGatekeeper.IsEligible(
            ChatResponseTestFactory.Interaction(errorCode: "TTS_FAILED")));
    }

    [Fact]
    public void CandidateIsDerivedFromTheInteractionsChatIdentity()
    {
        var interactionId = Guid.NewGuid();
        var fixture = Harness();
        var result = ChatResponseTestFactory.Interaction(
            provider: LiveChatProviderType.Kick, channel: "kick-channel", userId: "viewer",
            interactionId: interactionId);

        var candidate = fixture.Gatekeeper.CandidateFrom(result);
        var admission = fixture.Gatekeeper.Admit(candidate);

        Assert.Equal(LiveChatProviderType.Kick, candidate.Provider);
        Assert.True(admission.Allowed);
        Assert.Equal(interactionId, admission.Intent!.InteractionId);
        Assert.Equal("kick-channel", admission.Intent.ChannelId);
    }

    private static HarnessContext Harness(
        Action<ChatResponseOptions>? configure = null,
        StubChatResponseSender? sender = null)
    {
        var options = ChatResponseTestFactory.Options(configure);
        var time = new FixedTimeProvider(Now);
        var ledger = new ChatResponseLedger(Options.Create(options));
        var cooldowns = new ChatResponseCooldownTracker(Options.Create(options));
        sender ??= new StubChatResponseSender { Name = "Stub" };
        var registry = new StubChatResponseSenderRegistry([sender]);
        return new HarnessContext(
            options,
            sender,
            registry,
            ledger,
            cooldowns,
            time,
            ChatResponseTestFactory.Gatekeeper(options, registry, ledger, cooldowns, time));
    }

    private sealed record HarnessContext(
        ChatResponseOptions Options,
        StubChatResponseSender Sender,
        StubChatResponseSenderRegistry Registry,
        ChatResponseLedger Ledger,
        ChatResponseCooldownTracker Cooldowns,
        FixedTimeProvider Time,
        ChatResponseGatekeeper Gatekeeper);
}
