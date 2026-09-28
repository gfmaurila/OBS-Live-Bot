using Ardalis.Result;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Obs;

namespace ObsLiveBot.Application.Features.Obs.GetStatus;

public sealed record GetObsStatusQuery : IRequest<Result<ObsStatusResponse>>;

public sealed class GetObsStatusQueryHandler(IObsClient obsClient)
    : IRequestHandler<GetObsStatusQuery, Result<ObsStatusResponse>>
{
    public Task<Result<ObsStatusResponse>> Handle(
        GetObsStatusQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = obsClient.RuntimeState;

        var response = new ObsStatusResponse(
            state.ConnectionState.ToString(),
            state.ObsVersion,
            state.WebSocketVersion,
            state.CurrentProgramScene,
            state.IsStreaming,
            state.IsRecording,
            state.IsRecordingPaused,
            state.LastUpdatedUtc);

        return Task.FromResult(Result.Success(response));
    }
}
