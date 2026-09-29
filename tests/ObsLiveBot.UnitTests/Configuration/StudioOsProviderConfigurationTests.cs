using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Api.Configuration;
using ObsLiveBot.Infrastructure;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.UnitTests.Configuration;

public sealed class StudioOsProviderConfigurationTests
{
    [Fact]
    public void ProviderDocument_RejectsSecretFieldsAndUnsupportedSchema()
    {
        using var secretDocument = JsonDocument.Parse("""
            { "schemaVersion": 1, "providers": { "twitch": { "accessToken": "test-only" } } }
            """);
        using var oldSchema = JsonDocument.Parse("""
            { "schemaVersion": 2, "providers": {} }
            """);
        using var snakeCaseSecret = JsonDocument.Parse("""
            { "schemaVersion": 1, "providers": { "twitch": { "client_secret": "test-only" } } }
            """);

        Assert.Throws<InvalidDataException>(() =>
            StudioOsProviderConfigurationValidator.Validate(secretDocument.RootElement));
        Assert.Throws<InvalidDataException>(() =>
            StudioOsProviderConfigurationValidator.Validate(oldSchema.RootElement));
        Assert.Throws<InvalidDataException>(() =>
            StudioOsProviderConfigurationValidator.Validate(snakeCaseSecret.RootElement));
    }

    [Fact]
    public void ProviderDocument_MapsOnlyNonSecretProviderSettings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["providers:twitch:enabled"] = "true",
                ["providers:twitch:clientId"] = "public-client-id",
                ["providers:twitch:channel"] = "studio-channel",
                ["providers:twitch:broadcasterUserId"] = "12345",
                ["providers:twitch:accessToken"] = "test-only-token-must-not-bind",
                ["providers:youtube:channelId"] = "yt-channel",
                ["providers:kick:channel"] = "kick-channel"
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveChatInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var providers = provider.GetRequiredService<IOptions<LiveChatProvidersOptions>>().Value;
        var twitchAuth = provider.GetRequiredService<IOptions<TwitchOAuthOptions>>().Value;

        Assert.False(providers.Twitch.Enabled);
        Assert.Equal("public-client-id", providers.Twitch.ClientId);
        Assert.Equal("studio-channel", providers.Twitch.Channel);
        Assert.Null(providers.Twitch.AccessToken);
        Assert.False(twitchAuth.Enabled);
        Assert.Equal("public-client-id", twitchAuth.ClientId);
        Assert.Equal("studio-channel", twitchAuth.Channel);
        Assert.Equal("yt-channel", providers.YouTube.Channel);
        Assert.Equal("kick-channel", providers.Kick.Channel);
    }

    [Fact]
    public void ValidSchemaOne_AllowsEnabledTwitchAndYoutubeWithDeferredIdsAndDisabledKick()
    {
        using var document = JsonDocument.Parse("""
            {
              "schemaVersion": 1,
              "providers": {
                "twitch": { "enabled": true, "officialApiEnabled": true, "clientId": "public-id", "channel": "gfmaurila", "broadcasterUserId": "" },
                "youtube": { "enabled": true, "officialApiEnabled": true, "clientId": "public.apps.googleusercontent.com", "channelId": "" },
                "kick": { "enabled": false, "clientId": "", "channel": "" }
              }
            }
            """);

        StudioOsProviderConfigurationValidator.Validate(document.RootElement);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["providers:twitch:enabled"] = "true",
                ["providers:twitch:officialApiEnabled"] = "true",
                ["providers:twitch:clientId"] = "public-id",
                ["providers:twitch:channel"] = "gfmaurila",
                ["providers:twitch:broadcasterUserId"] = "",
                ["providers:youtube:enabled"] = "true",
                ["providers:youtube:officialApiEnabled"] = "true",
                ["providers:youtube:clientId"] = "public.apps.googleusercontent.com",
                ["providers:youtube:channelId"] = "",
                ["providers:kick:enabled"] = "false"
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveChatInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LiveChatProvidersOptions>>().Value;

        Assert.True(options.Twitch.Enabled);
        Assert.Equal("gfmaurila", options.Twitch.Channel);
        Assert.True(options.YouTube.Enabled);
        Assert.Equal("", options.YouTube.Channel);
        Assert.False(options.Kick.Enabled);
    }

    [Fact]
    public void ProviderConfigurationLoader_ReportsMissingFileWithoutThrowing()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var result = StudioOsProviderConfigurationLoader.Load(new ConfigurationManager(), directory);
            Assert.False(result.Loaded);
            Assert.Equal("file_missing", result.Status);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ProviderConfigurationLoader_ReportsMalformedJsonWithoutLeakingContent()
    {
        var directory = CreateTemporaryDirectory("{ \"schemaVersion\":");
        try
        {
            var result = StudioOsProviderConfigurationLoader.Load(new ConfigurationManager(), directory);
            Assert.False(result.Loaded);
            Assert.Equal("invalid_json", result.Status);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("clientSecret", "TEST_ONLY_NOT_A_SECRET")]
    [InlineData("accessToken", "TEST_ONLY_NOT_A_TOKEN")]
    [InlineData("refreshToken", "TEST_ONLY_NOT_A_TOKEN")]
    [InlineData("password", "TEST_ONLY_NOT_A_PASSWORD")]
    [InlineData("cookies", "TEST_ONLY_NOT_A_COOKIE")]
    public void ProviderConfigurationLoader_RejectsForbiddenFieldsWithoutEchoingValues(string name, string marker)
    {
        var directory = CreateTemporaryDirectory($"{{ \"schemaVersion\": 1, \"providers\": {{ \"twitch\": {{ \"{name}\": \"{marker}\" }} }} }}");
        try
        {
            var result = StudioOsProviderConfigurationLoader.Load(new ConfigurationManager(), directory);
            Assert.False(result.Loaded);
            Assert.Equal("invalid_configuration", result.Status);
            Assert.DoesNotContain(marker, result.Status, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ProviderConfigurationLoader_RejectsUnsupportedSchemaVersion()
    {
        var directory = CreateTemporaryDirectory("{ \"schemaVersion\": 999, \"providers\": {} }");
        try
        {
            var result = StudioOsProviderConfigurationLoader.Load(new ConfigurationManager(), directory);
            Assert.False(result.Loaded);
            Assert.Equal("invalid_configuration", result.Status);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void IncompleteTwitchConfiguration_IsolatedAndDisabledWhileValidYoutubeRemainsEnabled()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["providers:twitch:enabled"] = "true",
                ["providers:twitch:channel"] = "gfmaurila",
                ["providers:youtube:enabled"] = "true",
                ["providers:youtube:officialApiEnabled"] = "true",
                ["providers:youtube:clientId"] = "public.apps.googleusercontent.com",
                ["providers:youtube:channelId"] = ""
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveChatInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LiveChatProvidersOptions>>().Value;

        Assert.False(options.Twitch.Enabled);
        Assert.True(options.YouTube.Enabled);
    }

    private static string CreateTemporaryDirectory(string? content = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "studioos-provider-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (content is not null)
            File.WriteAllText(Path.Combine(directory, "studioos.providers.json"), content);
        return directory;
    }
}
