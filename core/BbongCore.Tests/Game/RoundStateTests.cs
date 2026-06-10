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
}
