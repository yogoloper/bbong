namespace BbongCore.Ai;

/// <summary>AI 봇 난이도 3단(README Phase 2).</summary>
public enum BotDifficulty
{
    Easy,    // 무조건 큰 수 버림, 뽕·스톱 안 함
    Normal,  // 쌍 보존 버림, 가능하면 뽕·스톱
    Hard     // Normal + 바가지 스톱 회피
}
