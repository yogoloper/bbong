using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BbongCore.Ai;
using BbongCore.Cards;
using BbongCore.Config;
using BbongCore.Game;
using BbongCore.Rules;

// BBONG 동시 접속 부하/정합 시뮬레이터.
// 게스트 N명이 동시에: 로그인 → 매치 시작(에스크로) → 코어로 봇게임 5판 → 정산 → /me 잔액 정합 검증.
// 서버(localhost:5080)와 docker compose(PG) 기동 후 실행:
//   dotnet run --project tools/BbongLoadSim -- --users 20 --games 5

var users = ArgInt("--users", 10);
var games = ArgInt("--games", 5);
var baseUrl = Arg("--url", "http://localhost:5080");

Console.WriteLine($"=== BBONG LoadSim — 유저 {users}명 × 게임 {games}회 → {baseUrl} ===\n");

var results = await Task.WhenAll(Enumerable.Range(0, users).Select(i => RunUserAsync(i)));

// ── 집계 ──
var allLatencies = results.SelectMany(r => r.LatenciesMs).OrderBy(x => x).ToList();
var totalRequests = allLatencies.Count;
var failures = results.Sum(r => r.HttpFailures);
var mismatches = results.Sum(r => r.BalanceMismatches);
var bankrupt = results.Count(r => r.Bankrupt);
var gamesPlayed = results.Sum(r => r.GamesPlayed);

Console.WriteLine($"\n=== 결과 ===");
Console.WriteLine($"  HTTP 요청 {totalRequests}건 | 실패 {failures} | 레이턴시 p50 {Percentile(50):0.0}ms p95 {Percentile(95):0.0}ms");
Console.WriteLine($"  게임 {gamesPlayed}회 완료 | 파산 이탈 {bankrupt}명 | 잔액 정합 불일치 {mismatches}건");
Console.WriteLine($"\n  {"유저",-6} {"게임",4} {"최종 잔액",10}  정합");
foreach (var r in results)
{
    Console.WriteLine($"  U{r.Index,-5} {r.GamesPlayed,4} {r.FinalBalance,10:N0}  {(r.BalanceMismatches == 0 ? "OK" : $"FAIL×{r.BalanceMismatches}")}");
}

if (failures > 0 || mismatches > 0)
{
    Console.WriteLine("\n✗ 실패 있음");
    return 1;
}

Console.WriteLine("\n✓ 전원 정합");
return 0;

// ── 유저 시나리오 ──

async Task<UserResult> RunUserAsync(int index)
{
    var result = new UserResult(index);
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    var rng = new Random(index * 7919 + 17); // 유저별 결정적 시드

    try
    {
        var guest = await PostAsync(http, result, "/auth/guest", body: null);
        var token = guest.GetProperty("accessToken").GetString();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        for (var g = 0; g < games; g++)
        {
            var me = await GetAsync(http, result, "/me");
            var balance = me.GetProperty("balance").GetInt64();

            var affordable = GameConfig.StakeOptions.Where(s => s <= balance).ToArray();
            if (affordable.Length == 0)
            {
                result.Bankrupt = true; // 판돈 최소값도 못 냄 → 이탈
                break;
            }

            var stake = affordable[rng.Next(affordable.Length)];
            var playerCount = rng.Next(GameConfig.MinPlayers, GameConfig.MaxPlayers + 1);

            var started = await PostAsync(http, result, "/match/start", new { stake, playerCount });
            var matchId = started.GetProperty("matchId").GetGuid();
            var expected = balance - stake;

            var winners = PlayGame(playerCount, seed: index * 1000 + g);
            var won = winners.Contains(0); // 내 좌석 = P0

            var settled = await PostAsync(http, result, $"/match/{matchId}/result",
                new { won, winnersCount = winners.Count });
            expected += settled.GetProperty("payout").GetInt64();

            var after = await GetAsync(http, result, "/me");
            var serverBalance = after.GetProperty("balance").GetInt64();
            if (serverBalance != expected)
            {
                result.BalanceMismatches++;
                Console.WriteLine($"  ✗ U{index} 게임{g}: 서버 {serverBalance} ≠ 예상 {expected}");
            }

            result.FinalBalance = serverBalance;
            result.GamesPlayed++;
        }
    }
    catch (Exception ex)
    {
        result.HttpFailures++;
        Console.WriteLine($"  ✗ U{index}: {ex.Message}");
    }

    return result;
}

// ── 봇 게임(코어 시뮬, demo/BbongDemo 이식 + 손털기 규칙 반영) ──

