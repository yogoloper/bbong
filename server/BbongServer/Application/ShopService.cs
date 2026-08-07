using System;
using System.Threading.Tasks;
using BbongServer.Domain.Shop;
using BbongServer.Domain.Wallet;

namespace BbongServer.Application;

/// <summary>
/// 상점 = 광고 보상 + 파산 구제(faucet, R3). 환전 출구 없음, 적립만.
/// 일반: 2000P/30분. 구제: 10000P/하루 3번, 잔액이 파산 임계값(최소 입장료 100) 이하일 때만.
/// </summary>
public sealed class ShopService
{
    public const long StandardReward = 2000;
    public static readonly TimeSpan StandardCooldown = TimeSpan.FromMinutes(30);

    public const long BankruptcyReward = 10_000;
    public const int BankruptcyDailyLimit = 3;
    public static readonly TimeSpan BankruptcyWindow = TimeSpan.FromHours(24);

    private readonly ILedgerStore _ledger;
    private readonly IAdRewardStore _rewards;
    private readonly IClock _clock;

    public ShopService(ILedgerStore ledger, IAdRewardStore rewards, IClock clock)
    {
        _ledger = ledger;
        _rewards = rewards;
        _clock = clock;
    }

    /// <summary>일반 광고 보상. 30분 쿨다운.</summary>
    public async Task ClaimStandardAsync(Guid userId)
    {
        var last = await _rewards.GetLastClaimAsync(userId, AdRewardKind.Standard);
        if (last is not null && _clock.UtcNow - last.Value < StandardCooldown)
        {
            throw new InvalidOperationException("아직 일반 광고 보상 쿨다운입니다.");
        }

        await GrantAsync(userId, AdRewardKind.Standard, StandardReward, LedgerReason.AdReward);
    }

    /// <summary>구제 광고 보상. 잔액이 파산 임계값 이하 + 하루 한도 내일 때만.</summary>
    public async Task ClaimBankruptcyAsync(Guid userId)
    {
        var wallet = await _ledger.LoadWalletAsync(userId);
        if (wallet.Balance > BbongCore.Config.GameConfig.BankruptcyThreshold)
        {
            throw new InvalidOperationException("파산 상태가 아닙니다(잔액이 충분합니다).");
        }

        var recent = await _rewards.CountSinceAsync(userId, AdRewardKind.Bankruptcy, _clock.UtcNow - BankruptcyWindow);
        if (recent >= BankruptcyDailyLimit)
        {
            throw new InvalidOperationException("구제 광고 하루 한도를 초과했습니다.");
        }

        await GrantAsync(userId, AdRewardKind.Bankruptcy, BankruptcyReward, LedgerReason.BankruptcyAid);
    }

    private Task GrantAsync(Guid userId, AdRewardKind kind, long amount, LedgerReason reason) =>
        _ledger.WithWalletLockAsync<object>(userId, async () =>
        {
            var wallet = await _ledger.LoadWalletAsync(userId);
            var entry = wallet.Credit(amount, reason);
            await _ledger.AppendAsync(new[] { entry });
            await _rewards.AppendAsync(new AdRewardClaim(userId, kind, _clock.UtcNow));
            return null;
        });
}
