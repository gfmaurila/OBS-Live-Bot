using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Infrastructure.Chat;

public sealed record SocialStreamNinjaCapturedEvent(
    long Cursor,
    string? SourceId,
    string Type,
    DateTimeOffset CapturedAtUtc,
    JsonElement Data);

public sealed partial class SocialStreamNinjaMessageMapper(IOptions<SocialStreamNinjaOptions> options)
{
    private readonly SocialStreamNinjaOptions _options = options.Value;

    public bool TryReadEnvelope(
        JsonElement envelope,
        out SocialStreamNinjaCapturedEvent? captured,
        out string rejection)
    {
        captured = null;
        rejection = string.Empty;
        if (Encoding.UTF8.GetByteCount(envelope.GetRawText()) > _options.MaxEventBytes)
        {
            rejection = "payload_too_large";
            return false;
        }

        var sourceEvent = envelope;
        if (TryString(envelope, out var outerType, "type") && outerType == "source.event" &&
            TryElement(envelope, out var outerData, "data"))
            sourceEvent = outerData;

        if (!TryElement(sourceEvent, out var rawData, "data") || rawData.ValueKind != JsonValueKind.Object)
        {
            rejection = "missing_event_data";
            return false;
        }

        var eventType = TryString(sourceEvent, out var type, "type") ? type! : "capture";
        if (eventType is "status" or "navigation" or "reload" or "error" or "viewer")
        {
            rejection = "non_chat_event";
            return false;
        }

        var capturedAt = ParseTimestamp(sourceEvent, DateTimeOffset.UtcNow, "at");
        var cursor = TryInt64(sourceEvent, out var eventCursor, "id") ? eventCursor : 0;
        TryString(sourceEvent, out var sourceId, "sourceId");
        captured = new SocialStreamNinjaCapturedEvent(cursor, sourceId, eventType, capturedAt, rawData.Clone());
        return true;
    }

