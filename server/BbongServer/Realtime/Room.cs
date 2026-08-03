using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using BbongCore.Cards;
using BbongCore.Config;
using BbongCore.Online;

namespace BbongServer.Realtime;

public enum RoomPhase
{
    Waiting,
    Playing,
    Closed
}

/// <summary>
/// 친구방 1개. 모든 입력(WS 메시지/타이머/끊김)은 커맨드로 들어와 단일 루프가 순차 처리.
/// 게임 중 이탈/끊김 = 방 해체(무료방, 재접속은 후속).
/// </summary>
public sealed class Room
{
    private readonly Channel<RoomCommand> _channel = Channel.CreateUnbounded<RoomCommand>();
    private readonly List<RoomMember> _members = new();
    private readonly RoomRegistry _registry;
    private bool _loopRunning;
    private GameSession? _session;

    internal Room(string code, RoomRegistry registry, Guid hostUserId)
    {
        Code = code;
        _registry = registry;
        HostUserId = hostUserId;
    }

    public string Code { get; }

    public Guid HostUserId { get; }

    public RoomPhase Phase { get; private set; } = RoomPhase.Waiting;

    public IReadOnlyList<RoomMember> Members => _members;

    /// <summary>루프 가동(운영 경로). 테스트는 Execute를 직접 호출한다.</summary>
    public void StartLoop()
    {
        _loopRunning = true;
        _ = Task.Run(RunLoopAsync);
    }

    /// <summary>루프 가동 여부에 따라 큐잉 또는 즉시 실행.</summary>
    public void Dispatch(RoomCommand cmd)
    {
        if (_loopRunning)
        {
            _channel.Writer.TryWrite(cmd);
        }
        else
        {
            Execute(cmd);
        }
    }

    private async Task RunLoopAsync()
    {
        await foreach (var cmd in _channel.Reader.ReadAllAsync())
        {
            try
            {
                Execute(cmd);
            }
            catch (Exception ex)
            {
                // 예기치 못한 오류는 좀비 방 방지를 위해 해체로 수렴
                CloseRoom($"서버 오류: {ex.Message}");
            }
        }
    }

    /// <summary>커맨드 1건 처리(루프 본체 — 테스트에서 직접 호출 가능).</summary>
    public void Execute(RoomCommand cmd)
    {
        if (Phase == RoomPhase.Closed)
        {
            return;
        }

        switch (cmd)
        {
            case JoinCmd join:
                HandleJoin(join.Member);
                break;
            case LeaveCmd leave:
                HandleLeaveOrDisconnect(leave.UserId);
                break;
            case DisconnectCmd gone:
                HandleLeaveOrDisconnect(gone.UserId);
                break;
            case StartGameCmd start:
                HandleStart(start.UserId);
                break;
            case ActionCmd action:
                HandleAction(action.UserId, action.Message);
                break;
            case PongTimeoutCmd timeout:
                if (_session is not null)
                {
                    Apply(_session.HandlePongTimeout(timeout.Token));
                }

                break;
            case NextRoundCmd next:
                if (_session is not null)
                {
                    Apply(_session.HandleNextRound(next.Token));
                }

                break;
            case TurnGapCmd gap:
                if (_session is not null)
                {
                    Apply(_session.HandleTurnGap(gap.Token));
                }

                break;
        }
    }

    // ── 대기실 ──

    private void HandleJoin(RoomMember member)
    {
        if (Phase == RoomPhase.Playing)
        {
            Send(member.Sink, new ErrorMsg { code = "room_playing", message = "이미 게임이 시작된 방입니다." });
            return;
        }

        if (_members.Count >= GameConfig.MaxPlayers)
        {
            Send(member.Sink, new ErrorMsg { code = "room_full", message = "방 정원이 가득 찼습니다." });
            return;
        }

        _members.Add(member);
        _registry.Index(member.UserId, this);
        BroadcastRoomUpdate();
    }

