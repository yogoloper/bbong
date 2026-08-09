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

    // ── 카드 카운팅: 공개 정보(버림·나간 패·내 손)로 스톱 바가지 위험 추정 ──

    /// <summary>내(합 4) + 뽕한 상대 1명. unseenLow=true면 미공개 풀이 저카드 천지(위험), false면 11·12뿐(안전).</summary>
    private static RoundState CountingRound(bool unseenLow)
    {
        var me = new Player(0, HandOf(C(1, CardColor.Red), C(3, CardColor.Red)), PongCount: 1);
        var opp = new Player(1, HandOf(C(6, CardColor.Red), C(6, CardColor.Blue)), PongCount: 1);
        // 미공개 풀 조작: 위험 케이스는 1·2만 남기고 전부 공개(버림), 안전 케이스는 11·12만 남김
        var all = Deck.CreateStandard().Cards.ToList();
        var mine = new[] { C(1, CardColor.Red), C(3, CardColor.Red), C(6, CardColor.Red), C(6, CardColor.Blue) };
        bool Unseen(Card c) => unseenLow ? c.Number <= 2 : c.Number >= 11;
        var visible = all.Where(c => !mine.Contains(c) && !Unseen(c)).ToList();
        var unseenPool = all.Where(c => !mine.Contains(c) && Unseen(c)).ToList();
        return RoundState.Restore(new[] { me, opp }, unseenPool, visible, 0, new SeededRandom(1));
    }

    [Test]
    public void Bagaji_risk_reflects_unseen_card_pool()
    {
        // 위험 풀: 미공개 9장(저카드 7 + 상대 숨은 2장) → 합<4 쌍 15/36 ≈ 0.417
        Assert.That(Bot.EstimateBagajiRisk(CountingRound(unseenLow: true), 0), Is.EqualTo(15.0 / 36).Within(1e-9));
        Assert.That(Bot.EstimateBagajiRisk(CountingRound(unseenLow: false), 0), Is.EqualTo(0.0));
    }

    [Test]
    public void Normal_counts_cards_to_avoid_bagaji_stops()
    {
        // 합 4(기본 30%). 안전 풀 → 30% 유지(roll 20 → 스톱). 위험 풀 → 30×(1−0.417)=17%로 급감(roll 20 → 보류)
        Assert.That(new Bot(BotDifficulty.Normal, new FixedRandom(20))
            .ShouldStop(CountingRound(unseenLow: false), 0), Is.True);
        Assert.That(new Bot(BotDifficulty.Normal, new FixedRandom(20))
            .ShouldStop(CountingRound(unseenLow: true), 0), Is.False);
    }

    // ── Hard 순위 전략: 1등이면 빨리 털고, 뒤지면 바가지 유도 ──

    private static GameState Standings(int myDebt, int otherDebt, int roundsPlayed = 2)
    {
        var g = GameState.Start(2, setRounds: 5);
        for (var i = 0; i < roundsPlayed; i++)
        {
            g = g.ApplyRoundScores(new[] { i == 0 ? myDebt : 0, i == 0 ? otherDebt : 0 });
        }

        return g;
    }

    [Test]
    public void Hard_leader_stops_more_eagerly()
    {
        var round = StopRound(declarerSum: 4, otherSum: 9); // 바가지 아님, 기본 40%
        // 1등(빚 적음) → 확률 상향: roll 60도 스톱
        Assert.That(new Bot(BotDifficulty.Hard, new FixedRandom(60))
            .ShouldStop(round, 0, Standings(myDebt: 5, otherDebt: 30)), Is.True);
        // 꼴찌 → 확률 하향: roll 25도 보류(빚 굳히기보다 한 방 노림)
        Assert.That(new Bot(BotDifficulty.Hard, new FixedRandom(25))
            .ShouldStop(round, 0, Standings(myDebt: 30, otherDebt: 5)), Is.False);
    }

    [Test]
    public void Hard_leader_does_not_gamble_on_pair_holding()
    {
        var round = PairStopRound(pairNumber: 4, otherSum: 12);
        // 1등 + 쌍: 보류 도박 대신 스톱으로 굳힌다(보류 굴림 0이어도 확률 25%라 25≥25 → 통과, 스톱 굴림 0 → 스톱)
        Assert.That(new Bot(BotDifficulty.Hard, new FixedRandom(30, 0))
            .ShouldStop(round, 0, Standings(myDebt: 0, otherDebt: 40)), Is.True);
        // 꼴찌 + 쌍: 보류 확률 85% — roll 30이면 보류(두 번 뽕 노림)
        Assert.That(new Bot(BotDifficulty.Hard, new FixedRandom(30, 0))
            .ShouldStop(round, 0, Standings(myDebt: 40, otherDebt: 0)), Is.False);
    }
}
