using System;
using BbongServer.Domain.Wallet;
using NUnit.Framework;

namespace BbongServer.Tests.Domain;

[TestFixture]
public class WalletTests
{
    private static Wallet NewWallet() => new(Guid.NewGuid());

    [Test]
    public void New_wallet_starts_empty()
    {
        var wallet = NewWallet();

        Assert.That(wallet.Balance, Is.EqualTo(0));
        Assert.That(wallet.Entries, Is.Empty);
    }

    [Test]
    public void Credit_increases_balance_and_records_ledger()
    {
        var wallet = NewWallet();

        wallet.Credit(1000, LedgerReason.AdReward, System.DateTimeOffset.UnixEpoch);

        Assert.That(wallet.Balance, Is.EqualTo(1000));
        Assert.That(wallet.Entries, Has.Count.EqualTo(1));
        Assert.That(wallet.Entries[0].Delta, Is.EqualTo(1000));
        Assert.That(wallet.Entries[0].Reason, Is.EqualTo(LedgerReason.AdReward));
    }

    [Test]
    public void Balance_is_sum_of_all_ledger_deltas()
    {
        var wallet = NewWallet();

        wallet.Credit(1000, LedgerReason.AdReward, System.DateTimeOffset.UnixEpoch);
        wallet.Credit(500, LedgerReason.DailyGrant, System.DateTimeOffset.UnixEpoch);
        wallet.Debit(300, LedgerReason.StakeEscrow, System.DateTimeOffset.UnixEpoch);

        Assert.That(wallet.Balance, Is.EqualTo(1200));
        Assert.That(wallet.Entries, Has.Count.EqualTo(3));
    }

    [Test]
    public void Debit_records_negative_delta()
    {
        var wallet = NewWallet();
        wallet.Credit(1000, LedgerReason.AdReward, System.DateTimeOffset.UnixEpoch);

        wallet.Debit(400, LedgerReason.StakeEscrow, System.DateTimeOffset.UnixEpoch);

        Assert.That(wallet.Entries[1].Delta, Is.EqualTo(-400));
        Assert.That(wallet.Balance, Is.EqualTo(600));
    }

    [Test]
    public void Debit_throws_when_insufficient_balance()
    {
        var wallet = NewWallet();
        wallet.Credit(100, LedgerReason.AdReward, System.DateTimeOffset.UnixEpoch);

        Assert.Throws<InvalidOperationException>(() => wallet.Debit(101, LedgerReason.StakeEscrow, System.DateTimeOffset.UnixEpoch));
        Assert.That(wallet.Balance, Is.EqualTo(100)); // 실패 시 변동 없음
    }

    [Test]
    public void Credit_and_debit_reject_non_positive_amounts()
    {
        var wallet = NewWallet();

        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Credit(0, LedgerReason.AdReward, System.DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Credit(-1, LedgerReason.AdReward, System.DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Debit(0, LedgerReason.StakeEscrow, System.DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Debit(-1, LedgerReason.StakeEscrow, System.DateTimeOffset.UnixEpoch));
    }

    [Test]
    public void Rehydrate_restores_balance_from_existing_entries()
    {
        var userId = Guid.NewGuid();
        var entries = new[]
        {
            new LedgerEntry(userId, 1000, LedgerReason.AdReward, DateTimeOffset.UnixEpoch, 1000),
            new LedgerEntry(userId, -200, LedgerReason.StakeEscrow, DateTimeOffset.UnixEpoch, 800)
        };

        var wallet = Wallet.Rehydrate(userId, entries);

        Assert.That(wallet.Balance, Is.EqualTo(800));
        Assert.That(wallet.Entries, Has.Count.EqualTo(2));
    }
}
