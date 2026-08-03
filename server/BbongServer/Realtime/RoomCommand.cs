using System;

namespace BbongServer.Realtime;

// 방으로 들어오는 모든 입력. 단일 소비 루프가 순차 처리해 레이스를 차단한다.
// "먼저 온 뽕 선언 승리" = 큐 도착 순서.

public abstract record RoomCommand;

public sealed record JoinCmd(RoomMember Member) : RoomCommand;

public sealed record LeaveCmd(Guid UserId) : RoomCommand;

public sealed record StartGameCmd(Guid UserId) : RoomCommand;

/// <summary>게임 중 클라 액션(파싱된 BbongCore.Online 메시지 객체).</summary>
public sealed record ActionCmd(Guid UserId, object Message) : RoomCommand;

public sealed record DisconnectCmd(Guid UserId) : RoomCommand;

/// <summary>뽕 창 타임아웃. Token이 현재 창과 일치할 때만 유효(stale 방지).</summary>
public sealed record PongTimeoutCmd(int Token) : RoomCommand;

/// <summary>판 종료 후 자동 다음 판. Token으로 stale 방지.</summary>
public sealed record NextRoundCmd(int Token) : RoomCommand;

/// <summary>버림 → 다음 턴 사이 전환 간격 만료. Token으로 stale 방지.</summary>
public sealed record TurnGapCmd(int Token) : RoomCommand;

/// <summary>턴 행동 대기 5초 만료(rules.md §3) → 자동 진행. Token으로 stale 방지.</summary>
public sealed record TurnTimeoutCmd(int Token) : RoomCommand;