    private void HandleLeaveOrDisconnect(Guid userId)
    {
        var member = _members.FirstOrDefault(m => m.UserId == userId);
        if (member is null)
        {
            return;
        }

        if (Phase == RoomPhase.Playing || userId == HostUserId)
        {
            // 게임 중 이탈 또는 호스트 퇴장 → 방 해체
            CloseRoom(Phase == RoomPhase.Playing ? "참가자 연결이 끊겨 방이 해체되었습니다." : "호스트가 방을 나갔습니다.");
            return;
        }

        _members.Remove(member);
        _registry.Detach(userId);
        BroadcastRoomUpdate();
    }

    private void HandleStart(Guid userId)
    {
        var requester = _members.FirstOrDefault(m => m.UserId == userId);
        if (requester is null)
        {
            return;
        }

        if (userId != HostUserId)
        {
            Send(requester.Sink, new ErrorMsg { code = "not_host", message = "호스트만 시작할 수 있습니다." });
            return;
        }

        if (Phase != RoomPhase.Waiting || _members.Count < GameConfig.MinPlayers)
        {
            Send(requester.Sink, new ErrorMsg { code = "not_enough_players", message = $"최소 {GameConfig.MinPlayers}명이 필요합니다." });
            return;
        }

        Phase = RoomPhase.Playing;
        var nicknames = _members.Select(m => m.Nickname).ToArray();
        for (var seat = 0; seat < _members.Count; seat++)
        {
            Send(_members[seat].Sink, new GameStartedMsg
            {
                yourSeat = seat,
                playerCount = _members.Count,
                nicknames = nicknames,
                setRounds = new GameConfig().SetRounds
            });
        }

        _session = new GameSession(nicknames, () => new SeededRandom(Random.Shared.Next()));
        Apply(_session.StartMatch());
    }

    private void HandleAction(Guid userId, object message)
    {
        if (Phase != RoomPhase.Playing || _session is null)
        {
            return;
        }

        var seat = _members.FindIndex(m => m.UserId == userId);
        if (seat < 0)
        {
            return;
        }

        Apply(_session.HandleAction(seat, message));
    }

    /// <summary>세션 결과 반영: 좌석별 송신 + 타이머 예약(만료 시 커맨드 재주입).</summary>
    private void Apply(SessionOutput output)
    {
        foreach (var outbound in output.Messages)
        {
            if (outbound.Seat is { } seat)
            {
                if (seat < _members.Count)
                {
                    Send(_members[seat].Sink, outbound.Message);
                }
            }
            else
            {
                Broadcast(outbound.Message);
            }
        }

        foreach (var timer in output.Timers)
        {
            ScheduleTimer(timer);
        }

        if (output.Messages.Any(o => o.Message is SetEndedMsg))
        {
            ReturnToWaiting(); // 세트 종료 → 대기실 복귀(재대결 가능)
        }
    }

    internal void ReturnToWaiting()
    {
        _session = null;
        Phase = RoomPhase.Waiting;
        BroadcastRoomUpdate();
    }

    private void ScheduleTimer(PendingTimer timer) =>
        _ = Task.Run(async () =>
        {
            await Task.Delay(timer.DelayMs);
            Dispatch(timer.Command);
        });

    // ── 공통 ──

    internal void CloseRoom(string reason)
    {
        Phase = RoomPhase.Closed;
        Broadcast(new RoomClosedMsg { reason = reason });
        foreach (var member in _members)
        {
            _registry.Detach(member.UserId);
        }

        _registry.Remove(Code);
        _channel.Writer.TryComplete();
    }

    private void BroadcastRoomUpdate()
    {
        var update = new RoomUpdateMsg
        {
            code = Code,
            hostUserId = HostUserId.ToString(),
            members = _members.Select(m => new RoomMemberDto { userId = m.UserId.ToString(), nickname = m.Nickname }).ToArray()
        };
        Broadcast(update);
    }

    private void Broadcast(object message)
    {
        foreach (var member in _members)
        {
            Send(member.Sink, message);
        }
    }

    private static void Send(ISessionSink sink, object message) =>
        _ = SafeSendAsync(sink, message);

    private static async Task SafeSendAsync(ISessionSink sink, object message)
    {
        try
        {
            await sink.SendAsync(message);
        }
        catch
        {
            // 송신 실패는 수신 루프의 끊김 감지(DisconnectCmd)로 수렴 — 여기선 무시
        }
    }
}
