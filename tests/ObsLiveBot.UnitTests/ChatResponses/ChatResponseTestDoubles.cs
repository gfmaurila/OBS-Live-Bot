using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.UnitTests.ChatResponses;

/// <summary>A sender that reports a fixed outcome and counts what it was asked to do.</summary>
internal sealed class StubChatResponseSender : IChatResponseSender
{
    private readonly object _gate = new();

    public string Name { get; init; } = "Stub";

    public bool IsDevelopment { get; init; }

    public bool IsAvailable { get; set; } = true;

    public IReadOnlyList<LiveChatProviderType> SupportedProviders { get; init; } =
        [LiveChatProviderType.Twitch, LiveChatProviderType.YouTube, LiveChatProviderType.Kick];

    public bool Success { get; set; } = true;

    public string? ErrorCode { get; set; }

    public bool IsSimulated { get; set; } = true;

    public Func<ChatResponseSendRequest, CancellationToken, Task<ChatResponseSendResult>>? Behaviour
    {
        get;
        set;
    }

    /// <summary>Signalled the first time a reply reaches this sender, so queue tests need no sleep.</summary>
    public TaskCompletionSource? Entered { get; init; }

    public List<ChatResponseSendRequest> Requests { get; } = [];

    public int RequestCount
    {
        get { lock (_gate) return Requests.Count; }
    }

    public Task<ChatResponseSendResult> SendAsync(
        ChatResponseSendRequest request,
        CancellationToken cancellationToken)
    {
        lock (_gate) Requests.Add(request);
        Entered?.TrySetResult();
        if (Behaviour is not null) return Behaviour(request, cancellationToken);
        return Task.FromResult(new ChatResponseSendResult(
            Success, ErrorCode, TimeSpan.FromMilliseconds(1), IsSimulated, "source-1"));
    }

    public ChatResponseSenderRuntimeState GetRuntimeState() =>
        new(IsAvailable, IsAvailable ? "Available" : "Unavailable", RequestCount, 0, 0, null, null, null);
}

internal sealed class StubChatResponseSenderRegistry(IChatResponseSender[] senders) : IChatResponseSenderRegistry
{
    public string SelectedName { get; set; } = "Stub";

    public IReadOnlyList<IChatResponseSender> GetSenders() => senders;

    public IChatResponseSender? GetSelected() => Find(SelectedName);

