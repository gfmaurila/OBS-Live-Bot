using MediatR;
using ObsLiveBot.Application.Features.Chat;
using ObsLiveBot.Contracts.Chat;
using ObsLiveBot.Application.Abstractions;
using System.Text.Json;
using ObsLiveBot.Infrastructure.Chat;

namespace ObsLiveBot.Api.Features.Chat;

public static class LiveChatEndpoints
{
    public static IEndpointRouteBuilder MapLiveChatEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment = false)
    {
        endpoints.MapGet("/api/chat/providers", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok((await sender.Send(new GetLiveChatProvidersQuery(), cancellationToken)).Value))
            .WithName("GetLiveChatProviders").WithTags("Live Chat").Produces<LiveChatProvidersResponse>();

        endpoints.MapGet("/api/chat/state", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok((await sender.Send(new GetLiveChatStateQuery(), cancellationToken)).Value))
            .WithName("GetLiveChatState").WithTags("Live Chat").Produces<LiveChatStateResponse>();

        endpoints.MapGet(
                "/api/chat/messages",
                async (int? limit, string? provider, ISender sender, CancellationToken cancellationToken) =>
                    Results.Ok((await sender.Send(
                        new GetLiveChatMessagesQuery(limit ?? 20, provider),
                        cancellationToken)).Value))
            .WithName("GetLiveChatMessages").WithTags("Live Chat")
            .Produces<IReadOnlyList<LiveChatEventResponse>>()
            .ProducesValidationProblem();

        endpoints.MapGet(
                "/api/chat/events",
                async (int? limit, string? provider, ISender sender, CancellationToken cancellationToken) =>
                    Results.Ok((await sender.Send(
                        new GetLiveChatEventsQuery(limit ?? 20, provider),
                        cancellationToken)).Value))
            .WithName("GetLiveChatEvents").WithTags("Live Chat")
            .Produces<IReadOnlyList<LiveChatEventResponse>>()
            .ProducesValidationProblem();

        if (isDevelopment)
        {
            endpoints.MapPost(
                    "/api/chat/socialstream/dev/ingest",
                    async (
                        JsonElement fixture,
                        SocialStreamNinjaLiveChatProvider provider,
                        CancellationToken cancellationToken) =>
                    {
                        var result = await provider.IngestFixtureAsync(fixture, cancellationToken);
                        return result is null
                            ? Results.ValidationProblem(new Dictionary<string, string[]>
                            {
                                ["fixture"] = ["The SSN-compatible fixture was rejected."]
                            })
                            : Results.Ok(new
                            {
                                accepted = result.Accepted,
                                duplicate = result.Duplicate,
                                eventId = result.Event?.EventId
                            });
                    })
                .WithName("IngestSocialStreamNinjaDevelopmentFixture")
                .WithTags("Live Chat")
                .Produces(StatusCodes.Status200OK)
                .ProducesValidationProblem();
        }

        endpoints.MapGet("/api/chat/twitch/auth/status", (ITwitchAuthorizationService authorization) =>
                Results.Ok(authorization.GetSnapshot()))
            .WithName("GetTwitchAuthorizationStatus").WithTags("Twitch")
            .Produces<TwitchAuthorizationSnapshot>();

        endpoints.MapPost("/api/chat/twitch/auth/start", async (
                ITwitchAuthorizationService authorization, CancellationToken cancellationToken) =>
                Results.Ok(await authorization.StartDeviceAuthorizationAsync(cancellationToken)))
            .WithName("StartTwitchDeviceAuthorization").WithTags("Twitch")
            .Produces<TwitchDeviceAuthorizationResponse>();

        endpoints.MapPost("/api/chat/twitch/auth/logout", async (
                ITwitchAuthorizationService authorization, CancellationToken cancellationToken) =>
                Results.Ok(await authorization.DisconnectAsync(cancellationToken)))
            .WithName("DisconnectTwitchAuthorization").WithTags("Twitch")
            .Produces<TwitchAuthorizationSnapshot>();

        return endpoints;
    }
}
