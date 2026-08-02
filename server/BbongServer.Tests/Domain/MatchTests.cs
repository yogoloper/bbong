using System;
using BbongServer.Domain.Matches;
using NUnit.Framework;

namespace BbongServer.Tests.Domain;

[TestFixture]
public class MatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static Match NewMatch(int stake = 1000, int playerCount = 4) =>
        Match.Start(Guid.NewGuid(), Guid.NewGuid(), stake, playerCount, Now);

    [Test]
    public void Start_creates_in_progress_match()
    {
        var match = NewMatch();

        Assert.That(match.Status, Is.EqualTo(MatchStatus.InProgress));
        Assert.That(match.SettledAt, Is.Null);
    }

    [Test]
    public void Settle_win_returns_share_and_marks_settled()
    {
        var match = NewMatch(stake: 1000, playerCount: 4);

        var payout = match.Settle(won: true, winnersCount: 1, Now.AddMinutes(5));

        Assert.That(payout, Is.EqualTo(4000)); // stake × 인원, 단독 1등
        Assert.That(match.Status, Is.EqualTo(MatchStatus.Settled));
        Assert.That(match.SettledAt, Is.EqualTo(Now.AddMinutes(5)));
    }

    [Test]
    public void Settle_loss_returns_zero_but_marks_settled()
    {
        var match = NewMatch();

        var payout = match.Settle(won: false, winnersCount: 1, Now);

        Assert.That(payout, Is.EqualTo(0));
        Assert.That(match.Status, Is.EqualTo(MatchStatus.Settled));
    }

    [Test]
    public void Settle_twice_throws()
    {
        var match = NewMatch();
        match.Settle(won: true, winnersCount: 1, Now);

        Assert.Throws<InvalidOperationException>(() => match.Settle(won: true, winnersCount: 1, Now));
    }

    [Test]
    public void Settle_rejects_winners_count_out_of_range()
    {
        var match = NewMatch(playerCount: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => match.Settle(won: true, winnersCount: 0, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.Settle(won: true, winnersCount: 5, Now));
    }
}
