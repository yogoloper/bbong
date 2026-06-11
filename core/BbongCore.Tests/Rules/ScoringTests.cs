using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Rules;
using NUnit.Framework;

namespace BbongCore.Tests.Rules;

[TestFixture]
public class ScoringTests
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
    public void Meld_winner_scores_the_meld_score()
    {
        // 스트레이트 -21로 판을 끝낸 승자 → -21
        var outcome = new PlayerOutcome(HandOf(1, 2, 3, 4, 5, 6),
            DeclaredMeld: new MeldResult(MeldType.Straight, -21));

        Assert.That(Scoring.Score(outcome), Is.EqualTo(-21));
    }

    [Test]
    public void Ttoittoi_winner_scores_zero_not_hand_sum()
    {
        // 또이또이(0점) 승자는 손패 합이 아니라 0
        var outcome = new PlayerOutcome(HandOf(12, 12, 11, 11, 10, 10),
            DeclaredMeld: new MeldResult(MeldType.Ttoittoi, 0));

        Assert.That(Scoring.Score(outcome), Is.EqualTo(0));
    }

    [Test]
    public void Non_winner_scores_remaining_hand_sum()
    {
        // 족보 미달성자 = 남은 손패 합 (3+7+9=19)
        var outcome = new PlayerOutcome(HandOf(3, 7, 9));

        Assert.That(Scoring.Score(outcome), Is.EqualTo(19));
    }

    [Test]
    public void Two_pong_winner_with_empty_hand_scores_zero()
    {
        // 두 번 뽕으로 손패 소진 → 빈 손패 합 = 0 (rules.md §4-3, §8)
        var outcome = new PlayerOutcome(HandOf());

        Assert.That(Scoring.Score(outcome), Is.EqualTo(0));
    }

    [Test]
    public void Pong_bak_adds_20_to_base()
    {
        // 뽕 박: 마지막 버린 자 손패 합 + 20 (rules.md §7)
        var outcome = new PlayerOutcome(HandOf(3, 7, 9), PongBak: true);

        Assert.That(Scoring.Score(outcome), Is.EqualTo(19 + 20));
    }

    [Test]
    public void Stop_bagaji_is_flat_30_ignoring_hand_sum()
    {
        // 스톱 바가지: 고정 30점 (손패 합 무관, rules.md §6)
        var outcome = new PlayerOutcome(HandOf(4, 4), StopBagaji: true);

        Assert.That(Scoring.Score(outcome), Is.EqualTo(30));
    }
}
