using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.Abstractions;

/// <summary>
/// Written chat replies. Separate from <see cref="NarrationOptions"/> and from the capture side of
/// <see cref="LiveChatOptions"/> on purpose: a viewer may hear the reply, read it, or both.
/// </summary>
public sealed class ChatResponseOptions
{
    public const string SectionName = "ChatResponses";

    /// <summary>
    /// The single switch that lets StudioOS write into a chat channel on its own. Ships off. An
    /// operator turns it on deliberately and turns it back off deliberately.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Name of the selected <see cref="IChatResponseSender"/>.</summary>
    public string Sender { get; set; } = "SocialStreamNinja";

    /// <summary>
    /// Optional extra restriction on the chat platforms that may receive a reply. Empty means "any
    /// platform the selected sender supports".
    /// </summary>
    public LiveChatProviderType[] AllowedProviders { get; set; } = [];

    /// <summary>Deterministic maximum length of the written text, counted in runes.</summary>
    public int MaxCharacters { get; set; } = 500;

    /// <summary>Prepended to every written reply. Empty means no prefix.</summary>
    public string MessagePrefix { get; set; } = string.Empty;

    public int MaxQueueSize { get; set; } = 4;

    public int GlobalCooldownSeconds { get; set; } = 8;

    public int UserCooldownSeconds { get; set; } = 30;

    public int CooldownCapacity { get; set; } = 500;

    public int IdempotencyCapacity { get; set; } = 500;

    /// <summary>
    /// How long a written reply is remembered as our own text. An incoming message that matches one is
    /// treated as the echo of that reply. Zero disables duplicate-text suppression and echo matching.
    /// </summary>
    public int EchoWindowSeconds { get; set; } = 120;

    public int EchoCapacity { get; set; } = 200;

    /// <summary>
    /// Accounts StudioOS posts from, in <c>Provider:UserId</c> form. A message from one of these is
    /// StudioOS's own and can never start another interaction, whatever its text happens to be.
    /// <para>
    /// This is the primary loop guard. Text matching is only a fallback for the first echo of a session,
    /// before an identity has been observed; once an echo has been seen the identity is learned and text
    /// is no longer consulted for that account.
    /// </para>
    /// </summary>
    public string[] SelfActorIdentities { get; set; } = [];

    /// <summary>Upper bound on learned and configured StudioOS actor identities.</summary>
    public int SelfActorCapacity { get; set; } = 100;

    public int HistoryCapacity { get; set; } = 100;

    /// <summary>Hard per-attempt ceiling for one platform write, independent of the HTTP client.</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Whether the deterministic <c>Development</c> sender may be selected at all. It never delivers
    /// anything, so it must stay an explicit choice and never a silent fallback.
    /// </summary>
    public bool AllowDevelopmentSender { get; set; } = true;
}

public sealed record ChatResponseSendRequest(
    Guid ChatResponseId,
    Guid? InteractionId,
    string? SourceMessageId,
    LiveChatProviderType Provider,
    string ChannelId,
    string? UserId,
    string? UserName,
    string Text,
    string CorrelationId,
    string IdempotencyKey);

public sealed record ChatResponseSendResult(
    bool Success,
    string? ErrorCode,
    TimeSpan Duration,
    bool IsSimulated,
    string? SourceId = null,
    int Attempt = 1,
    int MaxMessageCharacters = 0);

/// <summary>
/// The write boundary. Application knows only this contract; SSN, HTTP, DOM and per-platform transports
/// belong to Infrastructure, and capture never depends on this interface.
/// </summary>
public interface IChatResponseSender
{
    string Name { get; }

    /// <summary>True for senders that do not deliver anything to a real platform.</summary>
    bool IsDevelopment { get; }

    bool IsAvailable { get; }

    IReadOnlyList<LiveChatProviderType> SupportedProviders { get; }

    ChatResponseSenderRuntimeState GetRuntimeState();

    Task<ChatResponseSendResult> SendAsync(
        ChatResponseSendRequest request,
        CancellationToken cancellationToken);
}

public interface IChatResponseSenderRegistry
{
    IReadOnlyList<IChatResponseSender> GetSenders();

    IChatResponseSender? GetSelected();

    IChatResponseSender? Find(string? name);
}

/// <summary>
/// Bounded in-memory record of what StudioOS already wrote. It carries both identities the capability
/// needs: the idempotency keys of attempted writes and the texts of delivered ones. No persistence is
/// involved, so a restart forgets everything and simply starts clean.
/// </summary>
public interface IChatResponseLedger
{
    int IdempotencyCount { get; }

