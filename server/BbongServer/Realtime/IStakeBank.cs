using System;
using System.Threading.Tasks;

namespace BbongServer.Realtime;

/// <summary>
/// 맞춤게임(판돈 방) 자금 흐름: 입장 에스크로 → 퇴장 환불 / 게임 종료 배당(rules.md §9-1~9-3).
/// 방 루프는 이 인터페이스만 알고, 원장/락은 구현(LedgerStakeBank)이 책임진다.
/// gameId를 함께 넘겨 원장에 상관관계를 남긴다 — 이게 없으면 정산이 누락돼도 사후에 찾을 수 없다.
/// </summary>
public interface IStakeBank
{
    /// <summary>입장료 차감 시도. 잔액 부족이면 false(변동 없음).</summary>
    Task<bool> TryEscrowAsync(Guid userId, int stake, Guid? gameId = null);

    /// <summary>대기실 퇴장/입장 거절 환불.</summary>
    Task RefundAsync(Guid userId, int stake, Guid? gameId = null);

    /// <summary>게임 1등 배당.</summary>
    Task PayoutAsync(Guid userId, long amount, Guid? gameId = null);
}
