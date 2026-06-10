using BbongCore.Cards;
using BbongCore.Game;

// BBONG 코어 엔진 눈으로 확인용 데모.
// 덱 48장 생성 → 셔플 → 전원 5장 배분(rules.md §2) → 손패 출력.
// 게임 로직(코어) 동작을 UI 없이 콘솔로 보여줍니다.

static string Glyph(CardColor c) => c switch
{
    CardColor.Red => "R",
    CardColor.Blue => "B",
    CardColor.Green => "G",
    CardColor.Yellow => "Y",
    _ => "?"
};

static string Show(Card c) => $"{c.Number,2}{Glyph(c.Color)}";

const int playerCount = 4;
const int handSize = 5;

var deck = Deck.CreateStandard();
Console.WriteLine($"덱 생성: {deck.Cards.Count}장 (1~12 × 4색)\n");

// 셔플 (데모용 고정 시드 → 매번 같은 결과로 재현 가능)
var rng = new Random(42); // 고정 시드 → 매번 같은 결과로 재현
var shuffled = deck.Cards.OrderBy(_ => rng.Next()).ToList();

// 전원 5장씩 배분
var hands = new Hand[playerCount];
var cursor = 0;
for (var p = 0; p < playerCount; p++)
{
    hands[p] = new Hand(shuffled.Skip(cursor).Take(handSize));
    cursor += handSize;
}

Console.WriteLine($"플레이어 {playerCount}명에게 각 {handSize}장 배분:\n");
for (var p = 0; p < playerCount; p++)
{
    var sorted = hands[p].Cards.OrderBy(c => c.Number).ThenBy(c => c.Color);
    var line = string.Join("  ", sorted.Select(Show));
    Console.WriteLine($"  P{p + 1}: [ {line} ]  합 {hands[p].Sum()}");
}

var remaining = shuffled.Count - cursor;
Console.WriteLine($"\n바닥 더미(draw pile): {remaining}장 남음\n");

// 턴 동작 시연: P1이 드로우 1장 → 손패 6장 → 1장 버림 → 5장 복귀 (rules.md §3)
var drawn = shuffled[cursor];
Console.WriteLine($"[P1 턴] 드로우: {Show(drawn).Trim()}");
var afterDraw = hands[0].Draw(drawn);
Console.WriteLine($"  드로우 후 {afterDraw.Count}장, 합 {afterDraw.Sum()}");

var toDiscard = afterDraw.Cards.OrderByDescending(c => c.Number).First(); // 가장 큰 수 버림(데모 휴리스틱)
var afterDiscard = afterDraw.Discard(toDiscard);
Console.WriteLine($"  버림: {Show(toDiscard).Trim()} → {afterDiscard.Count}장, 합 {afterDiscard.Sum()}");
Console.WriteLine($"  (원본 손패 불변 확인: P1 여전히 {hands[0].Count}장)");
