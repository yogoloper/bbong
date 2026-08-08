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
    public void Room_counts_down_then_auto_starts_when_target_reached()
    {
        var (sink1, first) = NewMember("첫째");
        _registry.QuickMatch(first, 1000, 2, _bank, runLoop: false);
        var (sink2, second) = NewMember("둘째");

        var room = _registry.QuickMatch(second, 1000, 2, _bank, runLoop: false);

        Assert.That(sink1.Last<MatchStartingMsg>().seconds, Is.EqualTo(5)); // 정원 도달 → 5초 안내
        room.Execute(new StartCountdownCmd(room.CountdownTokenForTest));

        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Playing));
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
        var full = _registry.QuickMatch(b, 1000, 2, _bank, runLoop: false); // 정원 도달
        full.Execute(new StartCountdownCmd(full.CountdownTokenForTest));    // 카운트다운 만료 → 시작

        var (sink3, c) = NewMember("C");
        var next = _registry.QuickMatch(c, 1000, 2, _bank, runLoop: false);

        Assert.That(next, Is.Not.SameAs(full)); // 진행 중 방 제외 — 새 대기방
        Assert.That(sink3.Last<RoomUpdateMsg>().members, Has.Length.EqualTo(1));
    }

    // ── 위장 봇 충원 + 시작 카운트다운 ──

    private (Room room, FakeSessionSink sink, RoomMember member) QuickRoom(int players = 4, int stake = 1000)
    {
        var (sink, member) = NewMember("사람");
        var room = _registry.QuickMatch(member, stake, players, _bank, runLoop: false);
        return (room, sink, member);
    }

    [Test]
    public void Fill_bot_joins_disguised_as_a_user()
    {
        var (room, sink, _) = QuickRoom(players: 4);

        room.Execute(new FillBotCmd(room.FillTokenForTest));

        var update = sink.Last<RoomUpdateMsg>();
        Assert.That(update.members, Has.Length.EqualTo(2));
        var bot = update.members[1];
        Assert.That(bot.isBot, Is.False);                    // 유저처럼 보여야 함
        Assert.That(bot.nickname, Does.Not.Contain("봇"));
    }

    [Test]
    public void Full_room_counts_down_then_starts_with_hard_bots()
    {
        var (room, sink, _) = QuickRoom(players: 3);
        room.Execute(new FillBotCmd(room.FillTokenForTest));
        room.Execute(new FillBotCmd(room.FillTokenForTest)); // 3/3 — 정원 도달

        Assert.That(sink.Last<MatchStartingMsg>().seconds, Is.EqualTo(5)); // 카운트다운 안내
        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Waiting));            // 아직 시작 전

        room.Execute(new StartCountdownCmd(room.CountdownTokenForTest));

        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Playing));
        Assert.That(sink.Last<GameStartedMsg>().yourSeat, Is.EqualTo(0));
        Assert.That(room.SessionForTest!.IsBotSeat(1), Is.True);  // 위장 봇 좌석
        Assert.That(room.SessionForTest!.IsBotSeat(2), Is.True);
        Assert.That(room.SessionForTest!.BotDifficultyForTest, Is.EqualTo(BbongCore.Ai.BotDifficulty.Hard));
        Assert.That(sink.Last<GameStartedMsg>().nicknames.Count(n => n.Contains("봇")), Is.EqualTo(0));
    }

    [Test]
    public void Leaving_during_countdown_cancels_start()
    {
        var (_, host) = NewMember("호스트");
        var room = _registry.QuickMatch(host, 1000, 2, _bank, runLoop: false);
        var (_, second) = NewMember("둘째");
        _registry.QuickMatch(second, 1000, 2, _bank, runLoop: false); // 2/2 — 카운트다운 진입
        var token = room.CountdownTokenForTest;
        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Waiting));

        room.Execute(new LeaveCmd(second.UserId));   // 카운트다운 중 이탈
        room.Execute(new StartCountdownCmd(token));  // 예약돼 있던 시작 타이머는 무효

        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Waiting)); // 시작 취소 — 다시 대기
    }

    [Test]
    public void Fill_bots_contribute_house_money_to_the_pot()
    {
        var records = new List<(Guid, long)>();
        var bank = new RecordingBank(records);
        var (sink, member) = NewMember("사람");
        var room = _registry.QuickMatch(member, 1000, 4, bank, runLoop: false);
        room.Execute(new FillBotCmd(room.FillTokenForTest));
        room.Execute(new FillBotCmd(room.FillTokenForTest));
        room.Execute(new FillBotCmd(room.FillTokenForTest));
        room.Execute(new StartCountdownCmd(room.CountdownTokenForTest));

        room.ForceSetEndForTest(winnerSeats: new[] { 0 });

        Assert.That(records, Does.Contain((member.UserId, 4000L))); // 위장 봇 몫은 하우스 부담 — 총상금 그대로
    }

    [Test]
    public void Room_filled_by_fill_bots_is_not_matched()
    {
        var (_, first) = NewMember("첫째");
        var room = _registry.QuickMatch(first, 1000, 3, _bank, runLoop: false);
        room.Execute(new FillBotCmd(room.FillTokenForTest));
        room.Execute(new FillBotCmd(room.FillTokenForTest)); // 사람1+봇2 = 3/3(카운트다운 중)

        var (_, late) = NewMember("늦은이");
        var other = _registry.QuickMatch(late, 1000, 3, _bank, runLoop: false);

        Assert.That(other, Is.Not.SameAs(room)); // 점유 기준으로 만석 — 새 방
    }

    [Test]
    public void Racing_human_displaces_a_fill_bot()
    {
        var (sink, first) = NewMember("첫째");
        var room = _registry.QuickMatch(first, 1000, 3, _bank, runLoop: false);
        room.Execute(new FillBotCmd(room.FillTokenForTest));
        room.Execute(new FillBotCmd(room.FillTokenForTest)); // 3/3 — 카운트다운 진입

        var (lateSink, late) = NewMember("늦은이");
        room.Execute(new JoinCmd(late)); // 매칭 레이스로 늦게 도착한 사람

        var update = lateSink.Last<RoomUpdateMsg>();
        Assert.That(update.members, Has.Length.EqualTo(3));                       // 여전히 3/3
        Assert.That(update.members.Count(m => m.userId != ""), Is.EqualTo(2));    // 사람 2(봇 하나 방출)
        Assert.That(update.members.Any(m => m.nickname == "늦은이"), Is.True);
        Assert.That(room.Phase, Is.EqualTo(RoomPhase.Waiting));
        Assert.That(sink.Last<MatchStartingMsg>().seconds, Is.EqualTo(5));        // 카운트다운 재고지
    }

    private sealed class RecordingBank : IStakeBank
    {
        private readonly List<(Guid, long)> _payouts;
        public RecordingBank(List<(Guid, long)> payouts) => _payouts = payouts;
        public Task<bool> TryEscrowAsync(Guid userId, int stake) => Task.FromResult(true);
        public Task RefundAsync(Guid userId, int stake) => Task.CompletedTask;
        public Task PayoutAsync(Guid userId, long amount)
        {
            _payouts.Add((userId, amount));
            return Task.CompletedTask;
        }
    }
}
