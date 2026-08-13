using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongCore.Online;
using BbongServer.Realtime;
using NUnit.Framework;

namespace BbongServer.Tests.Realtime;

/// <summary>맞춤게임(판돈 방) — 에스크로/환불/정산/방 폭파 (rules.md §9-1~9-3).</summary>
[TestFixture]
public class StakeRoomTests
{
    /// <summary>즉시 완료 Task로 기록만 남기는 은행 — 방 루프의 fire-and-forget 호출을 동기 검증.</summary>
    private sealed class FakeBank : IStakeBank
    {
        public readonly List<(Guid UserId, int Stake)> Escrows = new();
        public readonly List<(Guid UserId, int Stake)> Refunds = new();
        public readonly List<(Guid UserId, long Amount)> Payouts = new();
        public bool NextEscrowFails;

        public Task<bool> TryEscrowAsync(Guid userId, int stake, Guid? gameId = null)
        {
            if (NextEscrowFails)
            {
                return Task.FromResult(false);
            }

            Escrows.Add((userId, stake));
            return Task.FromResult(true);
        }

        public Task RefundAsync(Guid userId, int stake, Guid? gameId = null)
        {
            Refunds.Add((userId, stake));
            return Task.CompletedTask;
        }

        public Task PayoutAsync(Guid userId, long amount, Guid? gameId = null)
        {
            Payouts.Add((userId, amount));
            return Task.CompletedTask;
        }
    }

