using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Api.Configuration;

public static class SocialStreamNinjaConfigurationLoader
{
    public static StudioOsProviderConfigurationLoadResult Load(
        IConfigurationBuilder configuration,
        string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return new(false, "path_not_configured");
        var path = Path.Combine(directory, "studioos.socialstream.json");
        if (!File.Exists(path)) return new(false, "file_missing");

        try
        {
            var json = File.ReadAllText(path).TrimStart('\uFEFF');
            using var document = JsonDocument.Parse(json);
            SocialStreamNinjaConfigurationValidator.Validate(document.RootElement);
            var root = document.RootElement;
            var connection = root.GetProperty("connection");
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [$"{SocialStreamNinjaOptions.SectionName}:Enabled"] = root.GetProperty("enabled").GetBoolean().ToString(),
                [$"{SocialStreamNinjaOptions.SectionName}:Endpoint"] = connection.GetProperty("endpoint").GetString(),
                [$"{SocialStreamNinjaOptions.SectionName}:ConfigureSources"] = GetBoolean(connection, "configureSources", true).ToString(),
                [$"{SocialStreamNinjaOptions.SectionName}:MaxEventBytes"] = GetInteger(connection, "maxEventBytes", 65_536).ToString()
            };

            var providers = root.GetProperty("providers");
            MapProvider(values, providers, "twitch", nameof(SocialStreamNinjaOptions.Twitch));
            MapProvider(values, providers, "youtube", nameof(SocialStreamNinjaOptions.YouTube));
            MapProvider(values, providers, "kick", nameof(SocialStreamNinjaOptions.Kick));
            configuration.AddInMemoryCollection(values);
            return new(true, "loaded");
        }
        catch (JsonException) { return new(false, "invalid_json"); }
        catch (InvalidDataException) { return new(false, "invalid_configuration"); }
        catch (IOException) { return new(false, "file_unavailable"); }
        catch (UnauthorizedAccessException) { return new(false, "file_unavailable"); }
    }

    private static void MapProvider(
        IDictionary<string, string?> values,
        JsonElement providers,
        string jsonName,
        string optionName)
    {
        if (!providers.TryGetProperty(jsonName, out var provider)) return;
        var prefix = $"{SocialStreamNinjaOptions.SectionName}:{optionName}";
        values[$"{prefix}:Enabled"] = provider.GetProperty("enabled").GetBoolean().ToString();
        values[$"{prefix}:Channel"] = provider.TryGetProperty("channel", out var channel) ? channel.GetString() : null;
        values[$"{prefix}:AuthMode"] = provider.TryGetProperty("authMode", out var authMode) ? authMode.GetString() : null;
        values[$"{prefix}:LiveChatUrl"] = provider.TryGetProperty("liveChatUrl", out var liveChatUrl) ? liveChatUrl.GetString() : null;
    }

    private static bool GetBoolean(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value) ? value.GetBoolean() : fallback;

    private static int GetInteger(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
}
