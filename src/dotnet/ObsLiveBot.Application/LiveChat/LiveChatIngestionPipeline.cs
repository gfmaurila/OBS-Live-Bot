using FluentValidation;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;

namespace ObsLiveBot.Application.LiveChat;

public sealed class LiveChatIngestionPipeline(
    IValidator<ProviderLiveChatEvent> validator,
    ILiveChatEventNormalizer normalizer,
    ILiveChatDeduplicator deduplicator,
    IPublisher mediator) : ILiveChatIngestionPipeline
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _sequence;

    public async Task<LiveChatIngestionResult> IngestAsync(
        ProviderLiveChatEvent providerEvent,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(providerEvent, cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = normalizer.Normalize(providerEvent);
            if (!deduplicator.TryAccept(normalized))
            {
                return new LiveChatIngestionResult(false, true, null);
            }

            normalized = normalized with { Sequence = ++_sequence };
            await mediator.Publish(
                new LiveChatEventReceivedNotification(normalized),
                cancellationToken).ConfigureAwait(false);
            return new LiveChatIngestionResult(true, false, normalized);
        }
        finally
        {
            _gate.Release();
        }
    }
}
