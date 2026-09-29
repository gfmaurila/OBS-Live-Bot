using System.Text.Json;

namespace ObsLiveBot.Infrastructure.Configuration;

public static class StudioOsProviderConfigurationValidator
{
    private static readonly HashSet<string> ForbiddenFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "accesstoken", "refreshtoken", "clientsecret", "authorizationcode", "password", "cookie",
        "cookies", "authorization", "authorizationheader", "obswebsocketpassword", "streamkey",
        "bearertoken", "apisecret", "privatesigningkey"
    };

    private static readonly HashSet<string> KnownProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "twitch", "youtube", "kick"
    };

    private static readonly HashSet<string> PublicProviderFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "enabled", "officialapienabled", "clientid", "channel", "channelid", "broadcasteruserid"
    };

    public static void Validate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1 ||
            !root.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Provider config must use schemaVersion 1 and include providers.");

        RejectSecretFields(providers);
        ValidateProviders(providers);
    }

    private static void RejectSecretFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var normalizedName = string.Concat(property.Name.Where(char.IsLetterOrDigit));
                if (ForbiddenFields.Contains(normalizedName))
                    throw new InvalidDataException("Provider config must not contain secret fields.");
                RejectSecretFields(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectSecretFields(item);
        }
    }

    private static void ValidateProviders(JsonElement providers)
    {
        foreach (var provider in providers.EnumerateObject())
        {
            if (!KnownProviders.Contains(provider.Name) || provider.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Provider config contains an unsupported provider entry.");

            foreach (var setting in provider.Value.EnumerateObject())
            {
                if (!PublicProviderFields.Contains(setting.Name))
                    throw new InvalidDataException("Provider config contains an unsupported provider setting.");

                var validType = setting.Name.Equals("enabled", StringComparison.OrdinalIgnoreCase) ||
                                setting.Name.Equals("officialApiEnabled", StringComparison.OrdinalIgnoreCase)
                    ? setting.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    : setting.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null;
                if (!validType)
                    throw new InvalidDataException("Provider config contains an invalid setting value type.");
            }
        }
    }
}
