using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Rules;

// BBONG 코어 엔진 5판 세트 완주 데모.
// 간단 봇(가장 큰 수 버림) + 뽕 인터럽트 + 족보/두 번 뽕/스톱 종료.
// 5판 누적 빚으로 1등을 가리고, 판돈(winner-takes-all)을 분배합니다.

static Card Highest(Hand h) => h.Cards.OrderByDescending(c => c.Number).First();

const int playerCount = 3;
const int stake = 1000;
var rng = new SeededRandom(7);
var game = GameState.Start(playerCount, setRounds: 5);

// 한 판을 끝까지 돌려 좌석별 점수를 반환합니다.
(int[] scores, string reason) PlayRound(int dealerSeat)
{
    var round = RoundState.Deal(Deck.CreateStandard(), playerCount, rng, dealerSeat);

    for (var turn = 0; turn < 500; turn++)
    {
        var seat = round.CurrentSeat;

        if (StopResolver.CanStop(round, seat))
        {
            var bagaji = StopResolver.IsBagaji(round, seat);
            return (RoundSettlement.SettleByStop(round, seat),
                $"P{seat} 스톱{(bagaji ? "(바가지)" : "")}");
        }

        round = round.Draw();
        var me = round.CurrentPlayer;

        if (round.CanNaturalPong())
        {
            var triple = me.Hand.Cards.GroupBy(c => c.Number).First(g => g.Count() >= 3).Key;
            var rest = new Hand(me.Hand.Cards.Where(c => c.Number != triple));
            round = round.NaturalPong(triple, Highest(rest));
            continue;
        }

        var meld = HandEvaluator.Evaluate(me.Hand);
        if (meld.Type != MeldType.None)
        {
            return (RoundSettlement.SettleByMeld(round, seat, meld), $"P{seat} {meld.Type}({meld.Score})");
        }

        var discard = Highest(me.Hand);
        round = round.Discard(discard);

        var ponger = -1;
        for (var s = 0; s < playerCount; s++)
        {
            if (round.CanPong(s)) { ponger = s; break; }
        }

        if (ponger >= 0)
        {
            var afterRemove = round.Players[ponger].Hand.Cards.Where(c => c.Number != discard.Number).ToList();
            if (afterRemove.Count == 0)
            {
                round = round.Pong(ponger, null);
                return (RoundSettlement.SettleByTwoPong(round, ponger, seat), $"P{ponger} 두 번 뽕(P{seat} 박)");
            }

            round = round.Pong(ponger, afterRemove.OrderByDescending(c => c.Number).First());
        }
    }

    // 안전장치: 미종료 시 손패 합으로 정산
    return (round.Players.Select(p => p.Hand.Sum()).ToArray(), "턴 한도 도달");
}

Console.WriteLine($"=== BBONG 5판 세트 ({playerCount}명, 판돈 {stake}) ===\n");

for (var r = 1; r <= 5; r++)
{
    var (scores, reason) = PlayRound(dealerSeat: (r - 1) % playerCount);
    game = game.ApplyRoundScores(scores);

    var line = string.Join("  ", Enumerable.Range(0, playerCount).Select(s => $"P{s} {scores[s]:+0;-0;0}"));
    Console.WriteLine($"{r}판: {line,-34} 누적[{string.Join(", ", game.CumulativeDebts)}]  ({reason})");
}

var winners = game.WinnerSeats();
var payouts = StakePot.Distribute(stake, playerCount, winners);

Console.WriteLine($"\n=== 세트 종료 ===");
Console.WriteLine($"최종 누적 빚: {string.Join(", ", Enumerable.Range(0, playerCount).Select(s => $"P{s}={game.CumulativeDebts[s]}"))}");
Console.WriteLine($"1등: {string.Join(", ", winners.Select(s => $"P{s}"))} (빚 최저)");
Console.WriteLine($"판돈 분배({stake}×{playerCount}={stake * playerCount}): {string.Join(", ", Enumerable.Range(0, playerCount).Select(s => $"P{s}={payouts[s]}"))}");
