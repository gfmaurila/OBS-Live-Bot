using Ardalis.Result;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Narration;

namespace ObsLiveBot.Application.Features.Narration.GetState;

public sealed record GetNarrationStateQuery : IRequest<Result<NarrationStateResponse>>;

public sealed class GetNarrationStateQueryHandler(INarrationService narration)
    : IRequestHandler<GetNarrationStateQuery, Result<NarrationStateResponse>>
{
    public async Task<Result<NarrationStateResponse>> Handle(
        GetNarrationStateQuery request,
        CancellationToken cancellationToken)
    {
        await narration.RefreshPlaybackStateAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(NarrationMappings.Map(narration.GetState()));
    }
}
