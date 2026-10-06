using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.ChatResponses;
using ObsLiveBot.Infrastructure.Health;

namespace ObsLiveBot.UnitTests.ChatResponses;

/// <summary>
/// Covers the two places where a written-reply misconfiguration would otherwise only be discovered at
/// the worst possible moment: startup, through the options validator, and runtime, through health.
/// </summary>
public sealed class ChatResponseHealthCheckTests
{
    [Fact]
    public async Task Disabled_IsHealthyBecauseThatIsTheStateTheOperatorAskedFor()
    {
        var check = new ChatResponseHealthCheck(
            Options.Create(new ChatResponseOptions { Enabled = false }),
            new ChatResponseSettingsStore(
                Options.Create(new ChatResponseOptions { Enabled = false }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatResponseSettingsStore>.Instance),
            Registry(new StubChatResponseSender { Name = "SocialStreamNinja" }));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("disabled", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledByRuntimeOverride_IsStillHealthyAndSaysSo()
    {
        var configured = new ChatResponseOptions { Enabled = true };
        var store = new ChatResponseSettingsStore(
            Options.Create(configured),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatResponseSettingsStore>.Instance);
        store.Update(new ChatResponseSettingsUpdate(Enabled: false));

        var check = new ChatResponseHealthCheck(
            Options.Create(configured),
            store,
            Registry(new StubChatResponseSender { Name = "SocialStreamNinja" }));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("runtime override", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnabledWithoutASender_IsDegraded()
    {
        var check = new ChatResponseHealthCheck(
            Options.Create(new ChatResponseOptions { Enabled = true, Sender = "SocialStreamNinja" }),
            EnabledStore(),
            Registry());

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task EnabledWithTheDevelopmentSender_IsDegradedBecauseNothingWouldBeDelivered()
    {
var check = new ChatResponseHealthCheck(
            Options.Create(new ChatResponseOptions { Enabled = true, Sender = "Development" }),
            EnabledStore(),
            new StubChatResponseSenderRegistry(
                [new StubChatResponseSender { Name = "Development", IsDevelopment = true }])
            {
                SelectedName = "Development"
            });

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("no reply reaches a platform", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnabledWithAnUnavailableSender_IsDegraded()
    {
        var check = new ChatResponseHealthCheck(
            Options.Create(new ChatResponseOptions { Enabled = true, Sender = "SocialStreamNinja" }),
            EnabledStore(),
            Registry(new StubChatResponseSender { Name = "SocialStreamNinja", IsAvailable = false }));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task EnabledWithAnAvailableRealSender_IsHealthy()
    {
        var check = new ChatResponseHealthCheck(
            Options.Create(new ChatResponseOptions { Enabled = true, Sender = "SocialStreamNinja" }),
            EnabledStore(),
            Registry(new StubChatResponseSender { Name = "SocialStreamNinja" }));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("SocialStreamNinja", result.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Unknown", 500, 4, 8, 30, 500, 500, 120, 200, 100, 15)]
    [InlineData("SocialStreamNinja", 0, 4, 8, 30, 500, 500, 120, 200, 100, 15)]
    [InlineData("SocialStreamNinja", 500, 0, 8, 30, 500, 500, 120, 200, 100, 15)]
    [InlineData("SocialStreamNinja", 500, 4, -1, 30, 500, 500, 120, 200, 100, 15)]
    [InlineData("SocialStreamNinja", 500, 4, 8, -1, 500, 500, 120, 200, 100, 15)]
    [InlineData("SocialStreamNinja", 500, 4, 8, 30, 0, 500, 120, 200, 100, 15)]
    [InlineData("SocialStreamNinja", 500, 4, 8, 30, 500, 0, 120, 200, 100, 15)]
    [InlineData("SocialStreamNinja", 500, 4, 8, 30, 500, 500, -1, 200, 100, 15)]
    [InlineData("SocialStreamNinja", 500, 4, 8, 30, 500, 500, 120, 0, 100, 15)]
[InlineData("SocialStreamNinja", 500, 4, 8, 30, 500, 500, 120, 200, 0, 15)]
    [InlineData("SocialStreamNinja", 500, 4, 8, 30, 500, 500, 120, 200, 100, 0)]
    public void OptionsValidator_RejectsValuesThatWouldOnlyFailInFrontOfAnAudience(
        string sender,
        int maxCharacters,
        int maxQueueSize,
        int globalCooldown,
        int userCooldown,
        int cooldownCapacity,
        int idempotencyCapacity,
        int echoWindow,
        int echoCapacity,
        int historyCapacity,
        int commandTimeout)
    {
        var options = new ChatResponseOptions
        {
            Sender = sender,
            MaxCharacters = maxCharacters,
            MaxQueueSize = maxQueueSize,
            GlobalCooldownSeconds = globalCooldown,
            UserCooldownSeconds = userCooldown,
            CooldownCapacity = cooldownCapacity,
            IdempotencyCapacity = idempotencyCapacity,
            EchoWindowSeconds = echoWindow,
            EchoCapacity = echoCapacity,
            HistoryCapacity = historyCapacity,
            CommandTimeoutSeconds = commandTimeout
        };

        var result = new ChatResponseOptionsValidator().Validate(ChatResponseOptions.SectionName, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void OptionsValidator_RejectsAPrefixThatWouldLeaveNoRoomForAnyReply()
    {
        var options = new ChatResponseOptions
        {
            Sender = "SocialStreamNinja",
            MaxCharacters = 20,
            MessagePrefix = "this prefix is far longer than twenty characters"
        };

        var result = new ChatResponseOptionsValidator().Validate(ChatResponseOptions.SectionName, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("MessagePrefix", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionsValidator_RejectsAnAllowListEntryThatNamesNoPlatform()
    {
        var options = new ChatResponseOptions
        {
            Sender = "SocialStreamNinja",
            AllowedProviders = [LiveChatProviderType.Unknown]
        };

        var result = new ChatResponseOptionsValidator().Validate(ChatResponseOptions.SectionName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("AllowedProviders", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionsValidator_RejectsTheDevelopmentSenderWhenTheOperatorForbadeIt()
    {
        var options = new ChatResponseOptions
        {
            Sender = "Development",
            AllowDevelopmentSender = false
        };

        var result = new ChatResponseOptionsValidator().Validate(ChatResponseOptions.SectionName, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void OptionsValidator_AcceptsTheShippedDefaults()
    {
        var result = new ChatResponseOptionsValidator().Validate(
            ChatResponseOptions.SectionName,
            new ChatResponseOptions { Sender = "SocialStreamNinja" });

        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void OptionsValidator_AcceptsAnEmptyAllowListAsNoExtraRestriction()
    {
        var result = new ChatResponseOptionsValidator().Validate(
            ChatResponseOptions.SectionName,
            new ChatResponseOptions { Sender = "SocialStreamNinja", AllowedProviders = [] });

        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
    }

    private static ChatResponseSettingsStore EnabledStore()
    {
        var options = new ChatResponseOptions { Enabled = true };
        return new ChatResponseSettingsStore(
            Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatResponseSettingsStore>.Instance);
    }

private static StubChatResponseSenderRegistry Registry(params IChatResponseSender[] senders) =>
        new([.. senders]) { SelectedName = "SocialStreamNinja" };
}
