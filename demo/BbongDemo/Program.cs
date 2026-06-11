using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Rules;

// BBONG 코어 엔진 한 판 완주 데모.
// 간단 봇(가장 큰 수 버림) + 뽕 인터럽트 + 족보/두 번 뽕/스톱 종료.
// 실제 코어 로직만으로 한 판이 끝까지 돌아가는지 콘솔로 확인합니다.

static string Glyph(CardColor c) => c switch
{
    CardColor.Red => "R", CardColor.Blue => "B", CardColor.Green => "G", CardColor.Yellow => "Y", _ => "?"
};
static string Show(Card c) => $"{c.Number}{Glyph(c.Color)}";
static string ShowHand(Hand h) =>
    string.Join(" ", h.Cards.OrderBy(c => c.Number).ThenBy(c => c.Color).Select(Show));
static Card Highest(Hand h) => h.Cards.OrderByDescending(c => c.Number).First();

const int playerCount = 3;
var round = RoundState.Deal(Deck.CreateStandard(), playerCount, new SeededRandom(7));

Console.WriteLine($"=== BBONG 한 판 시작 ({playerCount}명) ===\n");
foreach (var p in round.Players)
{
    Console.WriteLine($"  P{p.Seat}: [ {ShowHand(p.Hand)} ]");
}
Console.WriteLine($"  바닥 더미 {round.DrawPile.Count}장\n");

int[]? scores = null;
string endReason = "";
var turn = 0;

while (turn++ < 500)
{
    var seat = round.CurrentSeat;

    // 1) 스톱 가능하면 선언 (뽕 2명↑ + 합 한도)
    if (StopResolver.CanStop(round, seat))
    {
        var bagaji = StopResolver.IsBagaji(round, seat);
        scores = RoundSettlement.SettleByStop(round, seat);
        endReason = $"P{seat} 스톱 선언 (손합 {round.Players[seat].Hand.Sum()}{(bagaji ? ", 바가지!" : "")})";
        break;
    }

    // 2) 드로우
    round = round.Draw();
    var me = round.CurrentPlayer;
    Console.WriteLine($"[T{turn}] P{seat} 드로우 → [ {ShowHand(me.Hand)} ]");

    // 3) 자연뽕 (6장에 같은 숫자 3장)
    if (round.CanNaturalPong())
    {
        var triple = me.Hand.Cards.GroupBy(c => c.Number).First(g => g.Count() >= 3).Key;
        var rest = new Hand(me.Hand.Cards.Where(c => c.Number != triple));
        var toss = Highest(rest);
        round = round.NaturalPong(triple, toss);
        Console.WriteLine($"     P{seat} 자연뽕! {triple} 3장 내려놓고 {Show(toss)} 버림 → 손패 2장");
        continue;
    }

    // 4) 6장 족보 체크
    var meld = HandEvaluator.Evaluate(me.Hand);
    if (meld.Type != MeldType.None)
    {
        scores = RoundSettlement.SettleByMeld(round, seat, meld);
        endReason = $"P{seat} 족보 {meld.Type} ({meld.Score}점) 선언";
        break;
    }

    // 5) 버림 (가장 큰 수)
    var discard = Highest(me.Hand);
    round = round.Discard(discard);
    Console.WriteLine($"     P{seat} 버림 {Show(discard)}");

    // 6) 뽕 인터럽트 (버린 당사자 외 첫 번째 가능자)
    var ponger = -1;
    for (var s = 0; s < playerCount; s++)
    {
        if (round.CanPong(s)) { ponger = s; break; }
    }

    if (ponger >= 0)
    {
        var pongedNumber = discard.Number;
        var afterRemove = round.Players[ponger].Hand.Cards.Where(c => c.Number != pongedNumber).ToList();

        if (afterRemove.Count == 0)
        {
            // 둘째 뽕 → 손 소진, 판 종료(박: 마지막 버린 자 = seat)
            round = round.Pong(ponger, null);
            scores = RoundSettlement.SettleByTwoPong(round, ponger, lastDiscarderSeat: seat);
            endReason = $"P{ponger} 두 번째 뽕! 손 소진 → 종료 (P{seat} 박 +20)";
            break;
        }

        var toss = afterRemove.OrderByDescending(c => c.Number).First();
        round = round.Pong(ponger, toss);
        Console.WriteLine($"     >>> P{ponger} 뽕! {pongedNumber} 3장 고정 + {Show(toss)} 버림 → 손패 2장 (턴 P{round.CurrentSeat})");
    }
}

Console.WriteLine($"\n=== 판 종료: {endReason} ===\n");
Console.WriteLine("최종 손패 & 그 판 점수(빚):");
for (var s = 0; s < playerCount; s++)
{
    var p = round.Players[s];
    var sc = scores is null ? 0 : scores[s];
    Console.WriteLine($"  P{s}: [ {ShowHand(p.Hand)} ]  → {sc:+0;-0;0}점  (뽕 {p.PongCount}회)");
}
