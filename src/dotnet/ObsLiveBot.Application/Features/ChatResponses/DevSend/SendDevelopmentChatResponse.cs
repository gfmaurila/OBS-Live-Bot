using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Contracts.Chat;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.Features.ChatResponses.DevSend;

/// <summary>
/// Writes one operator-supplied line into a chat channel through the real production gate, sender and
/// recording path.
///
/// It exists because written replies ship disabled and automatic replies need a live audience to
/// trigger. This is the only way to verify a real platform write without enabling automatic replies and
/// without a viewer present. It is deliberately reachable only from the development endpoint, and it
/// leaves every configuration value exactly as it found them.
/// </summary>
public sealed record SendDevelopmentChatResponseCommand(
    string Provider,
    string ChannelId,
    string Text) : IRequest<DevelopmentChatResponseResponse>;

public sealed class SendDevelopmentChatResponseCommandValidator : AbstractValidator<SendDevelopmentChatResponseCommand>
{
    public SendDevelopmentChatResponseCommandValidator(IOptions<ChatResponseOptions> options)
    {
        var limit = Math.Min(options.Value.MaxCharacters, 2000);
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(64);
        RuleFor(command => command.ChannelId).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Text).NotEmpty().MaximumLength(limit);
    }
}

public sealed class SendDevelopmentChatResponseCommandHandler(
    IChatResponseSettingsStore settings,
    ChatResponseGatekeeper gatekeeper,
    IChatResponseWriter writer)
    : IRequestHandler<SendDevelopmentChatResponseCommand, DevelopmentChatResponseResponse>
{
    public async Task<DevelopmentChatResponseResponse> Handle(
        SendDevelopmentChatResponseCommand request,
        CancellationToken cancellationToken)
    {
        // A development send still obeys the master switch. If it did not, enabling the capability would
        // be optional for the operator, and a gate that can be side-stepped is not a gate.
        if (!settings.Get().Enabled)
            return Refused("WRITTEN_RESPONSES_DISABLED");

        if (!TryParseProvider(request.Provider, out var provider))
            return Refused("PROVIDER_UNKNOWN");

        var chatResponseId = Guid.NewGuid();
        var admission = gatekeeper.Admit(new ChatResponseCandidate(
            provider,
            request.ChannelId,
            UserId: null,
            request.Text,
            InteractionId: null,
            CorrelationId: chatResponseId.ToString("N"),
            UserName: null,
            // Stamped so this line can never be confused with a reply to a real viewer message, in the
            // history, in the logs or in the platform chat itself.
            SourceMessageId: "development-validation"));

        if (!admission.Allowed || admission.Intent is null)
            return Refused(admission.Reason ?? "REJECTED");

        var outcome = await writer.ExecuteDirectAsync(admission.Intent, cancellationToken)
            .ConfigureAwait(false);
        var result = outcome.Result;

        if (result is null)
            return Refused(outcome.Reason ?? "CHAT_SEND_UNREPORTED");

        return new DevelopmentChatResponseResponse(
            result.Status == ChatResponseStatus.Sent,
            result.ChatResponseId,
            result.Provider.ToString(),
            result.ChannelId,
            result.SenderName,
            result.Status.ToString(),
            result.Reason,
            result.Simulated,
            result.DurationMilliseconds,
            admission.CharacterCount,
            admission.Truncated,
            Origin: "development-validation",
            result.Attempt);
    }

    private static DevelopmentChatResponseResponse Refused(string reason) =>
        new(false, Guid.Empty, string.Empty, string.Empty, "none", ChatResponseStatus.Skipped.ToString(),
            reason, false, null, 0, false, "development-validation", 0);

    private static bool TryParseProvider(string value, out LiveChatProviderType provider)
    {
        if (Enum.TryParse(value, ignoreCase: true, out provider) && provider != LiveChatProviderType.Unknown)
            return true;
        provider = LiveChatProviderType.Unknown;
        return false;
    }
}
