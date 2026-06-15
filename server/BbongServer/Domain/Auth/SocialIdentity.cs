namespace BbongServer.Domain.Auth;

/// <summary>
/// 소셜 토큰 검증 결과 = provider + 그 provider 내 고유 사용자 식별자(Subject).
/// (Provider, Subject) 쌍이 우리 계정과 1:1 매핑된다.
/// </summary>
public sealed record SocialIdentity(SocialProvider Provider, string Subject);