    private RoomRegistry _registry = null!;
    private FakeBank _bank = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new RoomRegistry();
        _bank = new FakeBank();
    }

    private static (FakeSessionSink sink, RoomMember member) NewMember(string nickname)
    {
        var sink = new FakeSessionSink(Guid.NewGuid());
        return (sink, new RoomMember(sink, sink.UserId, nickname));
    }

    /// <summary>판돈 1000 방 생성(방장 에스크로는 생성 전 WsEndpoint에서 완료된 상태를 가정).</summary>
    private (Room room, FakeSessionSink hostSink, RoomMember host) CreatedStakeRoom(int stake = 1000)
    {
        var (sink, member) = NewMember("호스트");
        var room = _registry.Create(member, runLoop: false, stake: stake, bank: _bank);
        return (room, sink, member);
    }

    private RoomMember Join(Room room, string nickname, out FakeSessionSink sink)
    {
        (sink, var member) = NewMember(nickname);
        room.Execute(new JoinCmd(member));
        return member;
    }

    [Test]
    public void Room_update_carries_stake()
    {
        var (_, hostSink, _) = CreatedStakeRoom(1000);

        Assert.That(hostSink.Last<RoomUpdateMsg>().stake, Is.EqualTo(1000));
    }

    [Test]
    public void Rejected_join_refunds_escrow()
    {
        var (room, _, _) = CreatedStakeRoom();
        for (var i = 0; i < 5; i++)
        {
            Join(room, $"손님{i}", out _);
        }

        var late = Join(room, "일곱째", out var lateSink); // 정원 초과 — 선 에스크로는 환불돼야 함

        Assert.That(lateSink.Last<ErrorMsg>().code, Is.EqualTo("room_full"));
        Assert.That(_bank.Refunds, Does.Contain((late.UserId, 1000)));
    }

    [Test]
    public void Leaving_waiting_room_refunds_escrow()
    {
        var (room, _, _) = CreatedStakeRoom();
        var guest = Join(room, "손님", out _);

        room.Execute(new LeaveCmd(guest.UserId));

        Assert.That(_bank.Refunds, Does.Contain((guest.UserId, 1000)));
    }

    [Test]
    public void Set_end_pays_winner_takes_all_and_closes_room()
    {
        var (room, hostSink, host) = CreatedStakeRoom(1000);
        var guest = Join(room, "손님", out _);
        room.Execute(new StartGameCmd(host.UserId));

        room.ForceSetEndForTest(winnerSeats: new[] { 0 }); // seat0 = 호스트 단독 우승

        Assert.That(_bank.Payouts, Does.Contain((host.UserId, 2000L))); // 판돈 × 사람 2
        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Closed));          // 정산 후 방 폭파(§8)
        Assert.That(hostSink.Last<RoomClosedMsg>().reason, Does.Contain("정산"));
    }

    [Test]
    public void Tied_winners_split_pot_with_floor()
    {
        var (room, _, host) = CreatedStakeRoom(500);
        var guest = Join(room, "손님", out _);
        var third = Join(room, "셋째", out _);
        room.Execute(new StartGameCmd(host.UserId));

        room.ForceSetEndForTest(winnerSeats: new[] { 0, 1 }); // 공동 1등 2명, 총 1500 → 750씩

        Assert.That(_bank.Payouts, Does.Contain((host.UserId, 750L)));
        Assert.That(_bank.Payouts, Does.Contain((guest.UserId, 750L)));
        Assert.That(_bank.Payouts.All(p => p.UserId != third.UserId), Is.True);
    }

    [Test]
    public void Mid_game_leaver_forfeits_stake()
    {
        var (room, _, host) = CreatedStakeRoom(1000);
        var guest = Join(room, "손님", out _);
        room.Execute(new StartGameCmd(host.UserId));

        room.Execute(new LeaveCmd(guest.UserId)); // 게임 중 자진 이탈 → 봇 대체 + 몰수(§9-4)
        room.ForceSetEndForTest(winnerSeats: new[] { 0 });

        Assert.That(_bank.Refunds, Is.Empty);                            // 환불 없음
        Assert.That(_bank.Payouts, Does.Contain((host.UserId, 2000L))); // 이탈자 판돈 포함 몰아주기
    }

    /// <summary>
    /// 매칭 정원을 채우는 위장 봇은 입장료를 내지 않는다. 그 몫까지 사람이 가져가면
    /// 판마다 포인트가 새로 만들어진다 — 봇도 우승 후보라야 기대값이 맞는다.
    /// </summary>
    private (Room room, RoomMember host) QuickMatchRoomWithFillBots(int stake, int players, int bots)
    {
        var (_, member) = NewMember("호스트");
        var room = _registry.QuickMatch(member, stake, players, _bank, runLoop: false);
        for (var i = 0; i < bots; i++)
        {
            room.Execute(new FillBotCmd(room.FillTokenForTest));
        }

        room.Execute(new StartCountdownCmd(room.CountdownTokenForTest));
        return (room, member);
    }

    [Test]
    public void Bot_winner_keeps_its_share_out_of_a_human_pocket()
    {
        var (room, _) = QuickMatchRoomWithFillBots(stake: 1000, players: 2, bots: 1);

        room.ForceSetEndForTest(winnerSeats: new[] { 1 }); // seat1 = 위장 봇 단독 우승

        Assert.That(_bank.Payouts, Is.Empty); // 봇 몫은 소멸 — 사람에게 흘러가지 않는다
    }

    [Test]
    public void Tie_with_a_bot_pays_the_human_only_its_own_share()
    {
        var (room, host) = QuickMatchRoomWithFillBots(stake: 1000, players: 2, bots: 1);

        room.ForceSetEndForTest(winnerSeats: new[] { 0, 1 }); // 사람 + 봇 공동 1등

        Assert.That(_bank.Payouts, Does.Contain((host.UserId, 1000L))); // 2000 중 절반만
    }

    [Test]
    public void Human_winner_still_takes_the_whole_pot()
    {
        var (room, host) = QuickMatchRoomWithFillBots(stake: 1000, players: 2, bots: 1);

        room.ForceSetEndForTest(winnerSeats: new[] { 0 });

        Assert.That(_bank.Payouts, Does.Contain((host.UserId, 2000L)));
    }

    [Test]
    public void Free_room_never_touches_bank()
    {
        var (sink, member) = NewMember("호스트");
        var room = _registry.Create(member, runLoop: false); // stake 0 — 친구방
        var guest = Join(room, "손님", out _);
        room.Execute(new StartGameCmd(member.UserId));
        room.ForceSetEndForTest(winnerSeats: new[] { 0 });

        Assert.That(_bank.Escrows, Is.Empty);
        Assert.That(_bank.Refunds, Is.Empty);
        Assert.That(_bank.Payouts, Is.Empty);
        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Waiting)); // 무료방은 대기실 복귀 유지
    }
}
