using System;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;

namespace BbongCore.Online;

/// <summary>
/// 실시간 프로토콜용 카드. Card(record struct)는 Unity JsonUtility가 못 읽어
/// public 필드 클래스로 변환해 전송한다(클라·서버 공유 스키마).
/// </summary>
[Serializable]
public sealed class CardDto
{
    public int number;
    public int color;

    public static CardDto From(Card card) => new() { number = card.Number, color = (int)card.Color };

    public static CardDto[] FromAll(IEnumerable<Card> cards) => cards.Select(From).ToArray();

    public Card ToCard() => new(number, (CardColor)color);
}
