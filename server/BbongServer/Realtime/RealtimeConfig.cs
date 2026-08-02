namespace BbongServer.Realtime;

/// <summary>실시간(친구방) 서버 설정. 코어 GameConfig와 별개 — 연출/운영 파라미터.</summary>
public static class RealtimeConfig
{
    /// <summary>뽕 선언 창(초). 규칙은 2초지만 사람 대전 체감상 연습 모드와 동일한 5초.</summary>
    public const int PongWindowSeconds = 5;

    /// <summary>판 종료 전광판 후 다음 판 자동 시작까지(ms).</summary>
    public const int NextRoundDelayMs = 8000;

    public const int RoomCodeLength = 6;
}
