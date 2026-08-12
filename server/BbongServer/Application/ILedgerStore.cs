using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BbongServer.Domain.Wallet;

namespace BbongServer.Application;

/// <summary>원장 저장소(append-only). 잔액은 원장 합으로 Wallet 복원해 계산.</summary>
public interface ILedgerStore
{
    Task AppendAsync(IEnumerable<LedgerEntry> entries);

    /// <summary>전체 기록 로드(감사·조회용). 잔액만 필요하면 LoadBalanceAsync를 쓴다.</summary>
    Task<Wallet> LoadWalletAsync(Guid userId);

    /// <summary>
    /// 잔액만 조회. 기록마다 남는 BalanceAfter의 마지막 값을 읽으므로 원장 크기와 무관하다.
    /// 지갑 락 안에서 도는 경로(에스크로·정산·적립)는 이쪽을 쓴다.
    /// </summary>
    Task<Wallet> LoadBalanceAsync(Guid userId);

    /// <summary>
    /// 같은 유저의 지갑 변경(잔액 조회 → append)을 직렬화한다(R7 과인출 레이스 방지).
    /// PG 구현은 advisory lock, 인메모리는 유저별 세마포어.
    /// </summary>
    Task<T> WithWalletLockAsync<T>(Guid userId, Func<Task<T>> action);
}
