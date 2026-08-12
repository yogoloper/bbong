using System;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Wallet;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

[TestFixture]
public class ShopServiceTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    }

    private ShopService _shop = null!;
    private InMemoryLedgerStore _ledger = null!;
    private InMemoryAdRewardStore _rewards = null!;
    private FakeClock _clock = null!;
    private readonly Guid _user = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _ledger = new InMemoryLedgerStore();
        _rewards = new InMemoryAdRewardStore();
        _clock = new FakeClock();
        _shop = new ShopService(_ledger, _rewards, _clock);
    }

    private async Task<long> BalanceAsync() => (await _ledger.LoadWalletAsync(_user)).Balance;

    private async Task SeedBalanceAsync(long amount)
    {
        var wallet = new Wallet(_user);
        wallet.Credit(amount, LedgerReason.Welcome, System.DateTimeOffset.UnixEpoch);
        await _ledger.AppendAsync(wallet.Entries);
    }

    // ── 일반 보상 ──

    [Test]
    public async Task Standard_grants_2000()
    {
        await _shop.ClaimStandardAsync(_user);

        Assert.That(await BalanceAsync(), Is.EqualTo(ShopService.StandardReward));
        Assert.That(ShopService.StandardReward, Is.EqualTo(2000));
    }

    [Test]
    public async Task Standard_blocked_within_cooldown()
    {
        await _shop.ClaimStandardAsync(_user);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(29); // 30분 미만

        Assert.ThrowsAsync<InvalidOperationException>(() => _shop.ClaimStandardAsync(_user));
        Assert.That(await BalanceAsync(), Is.EqualTo(ShopService.StandardReward)); // 추가 지급 없음
    }

    [Test]
    public async Task Standard_allowed_after_cooldown()
    {
        await _shop.ClaimStandardAsync(_user);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(30);

        await _shop.ClaimStandardAsync(_user);

        Assert.That(await BalanceAsync(), Is.EqualTo(ShopService.StandardReward * 2));
    }

    // ── 구제 보상 ──

    [Test]
    public async Task Bankruptcy_rejected_when_balance_above_threshold()
    {
        await SeedBalanceAsync(101); // 100 초과 → 파산 아님

        Assert.ThrowsAsync<InvalidOperationException>(() => _shop.ClaimBankruptcyAsync(_user));
    }

    [Test]
    public async Task Bankruptcy_grants_10000_when_broke()
    {
        await SeedBalanceAsync(100); // 임계값 이하 = 파산

        await _shop.ClaimBankruptcyAsync(_user);

        Assert.That(await BalanceAsync(), Is.EqualTo(100 + ShopService.BankruptcyReward));
        Assert.That(ShopService.BankruptcyReward, Is.EqualTo(10000));
    }

    [Test]
    public async Task Bankruptcy_limited_to_3_per_day()
    {
        for (var i = 0; i < 3; i++)
        {
            // 매번 다시 파산 상태로 만들어 임계값 통과(잔액을 0으로 되돌림)
            await ResetToBrokeAsync();
            await _shop.ClaimBankruptcyAsync(_user);
            _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        }

        await ResetToBrokeAsync();
        Assert.ThrowsAsync<InvalidOperationException>(() => _shop.ClaimBankruptcyAsync(_user));
    }

    private async Task ResetToBrokeAsync()
    {
        var wallet = await _ledger.LoadWalletAsync(_user);
        if (wallet.Balance > 0)
        {
            var w = Wallet.Rehydrate(_user, wallet.Entries);
            w.Debit(wallet.Balance, LedgerReason.StakeEscrow, System.DateTimeOffset.UnixEpoch);
            await _ledger.AppendAsync(new[] { w.Entries[^1] });
        }
    }
}
