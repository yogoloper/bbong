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

    public Task<Wallet> LoadBalanceAsync(Guid userId)
    {
        lock (_gate)
        {
            var last = _entries.LastOrDefault(e => e.UserId == userId);
            return Task.FromResult(Wallet.FromBalance(userId, last?.BalanceAfter ?? 0));
        }
    }

    public Task<IReadOnlyList<UnsettledEscrow>> FindUnsettledEscrowsAsync(DateTimeOffset olderThan)
    {
        lock (_gate)
        {
            var settled = _entries
                .Where(e => e.Reason is LedgerReason.StakePayout or LedgerReason.StakeRefund && e.RefId is not null)
                .Select(e => (e.UserId, e.RefId))
                .ToHashSet();

            var stranded = _entries
                .Where(e => e.Reason == LedgerReason.StakeEscrow
                    && e.RefId is not null
                    && e.OccurredAt < olderThan
                    && !settled.Contains((e.UserId, e.RefId)))
                .Select(e => new UnsettledEscrow(e.UserId, e.RefId!.Value, -e.Delta))
                .ToList();

            return Task.FromResult<IReadOnlyList<UnsettledEscrow>>(stranded);
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, System.Threading.SemaphoreSlim> _userLocks = new();

    public async Task<T> WithWalletLockAsync<T>(Guid userId, Func<Task<T>> action)
    {
        var gate = _userLocks.GetOrAdd(userId, _ => new System.Threading.SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
