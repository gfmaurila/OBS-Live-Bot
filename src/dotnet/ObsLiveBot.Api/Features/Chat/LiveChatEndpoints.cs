using MediatR;
using ObsLiveBot.Application.Features.Chat;
using ObsLiveBot.Contracts.Chat;

namespace ObsLiveBot.Api.Features.Chat;

public static class LiveChatEndpoints
{
    public static IEndpointRouteBuilder MapLiveChatEndpoints(this IEndpointRouteBuilder endpoints)
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

        return endpoints;
    }
}
