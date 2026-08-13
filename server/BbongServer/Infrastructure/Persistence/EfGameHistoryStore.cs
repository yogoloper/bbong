using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Realtime;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>EF Core 기반 게임 히스토리 저장소(PostgreSQL).</summary>
public sealed class EfGameHistoryStore : IGameHistoryStore
{
    private readonly BbongDbContext _db;

    public EfGameHistoryStore(BbongDbContext db) => _db = db;

    public async Task CreateGameAsync(GameRecord game)
    {
        _db.Games.Add(new GameRow
        {
            Id = game.Id,
            RoomCode = game.RoomCode,
            Stake = game.Stake,
            TargetPlayers = game.TargetPlayers,
            StartedAtUtc = game.StartedAtUtc,
            Mode = game.Mode.ToString()
        });
        _db.GamePlayers.AddRange(game.Players.Select(p => new GamePlayerRow
        {
            GameId = game.Id,
            Seat = p.Seat,
            UserId = p.UserId,
            Nickname = p.Nickname,
            IsBot = p.IsBot
        }));
        await _db.SaveChangesAsync();
    }

    public async Task AppendEventsAsync(Guid gameId, IReadOnlyList<HistoryEvent> events)
    {
        var now = DateTimeOffset.UtcNow;
        _db.GameEvents.AddRange(events.Select(e => new GameEventRow
        {
            GameId = gameId,
            RoundIndex = e.RoundIndex,
            Seat = e.Seat,
            Type = e.Type,
            DataJson = e.DataJson,
            AtUtc = now
        }));
        await _db.SaveChangesAsync();
    }

    public async Task CompleteGameAsync(GameCompletion completion)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == completion.GameId);
        if (game is null)
        {
            return;
        }

        game.EndedAtUtc = completion.EndedAtUtc;
        game.WinnerSeats = string.Join(",", completion.WinnerSeats);

        var players = await _db.GamePlayers.Where(p => p.GameId == completion.GameId).ToListAsync();
        foreach (var p in players)
        {
            if (p.Seat < completion.FinalDebts.Length)
            {
                p.FinalDebt = completion.FinalDebts[p.Seat];
            }

            p.Payout = completion.PayoutsBySeat.TryGetValue(p.Seat, out var payout) ? payout : 0;
            p.Won = completion.WinnerSeats.Contains(p.Seat);
        }

        await _db.SaveChangesAsync();
    }
}

/// <summary>방 루프(장수명)용 — 호출마다 DI 스코프를 열어 EF 저장소 실행.</summary>
public sealed class ScopedGameHistoryStore : IGameHistoryStore
{
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopes;

    public ScopedGameHistoryStore(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task CreateGameAsync(GameRecord game)
    {
        using var scope = _scopes.CreateScope();
        await Store(scope).CreateGameAsync(game);
    }

    public async Task AppendEventsAsync(Guid gameId, IReadOnlyList<HistoryEvent> events)
    {
        using var scope = _scopes.CreateScope();
        await Store(scope).AppendEventsAsync(gameId, events);
    }

    public async Task CompleteGameAsync(GameCompletion completion)
    {
        using var scope = _scopes.CreateScope();
        await Store(scope).CompleteGameAsync(completion);
    }

    private static EfGameHistoryStore Store(Microsoft.Extensions.DependencyInjection.IServiceScope scope) =>
        new(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<BbongDbContext>(scope.ServiceProvider));
}
