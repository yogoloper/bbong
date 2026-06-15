using System;
using System.Threading.Tasks;
using BbongServer.Domain.Accounts;
using BbongServer.Domain.Wallet;
using BbongServer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace BbongServer.Tests.Infrastructure;

/// <summary>
/// EF Core 저장소의 실제 PostgreSQL round-trip 검증. Docker 인프라(compose) 기동 필요.
/// 각 테스트는 고유 userId로 격리. PG 미기동 환경에선 Category로 제외 가능.
/// </summary>
[TestFixture]
[Category("RequiresPostgres")]
public class EfStoreTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=bbong;Username=bbong;Password=bbong_dev";

    private static BbongDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BbongDbContext>().UseNpgsql(ConnectionString).Options);

    [Test]
    public async Task Account_persists_and_loads_by_id()
    {
        var account = new UserAccount(Guid.NewGuid(), isGuest: true, "테스트 너구리", DateTimeOffset.UtcNow);

        await using (var ctx = NewContext())
        {
            await new EfAccountStore(ctx).SaveAsync(account);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await new EfAccountStore(ctx).GetByIdAsync(account.Id);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Nickname, Is.EqualTo("테스트 너구리"));
            Assert.That(loaded.IsGuest, Is.True);
        }
    }

    [Test]
    public async Task Ledger_appends_and_reloads_balance()
    {
        var userId = Guid.NewGuid();
        var wallet = new Wallet(userId);
        wallet.Credit(1000, LedgerReason.Welcome);
        wallet.Debit(300, LedgerReason.StakeEscrow);

        await using (var ctx = NewContext())
        {
            await new EfLedgerStore(ctx).AppendAsync(wallet.Entries);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await new EfLedgerStore(ctx).LoadWalletAsync(userId);
            Assert.That(loaded.Balance, Is.EqualTo(700));
            Assert.That(loaded.Entries, Has.Count.EqualTo(2));
        }
    }
}
