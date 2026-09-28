using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Interactions;

public sealed class InteractionDecisionPolicy(
    IOptions<InteractionOptions> options,
    TimeProvider timeProvider) : IInteractionDecisionPolicy
{
    private readonly InteractionOptions _options = options.Value;
    private readonly HashSet<string> _selfIdentities = new(
        options.Value.SelfIdentities,
        StringComparer.OrdinalIgnoreCase);

    public InteractionDecision Decide(
        LiveChatEvent chatEvent,
        InteractionResponseMode? requestedMode,
        long sequence)
    {
        ArgumentNullException.ThrowIfNull(chatEvent);
        var mode = requestedMode ?? _options.DefaultResponseMode;
        var (type, reason, responseMode) = Evaluate(chatEvent, requestedMode, mode);
        return new InteractionDecision(
            Guid.NewGuid(),
            chatEvent.EventId,
            chatEvent.Provider,
            chatEvent.ChannelId ?? string.Empty,
            chatEvent.User.UserId,
            chatEvent.User.DisplayName ?? chatEvent.User.Username ?? chatEvent.User.UserId,
            type,
            reason,
            responseMode,
            timeProvider.GetUtcNow(),
            sequence,
            chatEvent.CorrelationId);
    }

    private (InteractionDecisionType Type, string Reason, InteractionResponseMode Mode) Evaluate(
        LiveChatEvent chatEvent,
        InteractionResponseMode? requestedMode,
        InteractionResponseMode resolvedMode)
    {
        if (!_options.Enabled)
            return (InteractionDecisionType.Ignore, "InteractionsDisabled", InteractionResponseMode.None);
        if (chatEvent.EventType != LiveChatEventType.Message)
            return (InteractionDecisionType.Ignore, "UnsupportedEventType", InteractionResponseMode.None);
        if (chatEvent.Provider == LiveChatProviderType.Unknown)
            return (InteractionDecisionType.Ignore, "UnknownProvider", InteractionResponseMode.None);
        if (string.IsNullOrWhiteSpace(chatEvent.ChannelId) || string.IsNullOrWhiteSpace(chatEvent.User.UserId))
            return (InteractionDecisionType.Ignore, "MissingIdentity", InteractionResponseMode.None);
        if (chatEvent.User.IsBot || _selfIdentities.Contains(chatEvent.User.Identity))
            return (InteractionDecisionType.Ignore, "SelfOrBotMessage", InteractionResponseMode.None);
        if (string.IsNullOrWhiteSpace(chatEvent.Message))
            return (InteractionDecisionType.Ignore, "EmptyMessage", InteractionResponseMode.None);
        if (chatEvent.Message.Length > _options.MaxMessageCharacters)
            return (InteractionDecisionType.Ignore, "MessageTooLong", InteractionResponseMode.None);
        if (requestedMode is not null && requestedMode != InteractionResponseMode.None)
            return (InteractionDecisionType.Respond, "ExplicitDevelopmentRequest", resolvedMode);
        if (string.Equals(chatEvent.Message, _options.ReservedCommandPrefix, StringComparison.OrdinalIgnoreCase) ||
            chatEvent.Message.StartsWith($"{_options.ReservedCommandPrefix} ", StringComparison.OrdinalIgnoreCase))
            return (InteractionDecisionType.Respond, "ReservedCommand", resolvedMode);
        if (chatEvent.Message.StartsWith('!'))
            return (InteractionDecisionType.Ignore, "UnrecognizedReservedCommand", InteractionResponseMode.None);
        return (InteractionDecisionType.Ignore, "NoResponseTrigger", InteractionResponseMode.None);
    }
}
