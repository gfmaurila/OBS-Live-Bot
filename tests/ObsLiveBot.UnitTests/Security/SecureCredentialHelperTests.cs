using System.Net;
using System.Security.Cryptography;
using System.Text;
using GfmStudioOS.SecureCredentialHelper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Infrastructure.Chat;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.UnitTests.Security;

public sealed class SecureCredentialHelperTests
{
    [Fact]
    public void DpapiCurrentUser_ProtectsAndUnprotectsLocalTestValue()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var protector = new DpapiCurrentUserProtector();
        var plaintext = Encoding.UTF8.GetBytes("unit-test-value");
        var protectedBytes = protector.Protect(plaintext);
        var restored = protector.Unprotect(protectedBytes);

        Assert.NotEqual(plaintext, protectedBytes);
        Assert.Equal("unit-test-value", Encoding.UTF8.GetString(restored));
    }

    [Fact]
    public async Task ProtectedStore_SupportsReadUpdateDeleteAndMissingCredential()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new ProtectedFileCredentialStore(new DpapiCurrentUserProtector(), directory);
            Assert.Null(await store.ReadAsync("Twitch", "OAuthTokens", CancellationToken.None));

            await store.WriteAsync("Twitch", "OAuthTokens", "test-credential-one", CancellationToken.None);
            var firstFile = Assert.Single(Directory.GetFiles(directory, "*.dpapi"));
            var firstPayload = await File.ReadAllBytesAsync(firstFile);
            Assert.True(firstPayload.AsSpan().IndexOf(Encoding.UTF8.GetBytes("test-credential-one")) < 0);
            Assert.Equal("test-credential-one",
                await store.ReadAsync("twitch", "oauthtokens", CancellationToken.None));

            await store.WriteAsync("Twitch", "OAuthTokens", "test-credential-two", CancellationToken.None);
            Assert.Equal("test-credential-two",
                await store.ReadAsync("Twitch", "OAuthTokens", CancellationToken.None));
            Assert.True(await store.DeleteAsync("Twitch", "OAuthTokens", CancellationToken.None));
            Assert.Null(await store.ReadAsync("Twitch", "OAuthTokens", CancellationToken.None));
            Assert.False(await store.DeleteAsync("Twitch", "OAuthTokens", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProtectedStore_RejectsInvalidProviderAndKey()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new ProtectedFileCredentialStore(new DpapiCurrentUserProtector(), directory);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.WriteAsync("../Twitch", "OAuthTokens", "unit-test-value", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.WriteAsync("Twitch", "../../secret", "unit-test-value", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProtectedStore_RejectsCorruptProtectedPayload()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = CreateTemporaryDirectory();
        try
        {
            var identity = Encoding.UTF8.GetBytes("twitch\0oauthtokens");
            var path = Path.Combine(directory, $"{Convert.ToHexString(SHA256.HashData(identity))}.dpapi");
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
            var store = new ProtectedFileCredentialStore(new DpapiCurrentUserProtector(), directory);
            await Assert.ThrowsAsync<CryptographicException>(() =>
                store.ReadAsync("Twitch", "OAuthTokens", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RequestAuthorizer_RejectsRemoteNetworkAndMissingOrIncorrectKey()
    {
        var allowed = new[] { "192.168.65.0/24" };
        Assert.True(CredentialHelperRequestAuthorizer.IsAllowed(IPAddress.Parse("192.168.65.2"), allowed));
        Assert.False(CredentialHelperRequestAuthorizer.IsAllowed(IPAddress.Parse("192.168.1.10"), allowed));
        Assert.False(CredentialHelperRequestAuthorizer.IsAllowed(null, allowed));

        Assert.True(CredentialHelperRequestAuthorizer.IsAuthorized(
            "Bearer helper-test-key", "helper-test-key"));
        Assert.False(CredentialHelperRequestAuthorizer.IsAuthorized(
            "Bearer wrong", "helper-test-key"));
        Assert.False(CredentialHelperRequestAuthorizer.IsAuthorized(
            "helper-test-key", "helper-test-key"));
    }

    [Fact]
    public void OptionsValidator_RequiresStrongKeyAndValidNetwork()
    {
        var validator = new CredentialHelperOptionsValidator();
        var invalid = validator.Validate(null, new CredentialHelperOptions
        {
            SharedKey = "short",
            AllowedClientNetworks = ["not-a-cidr"]
        });
        Assert.True(invalid.Failed);

        var valid = validator.Validate(null, new CredentialHelperOptions
        {
            SharedKey = new string('x', 48),
            AllowedClientNetworks = ["192.168.65.0/24"]
        });
        Assert.True(valid.Succeeded);
    }

    [Fact]
    public void CredentialHelperClientOptions_RejectsNonLocalEndpointAndWeakKey()
    {
        var validator = new CredentialHelperClientOptionsValidator();
        Assert.True(validator.Validate(null, new CredentialHelperClientOptions
        {
            BaseUrl = "http://192.168.1.5:51823/",
            SharedKey = "short"
        }).Failed);

        Assert.True(validator.Validate(null, new CredentialHelperClientOptions
        {
            BaseUrl = "http://host.docker.internal:51823/",
            SharedKey = new string('x', 48),
            TimeoutSeconds = 3
        }).Succeeded);
    }

    [Fact]
    public async Task TwitchTokenStore_FailsClosedWhenHelperKeyIsMissing()
    {
        var store = new SecureHelperTwitchTokenStore(
            new ThrowingHttpClientFactory(),
            Options.Create(new CredentialHelperClientOptions { SharedKey = string.Empty }),
            NullLogger<SecureHelperTwitchTokenStore>.Instance);

        Assert.False(await store.CheckAvailabilityAsync(CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public void TwitchOAuthOptions_OnlyAllowsReadChatScope()
    {
        var validator = new TwitchOAuthOptionsValidator();
        Assert.True(validator.Validate(null, new TwitchOAuthOptions()).Succeeded);
        Assert.True(validator.Validate(null, new TwitchOAuthOptions
        {
            Scopes = ["user:read:chat", "user:write:chat"]
        }).Failed);
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Must fail closed first.");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gfm-secure-credential-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
