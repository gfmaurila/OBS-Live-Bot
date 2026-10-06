using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>
/// Reports every supported platform's read and write capability from live runtime state.
/// <para>
/// The two sides are decided independently and by different evidence. Read comes from the capture
/// registry, which is the only thing that knows whether a provider is connected. Write comes from the
/// adapter, the selected sender, and whether the write transport currently holds an authenticated source
/// for that platform. Neither is inferred from the other, which is the entire reason this type exists:
/// "capture connected" must never be reported as "can post".
/// </para>
/// <para>
/// The master switch is not part of any capability. Enabling or disabling automatic replies says nothing
/// about whether a platform could be written to, and folding the two together would make a disabled
/// capability look unsupported and hide the transport that will be used when it is enabled.
/// </para>
/// </summary>
public sealed class ChatProviderCapabilityService(
    ILiveChatProviderRegistry liveChatProviders,
    IChatResponseSenderRegistry senders,
    ChatWriteAdapterRegistry adapters,
    ChatWriteTargetResolver writeTargets,
    ILogger<ChatProviderCapabilityService> logger) : IChatProviderCapabilityProvider
{
    /// <summary>
    /// The platforms StudioOS captures and could write. Kept explicit so the capability report is a stable
    /// list an interface can render, instead of whatever happens to be registered today.
    /// </summary>
    private static readonly LiveChatProviderType[] ReportedProviders =
    [
        LiveChatProviderType.Twitch,
        LiveChatProviderType.YouTube,
        LiveChatProviderType.Kick
    ];

    public async Task<IReadOnlyList<ChatProviderCapability>> GetCapabilitiesAsync(
        CancellationToken cancellationToken)
    {
        var sender = senders.GetSelected();
        var results = new List<ChatProviderCapability>(ReportedProviders.Length);

        foreach (var provider in ReportedProviders)
            results.Add(await DescribeAsync(provider, sender, cancellationToken).ConfigureAwait(false));

        return results;
    }

    private async Task<ChatProviderCapability> DescribeAsync(
        LiveChatProviderType provider,
        IChatResponseSender? sender,
        CancellationToken cancellationToken)
    {
        var (canRead, readStatus, readDetail) = DescribeRead(provider);
        var adapter = adapters.Find(provider);

        if (adapter is null)
            return new ChatProviderCapability(
                provider,
                canRead, readStatus, readDetail,
                CanWrite: false,
                WriteReady: false,
                ChatCapabilityStatus.Unsupported,
                "No write adapter is registered for this platform.",
                WriteTransport: null,
                WriteRequiresAuthentication: false,
                WriteAuthenticated: false,
                MaxMessageCharacters: 0);

        ChatWriteProbe? probe = null;
        try
        {
            probe = await writeTargets
                .ProbeAsync(adapter, ChatResponsePolicy.NormalizeChannel(string.Empty), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Capability reporting must never fail. A transport that cannot be probed is reported as
            // not configured, which is the state an operator has to resolve, not a 500.
            logger.LogWarning(ex, "Write capability probe failed for {Provider}", provider);
        }

        var (canWrite, writeReady, writeStatus, writeDetail) = DescribeWrite(provider, sender, adapter, probe);
        return new ChatProviderCapability(
            provider,
            canRead, readStatus, readDetail,
            canWrite, writeReady, writeStatus, writeDetail,
            adapter.Transport,
            adapter.RequiresAuthenticatedSession,
            probe?.HasAnySource ?? false,
            adapter.MaxMessageCharacters);
    }

    private (bool CanRead, ChatCapabilityStatus Status, string Detail) DescribeRead(LiveChatProviderType provider)
    {
        var snapshot = liveChatProviders.GetProviders()
            .FirstOrDefault(candidate => candidate.Provider == provider);

        if (snapshot is null)
            return (false, ChatCapabilityStatus.Unsupported, "No capture provider is registered.");

        if (!snapshot.Enabled)
            return (false, ChatCapabilityStatus.NotConfigured, $"LiveChat:Providers:{provider} is disabled.");

        if (snapshot.State == LiveChatProviderState.AuthenticationRequired ||
            snapshot.State == LiveChatProviderState.AuthenticationFailed)
        {
            return (false, ChatCapabilityStatus.Degraded,
                "Capture requires platform authentication that is not currently valid.");
        }

        if (snapshot.State == LiveChatProviderState.Connected && snapshot.CaptureReady)
            return (true, ChatCapabilityStatus.Ready, "Capture is connected.");

        return (snapshot.State == LiveChatProviderState.Connected, ChatCapabilityStatus.Degraded,
            $"Capture is in state {snapshot.State}.");
    }

    private (bool CanWrite, bool Ready, ChatCapabilityStatus Status, string Detail) DescribeWrite(
        LiveChatProviderType provider,
        IChatResponseSender? sender,
        IChatWriteAdapter adapter,
        ChatWriteProbe? probe)
    {
        if (sender is null)
            return (true, false, ChatCapabilityStatus.NotConfigured,
                $"No sender named by ChatResponses:Sender is registered, so nothing would be delivered.");

        if (!sender.SupportedProviders.Contains(provider))
            return (false, false, ChatCapabilityStatus.Unsupported,
                $"Sender '{sender.Name}' does not support {provider}.");

        if (sender.IsDevelopment)
            return (false, false, ChatCapabilityStatus.NotConfigured,
                $"Sender '{sender.Name}' is simulated: replies are recorded but never delivered.");

        if (!sender.IsAvailable)
            return (true, false, ChatCapabilityStatus.Degraded,
                $"Sender '{sender.Name}' is unavailable.");

        if (probe is null)
            return (true, false, ChatCapabilityStatus.NotConfigured,
                "The write transport could not be probed.");

        if (probe.ErrorCode is not null)
            return (true, false, ChatCapabilityStatus.NotConfigured,
                $"The write transport did not answer ({probe.ErrorCode}); its last known state is shown.");

        if (!probe.HasAnySource)
            return (true, false, ChatCapabilityStatus.NotConfigured,
                adapter.RequiresAuthenticatedSession
                    ? $"No authenticated {provider} chat session is loaded in Social Stream Ninja."
                    : $"No {provider} chat source is loaded in Social Stream Ninja.");

        if (!probe.HasActiveSource)
            return (true, false, ChatCapabilityStatus.Degraded,
                $"A {provider} chat session is loaded but not active.");

        if (probe.SourceId is null)
            return (true, false, ChatCapabilityStatus.NotConfigured,
                $"No writable {provider} chat source could be resolved.");

        return (true, true, ChatCapabilityStatus.Ready,
            probe.ExactChannelMatch
                ? "A reply would be written to the source for the current channel."
                : "A reply would be written to the platform's active source; the capture and write " +
                  "channel identifiers differ.");
    }
}