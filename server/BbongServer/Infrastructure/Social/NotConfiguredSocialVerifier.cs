using System;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Auth;

namespace BbongServer.Infrastructure.Social;

/// <summary>
/// 실제 provider 검증기 미구현 상태의 자리표시자. bypass가 꺼진 환경에서 소셜 로그인 시도 시
/// 명확히 실패시킨다(나머지 기능은 정상). Google/Apple/Kakao 구현으로 교체 예정.
/// </summary>
public sealed class NotConfiguredSocialVerifier : ISocialTokenVerifier
{
    public Task<SocialIdentity> VerifyAsync(SocialProvider provider, string idToken) =>
        throw new InvalidOperationException(
            $"{provider} 소셜 검증기가 구성되지 않았습니다(BBONG_SOCIAL_DEV_BYPASS 또는 실제 검증기 필요).");
}
