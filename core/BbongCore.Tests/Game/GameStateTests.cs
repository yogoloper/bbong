using BbongCore.Game;
using NUnit.Framework;

namespace BbongCore.Tests.Game;

[TestFixture]
public class GameStateTests
{
    [Test]
    public void Start_has_zero_debts_and_no_rounds_played()
    {
        var game = GameState.Start(playerCount: 3);

        Assert.That(game.CumulativeDebts, Is.EqualTo(new[] { 0, 0, 0 }));
        Assert.That(game.RoundsPlayed, Is.EqualTo(0));
        Assert.That(game.IsSetOver, Is.False);
    }

    [Test]
    public void ApplyRoundScores_accumulates_debt_and_counts_round()
    {
        var game = GameState.Start(3)
            .ApplyRoundScores(new[] { 19, 0, 8 })
            .ApplyRoundScores(new[] { -100, 12, 5 });

        Assert.That(game.CumulativeDebts, Is.EqualTo(new[] { -81, 12, 13 }));
        Assert.That(game.RoundsPlayed, Is.EqualTo(2));
    }

    [Test]
    public void ApplyRoundScores_does_not_mutate_original()
    {
        var game = GameState.Start(3);

        game.ApplyRoundScores(new[] { 5, 5, 5 });

        Assert.That(game.CumulativeDebts, Is.EqualTo(new[] { 0, 0, 0 }));
        Assert.That(game.RoundsPlayed, Is.EqualTo(0));
    }

    [Test]
    public void Set_is_over_after_five_rounds()
    {
        var game = GameState.Start(2);
        for (var i = 0; i < 5; i++)
        {
            game = game.ApplyRoundScores(new[] { 1, 2 });
        }

        Assert.That(game.IsSetOver, Is.True);
    }

    [Test]
    public void WinnerSeats_is_the_lowest_cumulative_debt()
    {
        // 빚 최저(가장 많이 탕감)가 1등 (rules.md §8)
        var game = GameState.Start(3)
            .ApplyRoundScores(new[] { 20, -50, 10 });

        Assert.That(game.WinnerSeats(), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void WinnerSeats_returns_all_tied_seats()
    {
        var game = GameState.Start(3)
            .ApplyRoundScores(new[] { -30, -30, 10 });

        Assert.That(game.WinnerSeats(), Is.EqualTo(new[] { 0, 1 }));
    }
}
