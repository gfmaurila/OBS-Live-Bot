using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Mediator;
using ObsLiveBot.Contracts.Obs;

namespace ObsLiveBot.Application.Features.Obs.GetStatus;

public sealed record GetObsStatusQuery : IQuery<ObsStatusResponse>;

public sealed class GetObsStatusQueryHandler(IObsClient obsClient)
    : IQueryHandler<GetObsStatusQuery, ObsStatusResponse>
{
    public Task<ObsStatusResponse> HandleAsync(
        GetObsStatusQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = obsClient.RuntimeState;

        return Task.FromResult(new ObsStatusResponse(
            state.ConnectionState.ToString(),
            state.ObsVersion,
            state.WebSocketVersion,
            state.CurrentProgramScene,
            state.IsStreaming,
            state.IsRecording,
            state.IsRecordingPaused,
            state.LastUpdatedUtc));
    }
}
