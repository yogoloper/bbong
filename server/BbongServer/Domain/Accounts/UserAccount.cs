using System;
using BbongServer.Domain.Auth;

namespace BbongServer.Domain.Accounts;

/// <summary>
/// 유저 계정(회원). 게스트로 시작해 소셜(Apple/Google/Kakao)로 승격 가능.
/// 게스트 = Provider 없음. 소셜 연동 시 (Provider, SocialSubject)가 채워진다.
/// </summary>
public sealed class UserAccount
{
    private UserAccount(Guid id, string nickname, DateTimeOffset createdAt,
        SocialProvider? provider, string? socialSubject)
    {
        Id = id;
        Nickname = nickname;
        CreatedAt = createdAt;
        Provider = provider;
        SocialSubject = socialSubject;
    }

    /// <summary>
    /// 기기 재개용 장기 자격의 해시. 평문은 발급 시 한 번만 클라이언트에 내려주고 서버는 보관하지 않는다.
    /// 액세스 토큰(60분)과 달리 만료가 없어, 앱을 껐다 켜도 같은 계정으로 돌아올 수 있다.
    /// </summary>
    public string? ResumeSecretHash { get; private set; }

    public void SetResumeSecretHash(string hash) => ResumeSecretHash = hash;

    /// <summary>계정 상태. 정지·탈퇴 계정은 로그인·재개를 막는다.</summary>
    public AccountStatus Status { get; private set; } = AccountStatus.Active;

    /// <summary>마지막 접속 시각. CS 대응("언제 마지막으로 들어왔나")과 휴면 판단의 근거.</summary>
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>탈퇴 요청 시각. 유예 기간 계산과 실제 삭제 처리의 기준이 된다.</summary>
    public DateTimeOffset? DeletionRequestedAt { get; private set; }

    /// <summary>로그인·재개 성공 시 갱신.</summary>
    public void MarkLogin(DateTimeOffset at) => LastLoginAt = at;

    /// <summary>운영 제재. 사유·이력은 별도 테이블로 남길 여지를 둔다.</summary>
    public void Suspend() => Status = AccountStatus.Suspended;

    /// <summary>
    /// 탈퇴 요청(스토어 정책상 앱 내 삭제 경로 필수). 즉시 지우지 않고 표시만 해
    /// 유예 기간 동안 되돌릴 수 있게 하고, 정산·분쟁 기록은 보존한다.
    /// </summary>
    public void RequestDeletion(DateTimeOffset at)
    {
        Status = AccountStatus.PendingDeletion;
        DeletionRequestedAt = at;
    }

    public void CancelDeletion()
    {
        Status = AccountStatus.Active;
        DeletionRequestedAt = null;
    }

    public Guid Id { get; }

    public string Nickname { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public SocialProvider? Provider { get; private set; }

    public string? SocialSubject { get; private set; }

    public bool IsGuest => Provider is null;

    public static UserAccount NewGuest(Guid id, string nickname, DateTimeOffset createdAt) =>
        new(id, nickname, createdAt, provider: null, socialSubject: null);

    public static UserAccount NewSocial(Guid id, SocialIdentity identity, string nickname, DateTimeOffset createdAt) =>
        new(id, nickname, createdAt, identity.Provider, identity.Subject);

    /// <summary>닉네임 변경(검증은 호출자가 GameConfig.IsValidNickname으로).</summary>
    public void Rename(string nickname) => Nickname = nickname;

    /// <summary>게스트를 소셜 계정으로 승격(기존 id·잔액 유지). 이미 소셜이면 예외.</summary>
    public void LinkSocial(SocialIdentity identity)
    {
        if (!IsGuest)
        {
            throw new InvalidOperationException("이미 소셜 계정에 연동된 계정입니다.");
        }

        Provider = identity.Provider;
        SocialSubject = identity.Subject;
    }
}

/// <summary>계정 상태. 정지·탈퇴는 접속을 막지만 기록은 남긴다.</summary>
public enum AccountStatus
{
    Active,
    Suspended,
    PendingDeletion
}
