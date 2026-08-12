using System;
using System.Security.Cryptography;
using System.Text;

namespace BbongServer.Application;

/// <summary>
/// 기기 재개 자격의 생성·검증. 비밀번호와 달리 사람이 고르지 않는 128비트 난수라
/// 사전 공격 대상이 아니고, 매 요청 검증에 쓰이므로 느린 KDF 대신 SHA-256을 쓴다.
/// </summary>
public static class ResumeSecret
{
    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    public static string Hash(string secret) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>비교 시간이 값에 따라 달라지지 않게 고정 시간 비교를 쓴다.</summary>
    public static bool Matches(string secret, string hash)
    {
        var candidate = Encoding.UTF8.GetBytes(Hash(secret));
        var expected = Encoding.UTF8.GetBytes(hash);
        return CryptographicOperations.FixedTimeEquals(candidate, expected);
    }
}

/// <summary>게스트 등록 결과. 평문 시크릿은 이 응답에서만 나가고 서버에 남지 않는다.</summary>
public sealed record GuestRegistration(BbongServer.Domain.Accounts.UserAccount Account, string ResumeSecret);
