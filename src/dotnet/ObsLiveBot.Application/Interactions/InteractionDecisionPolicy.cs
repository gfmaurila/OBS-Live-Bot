using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;
using System.Text.RegularExpressions;

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
            chatEvent.CorrelationId,
            chatEvent.ProviderEventId);
    }

    private (InteractionDecisionType Type, string Reason, InteractionResponseMode Mode) Evaluate(
        LiveChatEvent chatEvent,
        InteractionResponseMode? requestedMode,
        InteractionResponseMode resolvedMode)
    {
        if (!_options.Enabled)
            return (InteractionDecisionType.Ignore, "InteractionsDisabled", InteractionResponseMode.None);
        if (requestedMode is not null &&
            chatEvent.Metadata.TryGetValue("interaction.suppressed", out var suppressed) &&
            string.Equals(suppressed, "true", StringComparison.OrdinalIgnoreCase))
            return (InteractionDecisionType.Ignore, "ExternalCaptureAutoResponseDisabled", InteractionResponseMode.None);
        if (chatEvent.EventType != LiveChatEventType.Message)
            return (InteractionDecisionType.Ignore, "UnsupportedEventType", InteractionResponseMode.None);
        if (chatEvent.Provider == LiveChatProviderType.Unknown || !Enum.IsDefined(chatEvent.Provider) ||
            chatEvent.User.Provider != chatEvent.Provider)
            return (InteractionDecisionType.Ignore, "InvalidProvider", InteractionResponseMode.None);
        if (string.IsNullOrWhiteSpace(chatEvent.ChannelId) || string.IsNullOrWhiteSpace(chatEvent.User.UserId))
            return (InteractionDecisionType.Ignore, "MissingIdentity", InteractionResponseMode.None);
        if (chatEvent.User.IsBot || _selfIdentities.Contains(chatEvent.User.Identity))
            return (InteractionDecisionType.Ignore, chatEvent.User.IsBot ? "BotMessage" : "SelfMessage", InteractionResponseMode.None);
        if (IsGeneratedOrSelf(chatEvent))
            return (InteractionDecisionType.Ignore, "SelfMessage", InteractionResponseMode.None);
        if (string.IsNullOrWhiteSpace(chatEvent.Message))
            return (InteractionDecisionType.Ignore, "EmptyMessage", InteractionResponseMode.None);
        if (chatEvent.Message.Length > _options.MaxMessageCharacters)
            return (InteractionDecisionType.Ignore, "MessageTooLong", InteractionResponseMode.None);
        if (requestedMode is not null && requestedMode != InteractionResponseMode.None)
            return (InteractionDecisionType.Respond, "ExplicitDevelopmentRequest", resolvedMode);
        if (chatEvent.Message.StartsWith(_options.ReservedCommandPrefix, StringComparison.OrdinalIgnoreCase) &&
            (chatEvent.Message.Length == _options.ReservedCommandPrefix.Length ||
             char.IsWhiteSpace(chatEvent.Message[_options.ReservedCommandPrefix.Length])))
        {
            if (string.IsNullOrWhiteSpace(chatEvent.Message[_options.ReservedCommandPrefix.Length..]))
                return (InteractionDecisionType.Ignore, "EmptyTriggerContent", InteractionResponseMode.None);
            return CanAutomaticallyRespond(chatEvent)
                ? (InteractionDecisionType.Respond, "CommandTrigger", InteractionResponseMode.Voice)
                : (InteractionDecisionType.Ignore, "AutoPlayDisabled", InteractionResponseMode.None);
        }
        var mentioned = _options.BotMentionTriggers.Any(trigger => ContainsMention(chatEvent.Message, trigger));
        if (mentioned)
        {
            if (string.IsNullOrWhiteSpace(RemoveMentions(chatEvent.Message)))
                return (InteractionDecisionType.Ignore, "EmptyTriggerContent", InteractionResponseMode.None);
            return CanAutomaticallyRespond(chatEvent)
                ? (InteractionDecisionType.Respond, "BotMention", InteractionResponseMode.Voice)
                : (InteractionDecisionType.Ignore, "AutoPlayDisabled", InteractionResponseMode.None);
        }
        if (chatEvent.Message.StartsWith('!'))
            return (InteractionDecisionType.Ignore, "UnrecognizedReservedCommand", InteractionResponseMode.None);
        return (InteractionDecisionType.Ignore, "NoTrigger", InteractionResponseMode.None);
    }

    private bool IsGeneratedOrSelf(LiveChatEvent chatEvent) =>
        (chatEvent.Metadata.TryGetValue("interaction.generatedByStudioOS", out var generated) &&
         string.Equals(generated, "true", StringComparison.OrdinalIgnoreCase)) ||
        (chatEvent.Metadata.TryGetValue("interaction.isSelf", out var self) &&
         string.Equals(self, "true", StringComparison.OrdinalIgnoreCase));

    private bool CanAutomaticallyRespond(LiveChatEvent chatEvent) =>
        _options.AutoPlayInteractions &&
        !(chatEvent.Metadata.TryGetValue("interaction.autoPlayAtReceipt", out var enabledAtReceipt) &&
          string.Equals(enabledAtReceipt, "false", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsMention(string message, string? trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger)) return false;
        return Regex.IsMatch(message, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(trigger)}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    }

    private string RemoveMentions(string message)
    {
        foreach (var mention in _options.BotMentionTriggers.OrderByDescending(value => value.Length))
        {
            if (string.IsNullOrWhiteSpace(mention)) continue;
            var pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(mention)}(?![\p{{L}}\p{{N}}_])[:,]?\s*";
            message = Regex.Replace(message, pattern, string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(50));
        }
        return message.Trim();
    }
}
