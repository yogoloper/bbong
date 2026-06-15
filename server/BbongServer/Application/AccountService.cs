using System;
using System.Threading.Tasks;
using BbongServer.Domain.Accounts;
using BbongServer.Domain.Auth;
using BbongServer.Domain.Wallet;

namespace BbongServer.Application;

/// <summary>계정 등록·로그인 유스케이스. 게스트 생성, 소셜 로그인, 게스트→소셜 승격.</summary>
public sealed class AccountService
{
    /// <summary>신규 가입 초기 지급액(클라 로컬 지갑과 일치, 후속 서버 정책으로 이관 가능).</summary>
    public const long StartingGrant = 10_000;

    private readonly IAccountStore _accounts;
    private readonly ILedgerStore _ledger;
    private readonly ISocialTokenVerifier _social;

    public AccountService(IAccountStore accounts, ILedgerStore ledger, ISocialTokenVerifier social)
    {
        _accounts = accounts;
        _ledger = ledger;
        _social = social;
    }

    public async Task<UserAccount> RegisterGuestAsync()
    {
        var id = Guid.NewGuid();
        var account = UserAccount.NewGuest(id, GuestNickname(id), DateTimeOffset.UtcNow);
        await PersistNewAsync(account);
        return account;
    }

    /// <summary>
    /// 소셜 로그인. idToken 검증 후 (provider, subject)로 기존 계정 있으면 반환,
    /// 없으면 신규 소셜 계정 생성 + 초기 지급.
    /// </summary>
    public async Task<UserAccount> LoginWithSocialAsync(SocialProvider provider, string idToken)
    {
        var identity = await _social.VerifyAsync(provider, idToken);

        var existing = await _accounts.GetBySocialAsync(identity.Provider, identity.Subject);
        if (existing is not null)
        {
            return existing;
        }

        var id = Guid.NewGuid();
        var account = UserAccount.NewSocial(id, identity, GuestNickname(id), DateTimeOffset.UtcNow);
        await PersistNewAsync(account);
        return account;
    }

    /// <summary>
    /// 게스트를 소셜 계정으로 승격(기존 id·잔액 유지). 해당 소셜이 이미 다른 계정에 연동돼 있으면 예외.
    /// </summary>
    public async Task<UserAccount> LinkSocialAsync(Guid guestUserId, SocialProvider provider, string idToken)
    {
        var identity = await _social.VerifyAsync(provider, idToken);

        var account = await _accounts.GetByIdAsync(guestUserId)
            ?? throw new InvalidOperationException("계정을 찾을 수 없습니다.");

        var alreadyLinked = await _accounts.GetBySocialAsync(identity.Provider, identity.Subject);
        if (alreadyLinked is not null)
        {
            throw new InvalidOperationException("해당 소셜 계정은 이미 다른 계정에 연동돼 있습니다.");
        }

        account.LinkSocial(identity); // 게스트 아니면 도메인에서 예외
        await _accounts.SaveAsync(account);
        return account;
    }

    /// <summary>닉네임 변경. 코어 GameConfig 규칙으로 서버 재검증(클라 신뢰 안 함).</summary>
    public async Task<UserAccount> RenameAsync(Guid userId, string nickname)
    {
        if (!BbongCore.Config.GameConfig.IsValidNickname(nickname))
        {
            throw new ArgumentException($"닉네임은 1~{BbongCore.Config.GameConfig.MaxNicknameLength}자여야 합니다.", nameof(nickname));
        }

        var account = await _accounts.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("계정을 찾을 수 없습니다.");

        account.Rename(nickname);
        await _accounts.SaveAsync(account);
        return account;
    }

    private async Task PersistNewAsync(UserAccount account)
    {
        var wallet = new Wallet(account.Id);
        wallet.Credit(StartingGrant, LedgerReason.Welcome);

        await _accounts.SaveAsync(account);
        await _ledger.AppendAsync(wallet.Entries);
    }

    /// <summary>id 기반 결정적 기본 닉네임("게스트 a1b2c3d4"). 고유 id 앞 8 hex라 충돌 사실상 없음, 12자.</summary>
    private static string GuestNickname(Guid id) => $"게스트 {id:N}"[..12];
}
