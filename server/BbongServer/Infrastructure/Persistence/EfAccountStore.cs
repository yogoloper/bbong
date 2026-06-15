using System;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Accounts;
using BbongServer.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>EF Core 기반 계정 저장소(PostgreSQL).</summary>
public sealed class EfAccountStore : IAccountStore
{
    private readonly BbongDbContext _db;

    public EfAccountStore(BbongDbContext db) => _db = db;

    public async Task SaveAsync(UserAccount account)
    {
        var existing = await _db.Accounts.FindAsync(account.Id);
        if (existing is null)
        {
            _db.Accounts.Add(account);
        }
        else if (!ReferenceEquals(existing, account))
        {
            _db.Entry(existing).CurrentValues.SetValues(account);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<UserAccount?> GetByIdAsync(Guid id) =>
        await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id);

    public async Task<UserAccount?> GetBySocialAsync(SocialProvider provider, string subject) =>
        await _db.Accounts.FirstOrDefaultAsync(a => a.Provider == provider && a.SocialSubject == subject);
}
