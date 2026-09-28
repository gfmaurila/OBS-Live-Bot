using System.Text.Json;
using ObsLiveBot.Infrastructure.Obs;

namespace ObsLiveBot.UnitTests.Obs;

public sealed class ObsProtocolMessagesTests
{
    [Fact]
    public void ServerWithoutAuthentication_AllowsEmptyPassword()
    {
        using var hello = JsonDocument.Parse("""{"obsWebSocketVersion":"5.6.3","rpcVersion":1}""");

        var identify = ObsProtocolMessages.CreateIdentify(hello.RootElement, string.Empty);
        var json = JsonSerializer.Serialize(identify);

        Assert.DoesNotContain("authentication", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"rpcVersion\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerWithAuthentication_UsesComputedResponseWithoutExposingPassword()
    {
        const string password = "future-secret";
        using var hello = JsonDocument.Parse("""{"authentication":{"challenge":"challenge","salt":"salt"}}""");

        var identify = ObsProtocolMessages.CreateIdentify(hello.RootElement, password);
        var json = JsonSerializer.Serialize(identify);

        Assert.Contains("authentication", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, json, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerWithAuthentication_RejectsMissingPasswordSafely()
    {
        using var hello = JsonDocument.Parse("""{"authentication":{"challenge":"challenge","salt":"salt"}}""");

        var exception = Assert.Throws<ObsAuthenticationException>(
            () => ObsProtocolMessages.CreateIdentify(hello.RootElement, string.Empty));

        Assert.DoesNotContain("challenge", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("salt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
