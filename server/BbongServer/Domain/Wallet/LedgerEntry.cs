using System;

namespace BbongServer.Domain.Wallet;

/// <summary>
/// 재화 변동 1건(append-only). Delta는 적립 +, 차감 -. DB ledger 테이블에 영속.
/// 시각·상관관계·재화 종류·결과 잔액을 함께 남긴다 — 사후에는 복원할 수 없는 정보다.
/// </summary>
public sealed record LedgerEntry(
    Guid UserId,
    long Delta,
    LedgerReason Reason,
    DateTimeOffset OccurredAt,
    long BalanceAfter,
    LedgerKind Kind = LedgerKind.Free,
    string? RefType = null,
    Guid? RefId = null);

/// <summary>
/// 재화 종류. 한국 게임법상 유상/무상 구분이 환불 계산에 영향을 준다.
/// IAP 도입(M6) 전까지는 전부 Free다.
/// </summary>
public enum LedgerKind
{
    Free,
    Paid
}

/// <summary>
/// 이 변동이 무엇 때문에 생겼는지. 정산 누락 감지(게임별 에스크로 ↔ 배당 대조)와
/// 담합 분석(같은 게임에 반복 등장하는 조합)이 이 값에 걸린다.
/// </summary>
public readonly record struct LedgerRef(string Type, Guid Id)
{
    public static LedgerRef Game(Guid gameId) => new("game", gameId);

    public static LedgerRef Purchase(Guid purchaseId) => new("purchase", purchaseId);
}
