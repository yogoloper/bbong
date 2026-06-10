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
}
