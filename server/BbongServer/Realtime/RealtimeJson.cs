using System;
using System.Text.Json;
using BbongCore.Online;

namespace BbongServer.Realtime;

/// <summary>
/// 실시간 메시지 직렬화. DTO는 클라 JsonUtility 호환을 위해 public 필드 —
/// 서버는 IncludeFields 옵션 전용 인스턴스로 처리(REST의 프로퍼티 직렬화와 분리).
/// </summary>
public static class RealtimeJson
{
    public static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    public static string Serialize(object message) =>
        JsonSerializer.Serialize(message, message.GetType(), Options);

    /// <summary>클라 메시지 2-pass 파싱: type 판별 → 타입별 역직렬화. 모르는 타입은 null.</summary>
    public static object? ParseClientMessage(string json)
    {
        string? type;
        try
        {
            using var doc = JsonDocument.Parse(json);
            type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }

        return type switch
        {
            ClientMessageType.CreateRoom => Deserialize<CreateRoomMsg>(json),
            ClientMessageType.JoinRoom => Deserialize<JoinRoomMsg>(json),
            ClientMessageType.LeaveRoom => Deserialize<LeaveRoomMsg>(json),
            ClientMessageType.StartGame => Deserialize<StartGameMsg>(json),
            ClientMessageType.AddBot => Deserialize<AddBotMsg>(json),
            ClientMessageType.RemoveBot => Deserialize<RemoveBotMsg>(json),
            ClientMessageType.StopDeclare => Deserialize<StopDeclareMsg>(json),
            ClientMessageType.ContinueTurn => Deserialize<ContinueTurnMsg>(json),
            ClientMessageType.Discard => Deserialize<DiscardMsg>(json),
            ClientMessageType.MeldDeclare => Deserialize<MeldDeclareMsg>(json),
            ClientMessageType.NaturalPong => Deserialize<NaturalPongMsg>(json),
            ClientMessageType.PongDeclare => Deserialize<PongDeclareMsg>(json),
            ClientMessageType.PongPass => Deserialize<PongPassMsg>(json),
            ClientMessageType.PongDiscard => Deserialize<PongDiscardMsg>(json),
            _ => null
        };
    }

    private static object? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
