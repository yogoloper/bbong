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

    // ── 스트레이트: 연속 6장, wrap 불가, 색무관, -(6장 합) (rules.md §5) ──

    [Test]
    public void Straight_one_to_six_scores_negative_sum()
    {
        // 1·2·3·4·5·6 = 21 → -21. 색 섞어도 무관.
        var hand = HandOf(
            new Card(1, CardColor.Red), new Card(2, CardColor.Blue),
            new Card(3, CardColor.Green), new Card(4, CardColor.Yellow),
            new Card(5, CardColor.Red), new Card(6, CardColor.Blue));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.Straight));
        Assert.That(result.Score, Is.EqualTo(-21));
    }

    [Test]
    public void Straight_seven_to_twelve_scores_negative_sum()
    {
        // 7·8·9·10·11·12 = 57 → -57
        var hand = HandOf(
            Red(7), Red(8), Red(9), Red(10), Red(11), Red(12));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.Straight));
        Assert.That(result.Score, Is.EqualTo(-57));
    }

    [Test]
    public void Not_straight_when_wrapping_around_twelve_to_one()
    {
        // 11·12·1·2·3·4 → wrap 불가
        var hand = HandOf(
            Red(11), Red(12), Red(1), Red(2), Red(3), Red(4));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.None));
    }

    [Test]
    public void Not_straight_when_gap_in_sequence()
    {
        // 1·2·3·4·5·7 (6 빠짐)
        var hand = HandOf(
            Red(1), Red(2), Red(3), Red(4), Red(5), Red(7));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.None));
    }

    [Test]
    public void Not_straight_when_duplicate_number()
    {
        // 1·2·3·4·5·5 (연속 아님, 중복)
        var hand = HandOf(
            new Card(1, CardColor.Red), new Card(2, CardColor.Red),
            new Card(3, CardColor.Red), new Card(4, CardColor.Red),
            new Card(5, CardColor.Red), new Card(5, CardColor.Blue));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.None));
    }

    // ── 10이하: 6장 합 ≤ 10, -100 (rules.md §5) ──

    [Test]
    public void TenOrUnder_when_six_card_sum_below_ten()
    {
        // 1·1·1·2·2·2 = 9 (3+3 분포 → 또이또이 아님)
        var hand = HandOf(
            new Card(1, CardColor.Red), new Card(1, CardColor.Blue), new Card(1, CardColor.Green),
            new Card(2, CardColor.Red), new Card(2, CardColor.Blue), new Card(2, CardColor.Green));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.TenOrUnder));
        Assert.That(result.Score, Is.EqualTo(-100));
    }

    [Test]
    public void TenOrUnder_at_boundary_sum_exactly_ten()
    {
        // 1·1·1·2·2·3 = 10
        var hand = HandOf(
            new Card(1, CardColor.Red), new Card(1, CardColor.Blue), new Card(1, CardColor.Green),
            new Card(2, CardColor.Red), new Card(2, CardColor.Blue), Red(3));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.TenOrUnder));
    }

    // ── 66이상: 6장 합 ≥ 66, -100 (rules.md §5) ──

    [Test]
    public void SixtySixOrOver_when_six_card_sum_at_least_66()
    {
        // 12·12·12·11·11·11 = 69 (3+3 → 또이또이 아님)
        var hand = HandOf(
            new Card(12, CardColor.Red), new Card(12, CardColor.Blue), new Card(12, CardColor.Green),
            new Card(11, CardColor.Red), new Card(11, CardColor.Blue), new Card(11, CardColor.Green));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.SixtySixOrOver));
        Assert.That(result.Score, Is.EqualTo(-100));
    }

    [Test]
    public void SixtySixOrOver_at_boundary_sum_exactly_66()
    {
        // 12·12·12·11·11·8 = 66
        var hand = HandOf(
            new Card(12, CardColor.Red), new Card(12, CardColor.Blue), new Card(12, CardColor.Green),
            new Card(11, CardColor.Red), new Card(11, CardColor.Blue), Red(8));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.SixtySixOrOver));
    }

    [Test]
    public void No_sum_meld_when_sum_between_11_and_65()
    {
        // 1·2·3·4·5·8 = 23
        var hand = HandOf(Red(1), Red(2), Red(3), Red(4), Red(5), Red(8));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.None));
    }

    [Test]
    public void No_sum_meld_for_five_card_hand_even_if_sum_below_ten()
    {
        // 5장(드로우 전)은 합산 족보 미판정 (rules.md §5: 6장 시점)
        var hand = HandOf(
            new Card(1, CardColor.Red), new Card(1, CardColor.Blue),
            new Card(2, CardColor.Red), new Card(2, CardColor.Blue), Red(3));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.None));
    }

    // ── 복수 성립: 가장 유리한(빚 최다 탕감 = 최저 점수) 족보 선택 (rules.md §5) ──

    [Test]
    public void Picks_most_favorable_when_ttoittoi_and_sixtysix_overlap()
    {
        // 12·12 / 11·11 / 10·10 = 합 66
        //   또이또이(0) + 66이상(-100) 동시 성립 → 더 유리한 -100 선택
        var hand = HandOf(
            new Card(12, CardColor.Red), new Card(12, CardColor.Blue),
            new Card(11, CardColor.Red), new Card(11, CardColor.Blue),
            new Card(10, CardColor.Red), new Card(10, CardColor.Blue));

        var result = HandEvaluator.Evaluate(hand);

        Assert.That(result.Type, Is.EqualTo(MeldType.SixtySixOrOver));
        Assert.That(result.Score, Is.EqualTo(-100));
    }
}
