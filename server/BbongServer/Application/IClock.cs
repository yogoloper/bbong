using System;

namespace BbongServer.Application;

/// <summary>현재 시각 추상화(쿨다운·일일 제한 계산용). 테스트는 FakeClock으로 시간 제어.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
