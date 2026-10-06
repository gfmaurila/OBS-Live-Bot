using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.ChatResponses;

namespace ObsLiveBot.UnitTests.ChatResponses;

/// <summary>
/// The real write route, driven through the real transport client with a scripted HTTP transport.
/// <para>
/// These exercise the actual SSN command contract - <c>getSources</c>, <c>inspectSourcePage</c>,
/// <c>interactSourcePage</c> - rather than a hand-written stand-in for it, because the write path has no
/// other way to work and a mock that agreed with the sender by construction would prove nothing.
/// </para>
/// </summary>
public sealed class SocialStreamNinjaChatResponseSenderTests
{
    [Fact]
    public async Task DeliveredReply_InspectsThePageFillsTheComposerAndPressesEnter()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")))
            .OnInteractOk()
            .OnInteractOk();

        var (result, _, _) = await SendAsync(transport);

        Assert.True(result.Success);
        Assert.Equal(1, result.Attempt);
        Assert.False(result.IsSimulated);
        Assert.Equal("twitch-1", result.SourceId);
        Assert.Equal(500, result.MaxMessageCharacters);
        Assert.Equal(["getSources", "inspectSourcePage", "interactSourcePage", "interactSourcePage"], transport.Actions);

        // The composer is filled first and only then submitted, in that order, with the reply's own text.
        Assert.Equal("fill", transport.InteractValue(0, "action"));
        Assert.Equal("boa noite", transport.InteractValue(0, "text"));
        Assert.Equal("pressKey", transport.InteractValue(1, "action"));
        Assert.Equal("Enter", transport.InteractValue(1, "key"));
        Assert.Null(transport.InteractValue(1, "text"));
    }

    [Fact]
    public async Task YouTubeReply_UsesTheSourcesShorterCeiling()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("yt-1", "youtube", "gfmaurila", videoId: "_j0cCIamgpc", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Chat message")))
            .OnInteractOk()
            .OnInteractOk();

        var (result, _, _) = await SendAsync(
            transport,
            provider: LiveChatProviderType.YouTube,
            channelId: "_j0cCIamgpc");

        Assert.True(result.Success);
        Assert.Equal(200, result.MaxMessageCharacters);
    }

    /// <summary>
    /// A reply longer than the platform accepts must be reported, never typed. Posting a truncated
    /// version would put words in a viewer's chat that the operator never wrote.
    /// </summary>
    [Fact]
    public async Task ReplyLongerThanThePlatformAccepts_IsRejectedWithoutTouchingTheComposer()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("yt-1", "youtube", "gfmaurila", videoId: "vid", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Chat message")));

        var (result, _, _) = await SendAsync(
            transport,
            provider: LiveChatProviderType.YouTube,
            channelId: "vid",
            text: new string('x', 201));

        Assert.False(result.Success);
        Assert.Equal("CHAT_TEXT_TOO_LONG", result.ErrorCode);
        Assert.Equal(["getSources", "inspectSourcePage"], transport.Actions);
    }

    [Fact]
    public async Task ReplyExactlyAtThePlatformLimit_IsStillAccepted()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("yt-1", "youtube", "gfmaurila", videoId: "vid", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Chat message")))
            .OnInteractOk()
            .OnInteractOk();

        var (result, _, _) = await SendAsync(
            transport,
            provider: LiveChatProviderType.YouTube,
            channelId: "vid",
            text: new string('x', 200));

        Assert.True(result.Success);
    }
    /// <summary>
    /// The page-reference rules are why a retry has to start over: a fresh inspection invalidates every
    /// earlier reference. Retrying therefore re-inspects rather than reusing the stale one.
    /// </summary>
    [Fact]
    public async Task StalePageReference_IsRetriedWithAFreshInspection()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")))
            .OnInteractRejected("STALE_PAGE_REF")
            .OnInspect(Pages(Element(Ref: "ref-2", Fillable: true, Name: "Send a message")))
            .OnInteractOk()
            .OnInteractOk();

        var (result, _, _) = await SendAsync(transport);

        Assert.True(result.Success);
        Assert.Equal(2, result.Attempt);
        Assert.Equal(
            [
                "getSources", "inspectSourcePage", "interactSourcePage",
                "inspectSourcePage", "interactSourcePage", "interactSourcePage"
            ],
            transport.Actions);

        // The retry filled the reference from the second inspection, never the stale first one.
        Assert.Equal(["ref-1", "ref-2", "ref-2"], transport.InteractRefs);
    }

    /// <summary>
    /// The same stale condition twice, each one detected before anything was typed. Both attempts are spent
    /// and the reply is reported undelivered - and still exactly one message could ever have been posted,
    /// because neither attempt ever reached the fill.
    /// </summary>
    [Fact]
    public async Task RepeatedStalePageReference_FailsAfterTheBoundedAttempts()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")))
            .OnInteractRejected("STALE_PAGE_REF")
            .OnInspect(Pages(Element(Ref: "ref-2", Fillable: true, Name: "Send a message")))
            .OnInteractRejected("STALE_PAGE_REF");

        var (result, sender, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_STALE_PAGE_REF", result.ErrorCode);
        Assert.Equal(2, result.Attempt);
        Assert.Equal(2, transport.InteractCount);

        // Two attempts of one reply, never two requests.
        var state = sender.GetRuntimeState();
        Assert.Equal(1, state.Requests);
        Assert.Equal(1, state.Failures);
        Assert.Equal("CHAT_STALE_PAGE_REF", state.LastErrorCode);
    }

    /// <summary>
    /// The core safety rule. Once the fill has been accepted, StudioOS cannot know whether Enter reached
    /// the page. Retrying could post the same reply twice in front of viewers, so the sequence is never
    /// repeated: the reply is reported as undelivered and at most one message is ever lost.
    /// </summary>
    [Fact]
    public async Task FailureAfterTheFill_IsNotRetried_SoNoReplyCanBePostedTwice()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")))
            .OnInteractOk()
            .OnInteractRejected("SOURCE_WINDOW_UNAVAILABLE");

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_SOURCE_WINDOW_UNAVAILABLE", result.ErrorCode);
        Assert.Equal(1, result.Attempt);
        // Exactly one inspection, one fill and one Enter: no second inspection, no second fill.
        Assert.Equal(["getSources", "inspectSourcePage", "interactSourcePage", "interactSourcePage"], transport.Actions);
        Assert.Equal(2, transport.InteractCount);
    }

    [Fact]
    public async Task TransportErrorOnTheEnterKey_IsNotRetried()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")))
            .OnInteractOk()
            .OnInteractHttpFailure();

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal(1, result.Attempt);
        Assert.Equal(4, transport.RequestCount);
    }

    /// <summary>
    /// A page with no fillable field is reported instead of being retried: another inspection of the same
    /// unfinished page would produce the same answer.
    /// </summary>
    [Fact]
    public async Task PageWithoutAFillableComposer_IsReportedAndNotRetried()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: false, Name: "Sign in")));

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_COMPOSER_NOT_FOUND", result.ErrorCode);
        Assert.Equal(["getSources", "inspectSourcePage"], transport.Actions);
    }

    /// <summary>
    /// The composer has to be the chat input, not a login field. A page offering both must resolve to the
    /// chat one, and an unsafe fill target is never attempted.
    /// </summary>
    [Fact]
    public async Task ComposerSelection_PrefersTheChatFieldOverAnUnrelatedFillableField()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(
                Element(Ref: "ref-search", Fillable: true, Name: "Search", Tag: "input"),
                Element(Ref: "ref-chat", Fillable: true, Name: "Send a message", Tag: "textarea", Role: "textbox")))
            .OnInteractOk()
            .OnInteractOk();

        var (result, _, _) = await SendAsync(transport);

        Assert.True(result.Success);
        Assert.Equal(["ref-chat", "ref-chat"], transport.InteractRefs);
    }

    /// <summary>
    /// A page whose only fillable field is a search box has no chat composer to identify, and typing a
    /// viewer-facing reply into it would be the worst available outcome: silently wrong, and reported as
    /// delivered. Nothing is typed and the reply is reported undelivered instead.
    /// </summary>
    [Fact]
    public async Task PageOfferingOnlyASearchField_IsRefusedRatherThanTypedInto()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-search", Fillable: true, Name: "Search", Tag: "input", Role: "")));

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_COMPOSER_NOT_FOUND", result.ErrorCode);
        Assert.Equal(["getSources", "inspectSourcePage"], transport.Actions);
        Assert.Equal(0, transport.InteractCount);
    }

    /// <summary>
    /// A credential field is never a composer, however the page presents it. This is the same rule as the
    /// search field with the worst possible mistake available: a public reply typed into a login form.
    /// </summary>
    [Fact]
    public async Task PageOfferingOnlyACredentialField_IsRefusedRatherThanTypedInto()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-password", Fillable: true, Name: "Password", Tag: "input")));

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_COMPOSER_NOT_FOUND", result.ErrorCode);
        Assert.Equal(0, transport.InteractCount);
    }

    /// <summary>
    /// A composer exposed as a content-editable box has no accessible name at all, so name evidence cannot
    /// be the only route to it. A page whose sole fillable surface is a multi-line text box is still enough:
    /// on a chat page there is nothing else that shape. A lone single-line input is not accepted.
    /// </summary>
    [Fact]
    public async Task SoleMultiLineFieldWithoutAnAccessibleName_IsAcceptedAsTheComposer()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-composer", Fillable: true, Name: "", Tag: "div", Role: "textbox")))
            .OnInteractOk()
            .OnInteractOk();

        var (result, _, _) = await SendAsync(transport);

        Assert.True(result.Success);
        Assert.Equal(["ref-composer", "ref-composer"], transport.InteractRefs);
    }

    [Fact]
    public async Task SoleSingleLineFieldWithoutAnAccessibleName_IsRefused()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-unknown", Fillable: true, Name: "", Tag: "input", Role: "")));

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_COMPOSER_NOT_FOUND", result.ErrorCode);
        Assert.Equal(0, transport.InteractCount);
    }

    /// <summary>
    /// Two unnamed fillable fields, both plain single-line inputs: nothing distinguishes them, so choosing
    /// either would be a coin toss on where a public reply ends up. The reply is refused instead.
    /// </summary>
    [Fact]
    public async Task AmbiguousUnnamedFillableFields_AreRefusedRatherThanGuessed()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(
                Element(Ref: "ref-a", Fillable: true, Name: "", Tag: "input", Role: ""),
                Element(Ref: "ref-b", Fillable: true, Name: "", Tag: "input", Role: "")));

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_COMPOSER_NOT_FOUND", result.ErrorCode);
        Assert.Equal(0, transport.InteractCount);
    }

    /// <summary>
    /// A rejection raised by the inspection - the window or the page went away - is proven to have happened
    /// before anything was typed, so the bounded retry is safe and the reply still reaches the chat.
    /// </summary>
    [Fact]
    public async Task RetryableFailureBeforeAnythingIsTyped_IsRetriedAndStillDelivers()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspectRejected("SOURCE_WINDOW_UNAVAILABLE")
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")))
            .OnInteractOk()
            .OnInteractOk();

        var (result, _, _) = await SendAsync(transport);

        Assert.True(result.Success);
        Assert.Equal(2, result.Attempt);
        Assert.Equal(
            ["getSources", "inspectSourcePage", "inspectSourcePage", "interactSourcePage", "interactSourcePage"],
            transport.Actions);
    }

    /// <summary>
    /// A stale reference reported by the Enter key is the one rejection that must never be retried even
    /// though it is retryable everywhere else: by then the text is in the composer and StudioOS has no way
    /// to know whether Enter reached the page. Retrying would risk the same reply appearing twice.
    /// </summary>
    [Fact]
    public async Task StaleReferenceOnTheEnterKey_IsReportedWithoutRetrying()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")))
            .OnInteractOk()
            .OnInteractRejected("STALE_PAGE_REF");

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_STALE_PAGE_REF", result.ErrorCode);
        Assert.Equal(1, result.Attempt);
        Assert.Equal(["getSources", "inspectSourcePage", "interactSourcePage", "interactSourcePage"], transport.Actions);
        Assert.Equal(2, transport.InteractCount);
    }

    [Fact]
    public async Task DisabledComposer_IsNeverFilled()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Disabled: true, Name: "Send a message")));

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_COMPOSER_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task NoSourceForThePlatform_ReportsThatTheChannelCannotBeWritten()
    {
        var transport = new RecordingTransport().OnGetSources();

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_SOURCE_NOT_FOUND", result.ErrorCode);
        Assert.Equal(["getSources"], transport.Actions);
    }

    [Fact]
    public async Task PlatformWithNoWriteAdapter_IsReportedUnsupported()
    {
        var transport = new RecordingTransport().OnGetSources();

        var (result, _, _) = await SendAsync(
            transport,
            registry: new ChatWriteAdapterRegistry([new TwitchChatWriteAdapter()]),
            provider: LiveChatProviderType.Kick);

        Assert.False(result.Success);
        Assert.Equal("PROVIDER_WRITE_UNSUPPORTED", result.ErrorCode);
        Assert.Empty(transport.Actions);
    }

    [Fact]
    public async Task UnreachableTransport_IsAFailureNotAnException()
    {
        var transport = new RecordingTransport()
            .OnGetSourcesFailure()
            .OnInspectFailure();

        var (result, _, _) = await SendAsync(transport);

        Assert.False(result.Success);
        Assert.Equal("CHAT_SOURCE_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task SenderIdentity_IsReportedWithoutProbingAnything()
    {
        var transport = new RecordingTransport().OnGetSources();
        var (_, sender, _) = await SendAsync(transport);

        Assert.Equal("SocialStreamNinja", sender.Name);
        Assert.False(sender.IsDevelopment);
        Assert.True(sender.IsAvailable);
        Assert.Equal(
            [LiveChatProviderType.Twitch, LiveChatProviderType.YouTube, LiveChatProviderType.Kick],
            sender.SupportedProviders);
    }

    /// <summary>
    /// Counters are the sender's own, so both replies have to go through one sender instance: a second
    /// instance would start from zero and prove nothing about accumulation.
    /// </summary>
    [Fact]
    public async Task RuntimeState_CountsRequestsSuccessesFailuresAndTheLastError()
    {
        var time = NewClock();
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")))
            .OnInteractOk()
            .OnInteractOk()
            .OnGetSources();
        var sender = CreateSender(transport, time);

        var delivered = await SendAsync(sender);
        Assert.True(delivered.Success);

        var afterSuccess = sender.GetRuntimeState();
        Assert.Equal(1, afterSuccess.Requests);
        Assert.Equal(1, afterSuccess.Successes);
        Assert.Equal(0, afterSuccess.Failures);
        Assert.Null(afterSuccess.LastErrorCode);
        Assert.NotNull(afterSuccess.LastSuccessAtUtc);

        // The resolver's cached view expires and the platform's window is gone by then, so the second reply
        // resolves no source. The clock has to move for this to be a fresh read rather than a cached one.
        time.Advance(TimeSpan.FromSeconds(30));
        var undeliverable = await SendAsync(sender);
        Assert.False(undeliverable.Success);
        Assert.Equal("CHAT_SOURCE_NOT_FOUND", undeliverable.ErrorCode);

        var afterFailure = sender.GetRuntimeState();
        Assert.Equal(2, afterFailure.Requests);
        Assert.Equal(1, afterFailure.Successes);
        Assert.Equal(1, afterFailure.Failures);
        Assert.Equal("CHAT_SOURCE_NOT_FOUND", afterFailure.LastErrorCode);
        Assert.NotNull(afterFailure.LastFailureAtUtc);

        // The earlier snapshot is a value, not a live view of the sender.
        Assert.Equal(0, afterSuccess.Failures);
    }

    /// <summary>
    /// A bounded retry is a transport attempt inside one logical request, never a second request. Both are
    /// observable, and they must not be conflated: the counters answer "how many replies did StudioOS try
    /// to deliver", the attempt answers "how much work one of them cost".
    /// </summary>
    [Fact]
    public async Task RetriesAreTransportAttemptsAndNotAdditionalResponseRequests()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")))
            .OnInteractRejected("STALE_PAGE_REF")
            .OnInspect(Pages(Element(Ref: "ref-2", Fillable: true, Name: "Send a message")))
            .OnInteractOk()
            .OnInteractOk();

        var (result, sender, _) = await SendAsync(transport);

        Assert.True(result.Success);
        Assert.Equal(2, result.Attempt);

        var state = sender.GetRuntimeState();
        Assert.Equal(1, state.Requests);
        Assert.Equal(1, state.Successes);
        Assert.Equal(0, state.Failures);
    }

    [Fact]
    public async Task CancellationPropagatesInsteadOfBecomingAFailedReply()
    {
        var transport = new RecordingTransport()
            .OnGetSources(Source("twitch-1", "twitch", "gfmaurila", active: true))
            .OnInspect(Pages(Element(Ref: "ref-1", Fillable: true, Name: "Send a message")));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(transport, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task WriteSideSourceListing_ProjectsOnlyWhatWriteResolutionNeeds()
    {
        var transport = new RecordingTransport().OnGetSources(
            Source("twitch-1", "twitch", "gfmaurila", active: true),
            Source("yt-1", "youtube", "gfmaurila", videoId: "vid", active: false),
            Source("broken", string.Empty, "x", active: true),        // No target: belongs to no platform.
            Source(string.Empty, "kick", "x", active: true));         // No id: cannot be typed into.

        var lister = new SocialStreamNinjaChatWriteClient(transport.CreateClient(), NullLogger<SocialStreamNinjaChatWriteClient>.Instance);

        var sources = await lister.ListSourcesAsync(CancellationToken.None);

        Assert.Equal(["twitch-1", "yt-1"], sources.Select(source => source.Id));
        Assert.True(sources[0].Active);
        Assert.False(sources[1].Active);
        Assert.Equal("vid", sources[1].VideoId);
        Assert.DoesNotContain(sources, source => source.Id == "broken");
    }

    [Fact]
    public async Task WriteSideSourceListing_TreatsARunningStatusAsActive()
    {
        var transport = new RecordingTransport().OnGetSourcesRaw(
            """[{"id":"yt-1","target":"youtube","videoId":"vid","status":"running"}]""");

        var lister = new SocialStreamNinjaChatWriteClient(transport.CreateClient(), NullLogger<SocialStreamNinjaChatWriteClient>.Instance);

        var sources = await lister.ListSourcesAsync(CancellationToken.None);

        Assert.True(Assert.Single(sources).Active);
    }

    [Fact]
    public async Task WriteSideSourceListing_ReportsAnEmptyViewWhenThereIsNoPayload()
    {
        var transport = new RecordingTransport().OnGetSourcesRaw("""{"unexpected":true}""");

        var lister = new SocialStreamNinjaChatWriteClient(transport.CreateClient(), NullLogger<SocialStreamNinjaChatWriteClient>.Instance);

        Assert.Empty(await lister.ListSourcesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FillLongerThanTheTransportAccepts_IsRejectedWithoutCallingTheTransport()
    {
        var transport = new RecordingTransport();
        var client = new SocialStreamNinjaChatWriteClient(transport.CreateClient(), NullLogger<SocialStreamNinjaChatWriteClient>.Instance);

        var outcome = await client.FillAsync("source", "ref", new string('x', 2001), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal("CHAT_TEXT_TOO_LONG", outcome.ErrorCode);
        Assert.Empty(transport.Actions);
    }

    private static async Task<(ChatResponseSendResult Result, SocialStreamNinjaChatResponseSender Sender, RecordingTransport Transport)>
        SendAsync(
            RecordingTransport transport,
            LiveChatProviderType provider = LiveChatProviderType.Twitch,
            string channelId = "gfmaurila",
            string text = "boa noite",
            ChatWriteAdapterRegistry? registry = null,
            CancellationToken cancellationToken = default)
    {
        var sender = CreateSender(transport, NewClock(), registry);
        var result = await SendAsync(sender, provider, channelId, text, cancellationToken);
        return (result, sender, transport);
    }

    private static async Task<ChatResponseSendResult> SendAsync(
        SocialStreamNinjaChatResponseSender sender,
        LiveChatProviderType provider = LiveChatProviderType.Twitch,
        string channelId = "gfmaurila",
        string text = "boa noite",
        CancellationToken cancellationToken = default) =>
        await sender.SendAsync(
            new ChatResponseSendRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "source-message-1",
                provider,
                channelId,
                "viewer-1",
                "viewer",
                text,
                "correlation",
                "idem-1"),
            cancellationToken);

    /// <summary>
    /// The real sender over the real write client. The clock is a parameter so a test that sends twice can
    /// move time between the two, which is what expires the resolver's cached view of the sources.
    /// </summary>
    private static SocialStreamNinjaChatResponseSender CreateSender(
        RecordingTransport transport,
        FixedTimeProvider time,
        ChatWriteAdapterRegistry? registry = null)
    {
        var client = new SocialStreamNinjaChatWriteClient(
            transport.CreateClient(), NullLogger<SocialStreamNinjaChatWriteClient>.Instance);
        var adapters = registry ?? new ChatWriteAdapterRegistry(
            [new TwitchChatWriteAdapter(), new YouTubeChatWriteAdapter(), new KickChatWriteAdapter()]);
        return new SocialStreamNinjaChatResponseSender(
            client,
            new ChatWriteTargetResolver(client, time, NullLogger<ChatWriteTargetResolver>.Instance),
            adapters,
            time,
            NullLogger<SocialStreamNinjaChatResponseSender>.Instance);
    }

    private static FixedTimeProvider NewClock() =>
        new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));

    private static SocialStreamNinjaSourceStub Source(
        string id,
        string target,
        string? username,
        string? videoId = null,
        bool active = false) =>
        new(id, target, username, videoId, active);

    /// <summary>
    /// An inspection payload in SSApp's own field names. The names matter: the client reads them exactly
    /// as SSApp sends them, so an element named differently here would be silently dropped.
    /// </summary>
    private static string Pages(params SocialStreamNinjaPageElement[] elements) =>
        JsonSerializer.Serialize(new
        {
            elements = elements.Select(element => new
            {
                @ref = element.Ref,
                frameIndex = element.FrameIndex,
                tag = element.Tag,
                role = element.Role,
                name = element.Name,
                fillable = element.Fillable,
                disabled = element.Disabled
            })
        });

    private static SocialStreamNinjaPageElement Element(
        string Ref,
        bool Fillable,
        string Name = "",
        string Tag = "textarea",
        string Role = "textbox",
        bool Disabled = false) =>
        new(Ref, FrameIndex: 0, Tag, Role, Name, Fillable, Disabled);

    private sealed record SocialStreamNinjaSourceStub(
        string Id,
        string Target,
        string? Username,
        string? VideoId,
        bool Active);

    /// <summary>
    /// Scripted SSApp transport. Every call is recorded in order, so a test can assert not only what the
    /// sender decided but exactly which SSN commands it issued to get there - which is the only way to
    /// prove a sequence was not repeated when it must not be.
    /// </summary>
    private sealed class RecordingTransport
    {
        private readonly Queue<Func<(HttpStatusCode Status, string Body)>> _responses = new();

        public List<string> Actions { get; } = [];

        public List<string> InteractRefs { get; } = [];

        /// <summary>One entry per <c>interactSourcePage</c> command, in the order they were issued.</summary>
        public List<JsonElement> InteractValues { get; } = [];

        public int RequestCount => Actions.Count;

        public int InteractCount => InteractValues.Count;

        public RecordingTransport OnGetSources(params SocialStreamNinjaSourceStub[] sources) =>
            OnGetSourcesRaw(JsonSerializer.Serialize(new
            {
                sources = sources.Select(source => new
                {
                    id = source.Id,
                    target = source.Target,
                    username = source.Username,
                    videoId = source.VideoId,
                    active = source.Active,
                    status = source.Active ? "running" : "stopped"
                })
            }));

        public RecordingTransport OnGetSourcesRaw(string json) => Enqueue(HttpStatusCode.OK, Ok(json));

        public RecordingTransport OnGetSourcesFailure() => Enqueue(HttpStatusCode.ServiceUnavailable, """{"ok":false}""");

        public RecordingTransport OnInspect(string json) => Enqueue(HttpStatusCode.OK, Ok(json));

        public RecordingTransport OnInspectRejected(string code) => Enqueue(HttpStatusCode.OK, Rejected(code));

        public RecordingTransport OnInspectFailure() => Enqueue(HttpStatusCode.ServiceUnavailable, """{"ok":false}""");

        public RecordingTransport OnInteractOk() => Enqueue(HttpStatusCode.OK, Ok("""{"payload":{}}"""));

        public RecordingTransport OnInteractRejected(string code) => Enqueue(HttpStatusCode.OK, Rejected(code));

        public RecordingTransport OnInteractHttpFailure() => Enqueue(HttpStatusCode.BadGateway, """{"ok":false}""");

        public HttpClient CreateClient() =>
            new(new StubHandler(this)) { BaseAddress = new Uri("http://ssn.local/") };

        /// <summary>A field of one issued interact command, read the way SSApp received it.</summary>
        public string? InteractValue(int index, string name) =>
            InteractValues[index].GetProperty(name).ValueKind == JsonValueKind.Null
                ? null
                : InteractValues[index].GetProperty(name).GetString();

        private RecordingTransport Enqueue(HttpStatusCode status, string body)
        {
            _responses.Enqueue(() => (status, body));
            return this;
        }

        /// <summary>SSApp answers a successful command with an <c>ok</c> flag and a payload object.</summary>
        private static string Ok(string json) => $$"""{"ok":true,"payload":{{json}}}""";

        /// <summary>A command SSApp understood and refused, carrying its own error code.</summary>
        private static string Rejected(string code) =>
            "{\"ok\":false,\"error\":{\"code\":\"" + code + "\",\"message\":\"rejected\"}}";

        private sealed class StubHandler(RecordingTransport transport) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(body);
                var action = document.RootElement.GetProperty("action").GetString()!;
                transport.Actions.Add(action);

                if (action == "interactSourcePage")
                {
                    var value = document.RootElement.GetProperty("value").Clone();
                    transport.InteractValues.Add(value);
                    transport.InteractRefs.Add(value.GetProperty("ref").GetString()!);
                }

                if (transport._responses.Count == 0)
                    throw new InvalidOperationException($"No scripted response for {action}.");

                var scripted = transport._responses.Dequeue();
                var (status, responseBody) = scripted();
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(responseBody)
                });
            }
        }
    }
}