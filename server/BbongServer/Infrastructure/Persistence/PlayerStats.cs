using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Realtime;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>
/// 유저 전적. 맞춤게임(빠른매칭)만 집계한다 — 친구방은 상대를 직접 고를 수 있고 봇으로
/// 채워 이길 수도 있어 승률이 의미를 잃는다. 기록 자체는 두 모드 모두 남는다.
/// </summary>
public sealed record PlayerStats(int Games, int Wins, int WinRate, long TotalWinnings)
{
    public static readonly PlayerStats Empty = new(0, 0, 0, 0);

    public static async Task<PlayerStats> ForAsync(BbongDbContext db, Guid userId)
    {
        var rows = await (
            from p in db.GamePlayers
            join g in db.Games on p.GameId equals g.Id
            where p.UserId == userId
                && g.Mode == nameof(GameMode.QuickMatch)
                && g.EndedAtUtc != null // 진행 중이거나 유실된 판은 전적이 아니다
            select new { p.Won, p.Payout }).ToListAsync();

        if (rows.Count == 0)
        {
            return Empty;
        }

        var wins = rows.Count(r => r.Won);
        return new PlayerStats(
            rows.Count,
            wins,
            (int)Math.Round(wins * 100.0 / rows.Count),
            rows.Sum(r => r.Payout));
    }
}
