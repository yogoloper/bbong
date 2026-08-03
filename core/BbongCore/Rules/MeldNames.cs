namespace BbongCore.Rules;

/// <summary>
/// 족보 표시명 단일 출처(rules.md §5). 연습·친구방·향후 모드 어디서든 이 이름만 사용.
/// 통신 DTO에는 enum 문자열을 싣고, 화면에 보일 때 이걸로 변환한다.
/// </summary>
public static class MeldNames
{
    public static string Korean(MeldType type) => type switch
    {
        MeldType.Chongtong => "총통",
        MeldType.Ttoittoi => "또이또이",
        MeldType.Straight => "스트레이트",
        MeldType.TenOrUnder => "10이하",
        MeldType.SixtySixOrOver => "66이상",
        _ => type.ToString()
    };
}
