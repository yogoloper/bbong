using System;
using BbongServer.Domain.Wallet;
using NUnit.Framework;

namespace BbongServer.Tests.Domain;

/// <summary>
/// 원장 1건이 갖춰야 할 정보 — 언제(OccurredAt), 무엇 때문에(RefType/RefId),
/// 어떤 재화로(Kind), 그 결과 잔액이 얼마가 됐는지(BalanceAfter).
/// 이게 없으면 환불 문의("언제 얼마를 샀고 어디까지 썼나")와 정산 누락 감지가 불가능하다.
/// </summary>
[TestFixture]
public class LedgerEntryTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 13, 1, 2, 3, TimeSpan.Zero);

    [Test]
    public void Credit_records_when_it_happened()
    {
        var wallet = new Wallet(Guid.NewGuid());

        var entry = wallet.Credit(1000, LedgerReason.Welcome, At);

        Assert.That(entry.OccurredAt, Is.EqualTo(At));
    }

    [Test]
    public void Credit_records_resulting_balance()
    {
        var wallet = new Wallet(Guid.NewGuid());

        wallet.Credit(1000, LedgerReason.Welcome, At);
        var second = wallet.Credit(500, LedgerReason.AdReward, At);

        Assert.That(second.BalanceAfter, Is.EqualTo(1500));
    }

    [Test]
    public void Debit_records_resulting_balance()
    {
        var wallet = new Wallet(Guid.NewGuid());
        wallet.Credit(1000, LedgerReason.Welcome, At);

        var entry = wallet.Debit(300, LedgerReason.StakeEscrow, At);

        Assert.That(entry.BalanceAfter, Is.EqualTo(700));
        Assert.That(entry.Delta, Is.EqualTo(-300));
    }

    [Test]
    public void Entry_carries_what_caused_it()
    {
        var wallet = new Wallet(Guid.NewGuid());
        wallet.Credit(1000, LedgerReason.Welcome, At);
        var gameId = Guid.NewGuid();

        var entry = wallet.Debit(1000, LedgerReason.StakeEscrow, At, LedgerRef.Game(gameId));

        Assert.That(entry.RefType, Is.EqualTo("game"));
        Assert.That(entry.RefId, Is.EqualTo(gameId));
    }

    [Test]
    public void Entry_without_a_cause_leaves_the_reference_empty()
    {
        var wallet = new Wallet(Guid.NewGuid());

        var entry = wallet.Credit(1000, LedgerReason.Welcome, At);

        Assert.That(entry.RefType, Is.Null);
        Assert.That(entry.RefId, Is.Null);
    }

    /// <summary>유상/무상 구분은 환불 계산에 쓰인다. IAP 이전이라 지금은 전부 무상이다.</summary>
    [Test]
    public void Entries_are_free_currency_by_default()
    {
        var wallet = new Wallet(Guid.NewGuid());

        var entry = wallet.Credit(1000, LedgerReason.Welcome, At);

        Assert.That(entry.Kind, Is.EqualTo(LedgerKind.Free));
    }

    [Test]
    public void Purchases_are_paid_currency()
    {
        var wallet = new Wallet(Guid.NewGuid());

        var entry = wallet.Credit(1000, LedgerReason.Purchase, At, kind: LedgerKind.Paid);

        Assert.That(entry.Kind, Is.EqualTo(LedgerKind.Paid));
    }

    [Test]
    public void Rehydrated_wallet_keeps_the_stored_balance()
    {
        var userId = Guid.NewGuid();
        var wallet = new Wallet(userId);
        wallet.Credit(1000, LedgerReason.Welcome, At);
        wallet.Debit(400, LedgerReason.StakeEscrow, At);

        var restored = Wallet.Rehydrate(userId, wallet.Entries);

        Assert.That(restored.Balance, Is.EqualTo(600));
    }
}
