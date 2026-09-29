using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Api.Configuration;

public sealed record StudioOsProviderConfigurationLoadResult(bool Loaded, string Status);

public static class StudioOsProviderConfigurationLoader
{
    public static StudioOsProviderConfigurationLoadResult Load(
        IConfigurationBuilder configuration,
        string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return new(false, "path_not_configured");

        var path = Path.Combine(directory, "studioos.providers.json");
        if (!File.Exists(path))
            return new(false, "file_missing");

        try
        {
            var contents = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(contents);
            StudioOsProviderConfigurationValidator.Validate(document.RootElement);
            using var stream = new MemoryStream(contents, writable: false);
            configuration.AddJsonStream(stream);
            return new(true, "loaded");
        }
        catch (JsonException)
        {
            return new(false, "invalid_json");
        }
        catch (InvalidDataException)
        {
            return new(false, "invalid_configuration");
        }
        catch (IOException)
        {
            return new(false, "file_unavailable");
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "file_unavailable");
        }
    }
}
