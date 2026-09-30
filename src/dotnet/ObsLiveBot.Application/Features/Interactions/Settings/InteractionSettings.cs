using Ardalis.Result;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Interactions;

namespace ObsLiveBot.Application.Features.Interactions.Settings;

public sealed record GetInteractionSettingsQuery : IRequest<Result<InteractionSettingsResponse>>;

public sealed record UpdateInteractionSettingsCommand(
    bool AutoPlayInteractions,
    string TriggerCommand,
    int GlobalCooldownSeconds,
    int UserCooldownSeconds) : IRequest<Result<InteractionSettingsResponse>>;

public sealed class UpdateInteractionSettingsCommandValidator
    : AbstractValidator<UpdateInteractionSettingsCommand>
{
    public UpdateInteractionSettingsCommandValidator()
    {
        RuleFor(command => command.TriggerCommand)
            .NotEmpty().MinimumLength(2).MaximumLength(32)
            .Must(value => !string.IsNullOrWhiteSpace(value) && value.StartsWith('!') &&
                           !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl));
        RuleFor(command => command.GlobalCooldownSeconds).InclusiveBetween(1, 3600);
        RuleFor(command => command.UserCooldownSeconds).InclusiveBetween(1, 86_400);
    }
}

public sealed class GetInteractionSettingsQueryHandler(IOptions<InteractionOptions> options)
    : IRequestHandler<GetInteractionSettingsQuery, Result<InteractionSettingsResponse>>
{
    public Task<Result<InteractionSettingsResponse>> Handle(
        GetInteractionSettingsQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success(Map(options.Value)));
    }

    internal static InteractionSettingsResponse Map(InteractionOptions options) => new(
        options.AutoPlayInteractions,
        options.ReservedCommandPrefix,
        options.GlobalCooldownSeconds,
        options.UserCooldownSeconds);
}

public sealed class UpdateInteractionSettingsCommandHandler(
    IOptions<InteractionOptions> interactionOptions,
    IOptions<NarrationOptions> narrationOptions)
    : IRequestHandler<UpdateInteractionSettingsCommand, Result<InteractionSettingsResponse>>
{
    public Task<Result<InteractionSettingsResponse>> Handle(
        UpdateInteractionSettingsCommand request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var interaction = interactionOptions.Value;
        var narration = narrationOptions.Value;
        // Disable the admission gate first. When enabling, arm narration before opening admission.
        if (!request.AutoPlayInteractions)
        {
            interaction.AutoPlayInteractions = false;
            narration.AutoPlayInteractions = false;
        }

        interaction.ReservedCommandPrefix = request.TriggerCommand.Trim();
        interaction.GlobalCooldownSeconds = request.GlobalCooldownSeconds;
        interaction.UserCooldownSeconds = request.UserCooldownSeconds;

        if (request.AutoPlayInteractions)
        {
            narration.AutoPlayInteractions = true;
            interaction.AutoPlayInteractions = true;
        }

        return Task.FromResult(Result.Success(GetInteractionSettingsQueryHandler.Map(interaction)));
    }
}
