using System.Linq;
using BbongCore.Game;

namespace BbongCore.Rules;

/// <summary>손패의 족보를 판정합니다(rules.md §5).</summary>
public static class HandEvaluator
{
    public static MeldResult Evaluate(Hand hand)
    {
        if (IsChongtong(hand))
        {
            return new MeldResult(MeldType.Chongtong, -100);
        }

        if (IsTtoittoi(hand))
        {
            return new MeldResult(MeldType.Ttoittoi, 0);
        }

        if (IsStraight(hand))
        {
            return new MeldResult(MeldType.Straight, -hand.Sum());
        }

        return MeldResult.None;
    }

    /// <summary>총통: 같은 숫자가 4장(잔여 카드 무관). 5장·6장 상태 모두 성립.</summary>
    private static bool IsChongtong(Hand hand) =>
        hand.Cards
            .GroupBy(c => c.Number)
            .Any(g => g.Count() == 4);

    /// <summary>또이또이: 6장이 같은 숫자 2장씩 3쌍(2+2+2).</summary>
    private static bool IsTtoittoi(Hand hand)
    {
        if (hand.Count != 6)
        {
            return false;
        }

        var groups = hand.Cards.GroupBy(c => c.Number).ToList();
        return groups.Count == 3 && groups.All(g => g.Count() == 2);
    }

    /// <summary>스트레이트: 연속 숫자 6장(색 무관, wrap 불가). 중복 숫자 불가.</summary>
    private static bool IsStraight(Hand hand)
    {
        if (hand.Count != 6)
        {
            return false;
        }

        var numbers = hand.Cards.Select(c => c.Number).Distinct().ToList();
        return numbers.Count == 6 && numbers.Max() - numbers.Min() == 5;
    }
}
