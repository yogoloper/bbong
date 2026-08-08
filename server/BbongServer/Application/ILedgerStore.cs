using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BbongServer.Domain.Wallet;

namespace BbongServer.Application;

/// <summary>원장 저장소(append-only). 잔액은 원장 합으로 Wallet 복원해 계산.</summary>
public interface ILedgerStore
{
    Task AppendAsync(IEnumerable<LedgerEntry> entries);

    Task<Wallet> LoadWalletAsync(Guid userId);

    /// <summary>
    /// 같은 유저의 지갑 변경(잔액 조회 → append)을 직렬화한다(R7 과인출 레이스 방지).
    /// PG 구현은 advisory lock, 인메모리는 유저별 세마포어.
    /// </summary>
    Task<T> WithWalletLockAsync<T>(Guid userId, Func<Task<T>> action);
}
