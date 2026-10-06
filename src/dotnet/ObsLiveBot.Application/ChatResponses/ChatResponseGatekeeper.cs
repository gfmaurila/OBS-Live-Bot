using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.ChatResponses;

/// <summary>A candidate written reply, before any gate has been applied to it.</summary>
public sealed record ChatResponseCandidate(
    LiveChatProviderType Provider,
    string? ChannelId,
    string? UserId,
    string? Text,
    Guid? InteractionId = null,
    string? CorrelationId = null,
    string? UserName = null,
    string? SourceMessageId = null);

/// <summary>The gate's verdict, and the intent to execute when the verdict is positive.</summary>
public sealed record ChatResponseAdmission(
    bool Allowed,
    string? Reason,
    ChatResponseIntent? Intent,
    int CharacterCount = 0,
    bool Truncated = false)
{
    public static ChatResponseAdmission Reject(string reason) => new(false, reason, null);
}

/// <summary>
/// Every condition that must hold before StudioOS writes anything into a chat channel.
///
/// The gate is deliberately exhaustive and ordered from cheapest and most fundamental to most
/// specific, so a rejection always names the first real reason. Its most important property is that it
/// returns a verdict instead of throwing: this code runs on the interaction completion path, and a
/// written-reply problem must never become an interaction failure.
/// </summary>
public sealed class ChatResponseGatekeeper(
    IOptions<ChatResponseOptions> configuredOptions,
    IChatResponseSettingsStore settings,
    IChatResponseSenderRegistry registry,
    IChatResponseLedger ledger,
    IChatResponseSelfIdentityRegistry selfIdentities,
    ChatResponseCooldownTracker cooldowns,
    TimeProvider timeProvider,
    ILogger<ChatResponseGatekeeper> logger)
{
    /// <summary>
    /// Applies every gate in order. Never throws.
    /// </summary>
    public ChatResponseAdmission Admit(ChatResponseCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var options = configuredOptions.Value;

        if (!settings.Get().Enabled)
            return ChatResponseAdmission.Reject("WRITTEN_RESPONSES_DISABLED");

        var sender = registry.GetSelected();
        if (sender is null)
            return ChatResponseAdmission.Reject("CHAT_SENDER_NOT_SELECTED");

        if (sender.IsDevelopment && !options.AllowDevelopmentSender)
            return ChatResponseAdmission.Reject("DEVELOPMENT_SENDER_NOT_ALLOWED");

        if (!sender.IsAvailable)
            return ChatResponseAdmission.Reject("CHAT_SENDER_UNAVAILABLE");

        if (!sender.SupportedProviders.Contains(candidate.Provider))
            return ChatResponseAdmission.Reject("PROVIDER_NOT_SUPPORTED");

        if (options.AllowedProviders.Length > 0 && !options.AllowedProviders.Contains(candidate.Provider))
            return ChatResponseAdmission.Reject("PROVIDER_NOT_ALLOWED");

        var channel = ChatResponsePolicy.NormalizeChannel(candidate.ChannelId);

        // Second line of the loop guard. The decision policy already refuses messages that identify
        // themselves as ours, so reaching here with a StudioOS actor means something upstream changed its
        // mind. Refusing here is what guarantees the written-reply capability itself can never answer
        // itself, independently of the interaction path.
        if (selfIdentities.IsStudioOsActor(candidate.Provider, candidate.UserId, candidate.UserName))
            return ChatResponseAdmission.Reject("STUDIOOS_OWN_MESSAGE");

        var prepared = ChatResponsePolicy.PrepareText(candidate.Text, options.MessagePrefix, options.MaxCharacters);
        var domainAdmission = ChatResponsePolicy.Evaluate(true, sender.Name, channel, prepared);
        if (!domainAdmission.Allowed)
            return ChatResponseAdmission.Reject(domainAdmission.Reason!);

        var text = prepared.Text!;
        var now = timeProvider.GetUtcNow();

        // Duplicate text inside the echo window is almost always our own reply coming back at us, or
        // the same canned answer about to be posted twice. Rejecting it here also means the reply we
        // already wrote is never re-recorded, which keeps the echo ring stable.
        if (ledger.WasWritten(candidate.Provider, channel, text, now))
            return ChatResponseAdmission.Reject("DUPLICATE_TEXT");

        var cooldown = cooldowns.Check(candidate.Provider, channel, candidate.UserId, now);
        if (!cooldown.Accepted)
            return ChatResponseAdmission.Reject(cooldown.Reason!);

        var chatResponseId = candidate.InteractionId ?? Guid.NewGuid();
        var idempotencyKey = ChatResponsePolicy.ComposeIdempotencyKey(candidate.Provider, channel, chatResponseId);

        // The key is claimed last, after every other gate passed, so a reply refused for a transient
        // reason keeps its identity available for a later explicit retry.
        if (!ledger.TryReserve(idempotencyKey, now))
            return ChatResponseAdmission.Reject("DUPLICATE_WRITE");

        cooldowns.Commit(candidate.Provider, channel, candidate.UserId, now);

        var intent = new ChatResponseIntent(
            chatResponseId,
            candidate.InteractionId,
            candidate.SourceMessageId,
            candidate.Provider,
            channel,
            candidate.UserId,
            candidate.UserName,
            text,
            idempotencyKey,
            candidate.CorrelationId ?? chatResponseId.ToString("N"),
            now);

        if (prepared.Truncated)
            logger.LogInformation(
                "Chat response {ChatResponseId} truncated to {MaxCharacters} characters before write",
                chatResponseId,
                options.MaxCharacters);

        return new ChatResponseAdmission(true, null, intent, text.Length, prepared.Truncated);
    }

    /// <summary>
    /// Derives a candidate from a completed interaction. Only a decided-to-respond, successfully
    /// completed interaction that actually produced text is eligible; everything else is rejected here
    /// so a failure is never dressed up as a written reply.
    /// </summary>
    public ChatResponseCandidate CandidateFrom(InteractionResult result) =>
        new(
            result.Decision.Provider,
            result.Decision.ChannelId,
            result.Decision.UserId,
            result.ResponseText,
            result.InteractionId,
            result.CorrelationId,
            result.Decision.UserDisplayName,
            result.Decision.ProviderMessageId);

    /// <summary>
    /// The single source of truth for a written reply's eligibility. Only <c>Respond</c> reaches the
    /// gate: every other decision - <c>Ignore</c> for a missing trigger, a self message, a duplicate, a
    /// bot, an invalid or unsupported provider, or a failed validation - is already excluded here.
    /// </summary>
    public static bool IsEligible(InteractionResult result) =>
        result.Decision.DecisionType == InteractionDecisionType.Respond &&
        result.Status == InteractionStatus.Completed &&
        string.IsNullOrWhiteSpace(result.ErrorCode) &&
        !string.IsNullOrWhiteSpace(result.ResponseText);
}