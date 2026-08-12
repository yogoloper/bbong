using System;
using BbongServer.Domain.Auth;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>
/// 계정에 연결된 소셜 신원 1행. 계정 테이블의 컬럼 한 쌍으로는 1인 1소셜밖에 못 담아
/// 별도 테이블로 분리했다(구글로 가입 후 애플 추가 같은 흐름에 필요).
/// </summary>
public sealed class AccountSocialRow
{
    public long Id { get; set; }

    public Guid AccountId { get; set; }

    public SocialProvider Provider { get; set; }

    public string Subject { get; set; } = "";
}
