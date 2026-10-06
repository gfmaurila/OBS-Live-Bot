using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.UnitTests.ChatResponses;

public sealed class ChatResponseCooldownTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstReplyOnAChannelIsAlwaysAccepted()
    {
        var tracker = Tracker();

        Assert.True(tracker.Check(LiveChatProviderType.Twitch, "channel", "user", Now).Accepted);
    }

    [Fact]
    public void SecondReplyInsideTheGlobalWindowIsRefused()
    {
        var tracker = Tracker(globalSeconds: 8);
        tracker.Commit(LiveChatProviderType.Twitch, "channel", "user", Now);

        var refused = tracker.Check(LiveChatProviderType.Twitch, "channel", "other", Now.AddSeconds(5));

        Assert.False(refused.Accepted);
        Assert.Equal("GLOBAL_COOLDOWN", refused.Reason);
    }

    [Fact]
    public void GlobalWindowRefusesEvenForADifferentUser()
    {
        // The written reply is public, so one viewer spending the whole budget is the failure this stops.
        var tracker = Tracker(globalSeconds: 8);
        tracker.Commit(LiveChatProviderType.Twitch, "channel", "user-a", Now);

        Assert.False(tracker.Check(LiveChatProviderType.Twitch, "channel", "user-b", Now.AddSeconds(1)).Accepted);
    }

    [Fact]
    public void ReplyIsAcceptedOnceBothWindowsElapse()
    {
        var tracker = Tracker(globalSeconds: 8, userSeconds: 20);
        tracker.Commit(LiveChatProviderType.Twitch, "channel", "user", Now);

        Assert.True(tracker.Check(LiveChatProviderType.Twitch, "channel", "user", Now.AddSeconds(20)).Accepted);
    }

    [Fact]
    public void UserWindowOutlivesTheGlobalWindowAndIsReportedSeparately()
    {
        var tracker = Tracker(globalSeconds: 5, userSeconds: 30);
        tracker.Commit(LiveChatProviderType.Twitch, "channel", "user", Now);

        // Past the global window, so only the per-user gate can still refuse.
        var refused = tracker.Check(LiveChatProviderType.Twitch, "channel", "user", Now.AddSeconds(10));

        Assert.False(refused.Accepted);
        Assert.Equal("USER_COOLDOWN", refused.Reason);
    }

    [Fact]
    public void UserWindowDoesNotApplyToAnotherUser()
    {
        var tracker = Tracker(globalSeconds: 5, userSeconds: 30);
        tracker.Commit(LiveChatProviderType.Twitch, "channel", "user-a", Now);

        Assert.True(tracker.Check(LiveChatProviderType.Twitch, "channel", "user-b", Now.AddSeconds(10)).Accepted);
    }

    [Fact]
    public void CooldownsAreScopedPerChannel()
    {
        var tracker = Tracker(globalSeconds: 60);
        tracker.Commit(LiveChatProviderType.Twitch, "channel-a", "user", Now);

        Assert.True(tracker.Check(LiveChatProviderType.Twitch, "channel-b", "user", Now.AddSeconds(1)).Accepted);
    }

    [Fact]
    public void CooldownsAreScopedPerProvider()
    {
        var tracker = Tracker(globalSeconds: 60);
        tracker.Commit(LiveChatProviderType.Twitch, "channel", "user", Now);

        Assert.True(tracker.Check(LiveChatProviderType.Kick, "channel", "user", Now.AddSeconds(1)).Accepted);
    }

    [Fact]
    public void ReplyWithoutAUserIdIsGovernedOnlyByTheGlobalWindow()
    {
        var tracker = Tracker(globalSeconds: 8, userSeconds: 300);
        tracker.Commit(LiveChatProviderType.Twitch, "channel", null, Now);

        Assert.Equal("GLOBAL_COOLDOWN", tracker.Check(
            LiveChatProviderType.Twitch, "channel", null, Now.AddSeconds(5)).Reason);
        // The per-user window cannot apply to a reply with no identified viewer, so once the global
        // window elapses the reply is free.
        Assert.True(tracker.Check(LiveChatProviderType.Twitch, "channel", null, Now.AddSeconds(8)).Accepted);
    }

    [Fact]
    public void ZeroWindowsDisableRateLimitingEntirely()
    {
        var tracker = Tracker(globalSeconds: 0, userSeconds: 0);
        tracker.Commit(LiveChatProviderType.Twitch, "channel", "user", Now);

        Assert.True(tracker.Check(LiveChatProviderType.Twitch, "channel", "user", Now).Accepted);
    }

    [Fact]
    public void EntryRingIsBoundedAndEvictsOldestFirst()
    {
        var tracker = Tracker(globalSeconds: 0, capacity: 2);

        tracker.Commit(LiveChatProviderType.Twitch, "a", null, Now);
        tracker.Commit(LiveChatProviderType.Twitch, "b", null, Now);
        tracker.Commit(LiveChatProviderType.Twitch, "c", null, Now);

        Assert.Equal(2, tracker.Count);
    }

    [Fact]
    public void CapacityIsNeverBelowOne()
    {
        Assert.Equal(1, Tracker(capacity: 0).Capacity);
    }

    [Fact]
    public void RepeatedCommitToTheSameKeyUpdatesItInPlaceInsteadOfGrowingTheRing()
    {
        var tracker = Tracker(globalSeconds: 30, capacity: 4);
        tracker.Commit(LiveChatProviderType.Twitch, "channel", "user", Now);

        tracker.Commit(LiveChatProviderType.Twitch, "channel", "user", Now.AddSeconds(20));

        Assert.Equal(2, tracker.Count);
        // The window restarted at the newer commit, so the reply is refused again.
        Assert.False(tracker.Check(LiveChatProviderType.Twitch, "channel", "user", Now.AddSeconds(25)).Accepted);
    }

    [Fact]
    public void ConcurrentCommitsNeverLoseOrCorruptAnEntry()
    {
        var tracker = Tracker(globalSeconds: 0, capacity: 64);

        Parallel.For(0, 500, index =>
            tracker.Commit(LiveChatProviderType.Twitch, $"channel-{index % 32}", $"user-{index % 16}", Now));

        Assert.InRange(tracker.Count, 1, 64);
    }

    private static ChatResponseCooldownTracker Tracker(
        int globalSeconds = 8,
        int userSeconds = 30,
        int capacity = 500) =>
        new(Options.Create(new ChatResponseOptions
        {
            GlobalCooldownSeconds = globalSeconds,
            UserCooldownSeconds = userSeconds,
            CooldownCapacity = capacity
        }));
}
