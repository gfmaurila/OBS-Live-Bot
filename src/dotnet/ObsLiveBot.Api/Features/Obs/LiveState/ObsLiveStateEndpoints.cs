using MediatR;
using ObsLiveBot.Application.Features.Obs.GetEvents;
using ObsLiveBot.Application.Features.Obs.GetLiveState;
using ObsLiveBot.Contracts.Obs;

namespace ObsLiveBot.Api.Features.Obs.LiveState;

public static class ObsLiveStateEndpoints
{
    public static IEndpointRouteBuilder MapObsLiveStateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/obs/live-state", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok((await sender.Send(new GetObsLiveStateQuery(), cancellationToken)).Value))
            .WithName("GetObsLiveState").WithTags("OBS").Produces<ObsLiveStateResponse>();

        endpoints.MapGet("/api/obs/events", async (int? limit, ISender sender, CancellationToken cancellationToken) =>
            Results.Ok((await sender.Send(new GetRecentObsEventsQuery(limit ?? 20), cancellationToken)).Value))
            .WithName("GetRecentObsEvents").WithTags("OBS").Produces<IReadOnlyList<ObsEventResponse>>();

        return endpoints;
    }
}
