namespace BbongServer.Realtime;

/// <summary>실시간(친구방) 서버 설정. 코어 GameConfig와 별개 — 연출/운영 파라미터.</summary>
public static class RealtimeConfig
{
    /// <summary>뽕 선언 창(초). 규칙은 2초지만 사람 대전 체감상 연습 모드와 동일한 5초.</summary>
    public const int PongWindowSeconds = 5;

    /// <summary>판 종료 전광판 후 다음 판 자동 시작까지(ms).</summary>
    public const int NextRoundDelayMs = 8000;

    /// <summary>버림 → 다음 턴 사이 아무도 턴이 아닌 간격(ms). 연습 모드 TurnGapDelay(0.5초)와 동일.</summary>
    public const int TurnGapMs = 500;

    /// <summary>내 행동 대기 제한(초, rules.md §3). 초과 시 자동 진행(드로우 카드 버림/자동 계속).</summary>
    public const int TurnTimerSeconds = BbongCore.Config.GameConfig.TurnTimerSeconds;

    public const int RoomCodeLength = 6;
}
