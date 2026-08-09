using System;
using System.Collections.Generic;

namespace BbongServer.Realtime;

/// <summary>세션이 만든 송신 1건. Seat=null이면 전체 브로드캐스트.</summary>
public sealed record Outbound(int? Seat, object Message);

/// <summary>세션이 요청한 지연 커맨드(뽕 타임아웃/다음 판). Room이 Task.Delay 후 Dispatch.</summary>
public sealed record PendingTimer(RoomCommand Command, int DelayMs);

/// <summary>
/// GameSession 호출 1건의 결과(송신 목록 + 타이머 예약).
/// 세션은 전송/타이머를 모름 — 순수 호출-반환이라 WS 없이 단위테스트 가능.
/// </summary>
public sealed class SessionOutput
{
    public List<Outbound> Messages { get; } = new();

    public List<PendingTimer> Timers { get; } = new();

    /// <summary>게임 히스토리 이벤트(CS/디버깅용 영구 기록) — Room이 저장소로 흘려보낸다.</summary>
    public List<HistoryEvent> History { get; } = new();

    internal void ToSeat(int seat, object message) => Messages.Add(new Outbound(seat, message));

    internal void ToAll(object message) => Messages.Add(new Outbound(null, message));

    internal void After(RoomCommand command, int delayMs) => Timers.Add(new PendingTimer(command, delayMs));

    private static readonly System.Text.Json.JsonSerializerOptions HistoryJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 한글 원문 저장(CS 가독성)
    };

    internal void Log(int roundIndex, int? seat, string type, object data) =>
        History.Add(new HistoryEvent(roundIndex, seat, type,
            System.Text.Json.JsonSerializer.Serialize(data, HistoryJson)));
}

/// <summary>라운드 진행 1건의 영구 기록. DataJson은 이벤트별 자유 구조(JSONB 저장).</summary>
public sealed record HistoryEvent(int RoundIndex, int? Seat, string Type, string DataJson);
