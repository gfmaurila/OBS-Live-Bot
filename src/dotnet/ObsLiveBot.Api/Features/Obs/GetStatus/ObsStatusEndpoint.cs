using MediatR;
using ObsLiveBot.Application.Features.Obs.GetStatus;
using ObsLiveBot.Contracts.Obs;

namespace ObsLiveBot.Api.Features.Obs.GetStatus;

public static class ObsStatusEndpoint
{
    public static IEndpointRouteBuilder MapObsStatusEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/obs/status",
                async (ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetObsStatusQuery(),
                        cancellationToken);
                    return Results.Ok(result.Value);
                })
            .WithName("GetObsStatus")
            .WithTags("OBS")
            .Produces<ObsStatusResponse>();

        return endpoints;
    }
}
