using System.Linq;
using BbongCore.Config;

namespace BbongCore.Game;

/// <summary>스톱 선언 가능 여부와 스톱 바가지 판정(rules.md §6).</summary>
public static class StopResolver
{
    /// <summary>
    /// 스톱 가능: 뽕한 유저가 2명 이상이고, seat 본인도 뽕했으며,
    /// 손패 합이 한도(기본 10) 이하일 때(rules.md §6-1, §6-2).
    /// </summary>
    public static bool CanStop(RoundState round, int seat, int stopLimit = GameConfig.DefaultStopLimit)
    {
        var pongedCount = round.Players.Count(p => p.HasPonged);
        var player = round.Players[seat];

        return pongedCount >= 2 && player.HasPonged && player.Hand.Sum() <= stopLimit;
    }

    /// <summary>
    /// 스톱 바가지: 스톱 선언자보다 손패 합이 더 작은 '뽕한(2장)' 게이머가 있으면 true(rules.md §6-3).
    /// </summary>
    /// <summary>스톱 실패(박) 판정: 선언자보다 손합이 "적거나 같은" 뽕 게이머가 있으면 실패(§6 — 동점도 실패).</summary>
    public static bool IsBagaji(RoundState round, int stopSeat)
    {
        var declarerSum = round.Players[stopSeat].Hand.Sum();

        return round.Players.Any(p =>
            p.Seat != stopSeat && p.HasPonged && p.Hand.Sum() <= declarerSum);
    }
}
