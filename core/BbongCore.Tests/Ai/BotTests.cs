using System;
using BbongCore.Ai;
using BbongCore.Cards;
using BbongCore.Game;
using NUnit.Framework;

namespace BbongCore.Tests.Ai;

[TestFixture]
public class BotTests
{
    private static Card C(int n, CardColor color = CardColor.Red) => new(n, color);
    private static Hand HandOf(params Card[] cards) => new(cards);

    private static RoundState StopRound(int declarerSum, int otherSum)
    {
        var players = new[]
        {
            new Player(0, new Hand(new[] { C(declarerSum - 1), C(1, CardColor.Blue) }), PongCount: 1),
            new Player(1, new Hand(new[] { C(otherSum - 1), C(1, CardColor.Green) }), PongCount: 1)
        };
        return new RoundState(players, Array.Empty<Card>(), Array.Empty<Card>(), 0, new SeededRandom(1));
    }

    // ── Easy ──

    [Test]
    public void Easy_discards_highest_card()
    {
        var bot = new Bot(BotDifficulty.Easy);

        var discard = bot.ChooseDiscard(HandOf(C(2), C(5), C(11), C(3), C(9)));

        Assert.That(discard.Number, Is.EqualTo(11));
    }

    [Test]
    public void Easy_does_not_pong_or_stop()
    {
        var bot = new Bot(BotDifficulty.Easy);

        Assert.That(bot.ShouldPong(), Is.False);
        Assert.That(bot.ShouldStop(StopRound(declarerSum: 5, otherSum: 9), seat: 0), Is.False);
    }

    // ── Normal ──

    [Test]
    public void Normal_keeps_pairs_and_discards_highest_single()
    {
        var bot = new Bot(BotDifficulty.Normal);

        // 11이 쌍 → 보존. 단일 {2,5,9} 중 최대 9 버림
        var discard = bot.ChooseDiscard(HandOf(
            C(11, CardColor.Red), C(11, CardColor.Blue), C(2), C(5), C(9)));

        Assert.That(discard.Number, Is.EqualTo(9));
    }

    [Test]
    public void Normal_pongs_and_stops_when_eligible()
    {
        var bot = new Bot(BotDifficulty.Normal);

        Assert.That(bot.ShouldPong(), Is.True);
        // 바가지여도 Normal은 스톱 (단순)
        Assert.That(bot.ShouldStop(StopRound(declarerSum: 8, otherSum: 5), seat: 0), Is.True);
    }

    // ── Hard ──

    [Test]
    public void Hard_avoids_stop_when_it_would_be_bagaji()
    {
        var bot = new Bot(BotDifficulty.Hard);

        // 선언자 합 8 > 상대 5 → 바가지 → Hard는 스톱 안 함
        Assert.That(bot.ShouldStop(StopRound(declarerSum: 8, otherSum: 5), seat: 0), Is.False);
    }

    [Test]
    public void Hard_stops_when_safe()
    {
        var bot = new Bot(BotDifficulty.Hard);

        // 선언자 합 5 < 상대 9 → 바가지 아님 → 스톱
        Assert.That(bot.ShouldStop(StopRound(declarerSum: 5, otherSum: 9), seat: 0), Is.True);
    }
}
