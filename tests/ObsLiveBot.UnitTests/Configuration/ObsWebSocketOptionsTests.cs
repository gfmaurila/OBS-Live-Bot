using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.UnitTests.Configuration;

public sealed class ObsWebSocketOptionsTests
{
    private readonly ObsWebSocketOptionsValidator _validator = new();

    [Fact]
    public void EmptyPassword_IsValid()
    {
        var result = _validator.Validate(null, new ObsWebSocketOptions
        {
            Host = "localhost",
            Port = 4455,
            Password = string.Empty
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ConfiguredPassword_IsValid()
    {
        var result = _validator.Validate(null, new ObsWebSocketOptions
        {
            Host = "host.docker.internal",
            Port = 4455,
            Password = "test-only-password"
        });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("", 4455)]
    [InlineData("localhost", 0)]
    [InlineData("localhost", 65536)]
    public void InvalidHostOrPort_IsRejected(string host, int port)
    {
        var result = _validator.Validate(null, new ObsWebSocketOptions { Host = host, Port = port });

        Assert.True(result.Failed);
    }
}
