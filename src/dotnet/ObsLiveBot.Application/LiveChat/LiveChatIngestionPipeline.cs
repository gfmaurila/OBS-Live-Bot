using FluentValidation;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Events;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.LiveChat;

/// <summary>
/// Normalizes, deduplicates and publishes one inbound chat message.
/// <para>
/// It also carries the loop guard, because this is the only place on the capture side that sees both the
/// message and its account. The guard is applied after deduplication and before publication, so nothing
/// downstream - the buffer, the interaction decision, the writer - has to know it exists.
/// </para>
/// </summary>
public sealed class LiveChatIngestionPipeline(
    IValidator<ProviderLiveChatEvent> validator,
    ILiveChatEventNormalizer normalizer,
    ILiveChatDeduplicator deduplicator,
    IPublisher mediator,
    IChatResponseLedger? chatResponseLedger = null,
    IChatResponseSelfIdentityRegistry? selfIdentities = null,
    TimeProvider? timeProvider = null) : ILiveChatIngestionPipeline
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
            normalized = TagStudioOsMessage(normalized);

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

    /// <summary>
    /// Marks a message as StudioOS's own so the interaction decision policy refuses it as
    /// <c>SelfMessage</c> and no new interaction - and therefore no new reply - can start from it.
    /// <para>
    /// Identity is the guard; text is only the bootstrap. An account StudioOS posts from is recognised by
    /// identity alone, which survives a viewer copying the reply verbatim or a platform reformatting it.
    /// The first time one of our replies comes back, the account that delivered it is learned here so that
    /// from then on the identity alone is enough and the text is never compared again for that account.
    /// </para>
    /// <para>
    /// Order matters: identity is checked first, so a learned account costs no text comparison at all, and
    /// text is consulted only for an account not yet known.
    /// </para>
    /// </summary>
    private LiveChatEvent TagStudioOsMessage(LiveChatEvent normalized)
    {
        if (selfIdentities is null) return normalized;

        try
        {
            var identityMatched = selfIdentities.IsStudioOsActor(
                normalized.Provider,
                normalized.User.UserId,
                normalized.User.Username ?? normalized.User.DisplayName);

            var echoMatched = false;
            if (!identityMatched && chatResponseLedger is not null &&
                !string.IsNullOrWhiteSpace(normalized.Message))
            {
                echoMatched = chatResponseLedger.WasWritten(
                    normalized.Provider,
                    normalized.ChannelId ?? string.Empty,
                    normalized.Message,
                    (timeProvider ?? TimeProvider.System).GetUtcNow());

                // Bootstrap: the account behind the first recognised echo becomes a known StudioOS actor,
                // so every later message from it is suppressed by identity rather than by matching text.
                if (echoMatched)
                    selfIdentities.Learn(
                        normalized.Provider,
                        normalized.User.UserId,
                        normalized.User.Username ?? normalized.User.DisplayName);
            }

            if (!identityMatched && !echoMatched)
                return normalized;

            var metadata = new Dictionary<string, string?>(normalized.Metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["interaction.generatedByStudioOS"] = "true",
                ["interaction.chatResponseEcho"] = echoMatched ? "true" : "false",
                ["interaction.chatResponseGuard"] = identityMatched ? "identity" : "text-echo"
            };
            return normalized with { Metadata = metadata };
        }
        catch (Exception)
        {
            // A loop guard that throws would let a written reply take capture down, which is worse than
            // the loop it prevents. Capture continues; the decision policy still refuses messages that
            // identify themselves as ours by identity.
            return normalized;
        }
    }
}
