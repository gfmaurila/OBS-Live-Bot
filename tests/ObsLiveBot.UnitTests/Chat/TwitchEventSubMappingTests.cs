using System.Text.Json;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.Chat;

namespace ObsLiveBot.UnitTests.Chat;

public sealed class TwitchEventSubMappingTests
{
    [Fact]
    public void ReadSessionWelcome_AcceptsReplacementSessionAndKeepalive()
    {
        using var document = JsonDocument.Parse("""
            { "metadata": { "message_type": "session_welcome" },
              "payload": { "session": { "id": "replacement-session", "keepalive_timeout_seconds": 30 } } }
            """);

        var welcome = TwitchEventSubLiveChatProvider.ReadSessionWelcome(document.RootElement);

        Assert.Equal("replacement-session", welcome.SessionId);
        Assert.Equal(30, welcome.KeepaliveSeconds);
    }

    [Fact]
    public void ReadSessionWelcome_RejectsNonWelcomeOrInvalidKeepalive()
    {
        using var wrongType = JsonDocument.Parse("""
            { "metadata": { "message_type": "session_keepalive" }, "payload": { "session": {} } }
            """);
        using var invalidKeepalive = JsonDocument.Parse("""
            { "metadata": { "message_type": "session_welcome" },
              "payload": { "session": { "id": "replacement-session", "keepalive_timeout_seconds": 0 } } }
            """);

        Assert.Throws<InvalidDataException>(() =>
            TwitchEventSubLiveChatProvider.ReadSessionWelcome(wrongType.RootElement));
        Assert.Throws<InvalidDataException>(() =>
            TwitchEventSubLiveChatProvider.ReadSessionWelcome(invalidKeepalive.RootElement));
    }

    [Fact]
    public void MapChatMessage_PreservesUnicodeFragmentsAndProviderScopedIdentity()
    {
        const string json = """
        {
          "metadata": {
            "message_id": "event-envelope-1",
            "message_type": "notification",
            "subscription_type": "channel.chat.message",
            "message_timestamp": "2026-09-28T12:30:00Z"
          },
          "payload": { "event": {
            "broadcaster_user_id": "100",
            "broadcaster_user_login": "studio",
            "chatter_user_id": "200",
            "chatter_user_login": "viewer",
            "chatter_user_name": "Vïewer",
            "message_id": "chat-message-1",
            "message": { "text": "", "fragments": [
              { "type": "text", "text": "Olá " },
              { "type": "emote", "text": "Kappa" },
              { "type": "text", "text": " 😄" }
            ]},
            "badges": [{ "set_id": "subscriber" }],
            "color": "#00FFAA"
          }}
        }
        """;

        using var document = JsonDocument.Parse(json);
        var mapped = TwitchEventSubLiveChatProvider.MapChatMessage(
            document.RootElement, DateTimeOffset.Parse("2026-09-28T12:30:01Z"));

        Assert.NotNull(mapped);
        Assert.Equal(LiveChatProviderType.Twitch, mapped.Provider);
        Assert.Equal("chat-message-1", mapped.ProviderEventId);
        Assert.Equal("event-envelope-1", mapped.CorrelationId);
        Assert.Equal("Olá Kappa 😄", mapped.Message);
        Assert.Equal("Vïewer", mapped.User.DisplayName);
        Assert.True(mapped.User.IsSubscriber);
        Assert.Equal("Twitch:200", mapped.User.Identity);
        Assert.Equal("#00FFAA", mapped.Metadata["color"]);
    }

    [Fact]
    public void MapChatMessage_IgnoresNonChatNotifications()
    {
        using var document = JsonDocument.Parse("""
            { "metadata": { "message_type": "notification", "subscription_type": "channel.follow" } }
            """);

        Assert.Null(TwitchEventSubLiveChatProvider.MapChatMessage(document.RootElement, DateTimeOffset.UtcNow));
    }
}
