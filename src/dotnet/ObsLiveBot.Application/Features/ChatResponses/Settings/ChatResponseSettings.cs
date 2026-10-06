using FluentValidation;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Chat;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.Features.ChatResponses.Settings;

/// <summary>Current written-reply settings, including whether a runtime override is in force.</summary>
public sealed record GetChatResponseSettingsQuery : IRequest<ChatResponseSettingsResponse>;

/// <summary>
/// Turns automatic written replies on or off for the running process.
/// <para>
/// This is the only runtime-mutable written-reply setting, deliberately. Sender selection, provider
/// allow-lists, cooldowns and the queue ceiling stay startup facts, so nothing an operator can change
/// while a stream is live can alter what a reply is allowed to contain or which platform it may reach.
/// </para>
/// <para>
/// The change is in memory only. Restarting the process returns the capability to the configured value,
/// which ships disabled, so a stream cannot be left with automatic replies silently on.
/// </para>
/// </summary>
public sealed record UpdateChatResponseSettingsCommand(
    bool? Enabled = null,
    bool Reset = false) : IRequest<ChatResponseSettingsResponse>;

public sealed class UpdateChatResponseSettingsCommandValidator
    : AbstractValidator<UpdateChatResponseSettingsCommand>
{
    public UpdateChatResponseSettingsCommandValidator()
    {
        RuleFor(command => command)
            .Must(command => command.Reset || command.Enabled.HasValue)
            .WithMessage("Provide 'enabled' to change the master switch, or 'reset' to return to the configured value.");
    }
}

public sealed class GetChatResponseSettingsQueryHandler(IChatResponseSettingsStore settings)
    : IRequestHandler<GetChatResponseSettingsQuery, ChatResponseSettingsResponse>
{
    public Task<ChatResponseSettingsResponse> Handle(
        GetChatResponseSettingsQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult(ChatResponseSettingsMapper.Map(settings.Get()));
}

public sealed class UpdateChatResponseSettingsCommandHandler(IChatResponseSettingsStore settings)
    : IRequestHandler<UpdateChatResponseSettingsCommand, ChatResponseSettingsResponse>
{
    public Task<ChatResponseSettingsResponse> Handle(
        UpdateChatResponseSettingsCommand request,
        CancellationToken cancellationToken) =>
        Task.FromResult(ChatResponseSettingsMapper.Map(
            settings.Update(new ChatResponseSettingsUpdate(request.Enabled, request.Reset))));
}

internal static class ChatResponseSettingsMapper
{
    public static ChatResponseSettingsResponse Map(ChatResponseSettingsSnapshot snapshot) =>
        new(
            snapshot.Enabled,
            snapshot.ConfiguredEnabled,
            snapshot.Overridden,
            snapshot.Source);
}