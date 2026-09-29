namespace ObsLiveBot.Application.Abstractions;

public sealed record TwitchAuthorizationSnapshot(
    bool Enabled,
    bool Configured,
    bool CredentialStoreAvailable,
    string State,
    string? BroadcasterLogin,
    string? BroadcasterUserId,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LastValidatedAtUtc,
    string? ErrorCode);

public sealed record TwitchDeviceAuthorizationResponse(
    bool Started,
    string State,
    string? VerificationUri,
    string? UserCode,
    DateTimeOffset? ExpiresAtUtc,
    int? PollIntervalSeconds,
    string? ErrorCode);

public interface ITwitchAuthorizationService
{
    TwitchAuthorizationSnapshot GetSnapshot();
    Task<TwitchDeviceAuthorizationResponse> StartDeviceAuthorizationAsync(CancellationToken cancellationToken);
    Task<TwitchAuthorizationSnapshot> DisconnectAsync(CancellationToken cancellationToken);
}
