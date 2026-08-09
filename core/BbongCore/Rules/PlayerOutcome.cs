using BbongCore.Game;

namespace BbongCore.Rules;

/// <summary>
/// 판 종료 시점 한 플레이어의 정산 입력(rules.md §8).
/// DeclaredMeld가 있으면 족보 승자(점수=족보 점수), 없으면 남은 손패 합으로 계산.
/// </summary>
public sealed record PlayerOutcome(
    Hand RemainingHand,
    MeldResult? DeclaredMeld = null,
    bool PongBak = false,
    bool StopBagaji = false,
    bool NaturalPongBak = false);
