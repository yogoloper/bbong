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
    int? FinalDebt);

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

        // 인원은 좌석 수로 센다. 정원(TargetPlayers)은 친구방에서 0이고, 매칭에서도
        // 중도 이탈이 있으면 실제로 앉은 인원과 어긋난다.
        var gameIds = mine.Select(m => m.GameId).ToList();
        var seats = await db.GamePlayers
            .Where(p => gameIds.Contains(p.GameId))
            .GroupBy(p => p.GameId)
            .Select(grp => new { GameId = grp.Key, Count = grp.Count() })
            .ToDictionaryAsync(x => x.GameId, x => x.Count);

        return mine
            .Select(m => new PlayerHistoryEntry(
                m.EndedAt,
                m.Mode,
                seats.GetValueOrDefault(m.GameId),
                m.Stake,
                m.Won,
                m.Payout,
                m.FinalDebt))
            .ToList();
    }
}
