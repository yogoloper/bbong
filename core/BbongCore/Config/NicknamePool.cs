using System;

namespace BbongCore.Config;

/// <summary>
/// 게스트/봇 닉네임용 "형용사 동물" 조합 풀(클라 연습 모드와 동일 스타일).
/// 최장 조합 "능청스런 사막여우"(9자)에 " 봇"을 붙여도 MaxNicknameLength(12) 이내.
/// </summary>
public static class NicknamePool
{
    public static readonly string[] Adjectives =
    {
        "수줍은", "용감한", "날쌘", "졸린", "명랑한",
        "시크한", "엉뚱한", "우아한", "씩씩한", "능청스런",
        "재빠른", "느긋한", "새침한", "다정한", "사나운",
        "영리한", "배고픈", "신나는", "든든한", "근엄한",
        "깜찍한", "당당한", "야무진", "진지한", "활발한",
        "조용한", "수상한", "똑똑한", "짓궂은", "화끈한"
    };

    public static readonly string[] Animals =
    {
        "너구리", "두더지", "고슴도치", "다람쥐", "부엉이",
        "수달", "알파카", "펭귄", "사막여우", "호랑나비",
        "해달", "오소리", "담비", "족제비", "카피바라",
        "미어캣", "왈라비", "코알라", "판다", "레서판다",
        "물범", "돌고래", "두루미", "청설모", "까치",
        "제비", "살쾡이", "스라소니", "순록", "산양"
    };

    public static string Pick(Random rng) =>
        $"{Adjectives[rng.Next(Adjectives.Length)]} {Animals[rng.Next(Animals.Length)]}";
}
