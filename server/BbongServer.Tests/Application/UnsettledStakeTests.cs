using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Wallet;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

/// <summary>
/// 미정산 회수 — 에스크로만 있고 배당도 환불도 없는 게임의 입장료를 돌려준다.
/// 서버가 죽거나 방이 비정상 종료되면 실제로 발생한다(운영에서 4건, 11,000포인트가 이 상태였다).
/// </summary>
[TestFixture]
public class UnsettledStakeTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

    private InMemoryLedgerStore _ledger = null!;
    private LedgerStakeBank _bank = null!;
    private StubClock _clock = null!;
    private UnsettledStakeSweeper _sweeper = null!;

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = At;
    }

    [SetUp]
    public void SetUp()
    {
        _ledger = new InMemoryLedgerStore();
        _clock = new StubClock();
        _bank = new LedgerStakeBank(_ledger, _clock);
        _sweeper = new UnsettledStakeSweeper(_ledger, _bank, _clock);
    }

    private async Task<Guid> FundedUserAsync(long amount = 10_000)
    {
        var userId = Guid.NewGuid();
        var wallet = await _ledger.LoadBalanceAsync(userId);
        await _ledger.AppendAsync(new[] { wallet.Credit(amount, LedgerReason.Welcome, _clock.UtcNow) });
        return userId;
    }

    private async Task<long> BalanceAsync(Guid userId) => (await _ledger.LoadBalanceAsync(userId)).Balance;

    [Test]
    public async Task Escrow_with_no_settlement_is_refunded()
    {
        var userId = await FundedUserAsync();
        var gameId = Guid.NewGuid();
        await _bank.TryEscrowAsync(userId, 1_000, gameId);
        _clock.UtcNow = At.AddHours(2); // 방치 판정 기준을 넘긴다

        var refunded = await _sweeper.SweepAsync();

        Assert.That(refunded, Is.EqualTo(1));
        Assert.That(await BalanceAsync(userId), Is.EqualTo(10_000));
    }

    [Test]
    public async Task A_settled_game_is_left_alone()
    {
        var userId = await FundedUserAsync();
        var gameId = Guid.NewGuid();
        await _bank.TryEscrowAsync(userId, 1_000, gameId);
        await _bank.PayoutAsync(userId, 2_000, gameId);
        _clock.UtcNow = At.AddHours(2);

        var refunded = await _sweeper.SweepAsync();

        Assert.That(refunded, Is.EqualTo(0));
        Assert.That(await BalanceAsync(userId), Is.EqualTo(11_000));
    }

    /// <summary>진행 중인 게임을 건드리면 판이 살아 있는데 입장료가 돌아간다.</summary>
    [Test]
    public async Task A_recent_escrow_is_not_touched()
    {
        var userId = await FundedUserAsync();
        await _bank.TryEscrowAsync(userId, 1_000, Guid.NewGuid());

        var refunded = await _sweeper.SweepAsync(); // 시각 그대로 — 방금 시작한 게임

        Assert.That(refunded, Is.EqualTo(0));
        Assert.That(await BalanceAsync(userId), Is.EqualTo(9_000));
    }

    [Test]
    public async Task Sweeping_twice_refunds_once()
    {
        var userId = await FundedUserAsync();
        await _bank.TryEscrowAsync(userId, 1_000, Guid.NewGuid());
        _clock.UtcNow = At.AddHours(2);

        await _sweeper.SweepAsync();
        var second = await _sweeper.SweepAsync();

        Assert.That(second, Is.EqualTo(0));
        Assert.That(await BalanceAsync(userId), Is.EqualTo(10_000));
    }

    [Test]
    public async Task Each_stranded_player_of_a_game_gets_their_own_refund()
    {
        var first = await FundedUserAsync();
        var second = await FundedUserAsync();
        var gameId = Guid.NewGuid();
        await _bank.TryEscrowAsync(first, 1_000, gameId);
        await _bank.TryEscrowAsync(second, 1_000, gameId);
        _clock.UtcNow = At.AddHours(2);

        var refunded = await _sweeper.SweepAsync();

        Assert.That(refunded, Is.EqualTo(2));
        Assert.That(await BalanceAsync(first), Is.EqualTo(10_000));
        Assert.That(await BalanceAsync(second), Is.EqualTo(10_000));
    }

    /// <summary>상관관계가 없는 옛 기록은 어느 게임 것인지 알 수 없어 손대지 않는다.</summary>
    [Test]
    public async Task Escrow_without_a_game_reference_is_skipped()
    {
        var userId = await FundedUserAsync();
        var wallet = await _ledger.LoadBalanceAsync(userId);
        await _ledger.AppendAsync(new[] { wallet.Debit(1_000, LedgerReason.StakeEscrow, At) });
        _clock.UtcNow = At.AddHours(2);

        var refunded = await _sweeper.SweepAsync();

        Assert.That(refunded, Is.EqualTo(0));
        Assert.That(await BalanceAsync(userId), Is.EqualTo(9_000));
    }
}
