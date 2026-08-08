using System;

namespace BbongServer.Realtime;

// 방으로 들어오는 모든 입력. 단일 소비 루프가 순차 처리해 레이스를 차단한다.
// "먼저 온 뽕 선언 승리" = 큐 도착 순서.

public abstract record RoomCommand;

public sealed record JoinCmd(RoomMember Member) : RoomCommand;

public sealed record LeaveCmd(Guid UserId) : RoomCommand;

public sealed record StartGameCmd(Guid UserId) : RoomCommand;

/// <summary>대기실에서 방장이 봇 추가/삭제(사람+봇 합계가 정원).</summary>
public sealed record AddBotCmd(Guid RequesterUserId) : RoomCommand;

public sealed record RemoveBotCmd(Guid RequesterUserId) : RoomCommand;

/// <summary>맞춤게임 위장 봇 충원 시각 도래(10초 무입장 + 봇 간 1~10초 랜덤). Token으로 stale 방지.</summary>
public sealed record FillBotCmd(int Token) : RoomCommand;

/// <summary>맞춤게임 시작 카운트다운(5초) 만료. Token으로 stale 방지(카운트다운 중 이탈 시 무효).</summary>
public sealed record StartCountdownCmd(int Token) : RoomCommand;

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

/// <summary>봇 대체 좌석의 행동 차례(rules.md §9-4). 짧은 지연 후 봇이 결정. Token으로 stale 방지.</summary>
public sealed record BotActCmd(int Token) : RoomCommand;