    int IdempotencyCapacity { get; }

    /// <summary>
    /// Claims a key before any platform write is attempted. False means this exact write was already
    /// attempted, so claiming is itself the suppression mechanism: no separate "committed" set exists.
    /// </summary>
    bool TryReserve(string idempotencyKey, DateTimeOffset now);

    /// <summary>
    /// Releases a claimed key so the same write may be attempted again. Used after a failed attempt,
    /// which keeps a transient platform error from permanently blocking a reply.
    /// </summary>
    void Forget(string idempotencyKey);

    /// <summary>Whether this exact text was already written on this channel inside the echo window.</summary>
    bool WasWritten(LiveChatProviderType provider, string channelId, string text, DateTimeOffset now);

    /// <summary>Remembers a delivered text so its own echo can be recognized.</summary>
    void RecordWrite(LiveChatProviderType provider, string channelId, string text, DateTimeOffset now);

    int EchoCount { get; }

    int EchoCapacity { get; }
}

public interface IChatResponseEventPublisher
{
    Task PublishAsync(ChatResponseEvent chatResponseEvent, CancellationToken cancellationToken);
}

/// <summary>
/// The accounts StudioOS speaks as.
///
/// This is the primary loop guard. A written reply returns through the same capture path as any viewer
/// message, so the reliable way to recognise it is the account identity that produced it - not the text,
/// which a viewer can trivially copy and which changes when a platform reflows or re-cases a line.
/// <para>
/// Identities are learned as well as configured. The first time an echo of a written reply is captured,
/// the account that produced it is recorded here; from then on that account is recognised by identity
/// alone, and matching text is no longer needed.
/// </para>
/// </summary>
public interface IChatResponseSelfIdentityRegistry
{
    int Count { get; }

    int Capacity { get; }

    /// <summary>Whether this account is one StudioOS posts from.</summary>
    bool IsStudioOsActor(LiveChatProviderType provider, string? userId, string? username = null);

    /// <summary>
    /// Records an account observed delivering one of our own replies. Returns false when it was already
    /// known, so the caller can tell a genuine new learning from a repeat.
    /// </summary>
    bool Learn(LiveChatProviderType provider, string? userId, string? username);

    IReadOnlyList<string> GetIdentities();
}

/// <summary>
/// Reports each platform's read and write capabilities. Implemented where both sides are visible - the
/// capture registry and the write transport - because neither side alone can answer the question.
/// </summary>
public interface IChatProviderCapabilityProvider
{
    Task<IReadOnlyList<ChatProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Written-reply settings with a deliberate runtime override.
/// <para>
/// The override exists so an operator can enable automatic written replies for one controlled validation
/// and then turn them off again, without editing shipped configuration and restarting the process. It is
/// held in memory only, so a restart always returns to the shipped value - which ships disabled. That is
/// the whole safety argument for this type: there is no state file that could carry "enabled" across a
/// reboot.
/// </para>
/// </summary>
public interface IChatResponseSettingsStore
{
    ChatResponseSettingsSnapshot Get();

    ChatResponseSettingsSnapshot Update(ChatResponseSettingsUpdate update);

    /// <summary>The override in force, or null when configuration governs. Never persisted.</summary>
    bool? OverriddenEnabled { get; }
}

/// <summary>
/// The write pipeline: gate, idempotency, bounded queue, single reader, sender selection, recording.
/// Failures inside it are represented in the recorded result and never surface to the caller.
/// </summary>
public interface IChatResponseWriter
{
    ChatResponseStateSnapshot GetState();

    IReadOnlyList<ChatResponseResult> GetRecent(int limit);

    /// <summary>Automatic path. Never throws and never blocks on the platform.</summary>
    Task<ChatResponseEnqueueResult> EnqueueAsync(ChatResponseIntent intent, CancellationToken cancellationToken);

    /// <summary>
    /// Development-only path. Runs the same gate, sender and recording inline so an operator can see
    /// the outcome of one controlled send. It is named separately so no automatic path can reach it.
    /// </summary>
    Task<ChatResponseEnqueueResult> ExecuteDirectAsync(ChatResponseIntent intent, CancellationToken cancellationToken);
}

public sealed record ChatResponseEnqueueResult(
    bool Accepted,
    ChatResponseResult? Result,
    string? Reason,
    ChatResponseStatus Status);
