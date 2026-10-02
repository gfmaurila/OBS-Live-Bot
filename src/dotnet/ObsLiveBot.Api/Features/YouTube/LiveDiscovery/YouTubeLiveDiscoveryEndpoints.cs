using MediatR;
using ObsLiveBot.Application.Features.YouTube.GetLiveDiscovery;
using ObsLiveBot.Contracts.YouTube;

namespace ObsLiveBot.Api.Features.YouTube.LiveDiscovery;

public static class YouTubeLiveDiscoveryEndpoints
{
    /// <summary>
    /// Read-only projection of automatic YouTube live ownership. It reports the discovered public
    /// video and the SSN source it maps to; it never starts, stops or reconfigures anything.
    /// </summary>
    public static IEndpointRouteBuilder MapYouTubeLiveDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/youtube/live-discovery", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok((await sender.Send(new GetYouTubeLiveDiscoveryQuery(), cancellationToken)).Value))
            .WithName("GetYouTubeLiveDiscovery").WithTags("YouTube").Produces<YouTubeLiveDiscoveryResponse>();

        return endpoints;
    }
}
