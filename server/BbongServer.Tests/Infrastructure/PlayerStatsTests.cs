using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Infrastructure.Persistence;
using BbongServer.Realtime;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace BbongServer.Tests.Infrastructure;

/// <summary>
/// 유저 전적 — 맞춤게임과 친구방을 한 덩어리로 섞지 않는다. 친구방은 상대를 고를 수 있고
/// 봇으로 채워 이길 수도 있어 같은 승률로 읽으면 안 된다. 대신 각각을 따로 집계하고,
/// 인원(2~6)별로도 나눠 준다 — 2인전 승률과 6인전 승률은 다른 게임이다.
/// </summary>
[TestFixture]
public class PlayerStatsTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=bbong;Username=bbong;Password=bbong_dev";

    private static BbongDbContext NewDb() =>
        new(new DbContextOptionsBuilder<BbongDbContext>().UseNpgsql(ConnectionString).Options);

    private static async Task RecordGameAsync(BbongDbContext db, Guid userId, GameMode mode,
        bool won, long payout, int seats = 2, int stake = 1000)
    {
        var gameId = Guid.NewGuid();
        db.Games.Add(new GameRow
        {
            Id = gameId,
            RoomCode = "000000",
            Stake = stake,
            TargetPlayers = mode == GameMode.QuickMatch ? seats : 0,
            StartedAtUtc = DateTimeOffset.UtcNow,
            EndedAtUtc = DateTimeOffset.UtcNow,
            WinnerSeats = won ? "0" : "1",
            Mode = mode.ToString()
        });

        for (var seat = 0; seat < seats; seat++)
        {
            db.GamePlayers.Add(new GamePlayerRow
            {
                GameId = gameId,
                Seat = seat,
                UserId = seat == 0 ? userId : null,
                Nickname = seat == 0 ? "나" : $"봇{seat}",
                IsBot = seat != 0,
                FinalDebt = won && seat == 0 ? -7 : 21,
                Payout = seat == 0 ? payout : 0,
                Won = seat == 0 && won
            });
        }

        await db.SaveChangesAsync();
    }

    private static ModeStats Mode(PlayerStatsBreakdown breakdown, GameMode mode) =>
        breakdown.Modes.Single(m => m.Mode == mode.ToString());

    [Test]
    public async Task Each_mode_is_counted_on_its_own()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000);
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: false, payout: 0);
        await RecordGameAsync(db, userId, GameMode.Friend, won: true, payout: 0, stake: 0);

        var breakdown = await PlayerStats.BreakdownAsync(db, userId);

        Assert.That(Mode(breakdown, GameMode.QuickMatch).Games, Is.EqualTo(2));
        Assert.That(Mode(breakdown, GameMode.QuickMatch).Wins, Is.EqualTo(1));
        Assert.That(Mode(breakdown, GameMode.Friend).Games, Is.EqualTo(1));
        Assert.That(Mode(breakdown, GameMode.Friend).Wins, Is.EqualTo(1));
    }

    [Test]
    public async Task Both_modes_come_back_even_when_one_was_never_played()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000);

        var breakdown = await PlayerStats.BreakdownAsync(db, userId);

        Assert.That(breakdown.Modes.Select(m => m.Mode),
            Is.EqualTo(new[] { nameof(GameMode.QuickMatch), nameof(GameMode.Friend) }));
        Assert.That(Mode(breakdown, GameMode.Friend).Games, Is.EqualTo(0));
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

        var breakdown = await PlayerStats.BreakdownAsync(db, userId);

        Assert.That(Mode(breakdown, GameMode.QuickMatch).WinRate, Is.EqualTo(50));
    }

    [Test]
    public async Task Total_winnings_add_up()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000);
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 3000);

        var breakdown = await PlayerStats.BreakdownAsync(db, userId);

        Assert.That(Mode(breakdown, GameMode.QuickMatch).TotalWinnings, Is.EqualTo(5000));
    }

    [Test]
    public async Task Games_are_split_by_how_many_sat_down()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000, seats: 2);
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: false, payout: 0, seats: 4);
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 5000, seats: 4);

        var quick = Mode(await PlayerStats.BreakdownAsync(db, userId), GameMode.QuickMatch);

        var twos = quick.ByPlayers.Single(b => b.Players == 2);
        var fours = quick.ByPlayers.Single(b => b.Players == 4);
        Assert.That(twos.Games, Is.EqualTo(1));
        Assert.That(fours.Games, Is.EqualTo(2));
        Assert.That(fours.Wins, Is.EqualTo(1));
        Assert.That(fours.WinRate, Is.EqualTo(50));
    }

    [Test]
    public async Task Every_seat_count_from_two_to_six_has_a_row()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000, seats: 3);

        var quick = Mode(await PlayerStats.BreakdownAsync(db, userId), GameMode.QuickMatch);

        Assert.That(quick.ByPlayers.Select(b => b.Players), Is.EqualTo(new[] { 2, 3, 4, 5, 6 }));
        Assert.That(quick.ByPlayers.Single(b => b.Players == 5).Games, Is.EqualTo(0));
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

        var quick = Mode(await PlayerStats.BreakdownAsync(db, userId), GameMode.QuickMatch);

        Assert.That(quick.Games, Is.EqualTo(0));
    }

    [Test]
    public async Task A_player_with_no_games_reads_as_zero()
    {
        await using var db = NewDb();

        var breakdown = await PlayerStats.BreakdownAsync(db, Guid.NewGuid());

        foreach (var mode in breakdown.Modes)
        {
            Assert.That(mode.Games, Is.EqualTo(0));
            Assert.That(mode.Wins, Is.EqualTo(0));
            Assert.That(mode.WinRate, Is.EqualTo(0));
            Assert.That(mode.TotalWinnings, Is.EqualTo(0));
        }
    }
}
