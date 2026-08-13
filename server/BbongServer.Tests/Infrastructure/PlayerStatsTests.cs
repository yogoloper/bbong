using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Infrastructure.Persistence;
using BbongServer.Realtime;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace BbongServer.Tests.Infrastructure;

/// <summary>
/// 유저 전적 집계 — 맞춤게임만 센다. 친구방은 상대를 고를 수 있고 봇으로 채워 이길 수도 있어
/// 승률이 의미를 잃는다. 기록 자체는 두 모드 모두 남는다(데이터 추적).
/// </summary>
[TestFixture]
public class PlayerStatsTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=bbong;Username=bbong;Password=bbong_dev";

    private static BbongDbContext NewDb() =>
        new(new DbContextOptionsBuilder<BbongDbContext>().UseNpgsql(ConnectionString).Options);

    private static async Task<Guid> RecordGameAsync(BbongDbContext db, Guid userId, GameMode mode,
        bool won, long payout, int stake = 1000)
    {
        var gameId = Guid.NewGuid();
        db.Games.Add(new GameRow
        {
            Id = gameId,
            RoomCode = "000000",
            Stake = stake,
            TargetPlayers = mode == GameMode.QuickMatch ? 2 : 0,
            StartedAtUtc = DateTimeOffset.UtcNow,
            EndedAtUtc = DateTimeOffset.UtcNow,
            WinnerSeats = won ? "0" : "1",
            Mode = mode.ToString()
        });
        db.GamePlayers.Add(new GamePlayerRow
        {
            GameId = gameId, Seat = 0, UserId = userId, Nickname = "나", IsBot = false,
            FinalDebt = won ? -7 : 21, Payout = payout, Won = won
        });
        await db.SaveChangesAsync();
        return gameId;
    }

    [Test]
    public async Task Stats_count_only_quick_match_games()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000);
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: false, payout: 0);
        await RecordGameAsync(db, userId, GameMode.Friend, won: true, payout: 0, stake: 0);

        var stats = await PlayerStats.ForAsync(db, userId);

        Assert.That(stats.Games, Is.EqualTo(2));
        Assert.That(stats.Wins, Is.EqualTo(1));
    }

    [Test]
    public async Task Win_rate_is_reported_as_a_percentage()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000);
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000);
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: false, payout: 0);
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: false, payout: 0);

        var stats = await PlayerStats.ForAsync(db, userId);

        Assert.That(stats.WinRate, Is.EqualTo(50));
    }

    [Test]
    public async Task Total_winnings_add_up()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000);
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 3000);

        var stats = await PlayerStats.ForAsync(db, userId);

        Assert.That(stats.TotalWinnings, Is.EqualTo(5000));
    }

    [Test]
    public async Task Unfinished_games_are_not_counted()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        var gameId = Guid.NewGuid();
        db.Games.Add(new GameRow
        {
            Id = gameId, RoomCode = "000000", Stake = 1000, TargetPlayers = 2,
            StartedAtUtc = DateTimeOffset.UtcNow, EndedAtUtc = null, Mode = "QuickMatch"
        });
        db.GamePlayers.Add(new GamePlayerRow
        {
            GameId = gameId, Seat = 0, UserId = userId, Nickname = "나", IsBot = false
        });
        await db.SaveChangesAsync();

        var stats = await PlayerStats.ForAsync(db, userId);

        Assert.That(stats.Games, Is.EqualTo(0));
    }

    [Test]
    public async Task A_player_with_no_games_reads_as_zero()
    {
        await using var db = NewDb();

        var stats = await PlayerStats.ForAsync(db, Guid.NewGuid());

        Assert.That(stats.Games, Is.EqualTo(0));
        Assert.That(stats.Wins, Is.EqualTo(0));
        Assert.That(stats.WinRate, Is.EqualTo(0));
        Assert.That(stats.TotalWinnings, Is.EqualTo(0));
    }
}
