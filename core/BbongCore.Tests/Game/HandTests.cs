using System;
using BbongCore.Cards;
using BbongCore.Game;
using NUnit.Framework;

namespace BbongCore.Tests.Game;

[TestFixture]
public class HandTests
{
    private static Card C(int number, CardColor color = CardColor.Red) => new(number, color);

    [Test]
    public void Hand_starts_with_given_cards()
    {
        var hand = new Hand(new[] { C(3), C(7), C(9) });

        Assert.That(hand.Count, Is.EqualTo(3));
    }

    [Test]
    public void Draw_adds_one_card()
    {
        var hand = new Hand(new[] { C(3), C(7) });

        var after = hand.Draw(C(5));

        Assert.That(after.Count, Is.EqualTo(3));
    }

    [Test]
    public void Draw_does_not_mutate_original_hand()
    {
        var hand = new Hand(new[] { C(3), C(7) });

        hand.Draw(C(5));

        Assert.That(hand.Count, Is.EqualTo(2), "Draw는 새 Hand를 반환하고 원본을 바꾸지 않아야 합니다.");
    }

    [Test]
    public void Discard_removes_the_matching_card()
    {
        var hand = new Hand(new[] { C(3), C(7), C(9) });

        var after = hand.Discard(C(7));

        Assert.That(after.Count, Is.EqualTo(2));
        Assert.That(after.Contains(C(7)), Is.False);
    }

    [Test]
    public void Discard_card_not_in_hand_throws()
    {
        var hand = new Hand(new[] { C(3), C(7) });

        Assert.That(() => hand.Discard(C(5)), Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void Sum_adds_card_numbers()
    {
        var hand = new Hand(new[] { C(3), C(7), C(9) });

        Assert.That(hand.Sum(), Is.EqualTo(19));
    }
}
