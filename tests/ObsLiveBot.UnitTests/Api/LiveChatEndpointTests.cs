using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.LiveChat;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.UnitTests.Api;

public sealed class LiveChatEndpointTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("invalid")]
    public async Task ObsEvents_InvalidLimitReturnsBadRequest(string limit)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/obs/events?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task ObsEvents_ValidLimitReturnsOk(int limit)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/obs/events?limit={limit}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProvidersAndStateEndpoints_ReturnSafeResponses()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var providers = await client.GetAsync("/api/chat/providers");
        var state = await client.GetAsync("/api/chat/state");
        var payload = (await providers.Content.ReadAsStringAsync()) + (await state.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, providers.StatusCode);
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        Assert.DoesNotContain("accessToken", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clientSecret", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("invalid")]
    public async Task ChatMessages_InvalidLimitReturnsBadRequest(string limit)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/chat/messages?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChatMessages_InvalidProviderReturnsBadRequest()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/chat/messages?provider=InvalidProvider&limit=20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Twitch")]
    [InlineData("YouTube")]
    [InlineData("TikTok")]
    public async Task ChatMessages_KnownProviderWithoutMessagesReturnsOk(string provider)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/chat/messages?provider={provider}&limit=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MessagesAndEventsEndpoints_FilterWithoutDuplicatingStorage()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var buffer = factory.Services.GetRequiredService<ILiveChatBuffer>();
        buffer.Add(Event(LiveChatProviderType.Twitch, LiveChatEventType.Message, 1));
        buffer.Add(Event(LiveChatProviderType.YouTube, LiveChatEventType.SystemMessage, 2));

        var twitchMessages = await client.GetStringAsync("/api/chat/messages?provider=Twitch&limit=50");
        var allEvents = await client.GetStringAsync("/api/chat/events?limit=50");

        Assert.Contains("Twitch", twitchMessages, StringComparison.Ordinal);
        Assert.DoesNotContain("YouTube", twitchMessages, StringComparison.Ordinal);
        Assert.Contains("Twitch", allEvents, StringComparison.Ordinal);
        Assert.Contains("YouTube", allEvents, StringComparison.Ordinal);
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public async Task ChatValidationFailure_DoesNotAffectObsEndpoint()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var invalidChat = await client.GetAsync("/api/chat/messages?provider=invalid");
        var obsState = await client.GetAsync("/api/obs/live-state");

        Assert.Equal(HttpStatusCode.BadRequest, invalidChat.StatusCode);
        Assert.Equal(HttpStatusCode.OK, obsState.StatusCode);
    }

    private static LiveChatEvent Event(
        LiveChatProviderType provider,
        LiveChatEventType eventType,
        long sequence)
    {
        var user = new LiveChatUser(provider, $"user-{sequence}", null, null, false, false, false, false, false, []);
        return new LiveChatEvent(
            Guid.NewGuid(), eventType, provider, $"event-{sequence}", "channel", null,
            user, eventType == LiveChatEventType.Message ? "hello" : null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, sequence, Guid.NewGuid().ToString("N"),
            new Dictionary<string, string?>());
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
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
