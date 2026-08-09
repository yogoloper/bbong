using BbongCore.Ai;
using BbongCore.Cards;
using BbongCore.Config;
using BbongCore.Game;
using BbongCore.Rules;

// 봇 전용 라운드 시뮬레이터 — 라운드 종료 사유 분포를 측정한다(스톱 편중 튜닝용).
// 서버 GameSession의 진행 규칙을 타이머 없이 압축 재현: 스톱 → 드로우 → 족보/자연뽕 → 버림 → 뽕 체인.

var difficulty = args.Length > 0 && Enum.TryParse<BotDifficulty>(args[0], true, out var d) ? d : BotDifficulty.Normal;
var players = args.Length > 1 ? int.Parse(args[1]) : 4;
var roundsTarget = args.Length > 2 ? int.Parse(args[2]) : 3000;

var reasons = new Dictionary<string, int>();
var turnCounts = new List<int>();
var rng = new Random(42);

void Tally(string reason) => reasons[reason] = reasons.GetValueOrDefault(reason) + 1;

for (var r = 0; r < roundsTarget; r++)
{
    var bots = Enumerable.Range(0, players).Select(_ => new Bot(difficulty, new SeededRandom(rng.Next()))).ToArray();
    var round = RoundState.Deal(Deck.CreateStandard(), players, new SeededRandom(rng.Next()), dealerSeat: r % players);
    var turns = 0;
    string? end = null;

    while (end is null && turns < 500)
    {
        turns++;
        var seat = round.CurrentSeat;

        if (StopResolver.CanStop(round, seat) && bots[seat].ShouldStop(round, seat))
        {
            end = StopResolver.IsBagaji(round, seat) ? "스톱바가지" : "스톱";
            break;
        }

        if (!round.CanDraw)
        {
            end = "더미소진";
            break;
        }

        round = round.Draw();

        var meld = HandEvaluator.Evaluate(round.CurrentPlayer.Hand);
        if (meld.Type != MeldType.None)
        {
            end = "족보";
            break;
        }

        if (round.CanNaturalPong())
        {
            var number = round.CurrentPlayer.Hand.Cards.GroupBy(c => c.Number).First(g => g.Count() >= 3).Key;
            var hand = round.CurrentPlayer.Hand;
            var rest = hand.Cards.Where(c => c.Number != number).ToList();
            if (rest.Count == 0)
            {
                end = "자연뽕손털기";
                break;
            }

            round = round.NaturalPong(number, bots[seat].ChoosePongDiscard(new Hand(rest)));
        }
        else
        {
            round = round.Discard(bots[seat].ChooseDiscard(round.CurrentPlayer.Hand));
        }

        // 뽕 체인: 방금 버림(일반/자연뽕 토스)에 대해 다른 봇들이 뽕
        var guard = 0;
        while (end is null && guard++ < 8)
        {
            var ponger = Enumerable.Range(0, players)
                .FirstOrDefault(s => round.CanPong(s) && bots[s].ShouldPong(), -1);
            if (ponger < 0)
            {
                break;
            }

            if (round.CanPongThenNaturalPong(ponger))
            {
                end = "뽕바가지";
                break;
            }

            var hand = round.Players[ponger].Hand;
            var number = round.DiscardPile[^1].Number;
            var rest = hand.Cards.Where(c => c.Number != number)
                .Concat(hand.Cards.Where(c => c.Number == number).Skip(2)).ToList();
            if (hand.Cards.Count(c => c.Number == number) >= 2 && hand.Count == 2)
            {
                end = "뽕바가지"; // 두 번째 뽕 손 소진
                break;
            }

            round = round.Pong(ponger, bots[ponger].ChoosePongDiscard(new Hand(rest)));
        }
    }

    Tally(end ?? "미종결(500턴)");
    turnCounts.Add(turns);
}

Console.WriteLine($"난이도 {difficulty} · {players}인 · {roundsTarget}라운드 | 평균 {turnCounts.Average():F1}턴");
foreach (var (k, v) in reasons.OrderByDescending(kv => kv.Value))
{
    Console.WriteLine($"  {k,-12} {v,5}  ({100.0 * v / roundsTarget:F1}%)");
}
