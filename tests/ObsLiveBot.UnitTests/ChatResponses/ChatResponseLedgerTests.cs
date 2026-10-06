using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.UnitTests.ChatResponses;

public sealed class ChatResponseLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryReserve_ClaimsAKeyOnlyOnce()
    {
        var ledger = Ledger();

        Assert.True(ledger.TryReserve("key-1", Now));
        Assert.False(ledger.TryReserve("key-1", Now));
        Assert.Equal(1, ledger.IdempotencyCount);
    }

    [Fact]
    public void Forget_ReleasesAClaimSoAnExplicitRetryIsStillPossible()
    {
        var ledger = Ledger();
        ledger.TryReserve("key-1", Now);

        ledger.Forget("key-1");

        Assert.True(ledger.TryReserve("key-1", Now));
    }

    [Fact]
    public void IdempotencyRingIsBoundedAndEvictsOldestFirst()
    {
        var ledger = Ledger(idempotencyCapacity: 2);

        ledger.TryReserve("key-1", Now);
        ledger.TryReserve("key-2", Now);
        ledger.TryReserve("key-3", Now);

        Assert.Equal(2, ledger.IdempotencyCount);
        // The oldest claim is gone, so its reply could be written twice; the newest is still protected.
        Assert.True(ledger.TryReserve("key-1", Now));
        Assert.False(ledger.TryReserve("key-3", Now));
    }

    [Fact]
    public void IdempotencyCapacityIsNeverBelowOne()
    {
        // A capacity of zero would make TryReserve permanently false and silence the capability entirely.
        var ledger = Ledger(idempotencyCapacity: 0);

        Assert.True(ledger.TryReserve("key-1", Now));
        Assert.Equal(1, ledger.IdempotencyCapacity);
    }

    [Fact]
    public void TryReserve_RejectsBlankKeys()
    {
        var ledger = Ledger();

        Assert.Throws<ArgumentException>(() => ledger.TryReserve("  ", Now));
    }

    [Fact]
    public void WasWritten_IsFalseForTextThatWasNeverWritten()
    {
        var ledger = Ledger();

        Assert.False(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "hello", Now));
    }

    [Fact]
    public void RecordWrite_MakesTheSameTextRecognizable()
    {
        var ledger = Ledger();
        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "hello", Now);

        Assert.True(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "hello", Now));
        Assert.Equal(1, ledger.EchoCount);
    }

    [Fact]
    public void RecordWrite_MatchesRegardlessOfSpacingDifferences()
    {
        // This is the loop guard's whole value: the platform's echo of our reply may be reflowed.
        var ledger = Ledger();
        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "Boa  noite", Now);

        Assert.True(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "boa noite", Now));
    }

    [Fact]
    public void WasWritten_IsScopedToProviderAndChannel()
    {
        var ledger = Ledger();
        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "hello", Now);

        Assert.False(ledger.WasWritten(LiveChatProviderType.Kick, "channel", "hello", Now));
        Assert.False(ledger.WasWritten(LiveChatProviderType.Twitch, "other", "hello", Now));
    }

    [Fact]
    public void WasWritten_IsCaseInsensitiveOnChannelIdentity()
    {
        var ledger = Ledger();
        ledger.RecordWrite(LiveChatProviderType.Twitch, "Channel", "hello", Now);

        Assert.True(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "hello", Now));
    }

    [Fact]
    public void WasWritten_StopsMatchingAfterTheEchoWindow()
    {
        var ledger = Ledger(echoWindowSeconds: 60);
        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "hello", Now);

        Assert.True(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "hello", Now.AddSeconds(59)));
        Assert.False(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "hello", Now.AddSeconds(61)));
    }

    [Fact]
    public void ZeroEchoWindowDisablesBothRecordingAndMatching()
    {
        var ledger = Ledger(echoWindowSeconds: 0);

        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "hello", Now);

        Assert.False(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "hello", Now));
        Assert.Equal(0, ledger.EchoCount);
    }

    [Fact]
    public void EchoRingIsBoundedAndEvictsOldestFirst()
    {
        var ledger = Ledger(echoCapacity: 2);

        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "one", Now);
        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "two", Now);
        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "three", Now);

        Assert.Equal(2, ledger.EchoCount);
        Assert.False(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "one", Now));
        Assert.True(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "three", Now));
    }

    [Fact]
    public void RecordWrite_DoesNotDoubleCountTheSameTextButRestartsItsWindow()
    {
        var ledger = Ledger(echoWindowSeconds: 60);
        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "hello", Now);
        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "hello", Now.AddSeconds(30));

        Assert.Equal(1, ledger.EchoCount);
        // The second delivery means a second echo to recognise, so the window must follow the newest
        // write rather than the oldest.
        Assert.True(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "hello", Now.AddSeconds(89)));
        Assert.False(ledger.WasWritten(LiveChatProviderType.Twitch, "channel", "hello", Now.AddSeconds(91)));
    }

    [Fact]
    public void EchoRingNeverDropsBelowOne()
    {
        var ledger = Ledger(echoCapacity: 0);

        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", "hello", Now);

        Assert.Equal(1, ledger.EchoCapacity);
        Assert.Equal(1, ledger.EchoCount);
    }

    [Fact]
    public void RecordWrite_IgnoresEmptyText()
    {
        var ledger = Ledger();

        ledger.RecordWrite(LiveChatProviderType.Twitch, "channel", string.Empty, Now);

        Assert.Equal(0, ledger.EchoCount);
    }

    [Fact]
    public void ConcurrentReservations_ClaimEachKeyExactlyOnce()
    {
        var ledger = Ledger(idempotencyCapacity: 500);
        var claimed = 0;

        Parallel.For(0, 200, _ =>
        {
            if (ledger.TryReserve("shared-key", Now))
                Interlocked.Increment(ref claimed);
        });

        Assert.Equal(1, claimed);
    }

    private static ChatResponseLedger Ledger(
        int idempotencyCapacity = 500,
        int echoCapacity = 200,
        int echoWindowSeconds = 120) =>
        new(Options.Create(new ChatResponseOptions
        {
            IdempotencyCapacity = idempotencyCapacity,
            EchoCapacity = echoCapacity,
            EchoWindowSeconds = echoWindowSeconds
        }));
}
