using System;
using System.Linq;
using BbongCore.Online;
using BbongServer.Realtime;
using NUnit.Framework;

namespace BbongServer.Tests.Realtime;

/// <summary>게임 중 이탈자의 재접속 — 자리 복귀(봇 자리 되찾기 포함) (rules.md §9-4 후속).</summary>
[TestFixture]
public class ReconnectTests
{
    private RoomRegistry _registry = null!;

    [SetUp]
    public void SetUp() => _registry = new RoomRegistry();

    private static (FakeSessionSink sink, RoomMember member) NewMember(string nickname, Guid? userId = null)
    {
        var sink = new FakeSessionSink(userId ?? Guid.NewGuid());
        return (sink, new RoomMember(sink, sink.UserId, nickname));
    }

    private (Room room, FakeSessionSink hostSink, RoomMember host, FakeSessionSink guestSink, RoomMember guest) StartedRoom()
    {
        var (hostSink, host) = NewMember("호스트");
        var room = _registry.Create(host, runLoop: false);
        var (guestSink, guest) = NewMember("손님");
        room.Execute(new JoinCmd(guest));
        room.Execute(new StartGameCmd(host.UserId));
        return (room, hostSink, host, guestSink, guest);
    }

    [Test]
    public void Disconnected_player_reconnects_into_running_game()
    {
        var (room, _, _, _, guest) = StartedRoom();
        room.Execute(new DisconnectCmd(guest.UserId));
        Assert.That(_registry.FindByUser(guest.UserId), Is.Null);

        var (newSink, rejoin) = NewMember("손님", guest.UserId); // 같은 유저, 새 소켓
        room.Execute(new JoinCmd(rejoin));

        Assert.That(newSink.Last<GameStartedMsg>().yourSeat, Is.EqualTo(1)); // 원래 좌석 복귀
        Assert.That(newSink.Last<TurnBeganMsg>().view.mySeat, Is.EqualTo(1)); // 현재 판 상태 동기화
        Assert.That(_registry.FindByUser(guest.UserId), Is.SameAs(room));
        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Playing));
    }

    [Test]
    public void Voluntary_leaver_reclaims_seat_from_bot()
    {
        var (room, _, _, _, guest) = StartedRoom();
        room.Execute(new LeaveCmd(guest.UserId)); // 즉시 봇 대체
        Assert.That(room.SessionForTest!.IsBotSeat(1), Is.True);

        var (newSink, rejoin) = NewMember("손님", guest.UserId);
        room.Execute(new JoinCmd(rejoin));

        Assert.That(room.SessionForTest!.IsBotSeat(1), Is.False); // 봇 자리 되찾음
        Assert.That(newSink.Last<GameStartedMsg>().yourSeat, Is.EqualTo(1));
    }

    [Test]
    public void Stranger_still_rejected_while_playing()
    {
        var (room, _, _, _, _) = StartedRoom();

        var (lateSink, late) = NewMember("낯선이");
        room.Execute(new JoinCmd(late));

        Assert.That(lateSink.Last<ErrorMsg>().code, Is.EqualTo("room_playing"));
    }

    [Test]
    public void Reconnected_player_is_not_kicked_as_afk_at_round_end()
    {
        var (room, _, _, _, guest) = StartedRoom();
        room.Execute(new DisconnectCmd(guest.UserId));
        var (_, rejoin) = NewMember("손님", guest.UserId);
        room.Execute(new JoinCmd(rejoin));

        room.Execute(new DisconnectCmd(guest.UserId)); // 재이탈해도 방은 유지(다른 인원 있음)
        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Playing));
    }

    /// <summary>
    /// 재접속하려면 클라이언트가 방 코드를 알아야 한다. 모바일에서 앱을 잠깐 벗어나면
    /// 소켓이 끊기는데, 게임 화면은 그 값을 어디서도 받지 못하고 있었다.
    /// </summary>
    [Test]
    public void Game_start_tells_each_player_the_room_code()
    {
        var (room, hostSink, _, guestSink, _) = StartedRoom();

        foreach (var sink in new[] { hostSink, guestSink })
        {
            Assert.That(sink.Last<GameStartedMsg>().code, Is.EqualTo(room.Code));
        }
    }

    [Test]
    public void Rejoining_after_a_drop_tells_the_room_code_again()
    {
        var (room, _, _, _, guest) = StartedRoom();
        room.Execute(new DisconnectCmd(guest.UserId));

        var (freshSink, rejoin) = NewMember("손님", guest.UserId);
        room.Execute(new JoinCmd(rejoin));

        Assert.That(freshSink.Last<GameStartedMsg>().code, Is.EqualTo(room.Code));
        Assert.That(room.HasSeatFor(guest.UserId), Is.True);
    }

    /// <summary>
    /// 클라이언트는 서버가 옛 소켓의 죽음을 알아채기 전에 돌아올 수 있다(모바일에서 흔하다).
    /// 그때 "이미 방에 있습니다"로 막으면 영영 복귀하지 못한다 — 같은 방이면 재접속으로 처리해야 한다.
    /// </summary>
    [Test]
    public void Rejoining_the_same_room_before_the_drop_is_noticed_is_a_reconnect()
    {
        var (room, _, _, _, guest) = StartedRoom();

        // 끊김이 서버에 전달되지 않은 상태(DisconnectCmd 없음)에서 새 소켓으로 재입장
        var (freshSink, rejoin) = NewMember("손님", guest.UserId);
        room.Execute(new JoinCmd(rejoin));

        Assert.That(freshSink.SentOf<ErrorMsg>(), Is.Empty);
        Assert.That(freshSink.Last<GameStartedMsg>().code, Is.EqualTo(room.Code));
    }
}
