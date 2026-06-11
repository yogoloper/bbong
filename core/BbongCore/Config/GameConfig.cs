using System.Collections.Generic;
using System.Linq;

namespace BbongCore.Config;

/// <summary>
/// 게임 설정 단일 출처(rules.md 부록 설정값).
/// 고정 규칙 상수(const)와 방마다 바꿀 수 있는 가변 설정(record 속성)을 한곳에 모읍니다.
/// </summary>
public sealed record GameConfig(
    int StopLimit = GameConfig.DefaultStopLimit,
    int SetRounds = GameConfig.DefaultSetRounds)
{
    // ── 고정 규칙 상수 (rules.md) ──
    public const int HandSize = 5;            // 기본 손패
    public const int PongBakPenalty = 20;     // 뽕 박 (§7)
    public const int StopBagajiPenalty = 30;  // 스톱 바가지 (§6)
    public const int MinPlayers = 2;          // 방 최소 인원 (§9-1)
    public const int MaxPlayers = 6;          // 방 최대 인원 (§9-1)
    public const int PongTimerSeconds = 2;    // 뽕 입력 창 (§4-1)
    public const int TurnTimerSeconds = 5;    // 자기 턴 버림 제한 (§3)

    // ── 가변 설정 기본값 ──
    public const int DefaultStopLimit = 10;   // 스톱 2장 합 한도 (§6)
    public const int DefaultSetRounds = 5;    // 1세트 판 수 (§8)

    /// <summary>방 생성 시 선택 가능한 입장료(판돈) (§9-1).</summary>
    public static readonly IReadOnlyList<int> StakeOptions = new[] { 100, 500, 1000, 5000, 10000 };

    public static GameConfig Default { get; } = new();

    public static bool IsValidPlayerCount(int count) => count >= MinPlayers && count <= MaxPlayers;

    public static bool IsValidStake(int stake) => StakeOptions.Contains(stake);
}
