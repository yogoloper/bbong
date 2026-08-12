using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Accounts;
using BbongServer.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>
/// EF Core 기반 계정 저장소(PostgreSQL). 소셜 연동은 account_socials로 분리돼 있어
/// 계정을 읽고 쓸 때 함께 맞춰 준다.
/// </summary>
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

        await SyncSocialsAsync(account);
        await _db.SaveChangesAsync();
    }

    /// <summary>도메인이 들고 있는 연동 목록에 맞춰 추가분만 기록한다(연동 해제는 아직 없음).</summary>
    private async Task SyncSocialsAsync(UserAccount account)
    {
        if (account.Socials.Count == 0)
        {
            return;
        }

        var stored = await _db.AccountSocials
            .Where(s => s.AccountId == account.Id)
            .ToListAsync();

        foreach (var link in account.Socials)
        {
            if (stored.Any(s => s.Provider == link.Provider && s.Subject == link.Subject))
            {
                continue;
            }

            _db.AccountSocials.Add(new AccountSocialRow
            {
                AccountId = account.Id,
                Provider = link.Provider,
                Subject = link.Subject
            });
        }
    }

    public async Task<UserAccount?> GetByIdAsync(Guid id)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        return account is null ? null : await WithSocialsAsync(account);
    }

    public async Task<UserAccount?> GetBySocialAsync(SocialProvider provider, string subject)
    {
        var accountId = await _db.AccountSocials
            .Where(s => s.Provider == provider && s.Subject == subject)
            .Select(s => (Guid?)s.AccountId)
            .FirstOrDefaultAsync();

        return accountId is null ? null : await GetByIdAsync(accountId.Value);
    }

    private async Task<UserAccount> WithSocialsAsync(UserAccount account)
    {
        var links = await _db.AccountSocials
            .Where(s => s.AccountId == account.Id)
            .OrderBy(s => s.Id)
            .Select(s => new SocialLink(s.Provider, s.Subject))
            .ToListAsync();

        account.RestoreSocials(links);
        return account;
    }
}
