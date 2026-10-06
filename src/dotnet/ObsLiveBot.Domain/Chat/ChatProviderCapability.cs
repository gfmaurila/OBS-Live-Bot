namespace ObsLiveBot.Domain.Chat;

/// <summary>
/// How a chat platform stands on one specific side of the boundary: reading it, or writing into it.
/// These are reported separately and never inferred from each other. A connected capture provider says
/// nothing about whether StudioOS can post, because capture and write use different transports and
/// different authentication.
/// </summary>
public enum ChatCapabilityStatus
{
    /// <summary>The side works now.</summary>
    Ready = 0,

    /// <summary>The side is possible in principle but nothing is configured for it yet.</summary>
    NotConfigured = 1,

    /// <summary>No transport exists for this side on this platform.</summary>
    Unsupported = 2,

    /// <summary>It is configured and was working, and something is currently wrong with it.</summary>
    Degraded = 3
}

/// <summary>
/// One platform's read and write capabilities, as the future Command Center needs to display them:
/// "Twitch Read: Ready / Write: NotConfigured".
/// <para>
/// Every field here is derived from real runtime state. Nothing is hard-coded to look healthy, so a
/// provider cannot claim write support merely because its capture is connected.
/// </para>
/// </summary>
public sealed record ChatProviderCapability(
    LiveChatProviderType Provider,
    bool CanRead,
    ChatCapabilityStatus ReadStatus,
    string ReadDetail,
    bool CanWrite,
    bool WriteReady,
    ChatCapabilityStatus WriteStatus,
    string WriteDetail,
    string? WriteTransport,
    bool WriteRequiresAuthentication,
    bool WriteAuthenticated,
    int MaxMessageCharacters);

/// <summary>
/// The resolved write target for one platform: the SSN source a reply would be typed into, and whether
/// the channel matched it exactly or was inferred from the platform's live source.
/// </summary>
public sealed record ChatWriteTarget(string SourceId, string ChannelId, bool ExactChannelMatch);

/// <summary>
/// Result of asking the write transport what it currently has available for a platform. Cached for a
/// short period so capability reporting and send resolution share one bounded view of the same state.
/// </summary>
public sealed record ChatWriteProbe(
    LiveChatProviderType Provider,
    bool HasAnySource,
    bool HasActiveSource,
    string? SourceId,
    string? ChannelId,
    bool ExactChannelMatch,
    string? ErrorCode,
    DateTimeOffset ProbedAtUtc);