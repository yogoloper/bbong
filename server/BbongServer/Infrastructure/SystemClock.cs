using System;
using BbongServer.Application;

namespace BbongServer.Infrastructure;

/// <summary>실제 시스템 시각(운영). 테스트는 FakeClock으로 대체.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
