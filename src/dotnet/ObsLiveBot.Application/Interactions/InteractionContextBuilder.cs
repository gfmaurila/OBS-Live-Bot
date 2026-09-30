using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;
using System.Text.RegularExpressions;

namespace ObsLiveBot.Application.Interactions;

public sealed class InteractionContextBuilder(
    IOptions<InteractionOptions> options,
    IInteractionBuffer buffer) : IInteractionContextBuilder
{
    private readonly InteractionOptions _options = options.Value;

    public AiInteractionRequest Build(LiveChatEvent chatEvent, InteractionDecision decision)
    {
        var context = (_options.ContextMessageLimit == 0
                ? []
            : buffer.GetRecent(_options.ContextMessageLimit))
            .Where(item => item.Status == InteractionStatus.Completed &&
                           item.Decision.Provider == chatEvent.Provider &&
                           string.Equals(item.Decision.ChannelId, chatEvent.ChannelId, StringComparison.Ordinal) &&
                           !string.IsNullOrWhiteSpace(item.ResponseText))
            .Select(item => item.ResponseText!)
            .Reverse()
            .ToArray();
        return new AiInteractionRequest(
            decision.DecisionId,
            chatEvent.Provider,
            chatEvent.ChannelName ?? chatEvent.ChannelId ?? string.Empty,
            chatEvent.User.UserId,
            decision.UserDisplayName,
            RemoveTrigger(chatEvent.Message ?? string.Empty),
            context,
            _options.SystemInstructions,
            decision.CorrelationId);
    }

    private string RemoveTrigger(string message)
    {
        var command = _options.ReservedCommandPrefix;
        if (message.StartsWith(command, StringComparison.OrdinalIgnoreCase))
            return message[command.Length..].Trim();

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
