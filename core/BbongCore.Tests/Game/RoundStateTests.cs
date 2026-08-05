using System;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Game;
using NUnit.Framework;

namespace BbongCore.Tests.Game;

[TestFixture]
public class RoundStateTests
{
    private static RoundState Deal(int playerCount, int dealerSeat = 0) =>
        RoundState.Deal(Deck.CreateStandard(), playerCount, new SeededRandom(42), dealerSeat);

    private static Card Card(int n, CardColor c) => new(n, c);

    private static Hand HandOf(params Card[] cards) => new(cards);

    [Test]
    public void Restore_builds_round_from_explicit_state()
    {
        // 지정 손패/더미/턴 그대로 복원(튜토리얼 시나리오·상태 복원용)
        var players = new[]
        {
            new Player(0, HandOf(Card(8, CardColor.Red), Card(8, CardColor.Blue))),
            new Player(1, HandOf(Card(3, CardColor.Green))),
            new Player(2, HandOf(Card(4, CardColor.Red)))
        };

        // 직전 버림자 = seat1(현재 턴 2의 앞) → seat0이 8 페어로 뽕 가능
        var round = RoundState.Restore(players, new[] { Card(5, CardColor.Yellow) },
            new[] { Card(8, CardColor.Green) }, currentSeat: 2, new SeededRandom(1));

        Assert.That(round.CurrentSeat, Is.EqualTo(2));
        Assert.That(round.Players[0].Hand.Cards, Is.EqualTo(players[0].Hand.Cards));
        Assert.That(round.DrawPile.Single(), Is.EqualTo(Card(5, CardColor.Yellow)));
        Assert.That(round.DiscardPile.Last(), Is.EqualTo(Card(8, CardColor.Green)));
        Assert.That(round.CanPong(0), Is.True); // 복원 상태 위에서 규칙 판정이 그대로 동작
    }

    [Test]
    public void Deal_gives_each_player_five_cards()
    {
        var round = Deal(playerCount: 4);

        Assert.That(round.Players.Count, Is.EqualTo(4));
        Assert.That(round.Players.Select(p => p.Hand.Count), Is.All.EqualTo(5));
    }

    [Test]
    public void Deal_draw_pile_holds_the_rest()
    {
        // 48 - (4 × 5) = 28
        var round = Deal(playerCount: 4);

        Assert.That(round.DrawPile.Count, Is.EqualTo(28));
    }

    [Test]
    public void Deal_discard_pile_starts_empty()
    {
        // 셋업 시 미오픈, 선의 첫 버림으로 시작 (rules.md §2)
        var round = Deal(playerCount: 4);

        Assert.That(round.DiscardPile.Count, Is.EqualTo(0));
    }

    [Test]
    public void Deal_does_not_lose_or_duplicate_cards()
    {
        var round = Deal(playerCount: 5);

        var all = round.Players.SelectMany(p => p.Hand.Cards)
            .Concat(round.DrawPile)
            .Concat(round.DiscardPile)
            .ToList();

        Assert.That(all.Count, Is.EqualTo(48));
        Assert.That(all.Distinct().Count(), Is.EqualTo(48));
    }

