using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongCore.Online;
using BbongServer.Realtime;
using NUnit.Framework;

namespace BbongServer.Tests.Realtime;

/// <summary>맞춤게임 빠른매칭 — 조건(인원·입장료) 맞는 대기방 자동 배정, 없으면 생성, 정원 차면 자동 시작.</summary>
[TestFixture]
public class QuickMatchTests
{
    private sealed class NullBank : IStakeBank
    {
        public Task<bool> TryEscrowAsync(Guid userId, int stake) => Task.FromResult(true);
        public Task RefundAsync(Guid userId, int stake) => Task.CompletedTask;
        public Task PayoutAsync(Guid userId, long amount) => Task.CompletedTask;
    }

    private RoomRegistry _registry = null!;
    private NullBank _bank = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new RoomRegistry();
        _bank = new NullBank();
    }

    private static (FakeSessionSink sink, RoomMember member) NewMember(string nickname)
    {
        var sink = new FakeSessionSink(Guid.NewGuid());
        return (sink, new RoomMember(sink, sink.UserId, nickname));
    }

    [Test]
    public void First_matcher_creates_waiting_room_with_target()
    {
        var (sink, member) = NewMember("첫째");

        var room = _registry.QuickMatch(member, stake: 1000, players: 4, _bank, runLoop: false);

        Assert.That(room.Stake, Is.EqualTo(1000));
        Assert.That(room.TargetPlayers, Is.EqualTo(4));
        var update = sink.Last<RoomUpdateMsg>();
        Assert.That(update.targetPlayers, Is.EqualTo(4));
        Assert.That(update.members, Has.Length.EqualTo(1));
    }

    [Test]
    public void Same_condition_matcher_joins_existing_room()
    {
        var (_, first) = NewMember("첫째");
        var room1 = _registry.QuickMatch(first, 1000, 4, _bank, runLoop: false);
        var (sink2, second) = NewMember("둘째");

        var room2 = _registry.QuickMatch(second, 1000, 4, _bank, runLoop: false);

        Assert.That(room2, Is.SameAs(room1));
        Assert.That(sink2.Last<RoomUpdateMsg>().members, Has.Length.EqualTo(2));
    }

    [Test]
    public void Different_condition_gets_a_different_room()
    {
        var (_, first) = NewMember("첫째");
        var room1 = _registry.QuickMatch(first, 1000, 4, _bank, runLoop: false);

        var (_, otherStake) = NewMember("판돈다름");
        var (_, otherSize) = NewMember("인원다름");

        Assert.That(_registry.QuickMatch(otherStake, 5000, 4, _bank, runLoop: false), Is.Not.SameAs(room1));
        Assert.That(_registry.QuickMatch(otherSize, 1000, 2, _bank, runLoop: false), Is.Not.SameAs(room1));
    }

    [Test]
    public void Room_auto_starts_when_target_reached()
    {
        var (sink1, first) = NewMember("첫째");
        _registry.QuickMatch(first, 1000, 2, _bank, runLoop: false);
        var (sink2, second) = NewMember("둘째");

        var room = _registry.QuickMatch(second, 1000, 2, _bank, runLoop: false);

        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Playing)); // 정원 도달 → 자동 시작
        Assert.That(sink1.Last<GameStartedMsg>().yourSeat, Is.EqualTo(0));
        Assert.That(sink2.Last<GameStartedMsg>().yourSeat, Is.EqualTo(1));
        Assert.That(sink2.Last<GameStartedMsg>().stake, Is.EqualTo(1000));
    }

    [Test]
    public void Playing_room_is_not_matched_again()
    {
        var (_, a) = NewMember("A");
        _registry.QuickMatch(a, 1000, 2, _bank, runLoop: false);
        var (_, b) = NewMember("B");
        var full = _registry.QuickMatch(b, 1000, 2, _bank, runLoop: false); // 자동 시작됨

        var (sink3, c) = NewMember("C");
        var next = _registry.QuickMatch(c, 1000, 2, _bank, runLoop: false);

        Assert.That(next, Is.Not.SameAs(full)); // 진행 중 방 제외 — 새 대기방
        Assert.That(sink3.Last<RoomUpdateMsg>().members, Has.Length.EqualTo(1));
    }
}
