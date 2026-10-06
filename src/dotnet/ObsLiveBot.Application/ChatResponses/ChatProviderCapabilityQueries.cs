using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Chat;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.ChatResponses;

/// <summary>
/// Read and write capability per chat platform, for the future Command Center.
/// <para>
/// Deliberately a query rather than a field on the written-reply state: capability is a property of the
/// deployment, while state is a property of the last few seconds. Mixing them would make a single flag
/// appear to describe both.
/// </para>
/// </summary>
public sealed record GetChatProviderCapabilitiesQuery : IRequest<ChatProviderCapabilitiesResponse>;

public sealed class GetChatProviderCapabilitiesQueryHandler(IChatProviderCapabilityProvider capabilities)
    : IRequestHandler<GetChatProviderCapabilitiesQuery, ChatProviderCapabilitiesResponse>
{
    public async Task<ChatProviderCapabilitiesResponse> Handle(
        GetChatProviderCapabilitiesQuery request,
        CancellationToken cancellationToken)
    {
        var resolved = await capabilities.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        return new ChatProviderCapabilitiesResponse(resolved.Select(Map).ToArray());
    }

    private static ChatProviderCapabilityResponse Map(ChatProviderCapability capability) =>
        new(
            capability.Provider.ToString(),
            capability.CanRead,
            capability.ReadStatus.ToString(),
            capability.ReadDetail,
            capability.CanWrite,
            capability.WriteReady,
            capability.WriteStatus.ToString(),
            capability.WriteDetail,
            capability.WriteTransport,
            capability.WriteRequiresAuthentication,
            capability.WriteAuthenticated,
            capability.MaxMessageCharacters);
}