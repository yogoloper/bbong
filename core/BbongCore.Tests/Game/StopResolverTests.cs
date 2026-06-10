using System;
using BbongCore.Cards;
using BbongCore.Game;
using NUnit.Framework;

namespace BbongCore.Tests.Game;

[TestFixture]
public class StopResolverTests
{
    private static Card C(int n) => new(n, CardColor.Red);
    private static Hand HandOf(params int[] numbers) => new(Array.ConvertAll(numbers, C));

    private static RoundState Round(params Player[] players) =>
        new(players, Array.Empty<Card>(), Array.Empty<Card>(), 0, new SeededRandom(1));

    [Test]
    public void CanStop_true_when_two_ponged_and_sum_within_limit()
    {
        var round = Round(
            new Player(0, HandOf(3, 4), PongCount: 1), // 합 7
            new Player(1, HandOf(2, 4), PongCount: 1),
            new Player(2, HandOf(6, 8, 1, 2, 5)));      // 미뽕

        Assert.That(StopResolver.CanStop(round, seat: 0), Is.True);
    }

    [Test]
    public void CanStop_false_when_fewer_than_two_ponged()
    {
        var round = Round(
            new Player(0, HandOf(3, 4), PongCount: 1),
            new Player(1, HandOf(2, 4, 6, 8, 1)));      // 미뽕 → 뽕한 유저 1명뿐

        Assert.That(StopResolver.CanStop(round, seat: 0), Is.False);
    }

    [Test]
    public void CanStop_false_when_sum_exceeds_limit()
    {
        var round = Round(
            new Player(0, HandOf(6, 8), PongCount: 1),  // 합 14 > 10
            new Player(1, HandOf(2, 4), PongCount: 1));

        Assert.That(StopResolver.CanStop(round, seat: 0, stopLimit: 10), Is.False);
    }

    [Test]
    public void CanStop_respects_custom_stop_limit()
    {
        var round = Round(
            new Player(0, HandOf(3, 4), PongCount: 1),  // 합 7 > 5
            new Player(1, HandOf(2, 1), PongCount: 1));

        Assert.That(StopResolver.CanStop(round, seat: 0, stopLimit: 5), Is.False);
    }

    [Test]
    public void IsBagaji_true_when_other_ponged_player_has_lower_sum()
    {
        var round = Round(
            new Player(0, HandOf(3, 4), PongCount: 1),  // 합 7 (스톱 선언자)
            new Player(1, HandOf(2, 4), PongCount: 1));  // 합 6 < 7 → 바가지

        Assert.That(StopResolver.IsBagaji(round, stopSeat: 0), Is.True);
    }

    [Test]
    public void IsBagaji_false_when_stop_declarer_has_lowest_sum()
    {
        var round = Round(
            new Player(0, HandOf(1, 2), PongCount: 1),  // 합 3 (최저)
            new Player(1, HandOf(2, 4), PongCount: 1));

        Assert.That(StopResolver.IsBagaji(round, stopSeat: 0), Is.False);
    }
}
