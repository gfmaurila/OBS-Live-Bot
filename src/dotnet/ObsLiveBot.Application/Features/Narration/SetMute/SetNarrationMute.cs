using Ardalis.Result;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Narration;

namespace ObsLiveBot.Application.Features.Narration.SetMute;

public sealed record SetNarrationMuteCommand(bool Muted) : IRequest<Result<NarrationStateResponse>>;

public sealed class SetNarrationMuteCommandHandler(INarrationService narration)
    : IRequestHandler<SetNarrationMuteCommand, Result<NarrationStateResponse>>
{
    public async Task<Result<NarrationStateResponse>> Handle(
        SetNarrationMuteCommand request,
        CancellationToken cancellationToken)
    {
        await narration.SetMutedAsync(request.Muted, cancellationToken).ConfigureAwait(false);
        return Result.Success(NarrationMappings.Map(narration.GetState()));
    }
}
