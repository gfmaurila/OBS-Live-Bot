using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GfmStudioOS.SecureCredentialHelper;

public interface ISecureCredentialStore
{
    Task<bool> TryGetAsync(string provider, string key, CancellationToken cancellationToken);
    Task<string?> ReadAsync(string provider, string key, CancellationToken cancellationToken);
    Task WriteAsync(string provider, string key, string value, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string provider, string key, CancellationToken cancellationToken);
}

public sealed class ProtectedFileCredentialStore(
    ICredentialProtector protector,
    string storageDirectory) : ISecureCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> TryGetAsync(string provider, string key, CancellationToken cancellationToken) =>
        File.Exists(GetPath(provider, key));

    public async Task<string?> ReadAsync(string provider, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(provider, key);
        if (!File.Exists(path))
            return null;

        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        try
        {
            plaintext = protector.Unprotect(protectedBytes);
            var credential = JsonSerializer.Deserialize<CredentialPayload>(plaintext, JsonOptions);
            if (credential is null || string.IsNullOrEmpty(credential.Value))
                throw new InvalidDataException("Protected credential payload is invalid.");
            return credential.Value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task WriteAsync(
        string provider,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16_384)
            throw new ArgumentException("Credential value must contain 1–16384 characters.", nameof(value));

        Directory.CreateDirectory(storageDirectory);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new CredentialPayload(value), JsonOptions);
        byte[]? protectedBytes = null;
        var path = GetPath(provider, key);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            protectedBytes = protector.Protect(plaintext);
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public Task<bool> DeleteAsync(string provider, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(provider, key);
        if (!File.Exists(path))
            return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    private string GetPath(string provider, string key)
    {
        if (!CredentialNameValidator.IsValidProvider(provider))
            throw new ArgumentException("Provider name is invalid.", nameof(provider));
        if (!CredentialNameValidator.IsValidKey(key))
            throw new ArgumentException("Credential key is invalid.", nameof(key));

        var identity = Encoding.UTF8.GetBytes($"{provider.ToLowerInvariant()}\0{key.ToLowerInvariant()}");
        return Path.Combine(storageDirectory, $"{Convert.ToHexString(SHA256.HashData(identity))}.dpapi");
    }

    private sealed record CredentialPayload(string Value);
}
