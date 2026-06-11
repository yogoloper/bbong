using BbongCore.Ai;
using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Rules;

// BBONG 5판 세트 데모 — AI 봇 대전.
// 좌석별 난이도: P0=Hard, P1=Normal, P2=Easy.
// 봇이 버림/뽕/스톱을 결정하고, 5판 누적으로 1등 + 판돈 분배.

const int playerCount = 3;
const int stake = 1000;
var bots = new[] { new Bot(BotDifficulty.Hard), new Bot(BotDifficulty.Normal), new Bot(BotDifficulty.Easy) };
var rng = new SeededRandom(7);
var game = GameState.Start(playerCount, setRounds: 5);

(int[] scores, string reason) PlayRound(int dealerSeat)
{
    var round = RoundState.Deal(Deck.CreateStandard(), playerCount, rng, dealerSeat);

    for (var turn = 0; turn < 500; turn++)
    {
        var seat = round.CurrentSeat;

        // 1) 스톱 (봇 의사 반영)
        if (StopResolver.CanStop(round, seat) && bots[seat].ShouldStop(round, seat))
        {
            var bagaji = StopResolver.IsBagaji(round, seat);
            return (RoundSettlement.SettleByStop(round, seat), $"P{seat} 스톱{(bagaji ? "(바가지)" : "")}");
        }

        // 2) 드로우
        round = round.Draw();
        var me = round.CurrentPlayer;

        // 3) 자연뽕
        if (round.CanNaturalPong())
        {
            var triple = me.Hand.Cards.GroupBy(c => c.Number).First(g => g.Count() >= 3).Key;
            var rest = new Hand(me.Hand.Cards.Where(c => c.Number != triple));
            round = round.NaturalPong(triple, bots[seat].ChoosePongDiscard(rest));
            continue;
        }

        // 4) 6장 족보 → 선언
        var meld = HandEvaluator.Evaluate(me.Hand);
        if (meld.Type != MeldType.None)
        {
            return (RoundSettlement.SettleByMeld(round, seat, meld), $"P{seat} {meld.Type}({meld.Score})");
        }

        // 5) 버림 (봇 결정)
        var discard = bots[seat].ChooseDiscard(me.Hand);
        round = round.Discard(discard);

        // 6) 뽕 인터럽트 (봇 의사 반영)
        var ponger = -1;
        for (var s = 0; s < playerCount; s++)
        {
            if (round.CanPong(s) && bots[s].ShouldPong()) { ponger = s; break; }
        }

        if (ponger >= 0)
        {
            var rest = new Hand(round.Players[ponger].Hand.Cards.Where(c => c.Number != discard.Number));
            if (rest.Count == 0)
            {
                round = round.Pong(ponger, null);
                return (RoundSettlement.SettleByTwoPong(round, ponger, seat), $"P{ponger} 두 번 뽕(P{seat} 박)");
            }

            round = round.Pong(ponger, bots[ponger].ChoosePongDiscard(rest));
        }
    }

    return (round.Players.Select(p => p.Hand.Sum()).ToArray(), "턴 한도 도달");
}

var names = new[] { "P0(Hard)", "P1(Normal)", "P2(Easy)" };
Console.WriteLine($"=== BBONG 5판 세트 — AI 봇 대전 (판돈 {stake}) ===\n");

for (var r = 1; r <= 5; r++)
{
    var (scores, reason) = PlayRound(dealerSeat: (r - 1) % playerCount);
    game = game.ApplyRoundScores(scores);

    var line = string.Join("  ", Enumerable.Range(0, playerCount).Select(s => $"P{s} {scores[s]:+0;-0;0}"));
    Console.WriteLine($"{r}판: {line,-30} 누적[{string.Join(", ", game.CumulativeDebts)}]  ({reason})");
}

var winners = game.WinnerSeats();
var payouts = StakePot.Distribute(stake, playerCount, winners);

Console.WriteLine($"\n=== 세트 종료 ===");
for (var s = 0; s < playerCount; s++)
{
    var mark = winners.Contains(s) ? "  ← 1등" : "";
    Console.WriteLine($"  {names[s]}: 누적 빚 {game.CumulativeDebts[s],4}  판돈 {payouts[s],5}{mark}");
}
