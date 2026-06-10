using System.Collections.Generic;

namespace BbongCore.Cards;

/// <summary>Fisher-Yates 셔플 공용 헬퍼(덱 셔플·바닥 더미 재셔플에서 공유).</summary>
internal static class Shuffler
{
    public static List<Card> Shuffle(IReadOnlyList<Card> cards, IRandom random)
    {
        var result = new List<Card>(cards);
        for (var i = result.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        return result;
    }
}
