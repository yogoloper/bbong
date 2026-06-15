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

    public static LedgerRow From(LedgerEntry entry) => new()
    {
        UserId = entry.UserId,
        Delta = entry.Delta,
        Reason = entry.Reason
    };

    public LedgerEntry ToEntry() => new(UserId, Delta, Reason);
}
