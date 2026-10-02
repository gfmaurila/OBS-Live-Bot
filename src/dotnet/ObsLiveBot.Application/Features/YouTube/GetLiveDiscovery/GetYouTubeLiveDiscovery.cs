using Ardalis.Result;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.YouTube;

namespace ObsLiveBot.Application.Features.YouTube.GetLiveDiscovery;

public sealed record GetYouTubeLiveDiscoveryQuery : IRequest<Result<YouTubeLiveDiscoveryResponse>>;

public sealed class GetYouTubeLiveDiscoveryQueryHandler(IYouTubeLiveOwnershipReader ownership)
    : IRequestHandler<GetYouTubeLiveDiscoveryQuery, Result<YouTubeLiveDiscoveryResponse>>
{
    public Task<Result<YouTubeLiveDiscoveryResponse>> Handle(
        GetYouTubeLiveDiscoveryQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = ownership.State;
        // Only public values are projected: a public video ID and its public live/chat URLs.
        var response = new YouTubeLiveDiscoveryResponse(
            snapshot.Enabled,
            snapshot.DisabledReason,
            snapshot.ObsStreaming,
            snapshot.OwnsCurrentLive,
            snapshot.Channel,
            snapshot.CurrentVideoId,
            snapshot.CurrentVideoId is null ? null : YouTubeLiveSourceIdentity.PublicUrl(snapshot.CurrentVideoId),
            snapshot.CurrentVideoId is null ? null : YouTubeLiveSourceIdentity.ChatUrl(snapshot.CurrentVideoId),
            snapshot.CurrentSourceId,
            snapshot.LastDiscoveryMethod,
            snapshot.LastReason,
            snapshot.LastDiscoveryAtUtc,
            snapshot.LastReconciledAtUtc,
            snapshot.DiscoveryAttempts,
            snapshot.SourceEnsures,
            snapshot.SourceReleases,
            snapshot.ActiveSourceIds);
        return Task.FromResult(Result.Success(response));
    }
}
