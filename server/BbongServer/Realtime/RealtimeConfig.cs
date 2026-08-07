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

    /// <summary>봇 대체 좌석의 행동 지연(ms) — 즉답 대신 사람 같은 한 박자(연습 모드 BotDelay와 유사).</summary>
    public const int BotActDelayMs = 1000;

    /// <summary>재셔플 수렴 연출 시간 — 연출이 끝난 뒤부터 행동 시간을 재도록 타이머에 가산(연습 모드와 동일 페이싱).</summary>
    public const int ReshuffleFxMs = 900;

    public const int RoomCodeLength = 6;
}
