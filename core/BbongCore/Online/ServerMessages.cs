using System;

namespace BbongCore.Online;

// 서버 → 클라 메시지. 상태 전이 메시지는 좌석별 RoundView 동봉(개인화),
// 연출(타임라인/콜아웃/사운드)은 이벤트 필드로 트리거.

public static class ServerMessageType
{
    public const string Welcome = "welcome";
    public const string RoomUpdate = "roomUpdate";
    public const string GameStarted = "gameStarted";
    public const string RoundStarted = "roundStarted";
    public const string TurnBegan = "turnBegan";
    public const string DrewCard = "drewCard";
    public const string Discarded = "discarded";
    public const string PongWindowOpened = "pongWindowOpened";
    public const string PongWindowClosed = "pongWindowClosed";
    public const string Ponged = "ponged";
    public const string NaturalPonged = "naturalPonged";
    public const string StopDeclared = "stopDeclared";
    public const string MeldDeclared = "meldDeclared";
    public const string RoundEnded = "roundEnded";
    public const string SetEnded = "setEnded";
    public const string RoomClosed = "roomClosed";
    public const string Error = "error";
}

/// <summary>type만 뽑는 1차 파싱용 엔벨로프(클라 수신측).</summary>
[Serializable]
public sealed class ServerEnvelope
{
    public string type = "";
}

[Serializable]
public sealed class WelcomeMsg
{
    public string type = ServerMessageType.Welcome;
    public string userId = "";
}

[Serializable]
public sealed class RoomMemberDto
{
    public string userId = "";
    public string nickname = "";
}

[Serializable]
public sealed class RoomUpdateMsg
{
    public string type = ServerMessageType.RoomUpdate;
    public string code = "";
    public string hostUserId = "";
    public RoomMemberDto[] members = Array.Empty<RoomMemberDto>();
}

[Serializable]
public sealed class GameStartedMsg
{
    public string type = ServerMessageType.GameStarted;
    public int yourSeat;
    public int playerCount;
    public string[] nicknames = Array.Empty<string>();
    public int setRounds;
}

[Serializable]
public sealed class RoundStartedMsg
{
    public string type = ServerMessageType.RoundStarted;
    public int roundIndex;
    public int dealerSeat;
    public RoundView view = new();
}

[Serializable]
public sealed class TurnBeganMsg
{
    public string type = ServerMessageType.TurnBegan;
    public int seat;
    public RoundView view = new();
}

[Serializable]
public sealed class DrewCardMsg
{
    public string type = ServerMessageType.DrewCard;
    public int seat;
    public bool reshuffled;
    public RoundView view = new();
}

[Serializable]
public sealed class DiscardedMsg
{
    public string type = ServerMessageType.Discarded;
    public int seat;
    public CardDto card = new();
    public RoundView view = new();
}

[Serializable]
public sealed class PongWindowOpenedMsg
{
    public string type = ServerMessageType.PongWindowOpened;
    public int discarderSeat;
    public int number;
    public int seconds;
    public RoundView view = new();
}

[Serializable]
public sealed class PongWindowClosedMsg
{
    public string type = ServerMessageType.PongWindowClosed;
    public RoundView view = new();
}

[Serializable]
public sealed class PongedMsg
{
    public string type = ServerMessageType.Ponged;
    public int seat;
    public int number;
    public CardDto[] laid = Array.Empty<CardDto>();
    public RoundView view = new();
}

[Serializable]
public sealed class NaturalPongedMsg
{
    public string type = ServerMessageType.NaturalPonged;
    public int seat;
    public int number;
    public CardDto[] laid = Array.Empty<CardDto>();
    public RoundView view = new();
}

[Serializable]
public sealed class StopDeclaredMsg
{
    public string type = ServerMessageType.StopDeclared;
    public int seat;
    public bool bagaji;
}

[Serializable]
public sealed class MeldDeclaredMsg
{
    public string type = ServerMessageType.MeldDeclared;
    public int seat;
    public string meldType = "";
    public int meldScore;
}

[Serializable]
public sealed class RoundEndedMsg
{
    public string type = ServerMessageType.RoundEnded;
    public string reason = "";
    public int enderSeat;
    public int[] scores = Array.Empty<int>();
    public int[] cumulativeDebts = Array.Empty<int>();
    public int roundIndex;
    public int nextRoundInMs;
    public RoundView view = new();
}

[Serializable]
public sealed class SetEndedMsg
{
    public string type = ServerMessageType.SetEnded;
    public string reason = "";
    public int enderSeat;
    public int[] scores = Array.Empty<int>();
    public int[] cumulativeDebts = Array.Empty<int>();
    public int[] winnerSeats = Array.Empty<int>();
}

[Serializable]
public sealed class RoomClosedMsg
{
    public string type = ServerMessageType.RoomClosed;
    public string reason = "";
}

[Serializable]
public sealed class ErrorMsg
{
    public string type = ServerMessageType.Error;
    public string code = "";
    public string message = "";
}
