using System;

namespace BbongCore.Online;

// 클라 → 서버 메시지. 엔벨로프 {type, ...} 평면 필드 — type 먼저 파싱 후 타입별 재파싱(2-pass).
// JsonUtility 호환: public 필드 + 기본값. 옵셔널 카드에는 bool 플래그 사용.

/// <summary>type만 뽑는 1차 파싱용 엔벨로프.</summary>
[Serializable]
public sealed class ClientEnvelope
{
    public string type = "";
}

public static class ClientMessageType
{
    public const string CreateRoom = "createRoom";
    public const string JoinRoom = "joinRoom";
    public const string LeaveRoom = "leaveRoom";
    public const string StartGame = "startGame";
    public const string StopDeclare = "stopDeclare";
    public const string ContinueTurn = "continueTurn";
    public const string Discard = "discard";
    public const string MeldDeclare = "meldDeclare";
    public const string NaturalPong = "naturalPong";
    public const string PongDeclare = "pongDeclare";
    public const string PongPass = "pongPass";
    public const string PongDiscard = "pongDiscard";
}

[Serializable]
public sealed class CreateRoomMsg
{
    public string type = ClientMessageType.CreateRoom;
}

[Serializable]
public sealed class JoinRoomMsg
{
    public string type = ClientMessageType.JoinRoom;
    public string code = "";
}

[Serializable]
public sealed class LeaveRoomMsg
{
    public string type = ClientMessageType.LeaveRoom;
}

[Serializable]
public sealed class StartGameMsg
{
    public string type = ClientMessageType.StartGame;
}

[Serializable]
public sealed class StopDeclareMsg
{
    public string type = ClientMessageType.StopDeclare;
}

[Serializable]
public sealed class ContinueTurnMsg
{
    public string type = ClientMessageType.ContinueTurn;
}

[Serializable]
public sealed class DiscardMsg
{
    public string type = ClientMessageType.Discard;
    public CardDto card = new();
}

[Serializable]
public sealed class MeldDeclareMsg
{
    public string type = ClientMessageType.MeldDeclare;
}

/// <summary>자연뽕 선언. 손 소진(3장 전부 같은 숫자)이면 hasDiscard=false, card 무시.</summary>
[Serializable]
public sealed class NaturalPongMsg
{
    public string type = ClientMessageType.NaturalPong;
    public bool hasDiscard;
    public CardDto card = new();
}

[Serializable]
public sealed class PongDeclareMsg
{
    public string type = ClientMessageType.PongDeclare;
}

[Serializable]
public sealed class PongPassMsg
{
    public string type = ClientMessageType.PongPass;
}

[Serializable]
public sealed class PongDiscardMsg
{
    public string type = ClientMessageType.PongDiscard;
    public CardDto card = new();
}
