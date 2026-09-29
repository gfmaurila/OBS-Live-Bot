using System.Text.Json;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.Chat;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.UnitTests.Chat;

public sealed class SocialStreamNinjaTests
{
    [Theory]
    [InlineData("twitch", LiveChatProviderType.Twitch)]
    [InlineData("youtube", LiveChatProviderType.YouTube)]
    [InlineData("youtube-live", LiveChatProviderType.YouTube)]
    [InlineData("kick", LiveChatProviderType.Kick)]
    public void Mapper_PreservesUnderlyingProviderIdentity(string platform, LiveChatProviderType expected)
    {
        var result = Map(Event(platform));

        Assert.Equal(expected, result.Provider);
        Assert.Equal(expected, result.User.Provider);
        Assert.Equal($"{expected}:user-1", result.User.Identity);
        Assert.Equal("SocialStreamNinja", result.Metadata["source.adapter"]);
    }

    [Fact]
    public void Mapper_PreservesUnicodeAccentsAndEmoji()
    {
        var result = Map(Event("twitch", message: "Olá, São Paulo 👋🏽🚚"));

        Assert.Equal("Olá, São Paulo 👋🏽🚚", result.Message);
    }

    [Fact]
    public void Mapper_StripsMarkupAndDecodesEntities()
    {
        var result = Map(Event("youtube", message: "<b>Olá</b> &amp; tchau"));

        Assert.Equal("Olá & tchau", result.Message);
    }

    [Theory]
    [InlineData("membership", LiveChatEventType.Membership)]
    [InlineData("subscription", LiveChatEventType.Subscription)]
    [InlineData("gift", LiveChatEventType.Gift)]
    [InlineData("donation", LiveChatEventType.Donation)]
    [InlineData("follow", LiveChatEventType.Follow)]
    [InlineData("raid", LiveChatEventType.Raid)]
    [InlineData("moderation", LiveChatEventType.Moderation)]
    [InlineData("system", LiveChatEventType.SystemMessage)]
    [InlineData("new-event", LiveChatEventType.Unknown)]
    public void Mapper_MapsKnownTypesAndSafelyFallsBack(string eventName, LiveChatEventType expected)
    {
        var result = Map(Event("kick", message: null, eventName: eventName));

        Assert.Equal(expected, result.EventType);
    }

    [Fact]
    public void Mapper_UsesProviderScopedSyntheticIdentityWhenNativeIdIsMissing()
    {
        var twitch = Map(Event("twitch", userId: null));
        var kick = Map(Event("kick", userId: null));

        Assert.StartsWith("name:", twitch.User.UserId, StringComparison.Ordinal);
        Assert.Equal(twitch.User.UserId, kick.User.UserId);
        Assert.NotEqual(twitch.User.Identity, kick.User.Identity);
        Assert.Equal("true", twitch.Metadata["identity.synthetic"]);
    }

    [Fact]
    public void Mapper_RejectsMissingUser()
    {
        var mapper = Mapper();
        using var document = JsonDocument.Parse(Event("twitch", userId: null, username: null));

        Assert.True(mapper.TryReadEnvelope(document.RootElement, out var captured, out _));
        Assert.False(mapper.TryMap(captured!, out _, out var reason));
        Assert.Equal("missing_user", reason);
    }

    [Fact]
    public void Mapper_RejectsMissingProvider()
    {
        var mapper = Mapper();
        using var document = JsonDocument.Parse(Event(null));

        Assert.True(mapper.TryReadEnvelope(document.RootElement, out var captured, out _));
        Assert.False(mapper.TryMap(captured!, out _, out var reason));
        Assert.Equal("missing_or_unsupported_provider", reason);
    }

    [Fact]
    public void Envelope_RejectsMalformedShape()
    {
        var mapper = Mapper();
        using var document = JsonDocument.Parse("{\"type\":\"source.event\",\"data\":{}} ");

        Assert.False(mapper.TryReadEnvelope(document.RootElement, out _, out var reason));
        Assert.Equal("missing_event_data", reason);
    }

    [Fact]
    public void Envelope_RejectsOversizedPayload()
    {
        var mapper = Mapper(maxEventBytes: 4_096);
        using var document = JsonDocument.Parse(Event("twitch", message: new string('x', 5_000)));

        Assert.False(mapper.TryReadEnvelope(document.RootElement, out _, out var reason));
        Assert.Equal("payload_too_large", reason);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("navigation")]
    [InlineData("reload")]
    [InlineData("viewer")]
    public void Envelope_IgnoresNonChatEvents(string eventType)
    {
        var mapper = Mapper();
        using var document = JsonDocument.Parse(Event("twitch", capturedType: eventType));

        Assert.False(mapper.TryReadEnvelope(document.RootElement, out _, out var reason));
        Assert.Equal("non_chat_event", reason);
    }

