using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.LiveChat;

public sealed class LiveChatEventNormalizer(TimeProvider timeProvider) : ILiveChatEventNormalizer
{
    private static readonly string[] ForbiddenMetadataTerms = ["password", "token", "secret", "credential", "oauth"];

    public LiveChatEvent Normalize(ProviderLiveChatEvent providerEvent)
    {
        var eventId = Guid.NewGuid();
        var metadata = providerEvent.Metadata
            .Where(item => !ForbiddenMetadataTerms.Any(term => item.Key.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

        return new LiveChatEvent(
            eventId,
            providerEvent.EventType,
            providerEvent.Provider,
            providerEvent.ProviderEventId,
            providerEvent.ChannelId,
            providerEvent.ChannelName,
            providerEvent.User,
            providerEvent.Message,
            providerEvent.TimestampUtc.ToUniversalTime(),
            timeProvider.GetUtcNow(),
            0,
            string.IsNullOrWhiteSpace(providerEvent.CorrelationId)
                ? eventId.ToString("N")
                : providerEvent.CorrelationId,
            metadata);
    }
}
