namespace BbongCore.Cards;

/// <summary>숫자(1~12)와 색을 가진 카드. 점수값은 숫자 그대로(rules.md §1).</summary>
public readonly record struct Card(int Number, CardColor Color);
