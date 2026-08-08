using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Wallet;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>EF Core 기반 원장 저장소(PostgreSQL, append-only).</summary>
public sealed class EfLedgerStore : ILedgerStore
{
    private readonly BbongDbContext _db;

    public EfLedgerStore(BbongDbContext db) => _db = db;

    public async Task AppendAsync(IEnumerable<LedgerEntry> entries)
    {
        _db.Ledger.AddRange(entries.Select(LedgerRow.From));
        await _db.SaveChangesAsync();
    }

    public async Task<Wallet> LoadWalletAsync(Guid userId)
    {
        var entries = await _db.Ledger
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.Id)
            .Select(e => e.ToEntry())
            .ToListAsync();

        return Wallet.Rehydrate(userId, entries);
    }

    /// <summary>
    /// 트랜잭션 + pg_advisory_xact_lock으로 같은 유저의 잔액 조회→append를 직렬화(R7).
    /// 락은 트랜잭션 종료 시 자동 해제. PG가 아니면(테스트 등) 락 없이 실행.
    /// </summary>
    public async Task<T> WithWalletLockAsync<T>(Guid userId, Func<Task<T>> action)
    {
        if (!_db.Database.IsNpgsql())
        {
            return await action();
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({userId.ToString()}))");
        var result = await action();
        await tx.CommitAsync();
        return result;
    }
}
