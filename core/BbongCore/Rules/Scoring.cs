using BbongCore.Config;

namespace BbongCore.Rules;

/// <summary>판 종료 시 한 플레이어의 빚(점수)을 계산합니다(rules.md §6~8). 벌칙값은 GameConfig.</summary>
public static class Scoring
{
    public static int Score(PlayerOutcome outcome)
    {
        // 스톱 바가지: 손패 합 무관, 고정 30점(§6).
        if (outcome.StopBagaji)
        {
            return GameConfig.StopBagajiPenalty;
        }

        // 족보 승자는 족보 점수, 그 외(비승자·뽕소진·스톱)는 남은 손패 합.
        var baseScore = outcome.DeclaredMeld is { } meld
            ? meld.Score
            : outcome.RemainingHand.Sum();

        return baseScore + (outcome.PongBak ? GameConfig.PongBakPenalty : 0); // 뽕 박은 가산(§7)
    }
}
