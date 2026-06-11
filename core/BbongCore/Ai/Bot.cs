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

    /// <summary>버릴 카드 선택. Easy=최대 수, Normal·Hard=쌍 보존 후 최대 단일.</summary>
    public Card ChooseDiscard(Hand hand)
    {
        if (Difficulty == BotDifficulty.Easy)
        {
            return Highest(hand.Cards);
        }

        var singles = hand.Cards
            .Where(c => hand.Cards.Count(x => x.Number == c.Number) == 1)
            .ToList();

        return Highest(singles.Count > 0 ? singles : hand.Cards);
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
