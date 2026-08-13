using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Infrastructure.Persistence;
using BbongServer.Realtime;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace BbongServer.Tests.Infrastructure;

/// <summary>
/// 최근 게임 기록 — 전적 숫자만으로는 "왜 그런지"를 못 본다. 판별 결과·정산액·인원을
/// 최신순으로 돌려준다. 집계(PlayerStats)와 달리 친구방도 함께 보여준다:
/// 승률에서 빼는 것과 내 기록에서 감추는 것은 다른 얘기다.
/// </summary>
[TestFixture]
public class PlayerHistoryTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=bbong;Username=bbong;Password=bbong_dev";

    private static BbongDbContext NewDb() =>
        new(new DbContextOptionsBuilder<BbongDbContext>().UseNpgsql(ConnectionString).Options);

    private static async Task<Guid> RecordGameAsync(BbongDbContext db, Guid userId, GameMode mode,
        bool won, long payout, DateTimeOffset endedAt, int seats = 2, int stake = 1000)
    {
        var gameId = Guid.NewGuid();
        db.Games.Add(new GameRow
        {
            Id = gameId,
            RoomCode = "000000",
            Stake = stake,
            TargetPlayers = mode == GameMode.QuickMatch ? seats : 0,
            StartedAtUtc = endedAt.AddMinutes(-5),
            EndedAtUtc = endedAt,
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
        return gameId;
    }

    [Test]
    public async Task Most_recent_game_comes_first()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: false, payout: 0, endedAt: now.AddHours(-2));
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 2000, endedAt: now);

        var history = await PlayerHistory.ForAsync(db, userId, limit: 10);

        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history[0].Won, Is.True);
        Assert.That(history[0].Payout, Is.EqualTo(2000));
    }

    [Test]
    public async Task Friend_games_are_listed_even_though_they_are_not_counted_in_stats()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.Friend, won: true, payout: 0,
            endedAt: DateTimeOffset.UtcNow, stake: 0);

        var history = await PlayerHistory.ForAsync(db, userId, limit: 10);

        Assert.That(history, Has.Count.EqualTo(1));
        Assert.That(history[0].Mode, Is.EqualTo(nameof(GameMode.Friend)));
    }

    [Test]
    public async Task Each_entry_reports_how_many_played()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 5000,
            endedAt: DateTimeOffset.UtcNow, seats: 4);

        var history = await PlayerHistory.ForAsync(db, userId, limit: 10);

        Assert.That(history[0].Players, Is.EqualTo(4));
        Assert.That(history[0].Stake, Is.EqualTo(1000));
    }

    [Test]
    public async Task Rank_comes_from_the_final_debts_of_the_people_at_the_table()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        var gameId = Guid.NewGuid();
        db.Games.Add(new GameRow
        {
            Id = gameId, RoomCode = "000000", Stake = 1000, TargetPlayers = 3,
            StartedAtUtc = DateTimeOffset.UtcNow, EndedAtUtc = DateTimeOffset.UtcNow,
            WinnerSeats = "1", Mode = "QuickMatch"
        });
        // 빚이 적을수록 좋은 성적. 나(-3)는 우승자(-9)에 이어 2등이다.
        db.GamePlayers.Add(new GamePlayerRow
        {
            GameId = gameId, Seat = 0, UserId = userId, Nickname = "나", FinalDebt = -3
        });
        db.GamePlayers.Add(new GamePlayerRow
        {
            GameId = gameId, Seat = 1, UserId = Guid.NewGuid(), Nickname = "이긴사람",
            FinalDebt = -9, Won = true
        });
        db.GamePlayers.Add(new GamePlayerRow
        {
            GameId = gameId, Seat = 2, UserId = Guid.NewGuid(), Nickname = "꼴찌", FinalDebt = 12
        });
        await db.SaveChangesAsync();

        var history = await PlayerHistory.ForAsync(db, userId, limit: 10);

        Assert.That(history[0].Rank, Is.EqualTo(2));
    }

    [Test]
    public async Task Tied_players_share_a_rank()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        var gameId = Guid.NewGuid();
        db.Games.Add(new GameRow
        {
            Id = gameId, RoomCode = "000000", Stake = 1000, TargetPlayers = 3,
            StartedAtUtc = DateTimeOffset.UtcNow, EndedAtUtc = DateTimeOffset.UtcNow,
            WinnerSeats = "0,1", Mode = "QuickMatch"
        });
        db.GamePlayers.Add(new GamePlayerRow
        {
            GameId = gameId, Seat = 0, UserId = userId, Nickname = "나", FinalDebt = -5, Won = true
        });
        db.GamePlayers.Add(new GamePlayerRow
        {
            GameId = gameId, Seat = 1, UserId = Guid.NewGuid(), Nickname = "공동1등",
            FinalDebt = -5, Won = true
        });
        db.GamePlayers.Add(new GamePlayerRow
        {
            GameId = gameId, Seat = 2, UserId = Guid.NewGuid(), Nickname = "꼴찌", FinalDebt = 10
        });
        await db.SaveChangesAsync();

        var history = await PlayerHistory.ForAsync(db, userId, limit: 10);

        Assert.That(history[0].Rank, Is.EqualTo(1));
    }

    [Test]
    public async Task Bot_seats_do_not_take_a_rank_away_from_a_human()
    {
        // §9-4: 이탈 대체 봇은 우승 후보가 아니다. 빚만으로 줄 세우면 봇에게 밀려
        // "이겼는데 3등"처럼 승패와 어긋난 등수가 나온다.
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        var gameId = Guid.NewGuid();
        db.Games.Add(new GameRow
        {
            Id = gameId, RoomCode = "000000", Stake = 1000, TargetPlayers = 4,
            StartedAtUtc = DateTimeOffset.UtcNow, EndedAtUtc = DateTimeOffset.UtcNow,
            WinnerSeats = "0", Mode = "QuickMatch"
        });
        db.GamePlayers.Add(new GamePlayerRow
        {
            GameId = gameId, Seat = 0, UserId = userId, Nickname = "나", FinalDebt = 65,
            Won = true, Payout = 4000
        });
        for (var seat = 1; seat <= 3; seat++)
        {
            db.GamePlayers.Add(new GamePlayerRow
            {
                GameId = gameId, Seat = seat, UserId = null, Nickname = $"봇{seat}", IsBot = true,
                FinalDebt = seat == 3 ? 39 : 60 + seat
            });
        }

        await db.SaveChangesAsync();

        var history = await PlayerHistory.ForAsync(db, userId, limit: 10);

        Assert.That(history[0].Rank, Is.EqualTo(1));
        Assert.That(history[0].Humans, Is.EqualTo(1));
        Assert.That(history[0].Players, Is.EqualTo(4));
    }

    [Test]
    public async Task Everyone_else_at_the_table_is_named()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        await RecordGameAsync(db, userId, GameMode.QuickMatch, won: true, payout: 3000,
            endedAt: DateTimeOffset.UtcNow, seats: 3);

        var history = await PlayerHistory.ForAsync(db, userId, limit: 10);

        Assert.That(history[0].Opponents, Is.EqualTo(new[] { "봇1", "봇2" }));
    }

    [Test]
    public async Task Unfinished_games_are_left_out()
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

        var history = await PlayerHistory.ForAsync(db, userId, limit: 10);

        Assert.That(history, Is.Empty);
    }

    [Test]
    public async Task The_limit_caps_how_many_come_back()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var db = NewDb();
        for (var i = 0; i < 5; i++)
        {
            await RecordGameAsync(db, userId, GameMode.QuickMatch, won: i % 2 == 0, payout: 1000,
                endedAt: now.AddMinutes(-i));
        }

        var history = await PlayerHistory.ForAsync(db, userId, limit: 3);

        Assert.That(history, Has.Count.EqualTo(3));
    }
}
