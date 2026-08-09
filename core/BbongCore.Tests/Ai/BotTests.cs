using System;
using BbongCore.Ai;
using BbongCore.Cards;
using BbongCore.Game;
using NUnit.Framework;

namespace BbongCore.Tests.Ai;

[TestFixture]
public class BotTests
{
    /// <summary>Chance 판정을 강제하는 스텁: 0=항상 성공(스톱), max-1=항상 실패(보류).</summary>
    private sealed class FixedRandom : BbongCore.Cards.IRandom
    {
        private readonly int[] _values;
        private int _i;
        public FixedRandom(params int[] values) => _values = values;
        public int Next(int maxExclusive) => _values[Math.Min(_i++, _values.Length - 1)] % maxExclusive;
    }

    private static Card C(int n, CardColor color = CardColor.Red) => new(n, color);
    private static Hand HandOf(params Card[] cards) => new(cards);

    /// <summary>선언자 손패가 같은 숫자 쌍인 스톱 국면.</summary>
    private static RoundState PairStopRound(int pairNumber, int otherSum)
    {
        var players = new[]
        {
            new Player(0, HandOf(C(pairNumber, CardColor.Red), C(pairNumber, CardColor.Blue)), PongCount: 1),
            new Player(1, HandOf(C(otherSum / 2), C(otherSum - otherSum / 2)), PongCount: 1)
        };
        return RoundState.Restore(players, Array.Empty<Card>(), Array.Empty<Card>(), 0, new SeededRandom(1));
    }

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
    public void Normal_stop_is_probabilistic_by_hand_sum()
    {
        Assert.That(new Bot(BotDifficulty.Normal).ShouldPong(), Is.True);
        // 운이 좋으면(roll 0) 낮은 합에서 스톱, 나쁘면(roll 99) 보류 — 확률 밴드 동작
        Assert.That(new Bot(BotDifficulty.Normal, new FixedRandom(0))
            .ShouldStop(StopRound(declarerSum: 5, otherSum: 3), seat: 0), Is.True);
        Assert.That(new Bot(BotDifficulty.Normal, new FixedRandom(99))
            .ShouldStop(StopRound(declarerSum: 5, otherSum: 3), seat: 0), Is.False);
        // 높은 합(8)도 낮은 확률로는 지를 수 있고, 대개는 보류
        Assert.That(new Bot(BotDifficulty.Normal, new FixedRandom(0))
            .ShouldStop(StopRound(declarerSum: 8, otherSum: 9), seat: 0), Is.True);
        Assert.That(new Bot(BotDifficulty.Normal, new FixedRandom(50))
            .ShouldStop(StopRound(declarerSum: 8, otherSum: 9), seat: 0), Is.False);
    }

    // ── Hard ──

    [Test]
    public void Hard_avoids_stop_when_it_would_be_bagaji()
    {
        // 선언자 합 8 > 상대 5 → 바가지 → 운과 무관하게 절대 스톱 안 함
        Assert.That(new Bot(BotDifficulty.Hard, new FixedRandom(0))
            .ShouldStop(StopRound(declarerSum: 8, otherSum: 5), seat: 0), Is.False);
    }

    [Test]
    public void Hard_stop_is_probabilistic_when_safe()
    {
        Assert.That(new Bot(BotDifficulty.Hard, new FixedRandom(0))
            .ShouldStop(StopRound(declarerSum: 5, otherSum: 9), seat: 0), Is.True);
        Assert.That(new Bot(BotDifficulty.Hard, new FixedRandom(99))
            .ShouldStop(StopRound(declarerSum: 5, otherSum: 9), seat: 0), Is.False);
    }

    [Test]
    public void Hard_holds_a_pair_hoping_for_a_second_pong()
    {
        // 손패가 쌍(4·4, 합 8)이면 보류 굴림이 먼저 — 성공 시 두 번 뽕(손 털기·상대 박)을 노린다
        var round = PairStopRound(pairNumber: 4, otherSum: 12);
        Assert.That(new Bot(BotDifficulty.Hard, new FixedRandom(0, 0))
            .ShouldStop(round, seat: 0), Is.False); // 첫 굴림 0 → 쌍 보류 성공
        Assert.That(new Bot(BotDifficulty.Hard, new FixedRandom(99, 0))
            .ShouldStop(round, seat: 0), Is.True);  // 보류 실패 → 스톱 굴림 성공
    }

    [Test]
    public void Hard_dumps_high_card_in_post_pong_endgame()
    {
        var bot = new Bot(BotDifficulty.Hard);

        // 뽕 이후 3장: 족보(6장) 불가 → 연속(10·11) 보존 무의미. 고점 11 버리고 저점 지향.
        var discard = bot.ChooseDiscard(HandOf(C(10), C(11), C(3)));

        Assert.That(discard.Number, Is.EqualTo(11));
    }

    [Test]
    public void Hard_keeps_run_pieces_and_discards_isolated_card()
    {
        var bot = new Bot(BotDifficulty.Hard);

        // 9·10·11은 연속(스트레이트 노림) → 보존. 고립된 {2,5} 중 최고 5 버림.
        // (Normal이라면 최대 단일 11을 버려 run을 깨뜨림)
        var discard = bot.ChooseDiscard(HandOf(C(10), C(11), C(2), C(5), C(9)));

        Assert.That(discard.Number, Is.EqualTo(5));
    }

    [Test]
    public void Normal_breaks_run_by_discarding_highest_single()
    {
        var bot = new Bot(BotDifficulty.Normal);

        // Normal은 run을 모르고 최대 단일 11 버림 → Hard와 대비
        var discard = bot.ChooseDiscard(HandOf(C(10), C(11), C(2), C(5), C(9)));

        Assert.That(discard.Number, Is.EqualTo(11));
    }

    [Test]
    public void Hard_still_keeps_pairs()
    {
        var bot = new Bot(BotDifficulty.Hard);

        // 11 쌍 보존, 고립 단일 {2,5,9} 중 9 버림
        var discard = bot.ChooseDiscard(HandOf(
            C(11, CardColor.Red), C(11, CardColor.Blue), C(2), C(5), C(9)));

        Assert.That(discard.Number, Is.EqualTo(9));
    }
}
