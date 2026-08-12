using System;
using System.Collections.Generic;
using System.Linq;
using BbongServer.Domain.Auth;

namespace BbongServer.Domain.Accounts;

/// <summary>
/// 유저 계정(회원). 게스트로 시작해 소셜(Apple/Google/Kakao)로 승격 가능.
/// 게스트 = Provider 없음. 소셜 연동 시 (Provider, SocialSubject)가 채워진다.
/// </summary>
public sealed class UserAccount
{
    private UserAccount(Guid id, string nickname, DateTimeOffset createdAt)
    {
        Id = id;
        Nickname = nickname;
        CreatedAt = createdAt;
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

    private readonly List<SocialLink> _socials = new();

    /// <summary>연결된 소셜 신원들. 한 계정에 provider별로 하나씩 붙일 수 있다.</summary>
    public IReadOnlyList<SocialLink> Socials => _socials;

    /// <summary>첫 연동(호환용 파생값). 조회는 Socials를 쓴다.</summary>
    public SocialProvider? Provider => _socials.Count > 0 ? _socials[0].Provider : null;

    public string? SocialSubject => _socials.Count > 0 ? _socials[0].Subject : null;

    public bool IsGuest => _socials.Count == 0;

    /// <summary>저장소가 읽어온 연동 목록을 복원할 때 쓴다.</summary>
    public void RestoreSocials(IEnumerable<SocialLink> links)
    {
        _socials.Clear();
        _socials.AddRange(links);
    }

    public static UserAccount NewGuest(Guid id, string nickname, DateTimeOffset createdAt) =>
        new(id, nickname, createdAt);

    public static UserAccount NewSocial(Guid id, SocialIdentity identity, string nickname, DateTimeOffset createdAt)
    {
        var account = new UserAccount(id, nickname, createdAt);
        account.LinkSocial(identity);
        return account;
    }

    /// <summary>닉네임 변경(검증은 호출자가 GameConfig.IsValidNickname으로).</summary>
    public void Rename(string nickname) => Nickname = nickname;

    /// <summary>
    /// 소셜 연동(기존 id·잔액 유지). 같은 provider를 두 번 붙이는 것만 막는다 —
    /// 구글로 가입한 뒤 애플을 추가하는 흐름이 필요하기 때문이다.
    /// </summary>
    public void LinkSocial(SocialIdentity identity)
    {
        if (_socials.Any(s => s.Provider == identity.Provider))
        {
            throw new InvalidOperationException($"{identity.Provider} 계정은 이미 연동돼 있습니다.");
        }

        _socials.Add(new SocialLink(identity.Provider, identity.Subject));
    }
}

/// <summary>계정 상태. 정지·탈퇴는 접속을 막지만 기록은 남긴다.</summary>
public enum AccountStatus
{
    Active,
    Suspended,
    PendingDeletion
}

/// <summary>계정에 연결된 소셜 신원 하나.</summary>
public sealed record SocialLink(SocialProvider Provider, string Subject);
