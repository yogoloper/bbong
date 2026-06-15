using System;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Auth;

namespace BbongServer.Infrastructure.Social;

/// <summary>
/// 개발 전용 소셜 검증 우회(BBONG_SOCIAL_DEV_BYPASS=true). 실제 provider 검증 없이
/// idToken을 그대로 subject로 사용 → 앱 등록 전 소셜 로그인 흐름 통합 테스트용.
/// 운영에서는 절대 사용 금지(실제 provider 검증기로 교체).
/// </summary>
public sealed class DevBypassSocialVerifier : ISocialTokenVerifier
{
    public Task<SocialIdentity> VerifyAsync(SocialProvider provider, string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            throw new InvalidOperationException("idToken이 비어 있습니다.");
        }

        return Task.FromResult(new SocialIdentity(provider, idToken));
    }
}
