namespace BbongCore.Rules;

/// <summary>족보 종류(rules.md §5). None = 족보 없음.</summary>
public enum MeldType
{
    None,
    Chongtong,        // 총통: 같은 숫자 4장
    Ttoittoi,         // 또이또이: 같은 숫자 2장씩 3쌍
    Straight,         // 스트레이트: 연속 6장
    TenOrUnder,       // 10이하: 6장 합 ≤ 10
    SixtySixOrOver    // 66이상: 6장 합 ≥ 66
}
