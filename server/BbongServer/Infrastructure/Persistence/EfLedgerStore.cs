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
}
