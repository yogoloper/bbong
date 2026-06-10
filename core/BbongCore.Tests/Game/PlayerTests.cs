using BbongCore.Cards;
using BbongCore.Game;
using NUnit.Framework;

namespace BbongCore.Tests.Game;

[TestFixture]
public class PlayerTests
{
    private static Hand HandOf(params int[] numbers)
    {
        var cards = new Card[numbers.Length];
        for (var i = 0; i < numbers.Length; i++)
        {
            cards[i] = new Card(numbers[i], CardColor.Red);
        }

        return new Hand(cards);
    }

    [Test]
    public void New_player_starts_with_zero_debt_and_no_pong()
    {
        var player = new Player(Seat: 0, HandOf(3, 7, 9, 11, 12));

        Assert.That(player.Seat, Is.EqualTo(0));
        Assert.That(player.Hand.Count, Is.EqualTo(5));
        Assert.That(player.CumulativeDebt, Is.EqualTo(0));
        Assert.That(player.PongCount, Is.EqualTo(0));
        Assert.That(player.HasPonged, Is.False);
    }

    [Test]
    public void RecordPong_marks_has_ponged()
    {
        var player = new Player(0, HandOf(3, 7));

        var after = player.RecordPong();

        Assert.That(after.PongCount, Is.EqualTo(1));
        Assert.That(after.HasPonged, Is.True);
    }

    [Test]
    public void RecordPong_twice_counts_two_for_two_pong_finish()
    {
        // 두 번 뽕으로 손 소진(rules.md §4-3) 판정용 카운트
        var player = new Player(0, HandOf());

        var after = player.RecordPong().RecordPong();

        Assert.That(after.PongCount, Is.EqualTo(2));
    }

    [Test]
    public void WithHand_replaces_hand_and_keeps_seat_and_debt()
    {
        var player = new Player(2, HandOf(3, 7), CumulativeDebt: -50);

        var after = player.WithHand(HandOf(1, 2, 3));

        Assert.That(after.Hand.Count, Is.EqualTo(3));
        Assert.That(after.Seat, Is.EqualTo(2));
        Assert.That(after.CumulativeDebt, Is.EqualTo(-50));
    }

    [Test]
    public void AddDebt_accumulates_across_rounds()
    {
        // 1판 +19, 2판 -100 → 누적 -81 (음수 가능, rules.md §8)
        var player = new Player(0, HandOf());

        var after = player.AddDebt(19).AddDebt(-100);

        Assert.That(after.CumulativeDebt, Is.EqualTo(-81));
    }

    [Test]
    public void Operations_do_not_mutate_original_player()
    {
        var player = new Player(0, HandOf(3, 7));

        player.RecordPong();
        player.AddDebt(50);

        Assert.That(player.PongCount, Is.EqualTo(0));
        Assert.That(player.CumulativeDebt, Is.EqualTo(0));
    }
}
