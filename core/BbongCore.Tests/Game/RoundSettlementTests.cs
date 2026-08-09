using System;
using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Rules;
using NUnit.Framework;

namespace BbongCore.Tests.Game;

[TestFixture]
public class RoundSettlementTests
{
    private static Card C(int n) => new(n, CardColor.Red);
    private static Hand HandOf(params int[] numbers) => new(Array.ConvertAll(numbers, C));

    private static RoundState Round(params Player[] players) =>
        new(players, Array.Empty<Card>(), Array.Empty<Card>(), 0, new SeededRandom(1));

    // ── 족보 종료 (rules.md §8) ──

    [Test]
    public void SettleByMeld_winner_gets_meld_score_others_get_hand_sum()
    {
        var round = Round(
            new Player(0, HandOf(3, 7, 9)),           // 19
            new Player(1, HandOf(1, 2, 3, 4, 5, 6)),  // 스트레이트 승자
            new Player(2, HandOf(2, 4)),              // 6
            new Player(3, HandOf(10, 11)));           // 21

        var scores = RoundSettlement.SettleByMeld(round, winnerSeat: 1,
            new MeldResult(MeldType.Straight, -21));

        Assert.That(scores, Is.EqualTo(new[] { 19, -21, 6, 21 }));
    }

    [Test]
    public void SettleByMeld_ttoittoi_winner_scores_zero()
    {
        var round = Round(
            new Player(0, HandOf(3, 7, 9)),                  // 19
            new Player(1, HandOf(12, 12, 11, 11, 10, 10)));  // 또이또이 승자 → 0

        var scores = RoundSettlement.SettleByMeld(round, winnerSeat: 1,
            new MeldResult(MeldType.Ttoittoi, 0));

        Assert.That(scores, Is.EqualTo(new[] { 19, 0 }));
    }

    // ── 두 번 뽕 종료 (rules.md §4-3, §7) ──

    [Test]
    public void SettleByTwoPong_discarder_plus_20_others_hand_sum()
    {
        // 뽕 바가지: 먹인 승자 0(빈손) / 당한(버린) 자 손합+20 / 나머지는 손합
        var round = Round(
            new Player(0, HandOf(3, 7, 9)),  // 마지막 버린 자 → 19 + 20(박)
            new Player(1, HandOf()),         // 두 번 뽕 승자(빈 손) → 0
            new Player(2, HandOf(2, 4)));    // 구경꾼 → 6

        var scores = RoundSettlement.SettleByTwoPong(round, winnerSeat: 1, lastDiscarderSeat: 0);

        Assert.That(scores, Is.EqualTo(new[] { 39, 0, 6 }));
    }

    [Test]
    public void SettleByStop_bagaji_winner_zero_victim_plus_30_others_hand_sum()
    {
        // seat0 스톱(합 8) vs seat1 뽕 합 3(먹인 승자) vs seat2 구경꾼 합 6
        var round = Round(
            new Player(0, HandOf(3, 5), PongCount: 1),   // 당한 선언자 → 8 + 30
            new Player(1, HandOf(1, 2), PongCount: 1),   // 먹인 승자 → 0
            new Player(2, HandOf(2, 4)));                // 구경꾼 → 6

        var scores = RoundSettlement.SettleByStop(round, stopSeat: 0);

        Assert.That(scores, Is.EqualTo(new[] { 38, 0, 6 }));
    }

    // ── 강제 종료(바닥 소진): 전원 손패 합 ──

    [Test]
    public void SettleByExhaustion_everyone_scores_hand_sum()
    {
        var round = Round(
            new Player(0, HandOf(3, 7, 9)),  // 19
            new Player(1, HandOf(2, 4)),     // 6
            new Player(2, HandOf(5)));       // 5

        var scores = RoundSettlement.SettleByExhaustion(round);

        Assert.That(scores, Is.EqualTo(new[] { 19, 6, 5 }));
    }

    // ── 스톱 종료 (rules.md §6, §8) ──

    [Test]
    public void SettleByStop_without_bagaji_everyone_scores_hand_sum()
    {
        var round = Round(
            new Player(0, HandOf(1, 2), PongCount: 1),       // 3 (최저, 스톱 선언자)
            new Player(1, HandOf(2, 4), PongCount: 1),       // 6
            new Player(2, HandOf(6, 8, 1, 2, 5)));           // 22 (미뽕)

        var scores = RoundSettlement.SettleByStop(round, stopSeat: 0);

        Assert.That(scores, Is.EqualTo(new[] { 3, 6, 22 }));
    }

}
