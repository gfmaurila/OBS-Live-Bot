namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>
/// Lists the write targets the transport currently offers.
/// <para>
/// This is the write path's own port for asking "where can a reply be typed right now", and it exists so
/// the outgoing path never has to reach for the capture-side source client. Whether a reply can be written
/// is a question about an authenticated write session; the capture path answers a different question, and
/// borrowing its client would tie the two together - a capture outage would then look like a write outage
/// even when writing is perfectly possible, and vice versa.
/// </para>
/// </summary>
public interface IChatWriteSourceLister
{
    /// <summary>
    /// The sources currently available for writing. Throws on transport failure; callers decide whether
    /// that becomes an empty view or a reported error code.
    /// </summary>
    Task<IReadOnlyList<ChatWriteSource>> ListSourcesAsync(CancellationToken cancellationToken);
}
