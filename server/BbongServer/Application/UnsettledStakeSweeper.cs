using System;
using System.Threading.Tasks;
using BbongServer.Realtime;

namespace BbongServer.Application;

/// <summary>
/// 정산되지 않은 입장료를 돌려준다. 서버가 죽거나 방이 비정상 종료되면 에스크로만 남고
/// 배당·환불이 영영 오지 않는다 — 유저 입장에서는 포인트가 조용히 사라진 것으로 보인다.
/// 원장의 상관관계(RefId)로 "에스크로는 있는데 정산이 없는 게임"을 특정해 회수한다.
/// </summary>
public sealed class UnsettledStakeSweeper
{
    /// <summary>이 시간이 지나도록 정산이 없으면 방치로 본다. 한 세트는 보통 10분 안쪽이다.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    private readonly ILedgerStore _ledger;
    private readonly IStakeBank _bank;
    private readonly IClock _clock;

    public UnsettledStakeSweeper(ILedgerStore ledger, IStakeBank bank, IClock clock)
    {
        _ledger = ledger;
        _bank = bank;
        _clock = clock;
    }

    /// <summary>회수한 건수를 돌려준다. 환불도 원장에 같은 게임으로 기록되므로 두 번 돌지 않는다.</summary>
    public async Task<int> SweepAsync()
    {
        var stranded = await _ledger.FindUnsettledEscrowsAsync(_clock.UtcNow - StaleAfter);

        foreach (var escrow in stranded)
        {
            await _bank.RefundAsync(escrow.UserId, (int)escrow.Amount, escrow.GameId);
        }

        return stranded.Count;
    }
}
