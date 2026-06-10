using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Rules;
using NUnit.Framework;

namespace BbongCore.Tests.Rules;

[TestFixture]
public class HandEvaluatorTests
{
    private static Hand HandOf(params Card[] cards) => new(cards);
    private static Card Red(int n) => new(n, CardColor.Red);

    // ── 총통: 같은 숫자 4장, -100 (rules.md §5) ──

    [Test]
    public void Chongtong_with_four_same_number_in_five_card_hand()
    {
        // 7이 4색 + 잔여 1장
        var hand = HandOf(
            new Card(7, CardColor.Red), new Card(7, CardColor.Blue),
            new Card(7, CardColor.Green), new Card(7, CardColor.Yellow),
            Red(2));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.Chongtong));
        Assert.That(result.Score, Is.EqualTo(-100));
    }

    [Test]
    public void Chongtong_with_four_same_number_in_six_card_hand()
    {
        // 6장 상태에서도 4장 같으면 성립 (잔여 2장 무관)
        var hand = HandOf(
            new Card(11, CardColor.Red), new Card(11, CardColor.Blue),
            new Card(11, CardColor.Green), new Card(11, CardColor.Yellow),
            Red(2), Red(5));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.Chongtong));
    }

    [Test]
    public void Not_chongtong_when_only_three_same_number()
    {
        var hand = HandOf(
            new Card(7, CardColor.Red), new Card(7, CardColor.Blue),
            new Card(7, CardColor.Green), Red(2), Red(5));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.None));
        Assert.That(result.Score, Is.EqualTo(0));
    }
}
