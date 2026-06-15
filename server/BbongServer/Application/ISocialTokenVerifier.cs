using System.Threading.Tasks;
using BbongServer.Domain.Auth;

namespace BbongServer.Application;

/// <summary>
/// 소셜 idToken 검증 추상화. 구현은 provider별(Google/Apple/Kakao) JWKS 검증 또는
/// 개발용 bypass(BBONG_SOCIAL_DEV_BYPASS). 검증 실패 시 예외.
/// </summary>
public interface ISocialTokenVerifier
{
    Task<SocialIdentity> VerifyAsync(SocialProvider provider, string idToken);
}
