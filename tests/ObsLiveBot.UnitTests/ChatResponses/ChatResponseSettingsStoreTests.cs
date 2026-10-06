using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.UnitTests.ChatResponses;

public sealed class ChatResponseSettingsStoreTests
{
    [Fact]
    public void ConfiguredDisabled_IsTheShippedDefault()
    {
        var store = Store(configured: false);

        var snapshot = store.Get();

        Assert.False(snapshot.Enabled);
        Assert.False(snapshot.ConfiguredEnabled);
        Assert.False(snapshot.Overridden);
        Assert.Equal("Configuration", snapshot.Source);
        Assert.Null(store.OverriddenEnabled);
    }

    [Fact]
    public void RuntimeEnable_OverridesConfigurationAndIsReported()
    {
        var store = Store(configured: false);

        var snapshot = store.Update(new ChatResponseSettingsUpdate(Enabled: true));

        Assert.True(snapshot.Enabled);
        Assert.False(snapshot.ConfiguredEnabled);
        Assert.True(snapshot.Overridden);
        Assert.Equal("RuntimeOverride", snapshot.Source);
        Assert.True(store.Get().Enabled);
    }

    [Fact]
    public void Reset_ReturnsToTheConfiguredValue()
    {
        var store = Store(configured: false);
        store.Update(new ChatResponseSettingsUpdate(Enabled: true));

        var snapshot = store.Update(new ChatResponseSettingsUpdate(Reset: true));

        Assert.False(snapshot.Enabled);
        Assert.False(snapshot.Overridden);
        Assert.Equal("Configuration", snapshot.Source);
    }

    /// <summary>
    /// The safety property the whole type exists for. A new store instance is a restarted process: no
    /// override file, no persisted state, so automatic written replies come back off even though the
    /// previous process had them on.
    /// </summary>
    [Fact]
    public void Restart_ReturnsToConfiguredDisabled_BecauseNothingIsPersisted()
    {
        var before = Store(configured: false);
        before.Update(new ChatResponseSettingsUpdate(Enabled: true));
        Assert.True(before.Get().Enabled);

        var afterRestart = Store(configured: false);

        Assert.False(afterRestart.Get().Enabled);
    }

    [Fact]
    public void Restart_PreservesAnEnabledConfiguredValue()
    {
        var afterRestart = Store(configured: true);

        Assert.True(afterRestart.Get().Enabled);
        Assert.False(afterRestart.Get().Overridden);
    }

    [Fact]
    public void Update_WithNoFields_LeavesTheCurrentValueAlone()
    {
        var store = Store(configured: true);

        var snapshot = store.Update(new ChatResponseSettingsUpdate());

        Assert.True(snapshot.Enabled);
        Assert.False(snapshot.Overridden);
    }

    [Fact]
    public void Reset_TakesPrecedenceOverAnEnabledValueInTheSameRequest()
    {
        var store = Store(configured: false);
        store.Update(new ChatResponseSettingsUpdate(Enabled: true));

        var snapshot = store.Update(new ChatResponseSettingsUpdate(Enabled: true, Reset: true));

        Assert.False(snapshot.Enabled);
        Assert.False(snapshot.Overridden);
    }

    [Fact]
    public void RepeatedEnable_IsIdempotent()
    {
        var store = Store(configured: false);

        store.Update(new ChatResponseSettingsUpdate(Enabled: true));
        var second = store.Update(new ChatResponseSettingsUpdate(Enabled: true));

        Assert.True(second.Enabled);
        Assert.True(second.Overridden);
    }

    [Fact]
    public void Snapshots_AreImmutableValues()
    {
        var store = Store(configured: false);
        var before = store.Update(new ChatResponseSettingsUpdate(Enabled: true));

        store.Update(new ChatResponseSettingsUpdate(Enabled: false));

        Assert.True(before.Enabled);
        Assert.Equal("RuntimeOverride", before.Source);
    }

    /// <summary>
    /// Two readers racing a writer must never see a half-applied switch. Every snapshot a caller receives
    /// is internally consistent, which is what lets the gate read the switch once and act on it.
    /// </summary>
    [Fact]
    public void ConcurrentUpdates_AndReads_NeverObserveAnInconsistentSnapshot()
    {
        var store = Store(configured: false);
        var inconsistent = 0;

        Parallel.For(0, 400, index =>
        {
            if (index % 3 == 0)
                store.Update(new ChatResponseSettingsUpdate(Enabled: index % 6 == 0));

            var snapshot = store.Get();
            var expected = snapshot.Overridden ? snapshot.Enabled : snapshot.ConfiguredEnabled;
            if (snapshot.Enabled != expected) Interlocked.Increment(ref inconsistent);
        });

        Assert.Equal(0, inconsistent);
    }

    private static ChatResponseSettingsStore Store(bool configured) =>
        new(
            Options.Create(new ChatResponseOptions { Enabled = configured }),
            NullLogger<ChatResponseSettingsStore>.Instance);
}