using ObsLiveBot.Domain.Interactions;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Application.Abstractions;

public sealed class NarrationOptions
{
    public const string SectionName = "Narration";
    public bool Enabled { get; set; } = true;
    public bool AutoPlayInteractions { get; set; }
    public string SourceName { get; set; } = "GFM StudioOS - Narration";
    public int MaxQueueSize { get; set; } = 5;
    public int MaxConcurrentPlayback { get; set; } = 1;
    public int MaxNarrationSeconds { get; set; } = 15;
    public int PlaybackTimeoutSeconds { get; set; } = 25;
    public int StartTimeoutSeconds { get; set; } = 5;
    public int PollIntervalMilliseconds { get; set; } = 100;
    public double DefaultVolume { get; set; } = 70;
    public double MinimumVolume { get; set; } = 0;
    public double MaximumVolume { get; set; } = 100;
    public string MonitoringMode { get; set; } = "MonitorOff";
    public string AllowedRuntimeDirectory { get; set; } = "/app/data/runtime/tts";
    public string HostRuntimeDirectory { get; set; } = "D:/OBS-Live/OBS-Live-Bot/data/runtime/tts";
    public int EventBufferCapacity { get; set; } = 100;

    /// <summary>
    /// How long an accepted interaction may hold its admission slot while its own audio is still
    /// being produced, before the slot is force-closed and the following interactions are released.
    /// This is what stops one hung AI or TTS call from stalling narration forever; the abandoned
    /// group loses only its remaining role.
    /// </summary>
    public int GroupAdmissionTimeoutSeconds { get; set; } = 120;

    /// <summary>Reads the incoming chat message aloud in its own voice.</summary>
    public ChatVoiceOptions ChatVoice { get; set; } = new();

    /// <summary>Speaks the generated StudioOS reply in its own voice.</summary>
    public NarrationVoiceRoleOptions AssistantVoice { get; set; } = new();
}

/// <summary>
/// A semantic voice role. Roles are independent of each other and independent of any particular
/// voice model, so the same role can be re-pointed at another Piper voice without code changes.
/// </summary>
public class NarrationVoiceRoleOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Piper voice identifier. Empty falls back to the provider's configured default voice.</summary>
    public string VoiceId { get; set; } = string.Empty;

    /// <summary>
    /// Logical volume for this role, applied immediately before that role's playback. Null means
    /// "use the narration-wide volume", which keeps the previous single-voice behavior unchanged.
    /// </summary>
    public double? Volume { get; set; }
}

public sealed class ChatVoiceOptions : NarrationVoiceRoleOptions
{
    /// <summary>Whether the spoken text is prefixed with the sender's display name.</summary>
    public bool SpeakUserName { get; set; } = true;

    /// <summary>pt-BR friendly default. {username} and {message} are the only tokens.</summary>
    public string UserNameFormat { get; set; } = "{username} disse: {message}";

    /// <summary>Deterministic maximum length of the spoken message body.</summary>
    public int MaxMessageCharacters { get; set; } = 240;

    /// <summary>Spoken replacement for a URL. No summary or invented destination is produced.</summary>
    public string UrlSpokenWord { get; set; } = "link";
}

public sealed class NarrationPlaybackException(string errorCode, Exception? innerException = null)
    : Exception(errorCode, innerException)
{
    public string ErrorCode { get; } = errorCode;
}

public interface INarrationService
{
    NarrationStateSnapshot GetState();
    IReadOnlyList<NarrationEvent> GetRecentEvents(int limit);
    IReadOnlyList<NarrationResult> GetRecentResults(int limit);
    Task<NarrationEnqueueResult> EnqueueAsync(
        NarrationAudioArtifact artifact,
        string correlationId,
        CancellationToken cancellationToken);
    Task<NarrationEnqueueResult> EnqueueAsync(
        NarrationAudioArtifact artifact,
        string correlationId,
        NarrationVoiceRoleOptions role,
        int orderWithinInteraction,
        long groupSequence,
        CancellationToken cancellationToken);
    Task SetMutedAsync(bool muted, CancellationToken cancellationToken);
    Task<NarrationStateSnapshot> SetVolumeAsync(double volume, CancellationToken cancellationToken);
    Task<NarrationPlaybackSnapshot> RefreshPlaybackStateAsync(CancellationToken cancellationToken);
}

