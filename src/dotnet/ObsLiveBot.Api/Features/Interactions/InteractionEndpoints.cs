using MediatR;
using ObsLiveBot.Application.Features.Interactions.Get;
using ObsLiveBot.Application.Features.Interactions.Process;
using ObsLiveBot.Contracts.Interactions;

namespace ObsLiveBot.Api.Features.Interactions;

public static class InteractionEndpoints
{
    public static IEndpointRouteBuilder MapInteractionEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool developmentMode)
    {
        endpoints.MapGet(
                "/api/interactions/state",
                async (ISender sender, CancellationToken cancellationToken) =>
                    Results.Ok((await sender.Send(
                        new GetInteractionStateQuery(), cancellationToken)).Value))
            .WithName("GetInteractionState")
            .WithTags("Interactions")
            .Produces<InteractionStateResponse>();

        endpoints.MapGet(
                "/api/interactions/recent",
                async (int? limit, ISender sender, CancellationToken cancellationToken) =>
                    Results.Ok((await sender.Send(
                        new GetRecentInteractionsQuery(limit ?? 20), cancellationToken)).Value))
            .WithName("GetRecentInteractions")
            .WithTags("Interactions")
            .Produces<IReadOnlyList<InteractionResponse>>()
            .ProducesValidationProblem();

        endpoints.MapGet(
                "/api/interactions/providers",
                async (ISender sender, CancellationToken cancellationToken) =>
                    Results.Ok((await sender.Send(
                        new GetInteractionProvidersQuery(), cancellationToken)).Value))
            .WithName("GetInteractionProviders")
            .WithTags("Interactions")
            .Produces<InteractionProvidersResponse>();

        if (developmentMode)
        {
            endpoints.MapPost(
                    "/api/interactions/dev/test",
                    async (
                        DevelopmentInteractionTestRequest request,
                        ISender sender,
                        CancellationToken cancellationToken) =>
                        Results.Ok((await sender.Send(
                            new ProcessDevelopmentInteractionCommand(
                                request.Provider,
                                request.ChannelId,
                                request.UserId,
                                request.UserDisplayName,
                                request.Message,
                                request.ResponseMode),
                            cancellationToken)).Value))
                .WithName("TestDevelopmentInteraction")
                .WithTags("Interactions")
                .Produces<InteractionResponse>()
                .ProducesValidationProblem();
        }

        return endpoints;
    }
}
