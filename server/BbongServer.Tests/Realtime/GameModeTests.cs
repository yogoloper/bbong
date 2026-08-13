using System;
using System.Linq;
using BbongServer.Realtime;
using NUnit.Framework;

namespace BbongServer.Tests.Realtime;

/// <summary>
/// 전적은 맞춤게임만 집계한다. 친구방은 상대를 직접 고를 수 있어 승패를 주고받기 쉽고,
/// 봇만 채워 이길 수도 있다. 그러려면 기록에 어느 모드였는지가 남아야 한다.
/// </summary>
[TestFixture]
public class GameModeTests
{
    private RoomRegistry _registry = null!;
    private FakeHistory _history = null!;

    private sealed class FakeHistory : IGameHistoryStore
    {
        public GameRecord? Created;
        public GameCompletion? Completed;

        public System.Threading.Tasks.Task CreateGameAsync(GameRecord game)
        {
            Created = game;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task AppendEventsAsync(Guid gameId,
            System.Collections.Generic.IReadOnlyList<HistoryEvent> events) =>
            System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task CompleteGameAsync(GameCompletion completion)
        {
            Completed = completion;
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private sealed class NullBank : IStakeBank
    {
        public System.Threading.Tasks.Task<bool> TryEscrowAsync(Guid userId, int stake, Guid? gameId = null) =>
            System.Threading.Tasks.Task.FromResult(true);
        public System.Threading.Tasks.Task RefundAsync(Guid userId, int stake, Guid? gameId = null) =>
            System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task PayoutAsync(Guid userId, long amount, Guid? gameId = null) =>
            System.Threading.Tasks.Task.CompletedTask;
    }

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
    public void A_friend_room_game_is_recorded_as_such()
    {
        var (_, host) = NewMember("호스트");
        var room = _registry.Create(host, runLoop: false, history: _history);
        var (_, guest) = NewMember("손님");
        room.Execute(new JoinCmd(guest));

        room.Execute(new StartGameCmd(host.UserId));

        Assert.That(_history.Created!.Mode, Is.EqualTo(GameMode.Friend));
    }

    [Test]
    public void A_quick_match_game_is_recorded_as_such()
    {
        var (_, member) = NewMember("사람");
        var room = _registry.QuickMatch(member, 1000, 2, new NullBank(), runLoop: false, history: _history);
        room.Execute(new FillBotCmd(room.FillTokenForTest));

        room.Execute(new StartCountdownCmd(room.CountdownTokenForTest));

        Assert.That(_history.Created!.Mode, Is.EqualTo(GameMode.QuickMatch));
    }

    /// <summary>승패는 기록 시점에 좌석별로 확정해 둔다 — 나중에 우승 좌석 CSV를 파싱하지 않도록.</summary>
    [Test]
    public void Completion_marks_who_won()
    {
        var (_, host) = NewMember("호스트");
        var room = _registry.Create(host, runLoop: false, history: _history);
        var (_, guest) = NewMember("손님");
        room.Execute(new JoinCmd(guest));
        room.Execute(new StartGameCmd(host.UserId));

        room.ForceSetEndForTest(winnerSeats: new[] { 1 });

        Assert.That(_history.Completed!.WinnerSeats, Is.EqualTo(new[] { 1 }));
    }
}
