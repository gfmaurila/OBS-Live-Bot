using Ardalis.Result;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Obs;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Application.Features.Obs.GetLiveState;

public sealed record GetObsLiveStateQuery : IRequest<Result<ObsLiveStateResponse>>;

public sealed class GetObsLiveStateQueryHandler(IObsLiveStateReader reader)
    : IRequestHandler<GetObsLiveStateQuery, Result<ObsLiveStateResponse>>
{
    public Task<Result<ObsLiveStateResponse>> Handle(GetObsLiveStateQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = reader.State;
        var response = new ObsLiveStateResponse(
            state.ConnectionState.ToString(), state.IsSynchronized, state.IsStale,
            state.ObsVersion, state.WebSocketVersion, state.CurrentProgramScene,
            state.CurrentSceneCollection, state.CurrentProfile,
            new ObsOutputStateResponse(state.StreamState.ToString(), state.IsStreaming),
            new ObsRecordingStateResponse(state.RecordingState.ToString(), state.IsRecording, state.IsRecordingPaused),
            new ObsOutputStateResponse(state.ReplayBufferState.ToString(), state.ReplayBufferState is ObsReplayBufferState.Starting or ObsReplayBufferState.Running or ObsReplayBufferState.Stopping),
            new ObsVirtualCameraResponse(state.VirtualCameraState.ToString(), state.VirtualCameraState is ObsVirtualCameraState.Starting or ObsVirtualCameraState.Active or ObsVirtualCameraState.Stopping),
            state.LastEvent, state.LastEventAtUtc, state.LastUpdatedUtc);
        return Task.FromResult(Result.Success(response));
    }
}
