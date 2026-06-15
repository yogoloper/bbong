using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Wallet;

namespace BbongServer.Infrastructure.InMemory;

/// <summary>인메모리 원장 저장소(첫 골격·테스트용). 후속 EF Core + PostgreSQL로 대체.</summary>
public sealed class InMemoryLedgerStore : ILedgerStore
{
    private readonly object _gate = new();
    private readonly List<LedgerEntry> _entries = new();

    public Task AppendAsync(IEnumerable<LedgerEntry> entries)
    {
        lock (_gate)
        {
            _entries.AddRange(entries);
        }

        return Task.CompletedTask;
    }

    public Task<Wallet> LoadWalletAsync(Guid userId)
    {
        lock (_gate)
        {
            var owned = _entries.Where(e => e.UserId == userId).ToList();
            return Task.FromResult(Wallet.Rehydrate(userId, owned));
        }
    }
}
