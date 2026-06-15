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
}