    public IChatResponseSender? Find(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : senders.FirstOrDefault(sender =>
                string.Equals(sender.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A settings store whose effective value a test decides directly.</summary>
internal sealed class StubChatResponseSettingsStore(bool configuredEnabled = true) : IChatResponseSettingsStore
{
    public bool ConfiguredEnabled { get; set; } = configuredEnabled;

    public bool? OverriddenEnabled { get; private set; }

    public ChatResponseSettingsSnapshot Get() =>
        OverriddenEnabled is { } value
            ? new ChatResponseSettingsSnapshot(value, ConfiguredEnabled, true, "RuntimeOverride")
            : new ChatResponseSettingsSnapshot(ConfiguredEnabled, ConfiguredEnabled, false, "Configuration");

    public ChatResponseSettingsSnapshot Update(ChatResponseSettingsUpdate update)
    {
        if (update.Reset) OverriddenEnabled = null;
        else if (update.Enabled.HasValue) OverriddenEnabled = update.Enabled;
        return Get();
    }
}

/// <summary>Clock whose value only moves when a test moves it.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}

internal sealed class StubChatResponseEventPublisher : IChatResponseEventPublisher
{
    public List<ChatResponseEvent> Events { get; } = [];

    public bool Throw { get; set; }

    public Task PublishAsync(ChatResponseEvent chatResponseEvent, CancellationToken cancellationToken)
    {
        if (Throw) throw new InvalidOperationException("event publisher failed");
        Events.Add(chatResponseEvent);
        return Task.CompletedTask;
    }
}

internal sealed class CollectingPublisher : MediatR.IPublisher
{
    private readonly object _gate = new();

    public List<MediatR.INotification> Notifications { get; } = [];

    public int Count
    {
        get { lock (_gate) return Notifications.Count; }
    }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        lock (_gate) Notifications.Add((MediatR.INotification)notification);
        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : MediatR.INotification =>
        Publish((object)notification, cancellationToken);
}

internal static class ChatResponseTestFactory
{
    public static ChatResponseOptions Options(Action<ChatResponseOptions>? configure = null)
    {
        var options = new ChatResponseOptions
        {
            Enabled = true,
            Sender = "Stub",
            MaxCharacters = 500,
            GlobalCooldownSeconds = 0,
            UserCooldownSeconds = 0,
            EchoWindowSeconds = 120,
            CommandTimeoutSeconds = 1,
            HistoryCapacity = 10,
            MaxQueueSize = 4
        };
        configure?.Invoke(options);
        return options;
    }

    public static ChatResponseLedger Ledger(Action<ChatResponseOptions>? configure = null) =>
        new(Microsoft.Extensions.Options.Options.Create(Options(configure)));

    public static ChatResponseCooldownTracker Cooldowns(Action<ChatResponseOptions>? configure = null) =>
        new(Microsoft.Extensions.Options.Options.Create(Options(configure)));

    public static ChatResponseSelfIdentityRegistry SelfIdentities(
        Action<ChatResponseOptions>? configure = null) =>
        new(Microsoft.Extensions.Options.Options.Create(Options(configure)));

    public static ChatResponseGatekeeper Gatekeeper(
        ChatResponseOptions options,
        IChatResponseSenderRegistry registry,
        IChatResponseLedger ledger,
        ChatResponseCooldownTracker cooldowns,
        TimeProvider timeProvider,
        IChatResponseSelfIdentityRegistry? selfIdentities = null,
        IChatResponseSettingsStore? settings = null) =>
        new(
            Microsoft.Extensions.Options.Options.Create(options),
            settings ?? new StubChatResponseSettingsStore(options.Enabled),
            registry,
            ledger,
            selfIdentities ?? SelfIdentities(o =>
            {
                o.MaxCharacters = options.MaxCharacters;
                o.MessagePrefix = options.MessagePrefix;
            }),
            cooldowns,
            timeProvider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatResponseGatekeeper>.Instance);

    public static ChatResponseCandidate Candidate(
        string text = "boa noite",
        LiveChatProviderType provider = LiveChatProviderType.Twitch,
        string channel = "channel",
        string? userId = "user",
        Guid? interactionId = null,
        string? userName = null,
        string? sourceMessageId = null) =>
        new(provider, channel, userId, text, interactionId, "correlation", userName, sourceMessageId);

    public static InteractionResult Interaction(
        string? responseText = "boa noite",
        InteractionDecisionType decisionType = InteractionDecisionType.Respond,
        InteractionStatus status = InteractionStatus.Completed,
        string? errorCode = null,
        LiveChatProviderType provider = LiveChatProviderType.Twitch,
        string channel = "channel",
        string userId = "user",
        Guid? interactionId = null,
        string? userName = null,
        string? sourceMessageId = null)
    {
        var now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        return new InteractionResult(
            interactionId ?? Guid.NewGuid(),
            new InteractionDecision(
                Guid.NewGuid(),
                Guid.NewGuid(),
                provider,
                channel,
                userId,
                userName ?? userId,
                decisionType,
                "CommandTrigger",
                InteractionResponseMode.Voice,
                now,
                1,
                "correlation",
                sourceMessageId),
            status,
            responseText,
            AiProviderName: null,
            AiModelName: null,
            AiSuccess: null,
            AiDuration: null,
            AiFallbackUsed: false,
            PrimaryAiErrorCode: null,
            TtsProviderName: null,
            TtsSuccess: null,
            AudioFormat: null,
            AudioPath: null,
            TtsDuration: null,
            ErrorCode: errorCode,
            CreatedAtUtc: now,
            CompletedAtUtc: now,
            Sequence: 1,
            CorrelationId: "correlation");
    }
}
