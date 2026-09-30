using System.Text.Json;

namespace ObsLiveBot.Infrastructure.Configuration;

public static class SocialStreamNinjaConfigurationValidator
{
    private static readonly HashSet<string> ForbiddenFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "accesstoken", "refreshtoken", "clientsecret", "authorizationcode", "password", "cookie", "cookies",
        "authorization", "authorizationheader", "streamkey", "bearertoken", "privatesigningkey", "session",
        "sessionid", "sessioncookie", "roomid", "obswebsocketpassword", "senha", "usuario"
    };

    private static readonly HashSet<string> RootFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "schemaVersion", "enabled", "connection", "providers"
    };

    private static readonly HashSet<string> ConnectionFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "mode", "endpoint", "configureSources", "maxEventBytes"
    };

    private static readonly HashSet<string> ProviderFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "enabled", "channel", "authMode"
    };

    public static void Validate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
            version.GetInt32() != 1)
            throw new InvalidDataException("Social Stream configuration must use schemaVersion 1.");

        RejectSecrets(root);
        RejectUnknown(root, RootFields);

        if (!root.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Social Stream enabled must be boolean.");
        if (!root.TryGetProperty("connection", out var connection) || connection.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Social Stream connection is required.");
        RejectUnknown(connection, ConnectionFields);
        if (!connection.TryGetProperty("mode", out var mode) || mode.GetString() != "docker")
            throw new InvalidDataException("Social Stream connection mode must be docker.");
        if (!connection.TryGetProperty("endpoint", out var endpoint) || endpoint.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Social Stream endpoint is required.");

        if (!root.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Social Stream providers are required.");
        foreach (var provider in providers.EnumerateObject())
        {
            if (provider.Name is not ("twitch" or "youtube" or "kick") || provider.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Social Stream contains an unsupported provider.");
            RejectUnknown(provider.Value, ProviderFields);
            if (!provider.Value.TryGetProperty("enabled", out var providerEnabled) ||
                providerEnabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Social Stream provider enabled must be boolean.");
            if (provider.Value.TryGetProperty("channel", out var channel) &&
                channel.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new InvalidDataException("Social Stream provider channel must be a string.");
            if (provider.Value.TryGetProperty("authMode", out var authMode) &&
                (provider.Name != "youtube" || authMode.ValueKind != JsonValueKind.String ||
                 authMode.GetString() is not ("oauth" or "url")))
                throw new InvalidDataException("Social Stream YouTube authMode must be oauth or url.");
        }
    }

    private static void RejectSecrets(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var normalized = string.Concat(property.Name.Where(char.IsLetterOrDigit));
                if (ForbiddenFields.Contains(normalized))
                    throw new InvalidDataException("Social Stream configuration must not contain protected fields.");
                RejectSecrets(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectSecrets(item);
        }
    }

    private static void RejectUnknown(JsonElement element, HashSet<string> allowed)
    {
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name))
                throw new InvalidDataException("Social Stream configuration contains an unsupported setting.");
    }
}
