using System;
using System.Collections.Generic;
using System.Linq;

namespace BbongCore.Cards;

/// <summary>카드 더미. 표준 덱 = 숫자 1~12 × 4색 = 48장(rules.md §1).</summary>
public sealed class Deck
{
    private readonly List<Card> _cards;

    private Deck(IEnumerable<Card> cards)
    {
        _cards = cards.ToList();
    }

    public IReadOnlyList<Card> Cards => _cards;

    public static Deck CreateStandard()
    {
        var colors = (CardColor[])Enum.GetValues(typeof(CardColor));
        var cards =
            from number in Enumerable.Range(1, 12)
            from color in colors
            select new Card(number, color);

        return new Deck(cards);
    }

    /// <summary>Fisher-Yates 셔플. 원본은 두지 않고 섞인 새 Deck을 반환합니다.</summary>
    public Deck Shuffle(IRandom random)
    {
        var shuffled = new List<Card>(_cards);
        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return new Deck(shuffled);
    }
}
