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

    // 대기실에서 방장이 채운 봇 자리(닉네임만 보관 — 시작 시 뒷좌석에 배치)
    private readonly List<string> _botNames = new();
    private readonly HashSet<Guid> _absent = new(); // 게임 중 이탈(끊김/종료) — 좌석 유지, 판 종료 시 봇 대체(§9-4)
    private readonly RoomRegistry _registry;
    private readonly IStakeBank? _bank;
    private Guid?[] _seatUsers = Array.Empty<Guid?>(); // 게임 시작 시점 좌석→유저(봇/이후 이탈과 무관한 정산 기준)
    private bool _loopRunning;
    private GameSession? _session;

    internal Room(string code, RoomRegistry registry, Guid hostUserId, int stake = 0, IStakeBank? bank = null,
        int targetPlayers = 0)
    {
        Code = code;
        _registry = registry;
        HostUserId = hostUserId;
        Stake = stake;
        _bank = bank;
        TargetPlayers = targetPlayers;
    }

    public string Code { get; }

    /// <summary>입장료(0 = 무료 친구방). 에스크로는 입장 전에 WsEndpoint가 수행.</summary>
    public int Stake { get; }

    /// <summary>빠른매칭 목표 인원(0 = 수동 시작 친구방). 도달하면 자동 시작.</summary>
    public int TargetPlayers { get; }

    public Guid HostUserId { get; private set; }

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
                HandleLeaveOrDisconnect(leave.UserId, voluntary: true);
                break;
            case DisconnectCmd gone:
                HandleLeaveOrDisconnect(gone.UserId, voluntary: false);
                break;
            case StartGameCmd start:
                HandleStart(start.UserId);
                break;
            case AddBotCmd addBot:
                HandleAddBot(addBot.RequesterUserId);
                break;
            case RemoveBotCmd removeBot:
                HandleRemoveBot(removeBot.RequesterUserId);
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
            case TurnTimeoutCmd turnTimeout:
                if (_session is not null)
                {
                    Apply(_session.HandleTurnTimeout(turnTimeout.Token));
                }

                break;
            case BotActCmd botAct:
                if (_session is not null)
                {
                    Apply(_session.HandleBotAct(botAct.Token));
                }

                break;
        }
    }

    // ── 대기실 ──

    private void HandleJoin(RoomMember member)
    {
        if (Phase == RoomPhase.Playing)
        {
            var seat = _members.FindIndex(m => m.UserId == member.UserId);
            if (seat >= 0 && _session is not null)
            {
                Reconnect(seat, member); // 이탈했던 참가자 — 자리 복귀(§9-4 후속)
                return;
            }

            Send(member.Sink, new ErrorMsg { code = "room_playing", message = "이미 게임이 시작된 방입니다." });
            RefundStake(member.UserId); // 입장 전 선차감된 에스크로 반환
            return;
        }

        if (_members.Count + _botNames.Count >= GameConfig.MaxPlayers)
        {
            Send(member.Sink, new ErrorMsg { code = "room_full", message = "방 정원이 가득 찼습니다." });
            RefundStake(member.UserId);
            return;
        }

        _members.Add(member);
        _registry.Index(member.UserId, this);
        BroadcastRoomUpdate();

        if (TargetPlayers > 0 && _members.Count >= TargetPlayers)
        {
            StartGame(); // 빠른매칭: 정원 도달 → 방장 개입 없이 자동 시작
        }
    }

    /// <summary>재접속: 새 소켓으로 좌석 멤버 교체 + 게임 시작 정보/현재 판 상태 재전송 + 봇 자리 회수.</summary>
    private void Reconnect(int seat, RoomMember member)
    {
        _members[seat] = member;
        _absent.Remove(member.UserId);
        _registry.Index(member.UserId, this);

        var nicknames = _session!.Nicknames.ToArray();
        Send(member.Sink, new GameStartedMsg
        {
            yourSeat = seat,
            stake = Stake,
            playerCount = nicknames.Length,
            nicknames = nicknames,
            setRounds = new GameConfig().SetRounds
        });
        Apply(_session.HandleReconnect(seat));
    }

    /// <summary>이 유저가 게임 중 좌석 보유자인지(재접속 — 입장료 재청구 금지 판단용).</summary>
    public bool HasSeatFor(Guid userId) => Phase == RoomPhase.Playing && _members.Any(m => m.UserId == userId);

    /// <summary>테스트 전용.</summary>
    internal GameSession? SessionForTest => _session;

    private void HandleLeaveOrDisconnect(Guid userId, bool voluntary)
    {
        var member = _members.FirstOrDefault(m => m.UserId == userId);
        if (member is null)
        {
            return;
        }

        if (Phase == RoomPhase.Playing)
        {
            // 게임 중 이탈 → 방 유지(§9-4).
            // 명시적 나가기는 즉시 봇 대체, 끊김(앱 백그라운드 가능성)은 판 종료까지 자리 보전.
            _absent.Add(userId);
            _registry.Detach(userId);
            if (_members.All(m => _absent.Contains(m.UserId)))
            {
                CloseRoom("모든 참가자가 나가 방이 해체되었습니다.");
                return;
            }

            if (voluntary && _session is not null)
            {
                Apply(_session.ReplaceSeatWithBot(_members.IndexOf(member)));
            }

            return;
        }

        _members.Remove(member);
        RefundStake(member.UserId); // 대기실 이탈 — 게임 전이므로 입장료 반환
        _registry.Detach(userId);

        if (_members.Count == 0)
        {
            CloseRoom("모든 참가자가 나가 방이 닫혔습니다.");
            return;
        }

        if (userId == HostUserId)
        {
            HostUserId = _members[0].UserId; // 방장 퇴장 → 다음 입장자에게 위임(방 유지)
        }

        BroadcastRoomUpdate();
    }

    /// <summary>대기실 봇 추가(방장 전용). 사람+봇 합계가 정원(rules.md §9-4 봇과 동일 로직 재사용).</summary>
    private void HandleAddBot(Guid userId)
    {
        var requester = _members.FirstOrDefault(m => m.UserId == userId);
        if (requester is null || Phase != RoomPhase.Waiting)
        {
            return;
        }

        if (userId != HostUserId)
        {
            Send(requester.Sink, new ErrorMsg { code = "not_host", message = "호스트만 봇을 관리할 수 있습니다." });
            return;
        }

        if (_members.Count + _botNames.Count >= GameConfig.MaxPlayers)
        {
            Send(requester.Sink, new ErrorMsg { code = "room_full", message = "방 정원이 가득 찼습니다." });
            return;
        }

        string name;
        do
        {
            name = $"{NicknamePool.Pick(Random.Shared)} 봇";
        } while (_botNames.Contains(name) || _members.Any(m => m.Nickname == name));

        _botNames.Add(name);
        BroadcastRoomUpdate();
    }

    private void HandleRemoveBot(Guid userId)
    {
        var requester = _members.FirstOrDefault(m => m.UserId == userId);
        if (requester is null || Phase != RoomPhase.Waiting)
        {
            return;
        }

        if (userId != HostUserId)
        {
            Send(requester.Sink, new ErrorMsg { code = "not_host", message = "호스트만 봇을 관리할 수 있습니다." });
            return;
        }

        if (_botNames.Count == 0)
        {
            return;
        }

        _botNames.RemoveAt(_botNames.Count - 1);
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

        if (Phase != RoomPhase.Waiting || _members.Count + _botNames.Count < GameConfig.MinPlayers)
        {
            Send(requester.Sink, new ErrorMsg { code = "not_enough_players", message = $"최소 {GameConfig.MinPlayers}명이 필요합니다." });
            return;
        }

        StartGame();
    }

    private void StartGame()
    {
        Phase = RoomPhase.Playing;
        var nicknames = _members.Select(m => m.Nickname).Concat(_botNames).ToArray();
        _seatUsers = _members.Select(m => (Guid?)m.UserId)
            .Concat(_botNames.Select(_ => (Guid?)null))
            .ToArray(); // 정산은 시작 시점 기준 — 이탈해도 판돈은 팟에 남는다(§9-4 몰수)
        for (var seat = 0; seat < _members.Count; seat++)
        {
            Send(_members[seat].Sink, new GameStartedMsg
            {
                yourSeat = seat,
                stake = Stake,
                playerCount = nicknames.Length,
                nicknames = nicknames,
                setRounds = new GameConfig().SetRounds
            });
        }

        var botSeats = Enumerable.Range(_members.Count, _botNames.Count);
        _session = new GameSession(nicknames, () => new SeededRandom(Random.Shared.Next()), botSeats: botSeats);
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
                if (seat < _members.Count && !_absent.Contains(_members[seat].UserId))
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

        if (output.Messages.Select(o => o.Message).OfType<SetEndedMsg>().FirstOrDefault() is { } setEnded)
        {
            HandleSetEnded(setEnded.winnerSeats);
        }
    }

    /// <summary>세트 종료: 판돈 방은 우승자 배당 후 폭파(§9-2), 무료방은 대기실 복귀(재대결).</summary>
    private void HandleSetEnded(int[] winnerSeats)
    {
        if (Stake <= 0 || _bank is null)
        {
            ReturnToWaiting();
            return;
        }

        var winners = winnerSeats
            .Where(seat => seat < _seatUsers.Length && _seatUsers[seat] is not null)
            .Select(seat => _seatUsers[seat]!.Value)
            .ToList();

        if (winners.Count > 0)
        {
            var pot = (long)Stake * _seatUsers.Count(u => u is not null); // 사람 참가자 전원의 판돈(이탈자 몰수 포함)
            var share = pot / winners.Count; // 공동 1등 균등 분배, 나머지 절사(§9-3)
            foreach (var userId in winners)
            {
                _ = _bank.PayoutAsync(userId, share);
            }
        }

        CloseRoom("게임 종료 — 정산 완료");
    }

    /// <summary>테스트 전용: 게임 결과를 주입해 정산/복귀 경로를 구동.</summary>
    internal void ForceSetEndForTest(int[] winnerSeats) => HandleSetEnded(winnerSeats);

    private void RefundStake(Guid userId)
    {
        if (Stake > 0 && _bank is not null)
        {
            _ = _bank.RefundAsync(userId, Stake);
        }
    }

    internal void ReturnToWaiting()
    {
        _session = null;
        Phase = RoomPhase.Waiting;

        // 게임 중 이탈자(봇 대체됐던 좌석)는 대기실 복귀 시 정리
        _members.RemoveAll(m => _absent.Contains(m.UserId));
        _absent.Clear();
        if (_members.Count == 0)
        {
            CloseRoom("모든 참가자가 나가 방이 닫혔습니다.");
            return;
        }

        if (_members.All(m => m.UserId != HostUserId))
        {
            HostUserId = _members[0].UserId; // 방장이 이탈했으면 위임
        }

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
            stake = Stake,
            targetPlayers = TargetPlayers,
            members = _members
                .Select(m => new RoomMemberDto { userId = m.UserId.ToString(), nickname = m.Nickname })
                .Concat(_botNames.Select(n => new RoomMemberDto { nickname = n, isBot = true }))
                .ToArray()
        };
        Broadcast(update);
    }

    private void Broadcast(object message)
    {
        foreach (var member in _members)
        {
            if (!_absent.Contains(member.UserId))
            {
                Send(member.Sink, message);
            }
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
