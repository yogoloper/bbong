using System;
using BbongServer.Domain.Wallet;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>
/// 원장 1행의 EF 영속 표현. 도메인 LedgerEntry(순수 record, PK 없음)와 분리해
/// 도메인의 영속 무지(persistence ignorance)를 유지. 저장소가 양방향 변환.
/// </summary>
public sealed class LedgerRow
{
    public long Id { get; set; }              // DB 시퀀스 PK(append 순서)

    public Guid UserId { get; set; }

    public long Delta { get; set; }

    public LedgerReason Reason { get; set; }

    /// <summary>발생 시각. 기록하지 않으면 사후에 복원할 방법이 없다(환불·분쟁 대응의 전제).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>이 변동 직후의 잔액. 감사와 정렬에 쓰고, 원장 합산과 대조해 정합도 검증한다.</summary>
    public long BalanceAfter { get; set; }

    /// <summary>유상/무상 구분(환불 계산). IAP 이전이라 현재는 전부 Free.</summary>
    public LedgerKind Kind { get; set; }

    /// <summary>무엇 때문에 생긴 변동인지 — 예: ("game", gameId). 정산 누락 감지의 근거.</summary>
    public string? RefType { get; set; }

    public Guid? RefId { get; set; }

    public static LedgerRow From(LedgerEntry entry) => new()
    {
        UserId = entry.UserId,
        Delta = entry.Delta,
        Reason = entry.Reason,
        OccurredAt = entry.OccurredAt,
        BalanceAfter = entry.BalanceAfter,
        Kind = entry.Kind,
        RefType = entry.RefType,
        RefId = entry.RefId
    };

    public LedgerEntry ToEntry() =>
        new(UserId, Delta, Reason, OccurredAt, BalanceAfter, Kind, RefType, RefId);
}
