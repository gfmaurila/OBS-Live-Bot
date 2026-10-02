using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

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
            ChatTriggerText.Strip(chatEvent.Message ?? string.Empty, _options),
            context,
            _options.SystemInstructions,
            decision.CorrelationId);
    }
}
