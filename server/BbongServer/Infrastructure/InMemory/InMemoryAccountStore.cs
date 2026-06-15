using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Accounts;

namespace BbongServer.Infrastructure.InMemory;

/// <summary>인메모리 계정 저장소(첫 골격·테스트용). 후속 EF Core + PostgreSQL로 대체.</summary>
public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly ConcurrentDictionary<Guid, UserAccount> _accounts = new();

    public Task SaveAsync(UserAccount account)
    {
        _accounts[account.Id] = account;
        return Task.CompletedTask;
    }

    public Task<UserAccount?> GetByIdAsync(Guid id) =>
        Task.FromResult(_accounts.TryGetValue(id, out var account) ? account : null);
}
