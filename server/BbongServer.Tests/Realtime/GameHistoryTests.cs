using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongCore.Online;
using BbongServer.Realtime;
using NUnit.Framework;

namespace BbongServer.Tests.Realtime;

/// <summary>게임 히스토리 영구 기록 — 방이 세션 이벤트를 저장소로 흘려보낸다(CS/디버깅용).</summary>
[TestFixture]
public class GameHistoryTests
{
    private sealed class FakeHistory : IGameHistoryStore
    {
        public GameRecord? Created;
        public readonly List<HistoryEvent> Events = new();
        public GameCompletion? Completed;

        public Task CreateGameAsync(GameRecord game)
        {
            Created = game;
            return Task.CompletedTask;
        }

        public Task AppendEventsAsync(Guid gameId, IReadOnlyList<HistoryEvent> events)
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }

        public Task CompleteGameAsync(GameCompletion completion)
        {
            Completed = completion;
            return Task.CompletedTask;
        }
    }

    private RoomRegistry _registry = null!;
    private FakeHistory _history = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new RoomRegistry();
        _history = new FakeHistory();
    }

    private static (FakeSessionSink sink, RoomMember member) NewMember(string nickname)
    {
        var sink = new FakeSessionSink(Guid.NewGuid());
        return (sink, new RoomMember(sink, sink.UserId, nickname));
    }

    [Test]
    public void Room_records_game_events_and_completion()
    {
        var (_, host) = NewMember("호스트");
        var room = _registry.Create(host, runLoop: false, history: _history);
        var (_, guest) = NewMember("손님");
        room.Execute(new JoinCmd(guest));
        room.Execute(new StartGameCmd(host.UserId));

        Assert.That(_history.Created, Is.Not.Null);
        Assert.That(_history.Created!.Players, Has.Count.EqualTo(2));
        Assert.That(_history.Created.Players[0].UserId, Is.EqualTo(host.UserId));
        Assert.That(_history.Created.Players[0].IsBot, Is.False);
        Assert.That(_history.Events.Any(e => e.Type == "deal"), Is.True);  // 시작 직후 딜 스냅샷
        Assert.That(_history.Events.Any(e => e.Type == "draw"), Is.True);  // 선 자동 드로우

        room.ForceSetEndForTest(winnerSeats: new[] { 0 });

        Assert.That(_history.Completed, Is.Not.Null);
        Assert.That(_history.Completed!.WinnerSeats, Is.EqualTo(new[] { 0 }));
        Assert.That(_history.Completed.GameId, Is.EqualTo(_history.Created.Id));
    }

    [Test]
    public void Quick_match_records_disguised_bots_as_bots()
    {
        var bank = new NullBank();
        var (_, member) = NewMember("사람");
        var room = _registry.QuickMatch(member, 1000, 3, bank, runLoop: false, history: _history);
        room.Execute(new FillBotCmd(room.FillTokenForTest));
        room.Execute(new FillBotCmd(room.FillTokenForTest));
        room.Execute(new StartCountdownCmd(room.CountdownTokenForTest));

        Assert.That(_history.Created!.Stake, Is.EqualTo(1000));
        Assert.That(_history.Created.Players.Count(p => p.IsBot), Is.EqualTo(2)); // 위장 봇도 기록엔 봇으로
        Assert.That(_history.Created.Players.Single(p => !p.IsBot).UserId, Is.EqualTo(member.UserId));
    }

    private sealed class NullBank : IStakeBank
    {
        public Task<bool> TryEscrowAsync(Guid userId, int stake, Guid? gameId = null) => Task.FromResult(true);
        public Task RefundAsync(Guid userId, int stake, Guid? gameId = null) => Task.CompletedTask;
        public Task PayoutAsync(Guid userId, long amount, Guid? gameId = null) => Task.CompletedTask;
    }
}
