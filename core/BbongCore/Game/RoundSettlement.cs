using System.Linq;
using BbongCore.Rules;

namespace BbongCore.Game;

/// <summary>
/// 판 종료 시 좌석 순서대로 각자의 그 판 점수(빚 변화)를 산출합니다(rules.md §6~8).
/// 누적 적용은 GameState가 담당합니다.
/// </summary>
public static class RoundSettlement
{
    /// <summary>손 소진 종료(두 번 뽕 외 자연뽕 손 소진 등): 승자 0, 나머지 손패 합. 벌칙 없음.</summary>
    public static int[] SettleByHandClear(RoundState round, int winnerSeat) =>
        round.Players.Select(p => p.Seat == winnerSeat ? 0 : p.Hand.Sum()).ToArray();

    /// <summary>강제 종료(바닥 더미 재셔플 한도 초과 소진): 전원 남은 손패 합(rules.md §3, §8).</summary>
    public static int[] SettleByExhaustion(RoundState round) =>
        round.Players.Select(p => p.Hand.Sum()).ToArray();

    /// <summary>6장 족보 종료: 승자는 족보 점수, 나머지는 남은 손패 합(rules.md §5, §8).</summary>
    public static int[] SettleByMeld(RoundState round, int winnerSeat, MeldResult meld) =>
        round.Players
            .Select(p => p.Seat == winnerSeat
                ? Scoring.Score(new PlayerOutcome(p.Hand, DeclaredMeld: meld))
                : Scoring.Score(new PlayerOutcome(p.Hand)))
            .ToArray();

    /// <summary>
    /// 두 번 뽕 종료: 승자(빈 손)=0, 마지막 버린 자=손패 합+20(박), 나머지=손패 합(rules.md §4-3, §7).
    /// </summary>
    /// <summary>
    /// 뽕 바가지 종료(§7): 라운드를 끝나게 만든(마지막 버린) 사람만 손합+20 벌점, 나머지는 전원 0.
    /// 스톱 바가지(선언자만 +30, 나머지 0)와 대칭 구조.
    /// </summary>
    public static int[] SettleByTwoPong(RoundState round, int winnerSeat, int lastDiscarderSeat) =>
        round.Players
            .Select(p => p.Seat == lastDiscarderSeat
                ? Scoring.Score(new PlayerOutcome(p.Hand, PongBak: true))
                : 0)
            .ToArray();

    /// <summary>
    /// 스톱 종료(rules.md §6, §8).
    /// 바가지면 선언자=손패 합+30, 나머지 전원 0. 아니면 전원 남은 손패 합.
    /// </summary>
    public static int[] SettleByStop(RoundState round, int stopSeat, int stopLimit = 10)
    {
        var bagaji = StopResolver.IsBagaji(round, stopSeat);

        return round.Players
            .Select(p =>
            {
                if (!bagaji)
                {
                    return Scoring.Score(new PlayerOutcome(p.Hand));
                }

                return p.Seat == stopSeat
                    ? Scoring.Score(new PlayerOutcome(p.Hand, StopBagaji: true))
                    : 0;
            })
            .ToArray();
    }
}
