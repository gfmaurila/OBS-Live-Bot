using ObsLiveBot.Application.Features.Obs.GetStatus;
using ObsLiveBot.Application.Mediator;
using ObsLiveBot.Contracts.Obs;

namespace ObsLiveBot.Api.Features.Obs.GetStatus;

public static class ObsStatusEndpoint
{
    public static IEndpointRouteBuilder MapObsStatusEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/obs/status",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                {
                    var response = await mediator.SendAsync<ObsStatusResponse>(
                        new GetObsStatusQuery(),
                        cancellationToken);
                    return Results.Ok(response);
                })
            .WithName("GetObsStatus")
            .WithTags("OBS")
            .Produces<ObsStatusResponse>();

        return endpoints;
    }
}
