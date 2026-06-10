namespace BbongCore.Game;

/// <summary>
/// 플레이어 상태(불변). 연산은 새 Player를 반환합니다.
/// PongCount는 판 단위(스톱 자격 §6, 두 번 뽕 종료 §4-3), CumulativeDebt는 세트 단위(§8).
/// </summary>
public sealed record Player(int Seat, Hand Hand, int CumulativeDebt = 0, int PongCount = 0)
{
    public bool HasPonged => PongCount > 0;

    public Player WithHand(Hand hand) => this with { Hand = hand };

    public Player RecordPong() => this with { PongCount = PongCount + 1 };

    public Player AddDebt(int score) => this with { CumulativeDebt = CumulativeDebt + score };
}
