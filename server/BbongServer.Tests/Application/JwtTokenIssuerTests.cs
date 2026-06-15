using System;
using System.IdentityModel.Tokens.Jwt;
using BbongServer.Application;
using BbongServer.Infrastructure.Auth;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

[TestFixture]
public class JwtTokenIssuerTests
{
    private static JwtTokenIssuer NewIssuer() => new(new JwtSettings
    {
        Key = "test-signing-key-at-least-32-bytes-long!!",
        Issuer = "bbong-test",
        Audience = "bbong-client",
        AccessTokenMinutes = 60
    });

    [Test]
    public void Issued_access_token_carries_user_id_as_subject()
    {
        var issuer = NewIssuer();
        var userId = Guid.NewGuid();

        var token = issuer.IssueAccessToken(userId);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.That(jwt.Subject, Is.EqualTo(userId.ToString()));
        Assert.That(jwt.Issuer, Is.EqualTo("bbong-test"));
    }

    [Test]
    public void Issued_token_validates_against_same_settings()
    {
        var settings = new JwtSettings
        {
            Key = "test-signing-key-at-least-32-bytes-long!!",
            Issuer = "bbong-test",
            Audience = "bbong-client",
            AccessTokenMinutes = 60
        };
        var issuer = new JwtTokenIssuer(settings);
        var userId = Guid.NewGuid();

        var token = issuer.IssueAccessToken(userId);

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, settings.ValidationParameters(), out _);
        Assert.That(principal.Identity!.IsAuthenticated, Is.True);
    }

    [Test]
    public void Token_signed_with_other_key_fails_validation()
    {
        var issuer = NewIssuer();
        var token = issuer.IssueAccessToken(Guid.NewGuid());

        var otherSettings = new JwtSettings
        {
            Key = "a-completely-different-signing-key-32bytes",
            Issuer = "bbong-test",
            Audience = "bbong-client",
            AccessTokenMinutes = 60
        };

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, otherSettings.ValidationParameters(), out _));
    }
}
