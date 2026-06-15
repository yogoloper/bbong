using System;

namespace BbongServer.Application;

/// <summary>액세스 토큰 발급 추상화. 구현은 Infrastructure.Auth.JwtTokenIssuer.</summary>
public interface ITokenIssuer
{
    string IssueAccessToken(Guid userId);
}