static IReadOnlyList<int> PlayGame(int playerCount, int seed)
{
    var bots = Enumerable.Range(0, playerCount).Select(_ => new Bot(BotDifficulty.Normal)).ToArray();
    var rng = new SeededRandom(seed);
    var game = GameState.Start(playerCount, setRounds: 5);

    for (var r = 0; r < 5; r++)
    {
        var round = RoundState.Deal(Deck.CreateStandard(), playerCount, rng, dealerSeat: r % playerCount);
        game = game.ApplyRoundScores(PlayRound(round, bots, playerCount));
    }

    return game.WinnerSeats();
}

static int[] PlayRound(RoundState round, Bot[] bots, int playerCount)
{
    for (var turn = 0; turn < 500; turn++)
    {
        var seat = round.CurrentSeat;

        if (StopResolver.CanStop(round, seat) && bots[seat].ShouldStop(round, seat))
        {
            return RoundSettlement.SettleByStop(round, seat);
        }

        if (!round.CanDraw)
        {
            return RoundSettlement.SettleByExhaustion(round);
        }

        round = round.Draw();
        var me = round.CurrentPlayer;

        if (round.CanNaturalPong())
        {
            var triple = me.Hand.Cards.GroupBy(c => c.Number).First(g => g.Count() >= 3).Key;
            var laid = me.Hand.Cards.Where(c => c.Number == triple).Take(3).ToList();
            var rest = new Hand(me.Hand.Cards.Except(laid)); // 같은 숫자 4장째도 버림 후보
            if (rest.Count == 0)
            {
                round = round.NaturalPong(triple, null); // 3장 전부 같음 → 손 소진
                return RoundSettlement.SettleByHandClear(round, seat);
            }

            round = round.NaturalPong(triple, bots[seat].ChoosePongDiscard(rest));
            continue;
        }

        var meld = HandEvaluator.Evaluate(me.Hand);
        if (meld.Type != MeldType.None)
        {
            return RoundSettlement.SettleByMeld(round, seat, meld);
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
            var hand = round.Players[ponger].Hand;
            if (hand.Count == 2)
            {
                round = round.Pong(ponger, null); // 손 전체가 뽕 2장 → 손 소진
                return RoundSettlement.SettleByTwoPong(round, ponger, seat);
            }

            var pongLaid = hand.Cards.Where(c => c.Number == discard.Number).Take(2).ToList();
            var pongRest = new Hand(hand.Cards.Except(pongLaid)); // 같은 숫자 3장째도 버림 후보
            round = round.Pong(ponger, bots[ponger].ChoosePongDiscard(pongRest));
            if (round.Players[ponger].Hand.Count == 0)
            {
                return RoundSettlement.SettleByTwoPong(round, ponger, seat); // 추가 버림으로 손 털기
            }
        }
    }

    return round.Players.Select(p => p.Hand.Sum()).ToArray();
}

// ── HTTP 헬퍼(레이턴시 기록) ──

async Task<JsonElement> PostAsync(HttpClient http, UserResult result, string path, object? body)
{
    var sw = Stopwatch.StartNew();
    var response = body is null ? await http.PostAsync(path, null) : await http.PostAsJsonAsync(path, body);
    sw.Stop();
    result.LatenciesMs.Add(sw.Elapsed.TotalMilliseconds);

    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"{path} → {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    return await response.Content.ReadFromJsonAsync<JsonElement>();
}

async Task<JsonElement> GetAsync(HttpClient http, UserResult result, string path)
{
    var sw = Stopwatch.StartNew();
    var response = await http.GetAsync(path);
    sw.Stop();
    result.LatenciesMs.Add(sw.Elapsed.TotalMilliseconds);

    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"{path} → {(int)response.StatusCode}");
    }

    return await response.Content.ReadFromJsonAsync<JsonElement>();
}

double Percentile(int p)
{
    if (allLatencies.Count == 0)
    {
        return 0;
    }

    var idx = Math.Min(allLatencies.Count - 1, (int)Math.Ceiling(p / 100.0 * allLatencies.Count) - 1);
    return allLatencies[Math.Max(0, idx)];
}

string Arg(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

int ArgInt(string name, int fallback) => int.TryParse(Arg(name, fallback.ToString()), out var v) ? v : fallback;

/// <summary>유저 1명의 시나리오 결과.</summary>
sealed class UserResult(int index)
{
    public int Index { get; } = index;
    public int GamesPlayed { get; set; }
    public long FinalBalance { get; set; }
    public bool Bankrupt { get; set; }
    public int HttpFailures { get; set; }
    public int BalanceMismatches { get; set; }
    public List<double> LatenciesMs { get; } = new();
}
