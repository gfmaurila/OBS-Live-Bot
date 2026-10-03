using MediatR;
using ObsLiveBot.Application.Features.Narration.DevTest;
using ObsLiveBot.Application.Features.Narration.GetRecent;
using ObsLiveBot.Application.Features.Narration.GetState;
using ObsLiveBot.Application.Features.Narration.SetMute;
using ObsLiveBot.Application.Features.Narration.SetVolume;
using ObsLiveBot.Contracts.Narration;

namespace ObsLiveBot.Api.Features.Narration;

public static class NarrationEndpoints
{
    public static IEndpointRouteBuilder MapNarrationEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool developmentMode)
    {
        endpoints.MapGet("/api/narration/state", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok((await sender.Send(new GetNarrationStateQuery(), cancellationToken)).Value))
            .WithName("GetNarrationState")
            .WithTags("Narration")
            .Produces<NarrationStateResponse>();

        endpoints.MapGet("/api/narration/recent", async (int? limit, ISender sender, CancellationToken cancellationToken) =>
            Results.Ok((await sender.Send(new GetRecentNarrationQuery(limit ?? 20), cancellationToken)).Value))
            .WithName("GetRecentNarration")
            .WithTags("Narration")
            .Produces<NarrationRecentResponse>()
            .ProducesValidationProblem();

        endpoints.MapPut("/api/narration/mute", async (
                NarrationMuteRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
                Results.Ok((await sender.Send(new SetNarrationMuteCommand(request.Muted), cancellationToken)).Value))
            .WithName("SetNarrationMute")
            .WithTags("Narration")
            .Produces<NarrationStateResponse>()
            .ProducesValidationProblem();

        endpoints.MapPut("/api/narration/volume", async (
                NarrationVolumeRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
                Results.Ok((await sender.Send(new SetNarrationVolumeCommand(request.Volume), cancellationToken)).Value))
            .WithName("SetNarrationVolume")
            .WithTags("Narration")
            .Produces<NarrationStateResponse>()
            .ProducesValidationProblem();

        if (developmentMode)
        {
            endpoints.MapPost("/api/narration/dev/test", async (
                    NarrationDevTestRequest request,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var response = (await sender.Send(new TestNarrationCommand(request.Text), cancellationToken)).Value;
                    if (response.Accepted) return Results.Accepted("/api/narration/recent", response);
                    if (response.ErrorCode is "NARRATION_QUEUE_FULL" or "NARRATION_TOO_LONG")
                        return Results.Conflict(response);
                    return Results.Problem(
                        title: "Narration test was not accepted.",
                        detail: response.ErrorCode,
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                })
                .WithName("TestNarration")
                .WithTags("Narration")
                .Produces<NarrationDevTestResponse>(StatusCodes.Status202Accepted)
                .Produces<NarrationDevTestResponse>(StatusCodes.Status409Conflict)
                .ProducesValidationProblem();

            // Isolated dual voice playback. Development only, and it never changes the configured
            // autoplay switch: the automatic chat interaction stays silent while this speaks.
            endpoints.MapPost("/api/narration/dev/dual-voice", async (
                    NarrationDualVoiceDevTestRequest request,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var response = (await sender.Send(new TestDualVoiceNarrationCommand(
                        request.ChatText, request.AssistantText), cancellationToken)).Value;
                    if (response.Accepted) return Results.Accepted("/api/narration/recent", response);
                    return Results.Problem(
                        title: "Dual voice test was not accepted.",
                        detail: response.ErrorCode,
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                })
                .WithName("TestDualVoiceNarration")
                .WithTags("Narration")
                .Produces<NarrationDualVoiceDevTestResponse>(StatusCodes.Status202Accepted)
                .ProducesValidationProblem();
        }

        return endpoints;
    }
}
