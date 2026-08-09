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
    /// 뽕 바가지 종료(§7): 바가지 먹인 승자는 빈손이라 0, 당한(버린) 사람은 손합+20,
    /// 나머지는 자기 남은 손패 합을 빚으로 진다.
    /// </summary>
    public static int[] SettleByTwoPong(RoundState round, int winnerSeat, int lastDiscarderSeat) =>
        round.Players
            .Select(p => Scoring.Score(new PlayerOutcome(p.Hand, PongBak: p.Seat == lastDiscarderSeat)))
            .ToArray();

    /// <summary>
    /// 스톱 종료(rules.md §6, §8).
    /// 바가지면 선언자=손패 합+30, 나머지 전원 0. 아니면 전원 남은 손패 합.
    /// </summary>
    /// <summary>
    /// 스톱 종료(§6, §8).
    /// 성공(자기 손합이 뽕 게이머 중 유일 최저): 선언자는 (한도 − 손합)만큼 빚 청산(음수),
    /// 나머지는 자기 남은 손패 합.
    /// 실패(박): 먹인 승자 0, 당한 선언자 손합+30, 나머지는 자기 남은 손패 합.
    /// </summary>
    public static int[] SettleByStop(RoundState round, int stopSeat, int stopLimit = 10)
    {
        var bagaji = StopResolver.IsBagaji(round, stopSeat);
        var winner = bagaji ? BagajiWinner(round, stopSeat) : -1;

        return round.Players
            .Select(p =>
            {
                if (bagaji)
                {
                    if (p.Seat == winner)
                    {
                        return 0; // 바가지 먹인 사람
                    }

                    return Scoring.Score(new PlayerOutcome(p.Hand, StopBagaji: p.Seat == stopSeat));
                }

                return p.Seat == stopSeat
                    ? -(stopLimit - p.Hand.Sum()) // 성공 보상: 낮게 끊을수록 크게 청산 (예: 합 3 → -7)
                    : Scoring.Score(new PlayerOutcome(p.Hand));
            })
            .ToArray();
    }

    /// <summary>스톱 바가지 승자: 선언자보다 손합이 낮은 뽕 게이머 중 최저(동률이면 앞 좌석).</summary>
    public static int BagajiWinner(RoundState round, int stopSeat)
    {
        var winner = stopSeat;
        var min = round.Players[stopSeat].Hand.Sum();
        foreach (var p in round.Players)
        {
            if (p.Seat != stopSeat && p.HasPonged && p.Hand.Sum() <= min && p.Seat != winner
                && (winner == stopSeat || p.Hand.Sum() < min))
            {
                winner = p.Seat;
                min = p.Hand.Sum();
            }
        }

        return winner;
    }
}
