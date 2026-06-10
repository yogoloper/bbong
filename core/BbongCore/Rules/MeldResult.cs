namespace BbongCore.Rules;

/// <summary>족보 판정 결과. Score는 빚 기준(음수 = 탕감, rules.md §5).</summary>
public readonly record struct MeldResult(MeldType Type, int Score)
{
    public static readonly MeldResult None = new(MeldType.None, 0);
}
