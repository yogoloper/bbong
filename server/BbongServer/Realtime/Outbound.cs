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

    internal void ToSeat(int seat, object message) => Messages.Add(new Outbound(seat, message));

    internal void ToAll(object message) => Messages.Add(new Outbound(null, message));

    internal void After(RoomCommand command, int delayMs) => Timers.Add(new PendingTimer(command, delayMs));
}
