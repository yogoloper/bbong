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

    // ── 또이또이: 같은 숫자 2장씩 3쌍(2+2+2), 6장, 0점 (rules.md §5) ──

    [Test]
    public void Ttoittoi_with_three_pairs()
    {
        // 3·3 / 7·7 / 9·9
        var hand = HandOf(
            new Card(3, CardColor.Red), new Card(3, CardColor.Blue),
            new Card(7, CardColor.Red), new Card(7, CardColor.Green),
            new Card(9, CardColor.Red), new Card(9, CardColor.Yellow));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.Ttoittoi));
        Assert.That(result.Score, Is.EqualTo(0));
    }

    [Test]
    public void Not_ttoittoi_with_two_pairs_and_two_singles()
    {
        // 3·3 / 7·7 / 9 / 11  (쌍 2개뿐)
        var hand = HandOf(
            new Card(3, CardColor.Red), new Card(3, CardColor.Blue),
            new Card(7, CardColor.Red), new Card(7, CardColor.Green),
            new Card(9, CardColor.Red), new Card(11, CardColor.Yellow));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.None));
    }

    [Test]
    public void Not_ttoittoi_with_a_triple_and_a_pair_and_a_single()
    {
        // 3·3·3 / 7·7 / 9  (3+2+1, 쌍 3개 아님)
        var hand = HandOf(
            new Card(3, CardColor.Red), new Card(3, CardColor.Blue), new Card(3, CardColor.Green),
            new Card(7, CardColor.Red), new Card(7, CardColor.Green),
            new Card(9, CardColor.Red));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.None));
    }
}
