using System;

namespace BbongServer.Domain.Accounts;

/// <summary>
/// 유저 계정(회원). 게스트로 시작해 소셜(Apple/Google/Kakao)로 승격 가능.
/// 첫 골격은 User+Profile 핵심만. 아바타/레벨/통계는 후속 분리.
/// </summary>
public sealed class UserAccount
{
    public UserAccount(Guid id, bool isGuest, string nickname, DateTimeOffset createdAt)
    {
        Id = id;
        IsGuest = isGuest;
        Nickname = nickname;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public bool IsGuest { get; private set; }

    public string Nickname { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>닉네임 변경(검증은 호출자가 GameConfig.IsValidNickname으로).</summary>
    public void Rename(string nickname) => Nickname = nickname;
}
