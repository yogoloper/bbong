using System;

namespace BbongCore.Online;

/// <summary>라운드 진행 단계(프로토콜 문자열 상수 — enum 대신 JsonUtility 호환 string).</summary>
public static class RoundPhase
{
    public const string WaitingStop = "WaitingStop";           // 턴 시작, 스톱/계속 대기
    public const string WaitingDiscard = "WaitingDiscard";     // 드로우 후 버림(또는 족보/자연뽕) 대기
    public const string PongWindow = "PongWindow";             // 뽕 선언 창(5초)
    public const string WaitingPongDiscard = "WaitingPongDiscard"; // 뽕 선언자의 추가 버림 대기
    public const string TurnGap = "TurnGap";                   // 버림 → 다음 턴 사이 아무도 턴이 아닌 간격(연출)
    public const string RoundOver = "RoundOver";
    public const string SetOver = "SetOver";
}

/// <summary>좌석 공개 정보(타인 손패는 장수만).</summary>
[Serializable]
public sealed class SeatView
{
    public int seat;
    public string nickname = "";
    public int handCount;
    public bool pairExposed; // 손패 2장이 같은 숫자 — 전원 공개(뽕 바가지 예고, §7)
    public int pongCount;
    public bool hasPonged;
    public int cumulativeDebt;
}

/// <summary>
/// 좌석별 개인화 스냅샷. 내 손패 전체 + 타인은 카운트만.
/// 버림 더미/고정패 연출은 이벤트(discarded/ponged...)로 클라가 타임라인 구축.
/// </summary>
[Serializable]
public sealed class RoundView
{
    public int mySeat;
    public int currentSeat;
    public string phase = "";
    public int actorSeat;          // phase 행동 주체(뽕 창은 선언 대기 대상 아님 — 버린 사람)
    public int drawPileCount;
    public int reshuffleCount;
    public int pongNumber;         // PongWindow/WaitingPongDiscard일 때 대상 숫자
    public bool canStop;           // WaitingStop에서 내가 스톱 가능
    public bool canMeld;           // WaitingDiscard에서 내가 족보 선언 가능
    public string meldType = "";
    public int meldScore;
    public bool canNaturalPong;    // WaitingDiscard에서 내가 자연뽕 가능
    public int naturalPongNumber;
    public bool canPong;           // PongWindow에서 내가 뽕 선언 가능
    public CardDto[] myHand = Array.Empty<CardDto>();
    public SeatView[] seats = Array.Empty<SeatView>();
}
