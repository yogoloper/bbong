using System;
using System.Collections.Generic;
using System.Linq;

namespace BbongServer.Domain.Wallet;

/// <summary>
/// 유저 지갑 애그리거트. 잔액 = 원장(LedgerEntry) delta 합이 진실(architecture §5).
/// 환전 출구 없음 — Credit/Debit만 존재, 출금 메서드 미구현(전체이용가 전제, rules §9).
/// </summary>
public sealed class Wallet
{
    private readonly List<LedgerEntry> _entries;

    public Wallet(Guid userId)
    {
        UserId = userId;
        _entries = new List<LedgerEntry>();
    }

    private Wallet(Guid userId, long openingBalance)
    {
        UserId = userId;
        _entries = new List<LedgerEntry>();
        _openingBalance = openingBalance;
    }

    /// <summary>
    /// 과거 기록 없이 알려진 잔액에서 시작하는 지갑. 잔액만 필요한 경로(에스크로·적립)에서
    /// 원장 전체를 읽지 않기 위한 것으로, Entries에는 이번에 새로 쓴 기록만 담긴다.
    /// </summary>
    public static Wallet FromBalance(Guid userId, long balance) => new(userId, balance);

    private readonly long _openingBalance;

    private Wallet(Guid userId, IEnumerable<LedgerEntry> entries)
    {
        UserId = userId;
        _entries = entries.ToList();
    }

    public Guid UserId { get; }

    public long Balance => _openingBalance + _entries.Sum(e => e.Delta);

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    /// <summary>저장된 원장으로 지갑 복원(DB 로드 시).</summary>
    public static Wallet Rehydrate(Guid userId, IEnumerable<LedgerEntry> entries) => new(userId, entries);

    /// <summary>적립(amount &gt; 0). 광고·일일지급·구매 등.</summary>
    public LedgerEntry Credit(long amount, LedgerReason reason, DateTimeOffset at,
        LedgerRef? reference = null, LedgerKind kind = LedgerKind.Free)
    {
        RequirePositive(amount);
        return Append(amount, reason, at, reference, kind);
    }

    /// <summary>차감(amount &gt; 0). 잔액 부족 시 예외, 변동 없음.</summary>
    public LedgerEntry Debit(long amount, LedgerReason reason, DateTimeOffset at,
        LedgerRef? reference = null, LedgerKind kind = LedgerKind.Free)
    {
        RequirePositive(amount);
        if (Balance < amount)
        {
            throw new InvalidOperationException($"잔액 부족: 보유 {Balance}, 요청 {amount}");
        }

        return Append(-amount, reason, at, reference, kind);
    }

    private LedgerEntry Append(long delta, LedgerReason reason, DateTimeOffset at,
        LedgerRef? reference, LedgerKind kind)
    {
        // 기록 시점의 결과 잔액을 함께 남긴다 — 나중에 합산으로 재계산하지 않아도 감사·정렬에 쓸 수 있다.
        var entry = new LedgerEntry(UserId, delta, reason, at, Balance + delta, kind,
            reference?.Type, reference?.Id);
        _entries.Add(entry);
        return entry;
    }

    private static void RequirePositive(long amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "금액은 양수여야 합니다.");
        }
    }
}
