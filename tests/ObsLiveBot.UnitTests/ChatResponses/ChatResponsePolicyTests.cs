using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.UnitTests.ChatResponses;

public sealed class ChatResponsePolicyTests
{
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("  hello  ", "hello")]
    [InlineData("hello   world", "hello world")]
    [InlineData("hello\nworld", "hello world")]
    [InlineData("hello\r\n\tworld", "hello world")]
    [InlineData("hello\u0000world", "helloworld")]
    [InlineData("hello\u00a0world", "hello world")]
    [InlineData("\n\t  \r", "")]
    [InlineData("   ", "")]
    public void Collapse_ProducesOneDeterministicForm(string input, string expected)
    {
        Assert.Equal(expected, ChatResponsePolicy.Collapse(input));
    }

    [Fact]
    public void Collapse_IsWhatMakesTextIdenticalAcrossSpacingVariants()
    {
        // Two copies of one sentence that a viewer cannot tell apart must produce one key, otherwise
        // duplicate-text suppression and echo matching both silently stop working.
        Assert.Equal(
            ChatResponsePolicy.Collapse("Boa  noite!\nObrigado."),
            ChatResponsePolicy.Collapse("Boa noite!   Obrigado."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t")]
    public void PrepareText_RejectsTextThatCollapsesToNothing(string? raw)
    {
        var prepared = ChatResponsePolicy.PrepareText(raw, null, 500);

        Assert.False(prepared.Success);
        Assert.Equal("EMPTY_TEXT", prepared.ErrorCode);
        Assert.Null(prepared.Text);
    }

    [Fact]
    public void PrepareText_TruncatesOnRuneBoundaryAndReportsIt()
    {
        // Each emoji is two UTF-16 code units, so a naive Length cut would split one in half. Five
        // runes is four letters plus exactly one emoji.
        var prepared = ChatResponsePolicy.PrepareText("aaaa😀😀😀😀", null, 5);

        Assert.True(prepared.Success);
        Assert.Equal("aaaa😀", prepared.Text);
        Assert.True(prepared.Truncated);
        // Four letters plus four two-unit emoji: twelve UTF-16 units in the original.
        Assert.Equal(12, prepared.SourceCharacterCount);
        Assert.False(char.IsHighSurrogate(prepared.Text![^1]));
    }

    [Fact]
    public void PrepareText_DoesNotReportTruncationWhenTextFits()
    {
        var prepared = ChatResponsePolicy.PrepareText("short reply", null, 500);

        Assert.True(prepared.Success);
        Assert.False(prepared.Truncated);
        Assert.Equal("short reply", prepared.Text);
    }

    [Fact]
    public void PrepareText_PrependsPrefixWithExactlyOneSpace()
    {
        Assert.Equal("[bot] hi", ChatResponsePolicy.PrepareText("hi", "[bot]", 500)!.Text);
        Assert.Equal("[bot] hi", ChatResponsePolicy.PrepareText("hi", "[bot] ", 500)!.Text);
    }

    [Fact]
    public void PrepareText_CollapsesPrefixTooSoItCannotBypassLengthLimits()
    {
        var prepared = ChatResponsePolicy.PrepareText("hi", "[ bot ]", 500);

        Assert.Equal("[ bot ] hi", prepared.Text);
    }

    [Fact]
    public void PrepareText_EmptyPrefixChangesNothing()
    {
        Assert.Equal("hi", ChatResponsePolicy.PrepareText("hi", string.Empty, 500)!.Text);
    }

    [Fact]
    public void PrepareText_ZeroOrNegativeLimitMeansNoTruncation()
    {
        // 0 is treated as "unlimited" so a misconfigured limit cannot silently delete every reply.
        var prepared = ChatResponsePolicy.PrepareText(new string('x', 50), null, 0);

        Assert.True(prepared.Success);
        Assert.Equal(50, prepared.Text!.Length);
        Assert.False(prepared.Truncated);
    }

    [Fact]
    public void IdempotencyKey_IsStablePerProviderChannelAndResponse()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var first = ChatResponsePolicy.ComposeIdempotencyKey(LiveChatProviderType.Twitch, "channel", id);
        var second = ChatResponsePolicy.ComposeIdempotencyKey(LiveChatProviderType.Twitch, "channel", id);

        Assert.Equal(first, second);
        Assert.NotEqual(first, ChatResponsePolicy.ComposeIdempotencyKey(LiveChatProviderType.Kick, "channel", id));
        Assert.NotEqual(first, ChatResponsePolicy.ComposeIdempotencyKey(LiveChatProviderType.Twitch, "other", id));
    }

    [Fact]
    public void NormalizeChannel_TrimsAndTreatsNullAsEmpty()
    {
        Assert.Equal("channel", ChatResponsePolicy.NormalizeChannel("  channel "));
        Assert.Equal(string.Empty, ChatResponsePolicy.NormalizeChannel(null));
    }

    [Fact]
    public void Evaluate_RejectsEverythingWhenDisabled()
    {
        var text = ChatResponsePolicy.PrepareText("hi", null, 500);

        var admission = ChatResponsePolicy.Evaluate(false, "SocialStreamNinja", "channel", text);

        Assert.False(admission.Allowed);
        Assert.Equal("WRITTEN_RESPONSES_DISABLED", admission.Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenNoSenderIsNamed()
    {
        var text = ChatResponsePolicy.PrepareText("hi", null, 500);

        Assert.Equal(
            "SENDER_NOT_SELECTED",
            ChatResponsePolicy.Evaluate(true, "  ", "channel", text).Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenChannelIsMissing()
    {
        var text = ChatResponsePolicy.PrepareText("hi", null, 500);

        Assert.Equal(
            "MISSING_CHANNEL",
            ChatResponsePolicy.Evaluate(true, "SocialStreamNinja", "   ", text).Reason);
    }

    [Fact]
    public void Evaluate_AcceptsAFullySpecifiedRequest()
    {
        var text = ChatResponsePolicy.PrepareText("hi", null, 500);

        Assert.True(ChatResponsePolicy.Evaluate(true, "SocialStreamNinja", "channel", text).Allowed);
    }

    [Fact]
    public void Admission_RejectCarriesItsReason()
    {
        Assert.Equal("REASON", ChatResponsePolicy.Admission.Reject("REASON").Reason);
        Assert.Null(ChatResponsePolicy.Admission.Accept().Reason);
    }
}