    [Test]
    public void Deal_seats_players_in_order()
    {
        var round = Deal(playerCount: 3);

        Assert.That(round.Players.Select(p => p.Seat), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void Deal_starts_turn_at_dealer_seat()
    {
        // 선부터 진행 (rules.md §2)
        var round = Deal(playerCount: 4, dealerSeat: 0);

        Assert.That(round.CurrentSeat, Is.EqualTo(0));
    }

    // ── 드로우: 바닥 더미 1장 → 현재 플레이어 손패 (5→6) (rules.md §3) ──

    [Test]
    public void Draw_moves_top_of_draw_pile_into_current_hand()
    {
        var round = Deal(playerCount: 4);
        var top = round.DrawPile[0];

        var after = round.Draw();

        Assert.That(after.Players[0].Hand.Count, Is.EqualTo(6));
        Assert.That(after.DrawPile.Count, Is.EqualTo(27));
        Assert.That(after.Players[0].Hand.Contains(top), Is.True);
    }

    [Test]
    public void Draw_does_not_change_turn_or_other_players()
    {
        var round = Deal(playerCount: 4);

        var after = round.Draw();

        Assert.That(after.CurrentSeat, Is.EqualTo(0));
        Assert.That(after.Players[1].Hand.Count, Is.EqualTo(5));
    }

    [Test]
    public void Draw_does_not_mutate_original_round()
    {
        var round = Deal(playerCount: 4);

        round.Draw();

        Assert.That(round.Players[0].Hand.Count, Is.EqualTo(5));
        Assert.That(round.DrawPile.Count, Is.EqualTo(28));
    }

    // ── 버림: 손패 1장 → 버림 더미 (6→5), 다음 좌석으로 (rules.md §3) ──

    [Test]
    public void Discard_moves_card_to_discard_pile_and_returns_to_five()
    {
        var round = Deal(playerCount: 4).Draw();
        var toDiscard = round.Players[0].Hand.Cards[0];

        var after = round.Discard(toDiscard);

        Assert.That(after.Players[0].Hand.Count, Is.EqualTo(5));
        Assert.That(after.Players[0].Hand.Contains(toDiscard), Is.False);
        Assert.That(after.DiscardPile, Does.Contain(toDiscard));
    }

    [Test]
    public void Discard_advances_turn_to_next_seat()
    {
        var round = Deal(playerCount: 4).Draw();
        var toDiscard = round.Players[0].Hand.Cards[0];

        var after = round.Discard(toDiscard);

        Assert.That(after.CurrentSeat, Is.EqualTo(1));
    }

    [Test]
    public void Discard_wraps_turn_from_last_seat_to_first()
    {
        var round = Deal(playerCount: 4, dealerSeat: 3).Draw();
        var toDiscard = round.Players[3].Hand.Cards[0];

        var after = round.Discard(toDiscard);

        Assert.That(after.CurrentSeat, Is.EqualTo(0));
    }

    // ── 재셔플 한도(2회) 초과 시 드로우 불가 (rules.md §3) ──

    [Test]
    public void CanDraw_false_when_reshuffle_limit_reached_and_pile_empty()
    {
        var players = new[] { new Player(0, HandOf(Card(3, CardColor.Red), Card(4, CardColor.Red))) };
        var discard = new[] { Card(5, CardColor.Blue), Card(6, CardColor.Green), Card(8, CardColor.Yellow) };
        // 바닥 0장 + 이미 2회 재셔플함 → 더 못 뽑음
        var round = new RoundState(players, Array.Empty<Card>(), discard, 0, new SeededRandom(1), reshuffles: 2);

        Assert.That(round.CanDraw, Is.False);
        Assert.That(() => round.Draw(), Throws.InstanceOf<System.InvalidOperationException>());
    }

    [Test]
    public void Draw_increments_reshuffle_count_when_pile_empty()
    {
        var players = new[] { new Player(0, HandOf(Card(3, CardColor.Red), Card(4, CardColor.Red))) };
        var discard = new[] { Card(5, CardColor.Blue), Card(6, CardColor.Green), Card(8, CardColor.Yellow) };
        var round = new RoundState(players, Array.Empty<Card>(), discard, 0, new SeededRandom(1), reshuffles: 1);

        Assert.That(round.CanDraw, Is.True);
        var after = round.Draw();
        Assert.That(after.ReshuffleCount, Is.EqualTo(2));
    }

    // ── 바닥 더미 소진 재셔플: 버림 더미 맨 위 1장 남기고 나머지 셔플 (rules.md §3) ──

    [Test]
    public void Draw_reshuffles_discard_pile_when_draw_pile_empty()
    {
        var players = new[]
        {
            new Player(0, HandOf(Card(3, CardColor.Red), Card(3, CardColor.Blue))),
            new Player(1, HandOf(Card(7, CardColor.Red), Card(7, CardColor.Blue)))
        };
        // 버림 더미 3장(맨 위 = 마지막 = 8Y), 바닥 더미 0장
        var discard = new[] { Card(5, CardColor.Blue), Card(6, CardColor.Green), Card(8, CardColor.Yellow) };
        var round = new RoundState(players, Array.Empty<Card>(), discard, 0, new SeededRandom(1));

        var after = round.Draw();

        // 맨 위 8Y는 버림 더미에 남고, 5B·6G가 셔플돼 바닥 더미(2장) → 1장 드로우 → 1장 남음
        Assert.That(after.DiscardPile.Count, Is.EqualTo(1));
        Assert.That(after.DiscardPile[0], Is.EqualTo(Card(8, CardColor.Yellow)));
        Assert.That(after.DrawPile.Count, Is.EqualTo(1));
        Assert.That(after.CurrentPlayer.Hand.Count, Is.EqualTo(3));
    }
}
