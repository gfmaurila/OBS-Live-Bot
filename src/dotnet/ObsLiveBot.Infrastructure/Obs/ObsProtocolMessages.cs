using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ObsLiveBot.Infrastructure.Obs;

public static class ObsProtocolMessages
{
    public const int LiveStateEventSubscriptions =
        (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3) | (1 << 6) | (1 << 7) | (1 << 16) | (1 << 17);

    public static string ComputeAuthentication(string password, string salt, string challenge)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(challenge);

        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        var secret = Convert.ToBase64String(secretHash);
        var authenticationHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge));
        return Convert.ToBase64String(authenticationHash);
    }

    public static object CreateIdentify(JsonElement helloData, string? password)
    {
        if (!helloData.TryGetProperty("authentication", out var authentication))
        {
            return new { op = 1, d = new { rpcVersion = 1, eventSubscriptions = LiveStateEventSubscriptions } };
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new ObsAuthenticationException("OBS WebSocket requires authentication, but no password is configured.");
        }

        var challenge = authentication.GetProperty("challenge").GetString()
            ?? throw new InvalidDataException("OBS WebSocket authentication challenge is missing.");
        var salt = authentication.GetProperty("salt").GetString()
            ?? throw new InvalidDataException("OBS WebSocket authentication salt is missing.");
        var response = ComputeAuthentication(password, salt, challenge);

        return new { op = 1, d = new { rpcVersion = 1, authentication = response, eventSubscriptions = LiveStateEventSubscriptions } };
    }
}
