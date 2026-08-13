using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Realtime;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>인원별 성적 한 줄.</summary>
public sealed record SeatCountStats(int Players, int Games, int Wins, int WinRate, long TotalWinnings);

/// <summary>한 모드의 성적과 인원별 내역.</summary>
public sealed record ModeStats(
    string Mode, int Games, int Wins, int WinRate, long TotalWinnings, IReadOnlyList<SeatCountStats> ByPlayers);

/// <summary>모드별 전적 묶음. 화면은 이걸 탭으로 나눠 보여준다.</summary>
public sealed record PlayerStatsBreakdown(IReadOnlyList<ModeStats> Modes);

/// <summary>
/// 유저 전적. 맞춤게임과 친구방을 한 덩어리로 섞지 않는다 — 친구방은 상대를 직접 고를 수 있고
/// 봇으로 채워 이길 수도 있어 같은 승률로 읽으면 곤란하다. 대신 각각을 따로 집계하고,
/// 인원별로도 나눠 준다: 2인전과 6인전은 이길 확률부터 다른 게임이다.
/// </summary>
public static class PlayerStats
{
    /// <summary>게임 정원 범위. 안 해본 인원도 0으로 자리를 남겨 표가 들쭉날쭉하지 않게 한다.</summary>
    private static readonly int[] SeatCounts = [2, 3, 4, 5, 6];

    /// <summary>탭 순서. 판돈이 걸리는 맞춤게임이 먼저다.</summary>
    private static readonly string[] Modes = [nameof(GameMode.QuickMatch), nameof(GameMode.Friend)];

    public static async Task<PlayerStatsBreakdown> BreakdownAsync(BbongDbContext db, Guid userId)
    {
        var mine = await (
            from p in db.GamePlayers
            join g in db.Games on p.GameId equals g.Id
            where p.UserId == userId
                && g.EndedAtUtc != null // 진행 중이거나 유실된 판은 전적이 아니다
            select new { p.GameId, g.Mode, p.Won, p.Payout }).ToListAsync();

        // 인원은 좌석 수로 센다. 정원(TargetPlayers)은 친구방에서 0이고, 매칭에서도
        // 중도 이탈이 있으면 실제로 앉은 인원과 어긋난다.
        var seatCounts = new Dictionary<Guid, int>();
        if (mine.Count > 0)
        {
            var gameIds = mine.Select(m => m.GameId).Distinct().ToList();
            seatCounts = await db.GamePlayers
                .Where(p => gameIds.Contains(p.GameId))
                .GroupBy(p => p.GameId)
                .Select(grp => new { GameId = grp.Key, Count = grp.Count() })
                .ToDictionaryAsync(x => x.GameId, x => x.Count);
        }

        var rows = mine
            .Select(m => new { m.Mode, m.Won, m.Payout, Players = seatCounts.GetValueOrDefault(m.GameId) })
            .ToList();

        return new PlayerStatsBreakdown(Modes.Select(mode =>
        {
            var ofMode = rows.Where(r => r.Mode == mode).ToList();
            var (games, wins, winRate, winnings) = Tally(ofMode.Select(r => (r.Won, r.Payout)));
            return new ModeStats(mode, games, wins, winRate, winnings, SeatCounts.Select(seats =>
            {
                var ofSeats = ofMode.Where(r => r.Players == seats).ToList();
                var t = Tally(ofSeats.Select(r => (r.Won, r.Payout)));
                return new SeatCountStats(seats, t.Games, t.Wins, t.WinRate, t.TotalWinnings);
            }).ToList());
        }).ToList());
    }

    private static (int Games, int Wins, int WinRate, long TotalWinnings) Tally(
        IEnumerable<(bool Won, long Payout)> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var wins = list.Count(r => r.Won);
        return (list.Count, wins, (int)Math.Round(wins * 100.0 / list.Count), list.Sum(r => r.Payout));
    }
}
