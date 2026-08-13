using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>한 판의 결과 한 줄.</summary>
public sealed record PlayerHistoryEntry(
    DateTimeOffset EndedAt,
    string Mode,
    int Players,
    int Stake,
    bool Won,
    long Payout,
    int? FinalDebt,
    int Rank,
    int Humans,
    IReadOnlyList<string> Opponents);

/// <summary>
/// 최근 게임 기록. 승률 숫자만으로는 흐름이 안 보이니 판별로 결과·정산액을 최신순으로 준다.
/// 집계(PlayerStats)와 달리 친구방도 포함한다 — 승률에서 빼는 것과 내 기록에서 감추는 건 다른 얘기다.
/// </summary>
public static class PlayerHistory
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public static async Task<IReadOnlyList<PlayerHistoryEntry>> ForAsync(
        BbongDbContext db, Guid userId, int limit = DefaultLimit)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);

        var mine = await (
            from p in db.GamePlayers
            join g in db.Games on p.GameId equals g.Id
            where p.UserId == userId && g.EndedAtUtc != null
            orderby g.EndedAtUtc descending
            select new
            {
                p.GameId,
                MySeat = p.Seat,
                EndedAt = g.EndedAtUtc!.Value,
                g.Mode,
                g.Stake,
                p.Won,
                p.Payout,
                p.FinalDebt
            }).Take(limit).ToListAsync();

        if (mine.Count == 0)
        {
            return Array.Empty<PlayerHistoryEntry>();
        }

        // 인원·순위·상대는 모두 그 판의 좌석들에서 나온다. 정원(TargetPlayers)은 친구방에서 0이고
        // 매칭에서도 중도 이탈이 있으면 실제로 앉은 인원과 어긋나 쓸 수 없다.
        var gameIds = mine.Select(m => m.GameId).ToList();
        var seatRows = await db.GamePlayers
            .Where(p => gameIds.Contains(p.GameId))
            .Select(p => new { p.GameId, p.Seat, p.Nickname, p.IsBot, p.FinalDebt })
            .ToListAsync();
        var tables = seatRows.GroupBy(p => p.GameId).ToDictionary(grp => grp.Key, grp => grp.ToList());

        return mine.Select(m =>
        {
            var table = tables.GetValueOrDefault(m.GameId) ?? [];
            var humans = table.Where(p => !p.IsBot).ToList();
            return new PlayerHistoryEntry(
                m.EndedAt,
                m.Mode,
                table.Count,
                m.Stake,
                m.Won,
                m.Payout,
                m.FinalDebt,
                RankOf(m.FinalDebt, humans.Select(p => p.FinalDebt)),
                humans.Count,
                table.Where(p => p.Seat != m.MySeat).OrderBy(p => p.Seat).Select(p => p.Nickname).ToList());
        }).ToList();
    }

    /// <summary>
    /// 빚이 적을수록 좋은 성적이라 오름차순 등수다. 동점자는 같은 등수를 나눠 갖는다 —
    /// 우리 규칙상 공동 1등이 실제로 나오고, 그때 둘 다 상금을 받는다.
    /// 봇 좌석은 세지 않는다: 이탈 대체 봇은 우승 후보가 아니라서(§9-4) 빚만으로 줄 세우면
    /// "이겼는데 3등" 같은 모순이 나온다. 빚이 없는 좌석(미정산)은 맨 뒤로 민다.
    /// </summary>
    private static int RankOf(int? mine, IEnumerable<int?> humans)
    {
        var me = mine ?? int.MaxValue;
        return humans.Count(d => (d ?? int.MaxValue) < me) + 1;
    }
}
