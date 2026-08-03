using System;
using System.Linq;
using BbongCore.Online;
using BbongServer.Realtime;
using NUnit.Framework;

namespace BbongServer.Tests.Realtime;

[TestFixture]
public class RoomRegistryTests
{
    private RoomRegistry _registry = null!;

    [SetUp]
    public void SetUp() => _registry = new RoomRegistry();

    private static (FakeSessionSink sink, RoomMember member) NewMember(string nickname)
    {
        var sink = new FakeSessionSink(Guid.NewGuid());
        return (sink, new RoomMember(sink, sink.UserId, nickname));
    }

    /// <summary>방 생성(루프 미가동) + 생성자 입장 처리까지 실행.</summary>
    private (Room room, FakeSessionSink hostSink, RoomMember host) CreatedRoom()
    {
        var (sink, member) = NewMember("호스트");
        var room = _registry.Create(member, runLoop: false);
        return (room, sink, member);
    }

    private static RoomMember Join(Room room, string nickname, out FakeSessionSink sink)
    {
        (sink, var member) = NewMember(nickname);
        room.Execute(new JoinCmd(member));
        return member;
    }

    // ── 생성/입장 ──

    [Test]
    public void Create_issues_code_and_updates_creator()
    {
        var (room, hostSink, host) = CreatedRoom();

        var update = hostSink.Last<RoomUpdateMsg>();
        Assert.That(update.code, Has.Length.EqualTo(6));
        Assert.That(update.hostUserId, Is.EqualTo(host.UserId.ToString()));
        Assert.That(update.members.Single().nickname, Is.EqualTo("호스트"));
        Assert.That(_registry.FindByUser(host.UserId), Is.SameAs(room));
    }

    [Test]
    public void Join_broadcasts_member_list_to_everyone()
    {
        var (room, hostSink, _) = CreatedRoom();

        var guest = Join(room, "손님", out var guestSink);

        Assert.That(hostSink.Last<RoomUpdateMsg>().members, Has.Length.EqualTo(2));
        Assert.That(guestSink.Last<RoomUpdateMsg>().members.Select(m => m.nickname), Does.Contain("호스트"));
        Assert.That(_registry.FindByUser(guest.UserId), Is.SameAs(room));
    }

    [Test]
    public void TryJoin_unknown_code_returns_false()
    {
        var (_, member) = NewMember("길잃음");

        Assert.That(_registry.TryJoin("000000", member), Is.False);
    }

    [Test]
    public void Join_rejected_when_room_full()
    {
        var (room, _, _) = CreatedRoom();
        for (var i = 0; i < 5; i++) // 호스트 포함 6명 채움
        {
            Join(room, $"손님{i}", out _);
        }

        Join(room, "일곱째", out var lateSink);

        Assert.That(lateSink.Last<ErrorMsg>().code, Is.EqualTo("room_full"));
        Assert.That(lateSink.SentOf<RoomUpdateMsg>(), Is.Empty);
    }

    [Test]
    public void Join_rejected_while_playing()
    {
        var (room, _, host) = CreatedRoom();
        Join(room, "손님", out _);
        room.Execute(new StartGameCmd(host.UserId));

        Join(room, "지각생", out var lateSink);

        Assert.That(lateSink.Last<ErrorMsg>().code, Is.EqualTo("room_playing"));
    }

    // ── 퇴장/해체 ──

    [Test]
    public void Guest_leave_updates_remaining_members()
    {
        var (room, hostSink, _) = CreatedRoom();
        var guest = Join(room, "손님", out _);

        room.Execute(new LeaveCmd(guest.UserId));

        Assert.That(hostSink.Last<RoomUpdateMsg>().members, Has.Length.EqualTo(1));
        Assert.That(_registry.FindByUser(guest.UserId), Is.Null);
    }

    [Test]
    public void Host_leave_hands_host_to_next_member()
    {
        var (room, _, host) = CreatedRoom();
        var guest = Join(room, "손님", out var guestSink);

        room.Execute(new LeaveCmd(host.UserId));

        var update = guestSink.Last<RoomUpdateMsg>();
        Assert.That(update.hostUserId, Is.EqualTo(guest.UserId.ToString())); // 방장 위임, 방 유지
        Assert.That(update.members.Single().nickname, Is.EqualTo("손님"));
        Assert.That(_registry.FindByUser(host.UserId), Is.Null);
        Assert.That(_registry.FindByUser(guest.UserId), Is.SameAs(room));
    }

    [Test]
    public void Last_member_leave_closes_room()
    {
        var (room, _, host) = CreatedRoom();

        room.Execute(new LeaveCmd(host.UserId));

        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Closed));
        Assert.That(_registry.FindByUser(host.UserId), Is.Null);
    }

    [Test]
    public void New_host_can_start_after_migration()
    {
        var (room, _, host) = CreatedRoom();
        var guest = Join(room, "손님", out var guestSink);
        Join(room, "손님2", out _);
        room.Execute(new LeaveCmd(host.UserId));

        room.Execute(new StartGameCmd(guest.UserId));

        Assert.That(guestSink.Last<GameStartedMsg>().yourSeat, Is.EqualTo(0)); // 새 방장이 시작 가능
    }

    [Test]
    public void Disconnect_while_waiting_behaves_like_leave()
    {
        var (room, hostSink, _) = CreatedRoom();
        var guest = Join(room, "손님", out _);

        room.Execute(new DisconnectCmd(guest.UserId));

        Assert.That(hostSink.Last<RoomUpdateMsg>().members, Has.Length.EqualTo(1));
    }

    [Test]
    public void Disconnect_while_playing_closes_room()
    {
        var (room, hostSink, host) = CreatedRoom();
        var guest = Join(room, "손님", out _);
        room.Execute(new StartGameCmd(host.UserId));

        room.Execute(new DisconnectCmd(guest.UserId));

        Assert.That(hostSink.Last<RoomClosedMsg>().reason, Is.Not.Empty);
    }

    // ── 게임 시작 ──

    [Test]
    public void Start_rejected_for_non_host()
    {
        var (room, _, _) = CreatedRoom();
        var guest = Join(room, "손님", out var guestSink);

        room.Execute(new StartGameCmd(guest.UserId));

        Assert.That(guestSink.Last<ErrorMsg>().code, Is.EqualTo("not_host"));
    }

    [Test]
    public void Start_rejected_with_single_member()
    {
        var (room, hostSink, host) = CreatedRoom();

        room.Execute(new StartGameCmd(host.UserId));

        Assert.That(hostSink.Last<ErrorMsg>().code, Is.EqualTo("not_enough_players"));
    }

    [Test]
    public void Start_assigns_seats_in_join_order()
    {
        var (room, hostSink, host) = CreatedRoom();
        Join(room, "손님", out var guestSink);

        room.Execute(new StartGameCmd(host.UserId));

        var hostStart = hostSink.Last<GameStartedMsg>();
        var guestStart = guestSink.Last<GameStartedMsg>();
        Assert.That(hostStart.yourSeat, Is.EqualTo(0));
        Assert.That(guestStart.yourSeat, Is.EqualTo(1));
        Assert.That(hostStart.playerCount, Is.EqualTo(2));
        Assert.That(hostStart.nicknames, Is.EqualTo(new[] { "호스트", "손님" }));
    }
}
