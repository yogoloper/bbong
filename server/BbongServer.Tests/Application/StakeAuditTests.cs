using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Wallet;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

/// <summary>
/// 판돈 자금 흐름이 원장에 추적 가능한 형태로 남는지 — 어떤 게임의 에스크로인지, 배당이 갔는지.
/// 실제로 종료되지 않은 게임에 포인트가 묶인 사례가 있었고, 당시엔 상관관계가 없어 찾을 수 없었다.
/// </summary>
[TestFixture]
public class StakeAuditTests
{
    private InMemoryLedgerStore _ledger = null!;
    private LedgerStakeBank _bank = null!;

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
    }

    private FixedClock _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _ledger = new InMemoryLedgerStore();
        _clock = new FixedClock();
        _bank = new LedgerStakeBank(_ledger, _clock);
    }

    private async Task GrantAsync(Guid userId, long amount)
    {
        var wallet = await _ledger.LoadWalletAsync(userId);
        await _ledger.AppendAsync(new[] { wallet.Credit(amount, LedgerReason.Welcome, _clock.UtcNow) });
    }

    [Test]
    public async Task Escrow_records_which_game_took_the_stake()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        await GrantAsync(userId, 10_000);

        await _bank.TryEscrowAsync(userId, 1_000, gameId);

        var escrow = (await _ledger.LoadWalletAsync(userId)).Entries
            .Single(e => e.Reason == LedgerReason.StakeEscrow);
        Assert.That(escrow.RefType, Is.EqualTo("game"));
        Assert.That(escrow.RefId, Is.EqualTo(gameId));
        Assert.That(escrow.OccurredAt, Is.EqualTo(_clock.UtcNow));
    }

    [Test]
    public async Task Payout_records_the_same_game()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        await GrantAsync(userId, 10_000);
        await _bank.TryEscrowAsync(userId, 1_000, gameId);

        await _bank.PayoutAsync(userId, 2_000, gameId);

        var payout = (await _ledger.LoadWalletAsync(userId)).Entries
            .Single(e => e.Reason == LedgerReason.StakePayout);
        Assert.That(payout.RefId, Is.EqualTo(gameId));
    }

    /// <summary>
    /// 에스크로만 있고 배당·환불이 없는 게임 = 정산 누락. 원장만으로 특정할 수 있어야
    /// 미정산 정리 잡이 회수 대상을 찾을 수 있다.
    /// </summary>
    [Test]
    public async Task Unsettled_game_is_identifiable_from_the_ledger()
    {
        var settled = Guid.NewGuid();
        var stranded = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await GrantAsync(userId, 10_000);

        await _bank.TryEscrowAsync(userId, 1_000, settled);
        await _bank.PayoutAsync(userId, 2_000, settled);
        await _bank.TryEscrowAsync(userId, 1_000, stranded); // 이 게임은 끝나지 않았다

        var entries = (await _ledger.LoadWalletAsync(userId)).Entries;
        var unsettled = entries
            .Where(e => e.Reason == LedgerReason.StakeEscrow && e.RefId is not null)
            .Select(e => e.RefId!.Value)
            .Where(game => !entries.Any(e => e.RefId == game
                && e.Reason is LedgerReason.StakePayout or LedgerReason.StakeRefund))
            .ToList();

        Assert.That(unsettled, Is.EqualTo(new[] { stranded }));
    }

    [Test]
    public async Task Balance_after_tracks_every_movement()
    {
        var userId = Guid.NewGuid();
        await GrantAsync(userId, 10_000);
        await _bank.TryEscrowAsync(userId, 1_000, Guid.NewGuid());

        var entries = (await _ledger.LoadWalletAsync(userId)).Entries;

        Assert.That(entries.Select(e => e.BalanceAfter), Is.EqualTo(new long[] { 10_000, 9_000 }));
    }
}
