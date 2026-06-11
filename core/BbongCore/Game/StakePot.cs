using System.Collections.Generic;
using System.Linq;

namespace BbongCore.Game;

/// <summary>판돈 분배(rules.md §9). 환전 불가 가상 재화, 게임 내 빚과 별개.</summary>
public static class StakePot
{
    /// <summary>
    /// 세트 1등(들)에게 총 판돈(stake × 인원)을 지급합니다.
    /// 단독 1등은 전부, 공동 1등은 균등 분배(나머지 절사). 비승자는 0(§9-2, §9-3).
    /// </summary>
    public static int[] Distribute(int stakePerPlayer, int playerCount, IReadOnlyList<int> winnerSeats)
    {
        var pot = stakePerPlayer * playerCount;
        var share = pot / winnerSeats.Count; // 나머지 절사

        var winners = winnerSeats.ToHashSet();
        return Enumerable.Range(0, playerCount)
            .Select(seat => winners.Contains(seat) ? share : 0)
            .ToArray();
    }
}