    public bool TryMap(
        SocialStreamNinjaCapturedEvent captured,
        out ProviderLiveChatEvent? mapped,
        out string rejection)
    {
        mapped = null;
        rejection = string.Empty;
        var raw = captured.Data;
        if (!TryMapProvider(raw, out var provider))
        {
            rejection = "missing_or_unsupported_provider";
            return false;
        }

        var displayName = FirstString(raw, "chatname", "displayName", "display_name", "name", "username");
        var username = FirstString(raw, "username", "userName", "chatname", "name");
        var nativeUserId = FirstString(raw, "userid", "userId", "user_id", "senderId", "authorChannelId");
        var userId = nativeUserId;
        var syntheticIdentity = false;
        if (string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(username ?? displayName))
        {
            userId = "name:" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes((username ?? displayName)!.Trim().ToUpperInvariant()))).ToLowerInvariant();
            syntheticIdentity = true;
        }
        if (string.IsNullOrWhiteSpace(userId))
        {
            rejection = "missing_user";
            return false;
        }

        var message = PlainText(FirstString(raw, "chatmessage", "message", "text", "content"));
        var eventType = MapEventType(raw, captured.Type, message);
        if (eventType == LiveChatEventType.Message && string.IsNullOrWhiteSpace(message))
        {
            rejection = "missing_message";
            return false;
        }

        var channel = ProviderSettings(provider).Channel;
        var channelName = FirstString(raw, "channel", "channelName", "channel_name") ?? channel;
        var channelId = FirstString(raw, "channelId", "channelid", "channel_id") ?? channelName;
        var providerEventId = FirstString(raw, "messageId", "message_id", "eventId", "event_id", "chatId", "chatid");
        var badges = ReadBadges(raw);
        var timestamp = ParseTimestamp(raw, captured.CapturedAtUtc, "timestamp", "createdAt", "created_at", "time");
        var metadata = BuildMetadata(raw, captured, syntheticIdentity);
        var isModerator = HasRole(raw, badges, "moderator", "mod");
        var isBroadcaster = HasRole(raw, badges, "broadcaster", "host", "owner");
        var isSubscriber = HasRole(raw, badges, "subscriber", "member", "membership") ||
            HasMeaningfulValue(raw, "membership", "subscription");
        var isVerified = ReadBoolean(raw, "verified", "isVerified", "is_verified");
        var isBot = ReadBoolean(raw, "bot", "isBot", "is_bot");

        mapped = new ProviderLiveChatEvent(
            provider,
            eventType,
            providerEventId,
            channelId,
            channelName,
            new LiveChatUser(
                provider,
                userId,
                username,
                displayName,
                isBroadcaster,
                isModerator,
                isSubscriber,
                isVerified,
                isBot,
                badges),
            message,
            timestamp,
            captured.Cursor > 0 ? $"ssn-{captured.Cursor}" : null,
            metadata);
        return true;
    }

    private SocialStreamNinjaProviderSettings ProviderSettings(LiveChatProviderType provider) => provider switch
    {
        LiveChatProviderType.Twitch => _options.Twitch,
        LiveChatProviderType.YouTube => _options.YouTube,
        LiveChatProviderType.Kick => _options.Kick,
        _ => new()
    };

    private static bool TryMapProvider(JsonElement raw, out LiveChatProviderType provider)
    {
        var value = FirstString(raw, "type", "platform", "provider", "source", "target")?.Trim().ToLowerInvariant();
        provider = value switch
        {
            "twitch" => LiveChatProviderType.Twitch,
            "youtube" or "youtube-live" => LiveChatProviderType.YouTube,
            "kick" => LiveChatProviderType.Kick,
            _ => LiveChatProviderType.Unknown
        };
        return provider != LiveChatProviderType.Unknown;
    }

    private static LiveChatEventType MapEventType(JsonElement raw, string capturedType, string? message)
    {
        var eventName = (FirstString(raw, "event", "eventType", "event_type") ?? capturedType).ToLowerInvariant();
        if (eventName.Contains("moder", StringComparison.Ordinal) || eventName.Contains("delete", StringComparison.Ordinal)) return LiveChatEventType.Moderation;
        if (eventName.Contains("raid", StringComparison.Ordinal)) return LiveChatEventType.Raid;
        if (eventName.Contains("follow", StringComparison.Ordinal)) return LiveChatEventType.Follow;
        if (eventName.Contains("gift", StringComparison.Ordinal) || HasMeaningfulValue(raw, "gift", "giftCount")) return LiveChatEventType.Gift;
        if (eventName.Contains("donat", StringComparison.Ordinal) || HasMeaningfulValue(raw, "hasDonation", "donation", "donoValue")) return LiveChatEventType.Donation;
        if (eventName.Contains("subscr", StringComparison.Ordinal)) return LiveChatEventType.Subscription;
        if (eventName.Contains("member", StringComparison.Ordinal) || HasMeaningfulValue(raw, "membership")) return LiveChatEventType.Membership;
        if (eventName.Contains("system", StringComparison.Ordinal)) return LiveChatEventType.SystemMessage;
        if (!string.IsNullOrWhiteSpace(message) || eventName == "message") return LiveChatEventType.Message;
        return LiveChatEventType.Unknown;
    }

    private static Dictionary<string, string?> BuildMetadata(
        JsonElement raw,
        SocialStreamNinjaCapturedEvent captured,
        bool syntheticIdentity)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["source.adapter"] = "SocialStreamNinja",
            ["interaction.suppressed"] = "true",
            ["ssn.eventType"] = captured.Type,
            ["ssn.sourceId"] = captured.SourceId,
            ["identity.synthetic"] = syntheticIdentity.ToString().ToLowerInvariant()
        };
        AddMetadata(raw, metadata, "donation", "donation", "hasDonation", "donoValue");
        AddMetadata(raw, metadata, "membership", "membership", "subscription");
        AddMetadata(raw, metadata, "gift", "gift", "giftCount");
        return metadata;
    }

    private static void AddMetadata(JsonElement raw, IDictionary<string, string?> metadata, string key, params string[] names)
    {
        if (!TryElement(raw, out var value, names) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        if (!string.IsNullOrWhiteSpace(text)) metadata[key] = text.Length > 2_048 ? text[..2_048] : text;
    }

    private static IReadOnlyList<string> ReadBadges(JsonElement raw)
    {
        if (!TryElement(raw, out var value, "chatbadges", "badges") || value.ValueKind != JsonValueKind.Array) return [];
        var badges = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            string? badge = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object => FirstString(item, "type", "name", "text", "title"),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(badge) && badge.Length <= 128 && badges.Count < 32) badges.Add(badge);
        }
        return badges;
    }

    private static bool HasRole(JsonElement raw, IReadOnlyList<string> badges, params string[] roles) =>
        roles.Any(role => ReadBoolean(raw, role, $"is{char.ToUpperInvariant(role[0])}{role[1..]}") ||
            badges.Any(badge => badge.Contains(role, StringComparison.OrdinalIgnoreCase)));

    private static bool HasMeaningfulValue(JsonElement raw, params string[] names)
    {
        if (!TryElement(raw, out var value, names)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.TryGetDecimal(out var number) && number != 0,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()) &&
                                    !string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText().Length > 2,
            _ => false
        };
    }

    private static bool ReadBoolean(JsonElement raw, params string[] names)
    {
        if (!TryElement(raw, out var value, names)) return false;
        return value.ValueKind == JsonValueKind.True ||
               value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed;
    }

    private static DateTimeOffset ParseTimestamp(JsonElement raw, DateTimeOffset fallback, params string[] names)
    {
        if (!TryElement(raw, out var value, names)) return fallback;
        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.ToUniversalTime();
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            try { return number > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(number) : DateTimeOffset.FromUnixTimeSeconds(number); }
            catch (ArgumentOutOfRangeException) { }
        }
        return fallback;
    }

    private static string? PlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return WebUtility.HtmlDecode(HtmlTagRegex().Replace(value, string.Empty)).Trim();
    }

    private static string? FirstString(JsonElement element, params string[] names) =>
        TryString(element, out var value, names) ? value : null;

    private static bool TryString(JsonElement element, out string? value, params string[] names)
    {
        value = null;
        if (!TryElement(element, out var found, names) || found.ValueKind != JsonValueKind.String) return false;
        value = found.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryInt64(JsonElement element, out long value, params string[] names)
    {
        value = 0;
        return TryElement(element, out var found, names) && found.ValueKind == JsonValueKind.Number && found.TryGetInt64(out value);
    }

    private static bool TryElement(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();
}
