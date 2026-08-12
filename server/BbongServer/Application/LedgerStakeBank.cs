using System;
using System.Threading.Tasks;
using BbongServer.Domain.Wallet;
using BbongServer.Realtime;

namespace BbongServer.Application;

/// <summary>원장 기반 판돈 은행 — 유저별 락(R7) 안에서 잔액 조회→기록을 원자화.</summary>
public sealed class LedgerStakeBank : IStakeBank
{
    private readonly ILedgerStore _ledger;
    private readonly IClock _clock;

    public LedgerStakeBank(ILedgerStore ledger, IClock clock)
    {
        _ledger = ledger;
        _clock = clock;
    }

    public Task<bool> TryEscrowAsync(Guid userId, int stake, Guid? gameId = null) =>
        _ledger.WithWalletLockAsync(userId, async () =>
        {
            var wallet = await _ledger.LoadBalanceAsync(userId);
            if (wallet.Balance < stake)
            {
                return false;
            }

            await _ledger.AppendAsync(new[]
            {
                wallet.Debit(stake, LedgerReason.StakeEscrow, _clock.UtcNow, Reference(gameId))
            });
            return true;
        });

    public Task RefundAsync(Guid userId, int stake, Guid? gameId = null) =>
        CreditAsync(userId, stake, LedgerReason.StakeRefund, gameId);

    public Task PayoutAsync(Guid userId, long amount, Guid? gameId = null) =>
        CreditAsync(userId, amount, LedgerReason.StakePayout, gameId);

    private static LedgerRef? Reference(Guid? gameId) =>
        gameId is { } id ? LedgerRef.Game(id) : null;

    private Task CreditAsync(Guid userId, long amount, LedgerReason reason, Guid? gameId) =>
        _ledger.WithWalletLockAsync<object?>(userId, async () =>
        {
            var wallet = await _ledger.LoadBalanceAsync(userId);
            await _ledger.AppendAsync(new[] { wallet.Credit(amount, reason, _clock.UtcNow, Reference(gameId)) });
            return null;
        });
}
