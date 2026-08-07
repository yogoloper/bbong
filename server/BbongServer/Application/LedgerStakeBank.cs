using System;
using System.Threading.Tasks;
using BbongServer.Domain.Wallet;
using BbongServer.Realtime;

namespace BbongServer.Application;

/// <summary>원장 기반 판돈 은행 — 유저별 락(R7) 안에서 잔액 조회→기록을 원자화.</summary>
public sealed class LedgerStakeBank : IStakeBank
{
    private readonly ILedgerStore _ledger;

    public LedgerStakeBank(ILedgerStore ledger) => _ledger = ledger;

    public Task<bool> TryEscrowAsync(Guid userId, int stake) =>
        _ledger.WithWalletLockAsync(userId, async () =>
        {
            var wallet = await _ledger.LoadWalletAsync(userId);
            if (wallet.Balance < stake)
            {
                return false;
            }

            await _ledger.AppendAsync(new[] { wallet.Debit(stake, LedgerReason.StakeEscrow) });
            return true;
        });

    public Task RefundAsync(Guid userId, int stake) => CreditAsync(userId, stake, LedgerReason.StakeRefund);

    public Task PayoutAsync(Guid userId, long amount) => CreditAsync(userId, amount, LedgerReason.StakePayout);

    private Task CreditAsync(Guid userId, long amount, LedgerReason reason) =>
        _ledger.WithWalletLockAsync<object?>(userId, async () =>
        {
            var wallet = await _ledger.LoadWalletAsync(userId);
            await _ledger.AppendAsync(new[] { wallet.Credit(amount, reason) });
            return null;
        });
}