    [Fact]
    public void Mapper_PreservesNativeMessageIdTimestampBadgesAndFlags()
    {
        var result = Map("""
            {"type":"source.event","data":{"id":42,"sourceId":"source-1","type":"capture","at":"2026-09-29T12:00:00Z","data":{"type":"twitch","userid":"user-1","username":"tester","chatmessage":"hello","messageId":"native-1","timestamp":1720000000000,"chatbadges":["moderator",{"name":"subscriber"}],"verified":true}}}
            """);

        Assert.Equal("native-1", result.ProviderEventId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1720000000000), result.TimestampUtc);
        Assert.True(result.User.IsModerator);
        Assert.True(result.User.IsSubscriber);
        Assert.True(result.User.IsVerified);
        Assert.Equal(["moderator", "subscriber"], result.User.Badges);
        Assert.Equal("ssn-42", result.CorrelationId);
    }

    [Fact]
    public void Mapper_AlwaysSuppressesAutomaticInteraction()
    {
        var result = Map(Event("twitch"));

        Assert.Equal("true", result.Metadata["interaction.suppressed"]);
    }

    [Fact]
    public void OptionsValidator_AcceptsMinimalNonSecretDockerConfiguration()
    {
        var result = new SocialStreamNinjaOptionsValidator().Validate(null, OptionsValue());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("http://user:password@socialstream:17778")]
    [InlineData("ftp://socialstream")]
    [InlineData("http://socialstream:17778?token=value")]
    public void OptionsValidator_RejectsUnsafeEndpoint(string endpoint)
    {
        var options = OptionsValue();
        options.Endpoint = endpoint;

        Assert.True(new SocialStreamNinjaOptionsValidator().Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("clientSecret")]
    [InlineData("access_token")]
    [InlineData("cookies")]
    [InlineData("streamKey")]
    [InlineData("session")]
    public void ConfigurationValidator_RejectsProtectedFields(string field)
    {
        using var document = JsonDocument.Parse(ConfigurationJson(extraRoot: $",\"{field}\":\"redacted\""));

        Assert.Throws<InvalidDataException>(() =>
            SocialStreamNinjaConfigurationValidator.Validate(document.RootElement));
    }

    [Fact]
    public void ConfigurationValidator_AcceptsExpectedSchema()
    {
        using var document = JsonDocument.Parse(ConfigurationJson());

        SocialStreamNinjaConfigurationValidator.Validate(document.RootElement);
    }

    private static ProviderLiveChatEvent Map(string json)
    {
        var mapper = Mapper();
        using var document = JsonDocument.Parse(json);
        Assert.True(mapper.TryReadEnvelope(document.RootElement, out var captured, out var envelopeReason), envelopeReason);
        Assert.True(mapper.TryMap(captured!, out var mapped, out var mapReason), mapReason);
        return mapped!;
    }

    private static SocialStreamNinjaMessageMapper Mapper(int maxEventBytes = 65_536) =>
        new(Options.Create(OptionsValue(maxEventBytes)));

    private static SocialStreamNinjaOptions OptionsValue(int maxEventBytes = 65_536) => new()
    {
        Enabled = true,
        Endpoint = "http://gfm-studioos-socialstream:17778",
        MaxEventBytes = maxEventBytes,
        ConfigureSources = true,
        Twitch = new() { Enabled = true, Channel = "gfmaurila" },
        YouTube = new() { Enabled = true },
        Kick = new() { Enabled = true, Channel = "gfmaurila" }
    };

    private static string Event(
        string? platform,
        string? message = "hello",
        string? eventName = "message",
        string? userId = "user-1",
        string? username = "tester",
        string capturedType = "capture") => JsonSerializer.Serialize(new
        {
            type = "source.event",
            data = new
            {
                id = 10,
                sourceId = "source-1",
                type = capturedType,
                at = "2026-09-29T12:00:00Z",
                data = new
                {
                    type = platform,
                    userid = userId,
                    username,
                    chatname = username,
                    chatmessage = message,
                    messageId = "message-1",
                    @event = eventName
                }
            }
        });

    private static string ConfigurationJson(string extraRoot = "") => $$"""
        {
          "schemaVersion": 1,
          "enabled": true,
          "connection": {
            "mode": "docker",
            "endpoint": "http://gfm-studioos-socialstream:17778",
            "configureSources": true,
            "maxEventBytes": 65536
          },
          "providers": {
            "twitch": { "enabled": true, "channel": "gfmaurila" },
            "youtube": { "enabled": true },
            "kick": { "enabled": true, "channel": "gfmaurila" }
          }{{extraRoot}}
        }
        """;
}
