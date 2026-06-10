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
}
