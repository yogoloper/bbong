using System;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BbongServer.Infrastructure.Auth;

/// <summary>JWT 발급·검증 설정. Key는 환경변수/시크릿 매니저에서 주입(코드 하드코딩 금지).</summary>
public sealed class JwtSettings
{
    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = "bbong";

    public string Audience { get; set; } = "bbong-client";

    public int AccessTokenMinutes { get; set; } = 60;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey(),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
}
