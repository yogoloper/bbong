using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Game;

namespace BbongCore.Ai;

/// <summary>휴리스틱 AI 봇. 난이도에 따라 버림/뽕/스톱 결정이 달라집니다(README Phase 2).</summary>
public sealed class Bot
{
    public Bot(BotDifficulty difficulty) => Difficulty = difficulty;

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
    /// </summary>
    private static Card LeastUseful(Hand hand)
    {
        var cards = hand.Cards;

        int Usefulness(Card c)
        {
            var sameNumber = cards.Count(x => x.Number == c.Number) >= 2 ? 100 : 0;        // 쌍/총통 노림
            var adjacent = cards.Any(x => x.Number == c.Number - 1 || x.Number == c.Number + 1) ? 20 : 0; // 스트레이트 노림
            return sameNumber + adjacent + (13 - c.Number);                                  // 낮은 수 살짝 우대
        }

        return cards.OrderBy(Usefulness).ThenByDescending(c => c.Number).First();
    }

    /// <summary>뽕 가능 시 뽕할지. Easy는 안 함.</summary>
    public bool ShouldPong() => Difficulty != BotDifficulty.Easy;

    /// <summary>뽕 이후 버릴 카드(남은 손패 중 최대 수 → 저점 지향).</summary>
    public Card ChoosePongDiscard(Hand handAfterRemovingPair) => Highest(handAfterRemovingPair.Cards);

    /// <summary>스톱 가능 시 스톱할지. Easy=안 함, Normal=함, Hard=바가지면 회피.</summary>
    public bool ShouldStop(RoundState round, int seat) => Difficulty switch
    {
        BotDifficulty.Normal => true,
        BotDifficulty.Hard => !StopResolver.IsBagaji(round, seat),
        _ => false
    };

    private static Card Highest(IEnumerable<Card> cards) => cards.OrderByDescending(c => c.Number).First();
}
