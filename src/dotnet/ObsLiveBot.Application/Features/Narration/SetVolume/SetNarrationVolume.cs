using Ardalis.Result;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Narration;

namespace ObsLiveBot.Application.Features.Narration.SetVolume;

public sealed record SetNarrationVolumeCommand(double Volume) : IRequest<Result<NarrationStateResponse>>;

public sealed class SetNarrationVolumeCommandValidator : AbstractValidator<SetNarrationVolumeCommand>
{
    public SetNarrationVolumeCommandValidator(IOptions<NarrationOptions> options)
    {
        RuleFor(command => command.Volume)
            .Must(double.IsFinite)
            .WithMessage("Volume must be a finite number.")
            .InclusiveBetween(options.Value.MinimumVolume, options.Value.MaximumVolume);
    }
}

public sealed class SetNarrationVolumeCommandHandler(INarrationService narration)
    : IRequestHandler<SetNarrationVolumeCommand, Result<NarrationStateResponse>>
{
    public async Task<Result<NarrationStateResponse>> Handle(
        SetNarrationVolumeCommand request,
        CancellationToken cancellationToken)
    {
        var state = await narration.SetVolumeAsync(request.Volume, cancellationToken).ConfigureAwait(false);
        return Result.Success(NarrationMappings.Map(state));
    }
}
