using System;
using System.Threading.Tasks;
using BbongServer.Domain.Accounts;
using BbongServer.Domain.Wallet;

namespace BbongServer.Application;

/// <summary>계정 등록·조회 유스케이스. 게스트 생성 시 계정+초기 지급을 함께 처리.</summary>
public sealed class AccountService
{
    /// <summary>신규 가입 초기 지급액(클라 로컬 지갑과 일치, 후속 서버 정책으로 이관 가능).</summary>
    public const long StartingGrant = 10_000;

    private readonly IAccountStore _accounts;
    private readonly ILedgerStore _ledger;

    public AccountService(IAccountStore accounts, ILedgerStore ledger)
    {
        _accounts = accounts;
        _ledger = ledger;
    }

    public async Task<UserAccount> RegisterGuestAsync()
    {
        var id = Guid.NewGuid();
        var account = new UserAccount(id, isGuest: true, GuestNickname(id), DateTimeOffset.UtcNow);

        var wallet = new Wallet(id);
        wallet.Credit(StartingGrant, LedgerReason.Welcome);

        await _accounts.SaveAsync(account);
        await _ledger.AppendAsync(wallet.Entries);
        return account;
    }

    /// <summary>id 기반 결정적 기본 닉네임("게스트 a1b2c3d4"). 고유 id 앞 8 hex라 충돌 사실상 없음, 12자.</summary>
    private static string GuestNickname(Guid id) => $"게스트 {id:N}"[..12];
}
