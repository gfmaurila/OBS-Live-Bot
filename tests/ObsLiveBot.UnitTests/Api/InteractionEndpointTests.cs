using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ObsLiveBot.Contracts.Interactions;

namespace ObsLiveBot.UnitTests.Api;

public sealed class InteractionEndpointTests
{
    [Fact]
    public async Task InteractionReadEndpoints_ReturnOkAndDevelopmentProviders()
    {
        await using var factory = new InteractionApiFactory("Development");
        using var client = factory.CreateClient();

        var state = await client.GetAsync("/api/interactions/state");
        var recent = await client.GetAsync("/api/interactions/recent");
        var providers = await client.GetAsync("/api/interactions/providers");
        var providerPayload = await providers.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, providers.StatusCode);
        Assert.Contains("Development", providerPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", providerPayload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DevelopmentEndpoint_RunsSamePipelineAndStoresResult()
    {
        await using var factory = new InteractionApiFactory("Development");
        using var client = factory.CreateClient();
        var request = new DevelopmentInteractionTestRequest(
            "Development", "local", "dev-user", "Developer", "Olá StudioOS 🔥", "TextAndVoice");

        var response = await client.PostAsJsonAsync("/api/interactions/dev/test", request);
        var result = await response.Content.ReadFromJsonAsync<InteractionResponse>();
        var recent = await client.GetStringAsync("/api/interactions/recent?limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("Completed", result.Status);
        Assert.Equal("Development", result.AiProviderName);
        Assert.Equal("Development", result.TtsProviderName);
        Assert.Contains(result.InteractionId.ToString(), recent, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "local", "dev-user", "Text")]
    [InlineData("message", "", "dev-user", "Text")]
    [InlineData("message", "local", "", "Text")]
    [InlineData("message", "local", "dev-user", "None")]
    [InlineData("message", "local", "dev-user", "Invalid")]
    public async Task DevelopmentEndpoint_InvalidPayloadReturnsBadRequest(
        string message,
        string channelId,
        string userId,
        string responseMode)
    {
        await using var factory = new InteractionApiFactory("Development");
        using var client = factory.CreateClient();
        var request = new DevelopmentInteractionTestRequest(
            "Development", channelId, userId, "Developer", message, responseMode);

        var response = await client.PostAsJsonAsync("/api/interactions/dev/test", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentEndpoint_IsNotMappedInProduction()
    {
        await using var factory = new InteractionApiFactory("Production");
        using var client = factory.CreateClient();
        var request = new DevelopmentInteractionTestRequest(
            "Development", "local", "dev-user", "Developer", "hello", "Text");

        var response = await client.PostAsJsonAsync("/api/interactions/dev/test", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NarrationDevelopmentEndpoint_IsNotMappedInProduction()
    {
        await using var factory = new InteractionApiFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/narration/dev/test", new { text = "safe test" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("invalid")]
    public async Task Recent_InvalidLimitReturnsBadRequest(string limit)
    {
        await using var factory = new InteractionApiFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/interactions/recent?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ContainsInteractionEndpoints()
    {
        await using var factory = new InteractionApiFactory("Development");
        using var client = factory.CreateClient();

        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/interactions/state", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/interactions/recent", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/interactions/providers", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/interactions/dev/test", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/narration/state", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/narration/recent", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/narration/dev/test", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/narration/mute", swagger, StringComparison.Ordinal);
        Assert.Contains("/api/narration/volume", swagger, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_IncludesHealthyInteractionSubsystem()
    {
        await using var factory = new InteractionApiFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"interactions\":\"healthy\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"narration\":", payload, StringComparison.Ordinal);
    }

    private sealed class InteractionApiFactory(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Interactions:AiProvider", "Development");
            builder.UseSetting("Interactions:TtsProvider", "Development");
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
