using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ObsLiveBot.Contracts.Chat;

namespace ObsLiveBot.UnitTests.Api;

/// <summary>
/// Exercises the written-reply HTTP surface through the real host, so registration, the MediatR
/// validation pipeline and the endpoints themselves are all covered together.
/// <para>
/// The development endpoint is the only way to put StudioOS text in front of a real audience, so it is
/// the endpoint that most needs proof: it must exist in development, it must not exist in production, and
/// it must still obey the master switch.
/// </para>
/// </summary>
public sealed class ChatResponseEndpointTests
{
    [Fact]
    public async Task ChatResponseReadEndpoints_ReturnOkAndExposeBothSenders()
    {
        await using var factory = new ChatResponseApiFactory("Development");
        using var client = factory.CreateClient();

        var providers = await client.GetAsync("/api/chat-responses/providers");
        var settings = await client.GetAsync("/api/chat-responses/settings");
        var state = await client.GetAsync("/api/chat-responses/state");
        var recent = await client.GetAsync("/api/chat-responses/recent");
        var senders = await client.GetAsync("/api/chat-responses/senders");
        var senderPayload = await senders.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, providers.StatusCode);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, senders.StatusCode);
        Assert.Contains("SocialStreamNinja", senderPayload, StringComparison.Ordinal);
        Assert.Contains("Development", senderPayload, StringComparison.Ordinal);

        // The write path is the one place a secret would be most tempting to log or expose, so the payload
        // is checked for the words that would mean one leaked into a read-only surface.
        Assert.DoesNotContain("apiKey", senderPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", senderPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", senderPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("oauthRefreshToken", senderPayload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatResponseSettings_ShipDisabledAndReportConfigurationAsTheSource()
    {
        await using var factory = new ChatResponseApiFactory("Development");
        using var client = factory.CreateClient();

        var settings = await client.GetFromJsonAsync<ChatResponseSettingsResponse>("/api/chat-responses/settings");

        Assert.NotNull(settings);
        Assert.False(settings.Enabled);
        Assert.False(settings.ConfiguredEnabled);
        Assert.False(settings.Overridden);
        Assert.Equal("Configuration", settings.Source);
    }

    [Fact]
    public async Task ChatResponseSettings_OverrideIsRuntimeOnlyAndResetReturnsToTheShippedValue()
    {
        await using var factory = new ChatResponseApiFactory("Development");
        using var client = factory.CreateClient();

        // Enabling is a deliberate act with a deliberate undo, and nothing about it survives a restart -
        // which is the whole reason there is no state file for it.
        var enabled = await client.PutAsJsonAsync("/api/chat-responses/settings", new { enabled = true });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var active = await client.GetFromJsonAsync<ChatResponseSettingsResponse>("/api/chat-responses/settings");
        Assert.True(active!.Enabled);
        Assert.True(active.Overridden);
        Assert.Equal("RuntimeOverride", active.Source);

        var disabled = await client.PutAsJsonAsync("/api/chat-responses/settings", new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        var reset = await client.PutAsJsonAsync("/api/chat-responses/settings", new { reset = true });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var final = await client.GetFromJsonAsync<ChatResponseSettingsResponse>("/api/chat-responses/settings");
        Assert.False(final!.Enabled);
        Assert.False(final.Overridden);
    }

    [Fact]
    public async Task ChatResponseSettings_RequestWithoutAnythingToChangeIsRefused()
    {
        await using var factory = new ChatResponseApiFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/chat-responses/settings", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChatResponseState_ReportsTheBoundedCapacitiesThatMakeTheSubsystemBounded()
    {
        await using var factory = new ChatResponseApiFactory("Development");
        using var client = factory.CreateClient();

        var state = await client.GetFromJsonAsync<ChatResponseStateResponse>("/api/chat-responses/state");

        Assert.NotNull(state);
        Assert.False(state.Enabled);
        Assert.Equal("Disabled", state.Status);
        Assert.Equal("SocialStreamNinja", state.SelectedSender);
        Assert.True(state.QueueCapacity > 0);
        Assert.True(state.HistoryCapacity > 0);
        Assert.True(state.IdempotencyCapacity > 0);
        Assert.True(state.EchoCapacity > 0);
        Assert.True(state.CooldownCapacity > 0);
    }

    [Fact]
    public async Task DevelopmentSendEndpoint_RunsTheSamePipelineAndRecordsTheResult()
    {
        await using var factory = new ChatResponseApiFactory("Development", sender: "Development", enabled: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/chat-responses/dev/send",
            new DevelopmentChatResponseRequest("Twitch", "channel-1", "  linha   de  teste  "));
        var result = await response.Content.ReadFromJsonAsync<DevelopmentChatResponseResponse>();
        var recent = await client.GetFromJsonAsync<ChatResponseRecordResponse[]>(
            "/api/chat-responses/recent?limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Sent", result.Status);
        Assert.Equal("Twitch", result.Provider);
        Assert.Equal("channel-1", result.ChannelId);

        // Simulated is stated, never implied: nothing was delivered, and the response says so.
        Assert.True(result.Simulated);
        Assert.Equal("development-validation", result.Origin);

        Assert.NotNull(recent);
        var record = Assert.Single(recent);
        Assert.Equal("development-validation", record.SourceMessageId);
        Assert.NotEqual(Guid.Empty, record.ChatResponseId);

        // The collapsed text is what is recorded, so the platform could never receive two visually
        // identical lines that differ only by spacing.
        Assert.Equal("linha de teste", record.ResponseText);
        Assert.Equal(14, record.CharacterCount);
        Assert.Equal(14, result.CharacterCount);
    }

    [Fact]
    public async Task DevelopmentSendEndpoint_StillObeysTheMasterSwitch()
    {
        await using var factory = new ChatResponseApiFactory("Development", sender: "Development", enabled: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/chat-responses/dev/send",
            new DevelopmentChatResponseRequest("Twitch", "channel-1", "should never be written"));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("WRITTEN_RESPONSES_DISABLED", payload, StringComparison.Ordinal);
        Assert.Empty(await client.GetFromJsonAsync<ChatResponseRecordResponse[]>(
            "/api/chat-responses/recent?limit=10") ?? []);
    }

    [Theory]
    [InlineData("", "channel-1", "text")]
    [InlineData("Twitch", "", "text")]
    [InlineData("Twitch", "channel-1", "")]
    public async Task DevelopmentSendEndpoint_MissingInputReturnsBadRequest(
        string provider,
        string channelId,
        string text)
    {
        await using var factory = new ChatResponseApiFactory("Development", sender: "Development", enabled: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/chat-responses/dev/send", new DevelopmentChatResponseRequest(provider, channelId, text));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentSendEndpoint_UnknownPlatformIsRefusedRatherThanWrittenSomewhereElse()
    {
        await using var factory = new ChatResponseApiFactory("Development", sender: "Development", enabled: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/chat-responses/dev/send",
            new DevelopmentChatResponseRequest("Nirvana", "channel-1", "text"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("PROVIDER_UNKNOWN", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevelopmentSendEndpoint_IsNotMappedInProduction()
    {
        await using var factory = new ChatResponseApiFactory("Production", sender: "Development", enabled: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/chat-responses/dev/send",
            new DevelopmentChatResponseRequest("Twitch", "channel-1", "text"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("invalid")]
    public async Task Recent_InvalidLimitReturnsBadRequest(string limit)
    {
        await using var factory = new ChatResponseApiFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/chat-responses/recent?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ContainsChatResponseEndpoints()
    {
        await using var factory = new ChatResponseApiFactory("Development");
        using var client = factory.CreateClient();

        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/chat-responses/providers", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/chat-responses/settings", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/chat-responses/state", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/chat-responses/recent", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/chat-responses/senders", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/chat-responses/dev/send", swagger, StringComparison.Ordinal);
        Assert.Contains("ChatResponses", swagger, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_ExposesTheWrittenReplySubsystemAsHealthyWhileDisabled()
    {
        await using var factory = new ChatResponseApiFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Disabled is healthy: a machine that never opted in is in exactly the state it asked to be in.
        Assert.Contains("\"chatResponses\":\"healthy\"", payload, StringComparison.Ordinal);
    }

    private sealed class ChatResponseApiFactory(
        string environment,
        string sender = "SocialStreamNinja",
        bool enabled = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ChatResponses:Sender", sender);
            builder.UseSetting("ChatResponses:Enabled", enabled ? "true" : "false");
            builder.ConfigureServices(services =>
            {
                var hostedServices = services
                    .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
                    .ToArray();
                foreach (var descriptor in hostedServices)
                {
                    services.Remove(descriptor);
                }
            });
        }
    }
}
