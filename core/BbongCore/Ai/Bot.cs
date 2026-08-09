using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Config;
using BbongCore.Game;

namespace BbongCore.Ai;

/// <summary>휴리스틱 AI 봇. 난이도에 따라 버림/뽕/스톱 결정이 달라집니다(README Phase 2).</summary>
public sealed class Bot
{
    private static int _seedTail; // 같은 밀리초에 여러 봇을 만들어도 시드가 갈리게

    private readonly IRandom _rng;

    public Bot(BotDifficulty difficulty, IRandom? rng = null)
    {
        Difficulty = difficulty;
        _rng = rng ?? new SeededRandom(unchecked(System.Environment.TickCount * 31 + ++_seedTail));
    }

    public BotDifficulty Difficulty { get; }

    /// <summary>
    /// 버릴 카드 선택.
    /// Easy=최대 수, Normal=쌍 보존 후 최대 단일, Hard=쓸모도 최소 카드(쌍·연속 보존, 저점 지향).
    /// </summary>
    public Card ChooseDiscard(Hand hand) => Difficulty switch
    {
        BotDifficulty.Normal => HighestSingle(hand),
        BotDifficulty.Hard => LeastUseful(hand),
        _ => Highest(hand.Cards)
    };

    private static Card HighestSingle(Hand hand)
    {
        var singles = hand.Cards
            .Where(c => hand.Cards.Count(x => x.Number == c.Number) == 1)
            .ToList();

        return Highest(singles.Count > 0 ? singles : hand.Cards);
    }

    /// <summary>
    /// 쓸모도가 가장 낮은 카드를 버립니다(동점이면 큰 수). 쌍·연속(run) 조각과 낮은 수를 보존합니다.
    /// 연속 보존은 족보(6장)가 성립 가능할 때만 — 뽕 이후 3장 손패에선 무의미하고 고점만 끌어안습니다.
    /// </summary>
    private static Card LeastUseful(Hand hand)
    {
        var cards = hand.Cards;
        var meldFeasible = cards.Count >= GameConfig.HandSize; // 뽕 이후(≤3장)는 족보 불가

        int Usefulness(Card c)
        {
            var sameNumber = cards.Count(x => x.Number == c.Number) >= 2 ? 100 : 0;        // 쌍/총통 노림
            var adjacent = meldFeasible && cards.Any(x => x.Number == c.Number - 1 || x.Number == c.Number + 1) ? 20 : 0; // 스트레이트 노림
            return sameNumber + adjacent + (13 - c.Number);                                  // 낮은 수 살짝 우대
        }

        return cards.OrderBy(Usefulness).ThenByDescending(c => c.Number).First();
    }

    /// <summary>뽕 가능 시 뽕할지. Easy는 안 함.</summary>
    public bool ShouldPong() => Difficulty != BotDifficulty.Easy;

    /// <summary>뽕 이후 버릴 카드(남은 손패 중 최대 수 → 저점 지향).</summary>
    public Card ChoosePongDiscard(Hand handAfterRemovingPair) => Highest(handAfterRemovingPair.Cards);

    /// <summary>
    /// 스톱 가능 시 스톱할지. Easy=안 함.
    /// Normal/Hard=손합이 낮을수록 높은 확률로 스톱(항상 지르던 편중을 확률 밴드로 완화).
    /// Hard는 추가로 바가지 회피 + 손패가 쌍이면 두 번 뽕(손 털기·상대 박 +20)을 노리고 자주 보류.
    /// </summary>
    public bool ShouldStop(RoundState round, int seat)
    {
        if (Difficulty == BotDifficulty.Easy)
        {
            return false;
        }

        var hand = round.Players[seat].Hand;
        var sum = hand.Sum();
        if (sum > GameConfig.DefaultStopLimit)
        {
            return false;
        }

        if (Difficulty == BotDifficulty.Hard)
        {
            if (StopResolver.IsBagaji(round, seat))
            {
                return false; // 지면서 +30 무는 스톱은 절대 안 함
            }

            if (IsPair(hand) && Chance(70))
            {
                return false; // 쌍 유지 — 두 번 뽕 한 방을 노린다
            }
        }

        var hard = Difficulty == BotDifficulty.Hard;
        var pct = sum <= 2 ? (hard ? 85 : 80)
            : sum <= 5 ? (hard ? 40 : 30)
            : (hard ? 12 : 8);
        return Chance(pct);
    }

    private bool Chance(int percent) => _rng.Next(100) < percent;

    /// <summary>손패 2장이 같은 숫자(뽕 대기 쌍)인지.</summary>
    private static bool IsPair(Hand hand) =>
        hand.Count == 2 && hand.Cards[0].Number == hand.Cards[1].Number;

    private static Card Highest(IEnumerable<Card> cards) => cards.OrderByDescending(c => c.Number).First();
}