public interface IAudioPlaybackService
{
    Task<NarrationPlaybackSnapshot> GetStateAsync(string sourceName, CancellationToken cancellationToken);
    Task EnsureSourceAsync(string sourceName, string runtimeDirectory, CancellationToken cancellationToken);
    Task ConfigureAsync(string sourceName, double volumePercent, bool muted, CancellationToken cancellationToken);
    Task PlayAsync(string sourceName, NarrationAudioArtifact artifact, CancellationToken cancellationToken);
    Task<ObsMediaPlaybackState> WaitForCompletionAsync(
        string sourceName,
        TimeSpan expectedDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken);
    Task StopAndClearAsync(string sourceName, CancellationToken cancellationToken);
}

public interface INarrationArtifactValidator
{
    NarrationArtifactValidation Validate(NarrationAudioArtifact artifact, string allowedRuntimeDirectory);
}

public interface IAudioArtifactLeaseRegistry
{
    IDisposable Acquire(string path);
    bool IsLeased(string path);
}

public interface INarrationEventPublisher
{
    Task PublishAsync(NarrationEvent narrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// One interaction's worth of dual voice audio. Both artifacts belong to the same interaction and are
/// played in a fixed order: Chat first, Assistant second, never overlapping.
/// </summary>
/// <summary>
/// One accepted interaction's two narrations.
///
/// Each role is supplied as a promise that the interaction pipeline settles when that clip is known,
/// and a null result means there will be no clip for that role: synthesis failed, the model failed, its
/// reply was unusable, or the interaction was text-only. Resolving to null is what lets the slot close
/// promptly instead of waiting out the admission timeout, so a failed interaction can never hold up the
/// ones accepted after it.
/// </summary>
public sealed record DualVoiceNarrationRequest(
    Guid InteractionId,
    long AcceptedSequence,
    string CorrelationId,
    Task<TextToSpeechResult?>? ChatAudio,
    Task<TextToSpeechResult?>? AssistantAudio,
    TimeSpan AcceptedAtOffset);

/// <summary>
/// Serializes dual voice playback across concurrent interactions and preserves strict acceptance order.
///
/// Two guarantees matter here and are not provided by a plain FIFO queue. First, within an
/// interaction the Chat artifact is always enqueued before the Assistant artifact, so a slow AI
/// response can never let the reply overtake the message that prompted it. Second, a group is not
/// admitted until every group accepted earlier has been fully admitted, which keeps
/// "A chat, A assistant, B chat, B assistant" instead of interleaving.
///
/// Ordering is by <see cref="DualVoiceNarrationRequest.AcceptedSequence"/>, which the interaction
/// pipeline takes atomically when it accepts the chat message, not when the model answers. A
/// regression of the pipeline that submitted only after the reply was ready would let a fast reply
/// overtake an earlier slow one; the registration is deliberately placed at acceptance so that the
/// key and the order are the same fact.
/// </summary>
public interface IDualVoiceNarrationCoordinator
{
    /// <summary>
    /// Reserves an admission slot for one interaction and queues its roles in acceptance order.
    /// The returned task completes once the slot is reserved; callers are not required to await it,
    /// and the group's own audio may still be pending when it returns.
    /// </summary>
    Task SubmitAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Admits a group that an operator asked for explicitly, bypassing the autoplay gate.
    ///
    /// This is a development-only escape hatch, named separately so the automatic path can never reach
    /// it by accident. It deliberately does not change <see cref="NarrationOptions.AutoPlayInteractions"/>:
    /// an automated chat interaction still cannot reach playback while autoplay is off, and the switch
    /// keeps its value for the whole test.
    /// </summary>
    Task SubmitDevelopmentTestAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken);
}
