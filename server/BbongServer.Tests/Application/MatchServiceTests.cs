using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Matches;
using BbongServer.Domain.Wallet;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

[TestFixture]
public class MatchServiceTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
    }

    private MatchService _service = null!;
    private InMemoryLedgerStore _ledger = null!;
    private InMemoryMatchStore _matches = null!;
    private FakeClock _clock = null!;
    private readonly Guid _user = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _ledger = new InMemoryLedgerStore();
        _matches = new InMemoryMatchStore();
        _clock = new FakeClock();
        _service = new MatchService(_ledger, _matches, _clock);
    }

    private async Task<long> BalanceAsync() => (await _ledger.LoadWalletAsync(_user)).Balance;

    private async Task SeedBalanceAsync(long amount)
    {
        var wallet = new Wallet(_user);
        wallet.Credit(amount, LedgerReason.Welcome);
        await _ledger.AppendAsync(wallet.Entries);
    }

    // ── 매치 시작(에스크로) ──

    [Test]
    public async Task Start_escrows_stake_and_creates_match()
    {
        await SeedBalanceAsync(10_000);

        var (matchId, balance) = await _service.StartAsync(_user, stake: 1000, playerCount: 4);

        Assert.That(balance, Is.EqualTo(9_000));
        Assert.That(await BalanceAsync(), Is.EqualTo(9_000));

        var match = await _matches.GetByIdAsync(matchId);
        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Status, Is.EqualTo(MatchStatus.InProgress));
        Assert.That(match.Stake, Is.EqualTo(1000));
        Assert.That(match.PlayerCount, Is.EqualTo(4));
    }

    [Test]
    public async Task Start_records_escrow_in_ledger()
    {
        await SeedBalanceAsync(10_000);

        await _service.StartAsync(_user, stake: 500, playerCount: 2);

        var wallet = await _ledger.LoadWalletAsync(_user);
        Assert.That(wallet.Entries[^1].Reason, Is.EqualTo(LedgerReason.StakeEscrow));
        Assert.That(wallet.Entries[^1].Delta, Is.EqualTo(-500));
    }

    [Test]
    public async Task Start_rejects_insufficient_balance()
    {
        await SeedBalanceAsync(500);

        Assert.ThrowsAsync<InvalidOperationException>(() => _service.StartAsync(_user, stake: 1000, playerCount: 4));
        Assert.That(await BalanceAsync(), Is.EqualTo(500)); // 변동 없음
    }

    [Test]
    public async Task Start_rejects_invalid_stake()
    {
        await SeedBalanceAsync(10_000);

        Assert.ThrowsAsync<ArgumentException>(() => _service.StartAsync(_user, stake: 123, playerCount: 4));
    }

    [Test]
    public async Task Start_rejects_invalid_player_count()
    {
        await SeedBalanceAsync(10_000);

        Assert.ThrowsAsync<ArgumentException>(() => _service.StartAsync(_user, stake: 1000, playerCount: 1));
        Assert.ThrowsAsync<ArgumentException>(() => _service.StartAsync(_user, stake: 1000, playerCount: 7));
    }

    // ── 정산 ──

    private async Task<Guid> StartedMatchAsync(int stake = 1000, int playerCount = 4)
    {
        var (matchId, _) = await _service.StartAsync(_user, stake, playerCount);
        return matchId;
    }

    [Test]
    public async Task Settle_win_credits_full_pot()
    {
        await SeedBalanceAsync(10_000);
        var matchId = await StartedMatchAsync(stake: 1000, playerCount: 4);

        var (payout, balance) = await _service.SettleAsync(_user, matchId, won: true, winnersCount: 1);

        Assert.That(payout, Is.EqualTo(4_000));
        Assert.That(balance, Is.EqualTo(9_000 + 4_000));

        var wallet = await _ledger.LoadWalletAsync(_user);
        Assert.That(wallet.Entries[^1].Reason, Is.EqualTo(LedgerReason.StakePayout));
    }

    [Test]
    public async Task Settle_tied_winners_truncate_share()
    {
        await SeedBalanceAsync(10_000);
        var matchId = await StartedMatchAsync(stake: 100, playerCount: 5);

        var (payout, _) = await _service.SettleAsync(_user, matchId, won: true, winnersCount: 3);

        Assert.That(payout, Is.EqualTo(166)); // 500/3 절사 (rules.md §9-3)
    }

    [Test]
    public async Task Settle_loss_credits_nothing_but_settles()
    {
        await SeedBalanceAsync(10_000);
        var matchId = await StartedMatchAsync();

        var (payout, balance) = await _service.SettleAsync(_user, matchId, won: false, winnersCount: 1);

        Assert.That(payout, Is.EqualTo(0));
        Assert.That(balance, Is.EqualTo(9_000));
        Assert.That((await _matches.GetByIdAsync(matchId))!.Status, Is.EqualTo(MatchStatus.Settled));
    }

    [Test]
    public async Task Settle_twice_rejected()
    {
        await SeedBalanceAsync(10_000);
        var matchId = await StartedMatchAsync();
        await _service.SettleAsync(_user, matchId, won: true, winnersCount: 1);
        var before = await BalanceAsync();

        Assert.ThrowsAsync<InvalidOperationException>(() => _service.SettleAsync(_user, matchId, won: true, winnersCount: 1));
        Assert.That(await BalanceAsync(), Is.EqualTo(before)); // 이중 지급 없음
    }

    [Test]
    public async Task Settle_foreign_match_not_found()
    {
        await SeedBalanceAsync(10_000);
        var matchId = await StartedMatchAsync();

        var stranger = Guid.NewGuid();
        Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SettleAsync(stranger, matchId, won: true, winnersCount: 1));
    }

    [Test]
    public void Settle_unknown_match_not_found()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SettleAsync(_user, Guid.NewGuid(), won: true, winnersCount: 1));
    }

    // ── R7: 지갑 동시성 — 동시 에스크로가 과인출을 만들면 안 된다 ──

    [Test]
    public async Task Concurrent_escrow_debits_cannot_overdraw()
    {
        await SeedBalanceAsync(10_000); // 판돈 10000 매치를 딱 1번 시작할 수 있는 잔액

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await _service.StartAsync(_user, 10_000, 4);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false; // 잔액 부족 — 정상 거절
                }
            }))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.That(results.Count(ok => ok), Is.EqualTo(1)); // 성공은 정확히 1건
        Assert.That(await BalanceAsync(), Is.EqualTo(0));    // 과인출(음수) 금지
    }
}
