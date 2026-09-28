using Ardalis.Result;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Interactions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Features.Interactions.Process;

public sealed record ProcessInteractionCommand(
    LiveChatEvent ChatEvent,
    InteractionResponseMode? RequestedMode) : IRequest<Result<InteractionResponse>>;

public sealed class ProcessInteractionCommandValidator : AbstractValidator<ProcessInteractionCommand>
{
    public ProcessInteractionCommandValidator(IOptions<InteractionOptions> options)
    {
        RuleFor(command => command.ChatEvent).NotNull();
        RuleFor(command => command.ChatEvent.Provider)
            .Must(provider => provider != LiveChatProviderType.Unknown && Enum.IsDefined(provider))
            .When(command => command.ChatEvent is not null);
        RuleFor(command => command.ChatEvent.ChannelId).NotEmpty().MaximumLength(512)
            .When(command => command.ChatEvent is not null);
        RuleFor(command => command.ChatEvent.User.UserId).NotEmpty().MaximumLength(512)
            .When(command => command.ChatEvent?.User is not null);
        RuleFor(command => command.ChatEvent.Message)
            .NotEmpty()
            .MaximumLength(options.Value.MaxMessageCharacters)
            .When(command => command.ChatEvent is not null);
        RuleFor(command => command.RequestedMode)
            .Must(mode => mode is null || Enum.IsDefined(mode.Value));
    }
}

public sealed class ProcessInteractionCommandHandler(IInteractionOrchestrator orchestrator)
    : IRequestHandler<ProcessInteractionCommand, Result<InteractionResponse>>
{
    public async Task<Result<InteractionResponse>> Handle(
        ProcessInteractionCommand request,
        CancellationToken cancellationToken) =>
        Result.Success(InteractionResponseMapper.Map(
            await orchestrator.ProcessAsync(
                request.ChatEvent,
                request.RequestedMode,
                cancellationToken).ConfigureAwait(false)));
}

public sealed record ProcessDevelopmentInteractionCommand(
    string Provider,
    string ChannelId,
    string UserId,
    string UserDisplayName,
    string Message,
    string ResponseMode) : IRequest<Result<InteractionResponse>>;

public sealed class ProcessDevelopmentInteractionCommandValidator
    : AbstractValidator<ProcessDevelopmentInteractionCommand>
{
    public ProcessDevelopmentInteractionCommandValidator(IOptions<InteractionOptions> options)
    {
        RuleFor(command => command.Provider)
            .Must(value => string.Equals(value, "Development", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Provider must be Development.");
        RuleFor(command => command.ChannelId).NotEmpty().MaximumLength(512);
        RuleFor(command => command.UserId).NotEmpty().MaximumLength(512);
        RuleFor(command => command.UserDisplayName).NotEmpty().MaximumLength(512);
        RuleFor(command => command.Message).NotEmpty().MaximumLength(options.Value.MaxMessageCharacters);
        RuleFor(command => command.ResponseMode)
            .Must(value => Enum.TryParse<InteractionResponseMode>(value, true, out var mode) &&
                           mode is not InteractionResponseMode.None)
            .WithMessage("ResponseMode must be Text, Voice, or TextAndVoice.");
    }
}

public sealed class ProcessDevelopmentInteractionCommandHandler(ISender sender, TimeProvider timeProvider)
    : IRequestHandler<ProcessDevelopmentInteractionCommand, Result<InteractionResponse>>
{
    public Task<Result<InteractionResponse>> Handle(
        ProcessDevelopmentInteractionCommand request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = new LiveChatUser(
            LiveChatProviderType.Development,
            request.UserId,
            request.UserId,
            request.UserDisplayName,
            false,
            false,
            false,
            false,
            false,
            []);
        var chatEvent = new LiveChatEvent(
            Guid.NewGuid(),
            LiveChatEventType.Message,
            LiveChatProviderType.Development,
            $"dev-{Guid.NewGuid():N}",
            request.ChannelId,
            request.ChannelId,
            user,
            request.Message,
            now,
            now,
            0,
            Guid.NewGuid().ToString("N"),
            new Dictionary<string, string?>());
        var mode = Enum.Parse<InteractionResponseMode>(request.ResponseMode, true);
        return sender.Send(new ProcessInteractionCommand(chatEvent, mode), cancellationToken);
    }
}
