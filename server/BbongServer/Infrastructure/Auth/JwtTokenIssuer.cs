using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BbongServer.Application;
using Microsoft.IdentityModel.Tokens;

namespace BbongServer.Infrastructure.Auth;

/// <summary>HS256 JWT 발급. sub 클레임에 userId.</summary>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    private readonly JwtSettings _settings;

    public JwtTokenIssuer(JwtSettings settings) => _settings = settings;

    public string IssueAccessToken(Guid userId)
    {
        var credentials = new SigningCredentials(_settings.SigningKey(), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) },
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
