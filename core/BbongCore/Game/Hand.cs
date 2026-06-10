using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;

namespace BbongCore.Game;

/// <summary>플레이어 손패. 불변 — Draw/Discard는 새 Hand를 반환합니다(rules.md §3).</summary>
public sealed class Hand
{
    private readonly List<Card> _cards;

    public Hand(IEnumerable<Card> cards)
    {
        _cards = cards.ToList();
    }

    public IReadOnlyList<Card> Cards => _cards;

    public int Count => _cards.Count;

    public bool Contains(Card card) => _cards.Contains(card);

    /// <summary>카드 1장을 더한 새 Hand를 반환합니다(드로우).</summary>
    public Hand Draw(Card card) => new(_cards.Append(card));

    /// <summary>일치하는 카드 1장을 뺀 새 Hand를 반환합니다(버림). 없으면 예외.</summary>
    public Hand Discard(Card card)
    {
        if (!_cards.Contains(card))
        {
            throw new System.InvalidOperationException($"손에 없는 카드는 버릴 수 없습니다: {card}");
        }

        var remaining = new List<Card>(_cards);
        remaining.Remove(card);
        return new Hand(remaining);
    }

    /// <summary>손패 숫자 합(점수·스톱 판정용, rules.md §6~7).</summary>
    public int Sum() => _cards.Sum(c => c.Number);
}
