using System;
using System.Collections.Generic;
using System.Linq;

namespace BbongCore.Game;

/// <summary>
/// 세트(고정 5판) 진행 상태(불변). 좌석별 누적 빚을 모아 1등을 가립니다(rules.md §8).
/// 판별 단위 손패·더미는 RoundState가, 세트 누적은 GameState가 담당합니다.
/// </summary>
public sealed class GameState
{
    private readonly int[] _debts;

    private GameState(int[] debts, int roundsPlayed, int setRounds)
    {
        _debts = debts;
        RoundsPlayed = roundsPlayed;
        SetRounds = setRounds;
    }

    public IReadOnlyList<int> CumulativeDebts => _debts;

    public int PlayerCount => _debts.Length;

    public int RoundsPlayed { get; }

    public int SetRounds { get; }

    public bool IsSetOver => RoundsPlayed >= SetRounds;

    public static GameState Start(int playerCount, int setRounds = 5) =>
        new(new int[playerCount], roundsPlayed: 0, setRounds);

    /// <summary>한 판 정산 결과(좌석별 점수)를 누적 빚에 더하고 판 수를 1 올립니다.</summary>
    public GameState ApplyRoundScores(int[] scores)
    {
        if (scores.Length != PlayerCount)
        {
            throw new ArgumentException($"점수 개수({scores.Length})가 인원({PlayerCount})과 다릅니다.", nameof(scores));
        }

        var next = new int[PlayerCount];
        for (var seat = 0; seat < PlayerCount; seat++)
        {
            next[seat] = _debts[seat] + scores[seat];
        }

        return new GameState(next, RoundsPlayed + 1, SetRounds);
    }

    /// <summary>누적 빚이 가장 낮은(가장 많이 탕감한) 좌석들. 동점이면 복수.</summary>
    public IReadOnlyList<int> WinnerSeats()
    {
        var min = _debts.Min();
        return Enumerable.Range(0, PlayerCount).Where(seat => _debts[seat] == min).ToList();
    }
}
