using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Domain.Wallet;
using BbongServer.Infrastructure.InMemory;
using BbongServer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace BbongServer.Tests.Infrastructure;

/// <summary>
/// 잔액 조회는 원장을 전부 읽지 않아야 한다. 지금까지는 유저의 모든 기록을 메모리로 끌어와
/// 합산했고, 그 대부분이 지갑 락 안에서 돌았다. 기록마다 남는 BalanceAfter로 한 줄만 읽는다.
/// </summary>
[TestFixture]
public class LedgerBalanceTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=bbong;Username=bbong;Password=bbong_dev";

    private static BbongDbContext NewDb() =>
        new(new DbContextOptionsBuilder<BbongDbContext>().UseNpgsql(ConnectionString).Options);

    [Test]
    public void Wallet_from_a_known_balance_starts_there()
    {
        var wallet = Wallet.FromBalance(Guid.NewGuid(), 7_000);

        Assert.That(wallet.Balance, Is.EqualTo(7_000));
        Assert.That(wallet.Entries, Is.Empty); // 과거 기록은 싣지 않는다
    }

    [Test]
    public void Appending_to_a_known_balance_continues_the_running_total()
    {
        var wallet = Wallet.FromBalance(Guid.NewGuid(), 7_000);

        var entry = wallet.Debit(1_000, LedgerReason.StakeEscrow, At);

        Assert.That(entry.BalanceAfter, Is.EqualTo(6_000));
        Assert.That(wallet.Balance, Is.EqualTo(6_000));
        Assert.That(wallet.Entries, Has.Count.EqualTo(1)); // 새로 쓴 것만
    }

    [Test]
    public void Debit_beyond_a_known_balance_is_refused()
    {
        var wallet = Wallet.FromBalance(Guid.NewGuid(), 500);

        Assert.That(() => wallet.Debit(1_000, LedgerReason.StakeEscrow, At),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task In_memory_store_reports_the_same_balance_both_ways()
    {
        var store = new InMemoryLedgerStore();
        var userId = Guid.NewGuid();
        var wallet = new Wallet(userId);
        wallet.Credit(10_000, LedgerReason.Welcome, At);
        wallet.Debit(2_500, LedgerReason.StakeEscrow, At);
        await store.AppendAsync(wallet.Entries);

        var byBalance = await store.LoadBalanceAsync(userId);
        var byHistory = await store.LoadWalletAsync(userId);

        Assert.That(byBalance.Balance, Is.EqualTo(byHistory.Balance));
        Assert.That(byBalance.Balance, Is.EqualTo(7_500));
    }

    [Test]
    public async Task Postgres_store_reads_the_balance_from_the_last_entry()
    {
        var userId = Guid.NewGuid();
        await using var db = NewDb();
        var store = new EfLedgerStore(db);

        var wallet = new Wallet(userId);
        wallet.Credit(10_000, LedgerReason.Welcome, At);
        wallet.Debit(3_000, LedgerReason.StakeEscrow, At);
        await store.AppendAsync(wallet.Entries);

        var loaded = await store.LoadBalanceAsync(userId);

        Assert.That(loaded.Balance, Is.EqualTo(7_000));
    }

    [Test]
    public async Task A_user_with_no_entries_has_no_balance()
    {
        var store = new InMemoryLedgerStore();

        var wallet = await store.LoadBalanceAsync(Guid.NewGuid());

        Assert.That(wallet.Balance, Is.EqualTo(0));
    }

    /// <summary>회수 조회는 SQL로 번역돼야 한다(메모리에서 거르면 원장 전체를 읽는다).</summary>
    [Test]
    public async Task Postgres_store_finds_only_escrow_without_settlement()
    {
        var stranded = Guid.NewGuid();
        var settled = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var old = DateTimeOffset.UtcNow.AddHours(-3);

        await using var db = NewDb();
        var store = new EfLedgerStore(db);
        var wallet = new Wallet(userId);
        wallet.Credit(10_000, LedgerReason.Welcome, old);
        wallet.Debit(1_000, LedgerReason.StakeEscrow, old, LedgerRef.Game(stranded));
        wallet.Debit(1_000, LedgerReason.StakeEscrow, old, LedgerRef.Game(settled));
        wallet.Credit(2_000, LedgerReason.StakePayout, old, LedgerRef.Game(settled));
        await store.AppendAsync(wallet.Entries);

        var found = await store.FindUnsettledEscrowsAsync(DateTimeOffset.UtcNow.AddHours(-1));
        var mine = found.Where(e => e.UserId == userId).ToList();

        Assert.That(mine, Has.Count.EqualTo(1));
        Assert.That(mine[0].GameId, Is.EqualTo(stranded));
        Assert.That(mine[0].Amount, Is.EqualTo(1_000));
    }
}
