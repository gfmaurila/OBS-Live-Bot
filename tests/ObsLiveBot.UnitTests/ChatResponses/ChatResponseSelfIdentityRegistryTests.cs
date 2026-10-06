using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.UnitTests.ChatResponses;

/// <summary>
/// The loop guard's primary mechanism: recognising StudioOS's own chat account by identity, so a written
/// reply can never start another reply regardless of what its text says.
/// </summary>
public sealed class ChatResponseSelfIdentityRegistryTests
{
    [Fact]
    public void UnknownAccount_IsNotAStudioOsActor()
    {
        var registry = Registry();

        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "viewer-1"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "viewer-1", "viewer"));
    }

    [Fact]
    public void ConfiguredIdentity_IsRecognised()
    {
        var registry = Registry(o => o.SelfActorIdentities = ["Twitch:12345"]);

        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "12345"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void IdentityMatchIsCaseInsensitive()
    {
        var registry = Registry(o => o.SelfActorIdentities = ["Twitch:StudioAccount"]);

        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "studioaccount"));
    }

    /// <summary>
    /// A configured account is registered under the account key as well as the id key, because the
    /// YouTube capture route frequently reports only a display name. An operator should not have to know
    /// which form a given platform will produce.
    /// </summary>
    [Fact]
    public void ConfiguredIdentity_IsAlsoRecognisedByDisplayName()
    {
        var registry = Registry(o => o.SelfActorIdentities = ["YouTube:gfmaurila"]);

        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.YouTube, null, "gfmaurila"));
        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.YouTube, "name:abc123", "gfmaurila"));
        // The account id alone is not the configured value, so it must not match on its own.
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.YouTube, "name:abc123"));
    }

    /// <summary>
    /// The same account name on a different platform is a different person. Suppressing across platforms
    /// would silence a real viewer.
    /// </summary>
    [Fact]
    public void IdentityIsScopedToItsPlatform()
    {
        var registry = Registry(o => o.SelfActorIdentities = ["Twitch:shared"]);

        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "shared"));
        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.Twitch, null, "shared"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.YouTube, "shared"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.Kick, "shared"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.YouTube, null, "shared"));
    }

    [Theory]
    [InlineData("Twitch:12345", LiveChatProviderType.Twitch, "12345")]
    [InlineData("twitch:12345", LiveChatProviderType.Twitch, "12345")]
    [InlineData(" YouTube : gfm ", LiveChatProviderType.YouTube, "gfm")]
    [InlineData("Kick:abc", LiveChatProviderType.Kick, "abc")]
    public void ConfiguredIdentity_AcceptsProviderPrefixedForms(
        string configured,
        LiveChatProviderType expectedProvider,
        string account)
    {
        var registry = Registry(o => o.SelfActorIdentities = [configured]);

        Assert.Equal(1, registry.Count);
        Assert.True(registry.IsStudioOsActor(expectedProvider, account));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]              // No provider: refusing beats guessing across every platform.
    [InlineData("Twitch:")]           // No account.
    [InlineData("NotAPlatform:12345")] // Not a provider at all.
    [InlineData("Unknown:12345")]     // A defined member that is explicitly not a platform.
    [InlineData("7:12345")]           // A numeric value Enum.TryParse would otherwise accept.
    public void UnusableConfiguredIdentity_IsIgnoredRatherThanWideningSuppression(string? configured)
    {
        var registry = Registry(o => o.SelfActorIdentities = configured is null ? ["Twitch:kept"] : [configured, "Twitch:kept"]);

        // The good entry still works and the unusable one widened nothing.
        Assert.Equal(1, registry.Count);
        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "kept"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "12345"));
    }

    [Fact]
    public void Learn_RecognisesTheAccountOnTheNextLookup()
    {
        var registry = Registry();

        Assert.True(registry.Learn(LiveChatProviderType.YouTube, "name:hash", "gfmaurila"));

        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.YouTube, "name:hash"));
        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.YouTube, "anything-else", "gfmaurila"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Learn_ReportsFalseForAnAlreadyKnownAccount()
    {
        var registry = Registry(o => o.SelfActorIdentities = ["Twitch:12345"]);

        Assert.False(registry.Learn(LiveChatProviderType.Twitch, "12345", null));
        Assert.False(registry.Learn(LiveChatProviderType.Twitch, null, "12345"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Learn_IgnoresAnEmptyAccount()
    {
        var registry = Registry();

        Assert.False(registry.Learn(LiveChatProviderType.Twitch, null, null));
        Assert.False(registry.Learn(LiveChatProviderType.Twitch, "  ", "  "));
        Assert.Equal(0, registry.Count);
    }

    /// <summary>
    /// Learning one account must not make another account with a similar name look like StudioOS. The
    /// name guard is only ever an exact match, never a prefix or a substring.
    /// </summary>
    [Fact]
    public void Learn_DoesNotMatchAPartialAccountName()
    {
        var registry = Registry();
        registry.Learn(LiveChatProviderType.YouTube, null, "gfmaurila");

        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.YouTube, null, "gfmaurila"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.YouTube, null, "gfmaurila2"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.YouTube, null, "maurila"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.YouTube, "gfmaurila-x"));
    }

    [Fact]
    public void LearnedAccount_DoesNotSuppressOnAnotherPlatform()
    {
        var registry = Registry();
        registry.Learn(LiveChatProviderType.YouTube, "name:hash", "gfmaurila");

        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "name:hash"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.Twitch, null, "gfmaurila"));
    }

    [Fact]
    public void BoundedCapacity_EvictsOldestIdentityFirst()
    {
        var registry = Registry(o => o.SelfActorCapacity = 3);

        for (var i = 0; i < 6; i++)
            registry.Learn(LiveChatProviderType.Twitch, $"viewer-{i}", null);

        Assert.Equal(3, registry.Capacity);
        Assert.Equal(3, registry.Count);
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "viewer-0"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "viewer-1"));
        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.Twitch, "viewer-5"));
    }

    /// <summary>
    /// Names are evicted together with the identity that introduced them, so the registry's total
    /// footprint stays bounded by one number instead of growing with a second queue.
    /// </summary>
    [Fact]
    public void EvictedIdentity_AlsoReleasesTheNameItIntroduced()
    {
        var registry = Registry(o => o.SelfActorCapacity = 1);

        registry.Learn(LiveChatProviderType.YouTube, "name:hash-1", "first");
        registry.Learn(LiveChatProviderType.YouTube, "name:hash-2", "second");

        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.YouTube, "name:hash-1"));
        Assert.False(registry.IsStudioOsActor(LiveChatProviderType.YouTube, null, "first"));
        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.YouTube, "name:hash-2"));
        Assert.True(registry.IsStudioOsActor(LiveChatProviderType.YouTube, null, "second"));
    }

    [Fact]
    public void CapacityIsAlwaysAtLeastOne_SoTheLoopGuardCannotBeDisabledByAZeroSetting()
    {
        var registry = Registry(o => o.SelfActorCapacity = 0);

        Assert.Equal(1, registry.Capacity);
        Assert.True(registry.Learn(LiveChatProviderType.Twitch, "viewer", null));
    }

    [Fact]
    public void GetIdentities_ListsTheKnownAccounts()
    {
        var registry = Registry(o => o.SelfActorIdentities = ["Twitch:1", "YouTube:2"]);

        var identities = registry.GetIdentities();

        Assert.Equal(2, identities.Count);
        Assert.Contains(identities, value => value.Contains("1", StringComparison.Ordinal));
        Assert.Contains(identities, value => value.Contains("2", StringComparison.Ordinal));
    }

    [Fact]
    public void GetIdentities_ReturnsASnapshotThatLaterLearnsDoNotMutate()
    {
        var registry = Registry(o => o.SelfActorIdentities = ["Twitch:1"]);
        var before = registry.GetIdentities();

        registry.Learn(LiveChatProviderType.Twitch, "viewer-9", null);

        Assert.Single(before);
        Assert.Equal(2, registry.GetIdentities().Count);
    }

    [Fact]
    public void ConcurrentLearns_StayWithinCapacity()
    {
        var registry = Registry(o => o.SelfActorCapacity = 50);

        Parallel.For(0, 500, index =>
            registry.Learn(LiveChatProviderType.Twitch, $"viewer-{index}", $"viewer-{index}"));

        Assert.Equal(50, registry.Capacity);
        Assert.True(registry.Count <= 50);
    }

    [Fact]
    public void ConcurrentReadsAndLearns_NeverThrow()
    {
        var registry = Registry();

        Parallel.For(0, 400, index =>
        {
            if (index % 2 == 0)
                registry.Learn(LiveChatProviderType.Twitch, $"viewer-{index}", null);
            registry.IsStudioOsActor(LiveChatProviderType.Twitch, $"viewer-{index}");
            registry.GetIdentities();
        });

        Assert.True(registry.Count <= 400);
    }

    private static ChatResponseSelfIdentityRegistry Registry(
        Action<Application.Abstractions.ChatResponseOptions>? configure = null) =>
        ChatResponseTestFactory.SelfIdentities(configure);
}