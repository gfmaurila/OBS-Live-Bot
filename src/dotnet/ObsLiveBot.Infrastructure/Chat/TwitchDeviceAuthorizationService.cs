using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Infrastructure.Chat;

/// <summary>Public-client Twitch Device Code Flow. Device codes and tokens remain transient or DPAPI-backed.</summary>
public sealed class TwitchDeviceAuthorizationService(
    IHttpClientFactory httpClientFactory,
    ITwitchTokenStore tokenStore,
    IOptions<TwitchOAuthOptions> options,
    ILiveChatProviderRegistry providers,
    IHostApplicationLifetime lifetime,
    TimeProvider timeProvider,
    ILogger<TwitchDeviceAuthorizationService> logger) : ITwitchAuthorizationService
{
    private static readonly string[] RequiredScopes = ["user:read:chat"];
    private readonly object _gate = new();
    private string _state = "NotConfigured";
    private string? _errorCode;
    private string? _broadcasterId;
    private string? _broadcasterLogin;
    private DateTimeOffset? _expiresAtUtc;
    private DateTimeOffset? _lastValidatedAtUtc;
    private bool _helperAvailable;
    private bool _authorizationInProgress;
    private CancellationTokenSource? _authorizationCancellation;

    public TwitchAuthorizationSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var configured = options.Value.Enabled &&
                             !string.IsNullOrWhiteSpace(options.Value.ClientId) &&
                             !string.IsNullOrWhiteSpace(options.Value.Channel);
            return new TwitchAuthorizationSnapshot(
                options.Value.Enabled,
                configured,
                _helperAvailable,
                !configured ? "NotConfigured" : _state,
                _broadcasterLogin ?? options.Value.Channel,
                _broadcasterId,
                RequiredScopes,
                _expiresAtUtc,
                _lastValidatedAtUtc,
                _errorCode);
        }
    }

    public async Task<TwitchDeviceAuthorizationResponse> StartDeviceAuthorizationAsync(
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled || string.IsNullOrWhiteSpace(configuration.ClientId) ||
            string.IsNullOrWhiteSpace(configuration.Channel))
            return Failure("NotConfigured", "twitch_not_configured");

        if (!await tokenStore.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            SetState("CredentialStoreUnavailable", "credential_helper_unavailable", helperAvailable: false);
            return Failure("CredentialStoreUnavailable", "credential_helper_unavailable");
        }

        lock (_gate)
        {
            if (_authorizationInProgress)
                return Failure("AuthorizationPending", "authorization_already_in_progress");
            _authorizationInProgress = true;
            _authorizationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        }

        try
        {
            using var client = httpClientFactory.CreateClient("TwitchOAuth");
            using var request = new HttpRequestMessage(HttpMethod.Post, "oauth2/device")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = configuration.ClientId,
                    ["scopes"] = string.Join(' ', RequiredScopes)
                })
            };
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var device = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken)
                    .ConfigureAwait(false)
                : null;
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceCode) ||
                string.IsNullOrWhiteSpace(device.UserCode) ||
                !Uri.TryCreate(device.VerificationUri, UriKind.Absolute, out var verificationUri) ||
                verificationUri.Scheme != Uri.UriSchemeHttps)
            {
                ReleaseAuthorization("AuthorizationFailed", "device_authorization_failed");
                return Failure("AuthorizationFailed", "device_authorization_failed");
            }

            var interval = Math.Clamp(device.Interval ?? 5, 1, 30);
            var expires = Math.Clamp(device.ExpiresIn ?? configuration.DeviceAuthorizationTimeoutSeconds, 60, 1800);
            var expiresAt = timeProvider.GetUtcNow().AddSeconds(expires);
            SetState("AuthorizationPending", errorCode: null, helperAvailable: true);
            CancellationToken authorizationToken;
            lock (_gate) authorizationToken = _authorizationCancellation!.Token;
            _ = PollForDeviceAuthorizationAsync(device.DeviceCode, interval, expiresAt, authorizationToken);
            return new TwitchDeviceAuthorizationResponse(
                true, "AuthorizationPending", verificationUri.ToString(), device.UserCode,
                expiresAt, interval, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReleaseAuthorization("AuthorizationCancelled", "authorization_cancelled");
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("TWITCH_DEVICE_AUTH_START_FAILED errorType={ErrorType}", exception.GetType().Name);
            ReleaseAuthorization("AuthorizationFailed", "device_authorization_failed");
            return Failure("AuthorizationFailed", "device_authorization_failed");
        }
    }

    public async Task<TwitchAuthorizationSnapshot> DisconnectAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _authorizationInProgress = false;
            _authorizationCancellation?.Cancel();
            _authorizationCancellation?.Dispose();
            _authorizationCancellation = null;
        }
        try
        {
            await tokenStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
            providers.NotifyCredentialsChanged(ObsLiveBot.Domain.Chat.LiveChatProviderType.Twitch);
            SetState("NotAuthenticated", null, helperAvailable: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("TWITCH_CREDENTIAL_DELETE_FAILED errorType={ErrorType}", exception.GetType().Name);
            SetState("CredentialStoreUnavailable", "credential_helper_unavailable", helperAvailable: false);
        }
        return GetSnapshot();
    }

    private async Task PollForDeviceAuthorizationAsync(
        string deviceCode,
        int intervalSeconds,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("TwitchOAuth");
        try
        {
            while (!cancellationToken.IsCancellationRequested && timeProvider.GetUtcNow() < expiresAtUtc)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), timeProvider, cancellationToken)
                    .ConfigureAwait(false);
                using var response = await client.PostAsync("oauth2/token", new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = options.Value.ClientId!,
                        ["scopes"] = string.Join(' ', RequiredScopes),
                        ["device_code"] = deviceCode,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                    }), cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadFromJsonAsync<OAuthError>(cancellationToken)
                        .ConfigureAwait(false);
                    if (error?.Error == "authorization_pending") continue;
                    if (error?.Error == "slow_down") { intervalSeconds = Math.Min(30, intervalSeconds + 5); continue; }
                    if (error?.Error is "expired_token" or "access_denied")
                    {
                        ReleaseAuthorization("NotAuthenticated", error.Error);
                        return;
                    }
                    ReleaseAuthorization("AuthorizationFailed", "token_exchange_failed");
                    return;
                }

                var tokens = await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(cancellationToken)
                    .ConfigureAwait(false);
                if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken) ||
                    string.IsNullOrWhiteSpace(tokens.RefreshToken))
                {
                    ReleaseAuthorization("AuthorizationFailed", "invalid_token_response");
                    return;
                }

                var validation = await ValidateTokenAsync(client, tokens.AccessToken, cancellationToken)
                    .ConfigureAwait(false);
                if (validation is null || validation.ClientId != options.Value.ClientId ||
                    validation.Scopes is null ||
                    !RequiredScopes.All(scope => validation.Scopes.Contains(scope, StringComparer.Ordinal)) ||
                    string.IsNullOrWhiteSpace(validation.UserId) ||
                    !string.Equals(validation.Login, options.Value.Channel, StringComparison.OrdinalIgnoreCase))
                {
                    ReleaseAuthorization("AuthenticationFailed", "token_validation_failed");
                    return;
                }

                var expiry = timeProvider.GetUtcNow().AddSeconds(Math.Max(1, validation.ExpiresIn));
                await tokenStore.WriteAsync(new TwitchOAuthTokens(
                    tokens.AccessToken, tokens.RefreshToken, timeProvider.GetUtcNow(), expiry,
                    validation.UserId!, validation.Login!, validation.Scopes), cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    _broadcasterId = validation.UserId;
                    _broadcasterLogin = validation.Login;
                    _expiresAtUtc = expiry;
                    _lastValidatedAtUtc = timeProvider.GetUtcNow();
                    _helperAvailable = true;
                    _state = "Authenticated";
                    _errorCode = null;
                    _authorizationInProgress = false;
                    _authorizationCancellation?.Dispose();
                    _authorizationCancellation = null;
                }
                providers.NotifyCredentialsChanged(ObsLiveBot.Domain.Chat.LiveChatProviderType.Twitch);
                return;
            }
            ReleaseAuthorization("NotAuthenticated", "device_code_expired");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReleaseAuthorization("AuthorizationCancelled", "application_stopping");
        }
        catch (Exception exception)
        {
            logger.LogWarning("TWITCH_DEVICE_AUTH_POLL_FAILED errorType={ErrorType}", exception.GetType().Name);
            ReleaseAuthorization("AuthorizationFailed", "token_exchange_failed");
        }
    }

    private static async Task<TokenValidationResponse?> ValidateTokenAsync(
        HttpClient client, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "oauth2/validate");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", accessToken);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TokenValidationResponse>(cancellationToken).ConfigureAwait(false)
            : null;
    }

    private void SetState(string state, string? errorCode, bool helperAvailable)
    {
        lock (_gate)
        {
            _state = state;
            _errorCode = errorCode;
            _helperAvailable = helperAvailable;
        }
    }

    private void ReleaseAuthorization(string state, string errorCode)
    {
        lock (_gate)
        {
            _state = state;
            _errorCode = errorCode;
            _authorizationInProgress = false;
            _authorizationCancellation?.Dispose();
            _authorizationCancellation = null;
        }
    }

    private static TwitchDeviceAuthorizationResponse Failure(string state, string error) =>
        new(false, state, null, null, null, null, error);

    private sealed record DeviceCodeResponse(
        [property: JsonPropertyName("device_code")] string? DeviceCode,
        [property: JsonPropertyName("user_code")] string? UserCode,
        [property: JsonPropertyName("verification_uri")] string? VerificationUri,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("interval")] int? Interval);

    private sealed record DeviceTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);

    private sealed record TokenValidationResponse(
        [property: JsonPropertyName("client_id")] string? ClientId,
        [property: JsonPropertyName("login")] string? Login,
        [property: JsonPropertyName("user_id")] string? UserId,
        [property: JsonPropertyName("scopes")] string[] Scopes,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record OAuthError([property: JsonPropertyName("message")] string? Error);
}
