using BbongCore.Ai;
using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Rules;

// BBONG AI 봇 토너먼트 — 난이도별 평균 성적 검증.
// 좌석: P0=Hard, P1=Normal, P2=Easy. 1000세트(각 5판) 누적.
// 빚이 낮을수록 강함. 봇 고도화가 통계적으로 우위인지 + 판 종료 사유 분포를 확인합니다.

const int playerCount = 3;
const int sets = 1000;
var bots = new[] { new Bot(BotDifficulty.Hard), new Bot(BotDifficulty.Normal), new Bot(BotDifficulty.Easy) };
var names = new[] { "Hard  ", "Normal", "Easy  " };
var reasonNames = new[] { "스톱", "족보", "자연뽕 손소진", "두 번 뽕", "더미 소진" };

(int[] scores, int reason) PlayRound(RoundState round)
{
    for (var turn = 0; turn < 500; turn++)
    {
        var seat = round.CurrentSeat;

        if (StopResolver.CanStop(round, seat) && bots[seat].ShouldStop(round, seat))
        {
            return (RoundSettlement.SettleByStop(round, seat), 0);
        }

        if (!round.CanDraw)
        {
            return (RoundSettlement.SettleByExhaustion(round), 4); // 재셔플 한도 초과 강제 종료
        }

        round = round.Draw();
        var me = round.CurrentPlayer;

        if (round.CanNaturalPong())
        {
            var triple = me.Hand.Cards.GroupBy(c => c.Number).First(g => g.Count() >= 3).Key;
            var rest = new Hand(me.Hand.Cards.Where(c => c.Number != triple));
            if (rest.Count == 0)
            {
                round = round.NaturalPong(triple, null); // 3장 전부 같음 → 손 소진
                return (RoundSettlement.SettleByHandClear(round, seat), 2);
            }

            round = round.NaturalPong(triple, bots[seat].ChoosePongDiscard(rest));
            continue;
        }

        var meld = HandEvaluator.Evaluate(me.Hand);
        if (meld.Type != MeldType.None)
        {
            return (RoundSettlement.SettleByMeld(round, seat, meld), 1);
        }

        var discard = bots[seat].ChooseDiscard(me.Hand);
        round = round.Discard(discard);

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
                return (RoundSettlement.SettleByTwoPong(round, ponger, seat), 3);
            }

            round = round.Pong(ponger, bots[ponger].ChoosePongDiscard(rest));
        }
    }

    return (round.Players.Select(p => p.Hand.Sum()).ToArray(), 4);
}

var wins = new int[playerCount];
var totalDebt = new long[playerCount];
var reasonCounts = new int[reasonNames.Length];

for (var setNo = 1; setNo <= sets; setNo++)
{
    var rng = new SeededRandom(setNo);
    var game = GameState.Start(playerCount, setRounds: 5);

    for (var r = 0; r < 5; r++)
    {
        var round = RoundState.Deal(Deck.CreateStandard(), playerCount, rng, dealerSeat: r % playerCount);
        var (scores, reason) = PlayRound(round);
        reasonCounts[reason]++;
        game = game.ApplyRoundScores(scores);
    }

    var winners = game.WinnerSeats();
    foreach (var w in winners) wins[w]++; // 공동 1등은 모두 카운트
    for (var s = 0; s < playerCount; s++) totalDebt[s] += game.CumulativeDebts[s];
}

Console.WriteLine($"=== AI 봇 토너먼트 결과 ({sets}세트) ===\n");
Console.WriteLine($"  {"난이도",-8} {"1등 횟수",8} {"평균 누적빚",12}");
for (var s = 0; s < playerCount; s++)
{
    Console.WriteLine($"  {names[s]}   {wins[s],6}   {totalDebt[s] / (double)sets,10:0.0}");
}

var totalRounds = reasonCounts.Sum();
Console.WriteLine($"\n=== 판 종료 사유 분포 ({totalRounds}판) ===\n");
for (var i = 0; i < reasonNames.Length; i++)
{
    Console.WriteLine($"  {reasonNames[i],-10} {reasonCounts[i],5}판  {reasonCounts[i] * 100.0 / totalRounds,5:0.0}%");
}
