using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>
/// A sender that reports success without delivering anything to any platform.
///
/// It exists so the whole written-reply pipeline - gate, idempotency, queue, result recording, health -
/// can be exercised on a machine with no live chat at all. It is never a fallback: it reports success
/// only when an operator explicitly selects it by name, because a sender that silently degrades to
/// "looks like it worked" is the one thing a chat write must never be.
/// </summary>
public sealed class DevelopmentChatResponseSender(
    TimeProvider timeProvider,
    ILogger<DevelopmentChatResponseSender> logger) : IChatResponseSender
{
    private readonly object _counters = new();
    private long _requests;
    private long _successes;
    private DateTimeOffset? _lastSuccessAtUtc;

    public string Name => "Development";

    public bool IsDevelopment => true;

    public bool IsAvailable => true;

    public IReadOnlyList<LiveChatProviderType> SupportedProviders =>
    [
        LiveChatProviderType.Twitch,
        LiveChatProviderType.YouTube,
        LiveChatProviderType.Kick,
        LiveChatProviderType.TikTok
    ];

    /// <summary>
    /// Failures stay at zero by construction: this sender has exactly one outcome. Reporting a counter it
    /// can never increment would be noise, and reporting zeroes is honest rather than a limitation.
    /// </summary>
    public ChatResponseSenderRuntimeState GetRuntimeState()
    {
        lock (_counters)
        {
            return new ChatResponseSenderRuntimeState(
                IsAvailable,
                IsAvailable ? "Development" : "Unavailable",
                _requests,
                _successes,
                Failures: 0,
                LastSuccessAtUtc: _lastSuccessAtUtc,
                LastFailureAtUtc: null,
                LastErrorCode: null);
        }
    }

    public Task<ChatResponseSendResult> SendAsync(
        ChatResponseSendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var now = timeProvider.GetUtcNow();

        lock (_counters)
        {
            _requests++;
            _successes++;
            _lastSuccessAtUtc = now;
        }

        logger.LogInformation(
            "Development written reply {ChatResponseId} for {Provider} channel {ChannelId} ({Characters} characters) was recorded but not delivered",
            request.ChatResponseId, request.Provider, request.ChannelId, request.Text.Length);

        return Task.FromResult(new ChatResponseSendResult(
            Success: true,
            ErrorCode: null,
            Duration: stopwatch.Elapsed,
            IsSimulated: true,
            SourceId: null,
            Attempt: 1));
    }
}
