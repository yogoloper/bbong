namespace BbongCore.Cards;

/// <summary>
/// 난수 추상화. 셔플을 주입식으로 만들어 테스트는 재현 가능하게,
/// 서버는 보안 시드를 쓰도록 분리합니다(rules.md §2).
/// </summary>
public interface IRandom
{
    /// <summary>0 이상 maxExclusive 미만의 정수를 반환합니다.</summary>
    int Next(int maxExclusive);
}
