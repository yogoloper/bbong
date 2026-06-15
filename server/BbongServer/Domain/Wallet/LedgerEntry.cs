using System;

namespace BbongServer.Domain.Wallet;

/// <summary>재화 변동 1건(append-only). Delta는 적립 +, 차감 -. DB ledger 테이블에 영속.</summary>
public sealed record LedgerEntry(Guid UserId, long Delta, LedgerReason Reason);
