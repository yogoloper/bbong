using System;

namespace BbongCore.Cards;

/// <summary>고정 시드 난수. 같은 시드 → 같은 순서(테스트 재현용). 서버는 보안 RNG 별도 구현.</summary>
public sealed class SeededRandom : IRandom
{
    private readonly Random _random;

    public SeededRandom(int seed) => _random = new Random(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);
}
