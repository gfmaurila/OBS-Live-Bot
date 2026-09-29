using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Infrastructure.Chat;

public sealed record TwitchOAuthTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset StoredAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string UserId,
    string Login,
    string[] Scopes);

public interface ITwitchTokenStore
{
    Task<TwitchOAuthTokens?> ReadAsync(CancellationToken cancellationToken);
    Task WriteAsync(TwitchOAuthTokens tokens, CancellationToken cancellationToken);
    Task DeleteAsync(CancellationToken cancellationToken);
    Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken);
}

public sealed class SecureHelperTwitchTokenStore(
    IHttpClientFactory httpClientFactory,
    IOptions<CredentialHelperClientOptions> options,
    ILogger<SecureHelperTwitchTokenStore> logger) : ITwitchTokenStore
{
    private const string Provider = "Twitch";
    private const string CredentialKey = "OAuthTokens";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TwitchOAuthTokens?> ReadAsync(CancellationToken cancellationToken)
    {
        EnsureSharedKeyConfigured();
        using var client = CreateClient();
        using var response = await SendAsync(
            client,
            HttpMethod.Get,
            CredentialPath,
            content: null,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw CreateSafeException("Credential helper could not read a protected credential.");

        var credential = await response.Content.ReadFromJsonAsync<CredentialResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null || credential.Value.Length > 16_384)
            throw new InvalidDataException("Credential helper returned an invalid protected record.");
        return JsonSerializer.Deserialize<TwitchOAuthTokens>(credential.Value, JsonOptions)
               ?? throw new InvalidDataException("Protected Twitch credential payload is invalid.");
    }

    public async Task WriteAsync(TwitchOAuthTokens tokens, CancellationToken cancellationToken)
    {
        EnsureSharedKeyConfigured();
        var value = JsonSerializer.Serialize(tokens, JsonOptions);
        using var client = CreateClient();
        using var response = await SendAsync(
            client,
            HttpMethod.Put,
            CredentialPath,
            JsonContent.Create(new CredentialWriteRequest(value), options: JsonOptions),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateSafeException("Credential helper could not store a protected credential.");
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        EnsureSharedKeyConfigured();
        using var client = CreateClient();
        using var response = await SendAsync(
            client,
            HttpMethod.Delete,
            CredentialPath,
            content: null,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateSafeException("Credential helper could not delete a protected credential.");
    }

    public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (!IsSharedKeyConfigured())
        {
            logger.LogWarning("TWITCH_CREDENTIAL_HELPER_UNAVAILABLE reason=shared_key_not_configured");
            return false;
        }

        try
        {
            using var client = CreateClient();
            using var response = await SendAsync(
                client, HttpMethod.Get, "health", content: null, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "TWITCH_CREDENTIAL_HELPER_UNAVAILABLE errorType={ErrorType}", exception.GetType().Name);
            return false;
        }
    }

    private HttpClient CreateClient() => httpClientFactory.CreateClient("CredentialHelper");

    private const string CredentialPath = "v1/credentials/Twitch/OAuthTokens";

    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        EnsureSharedKeyConfigured();

        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", options.Value.SharedKey);
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "TWITCH_CREDENTIAL_HELPER_REQUEST_FAILED operation={Operation} errorType={ErrorType}",
                method.Method,
                exception.GetType().Name);
            throw CreateSafeException("Credential helper is unavailable.");
        }
    }

    private static HttpRequestException CreateSafeException(string message) => new(message);

    private bool IsSharedKeyConfigured() =>
        System.Text.Encoding.UTF8.GetByteCount(options.Value.SharedKey ?? string.Empty) >= 32;

    private void EnsureSharedKeyConfigured()
    {
        if (!IsSharedKeyConfigured())
            throw CreateSafeException("Credential helper is not configured.");
    }

    private sealed record CredentialResponse(string Value);
    private sealed record CredentialWriteRequest(string Value);
}
