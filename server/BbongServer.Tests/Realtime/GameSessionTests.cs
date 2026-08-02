using System;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Online;
using BbongServer.Realtime;
using NUnit.Framework;

namespace BbongServer.Tests.Realtime;

[TestFixture]
public class GameSessionTests
{
    private static Card C(int n, CardColor color) => new(n, color);

    private static GameSession NewSession(int playerCount = 3) =>
        new(Enumerable.Range(0, playerCount).Select(i => $"P{i}").ToArray(), () => new SeededRandom(1));

    /// <summary>rigged 상태로 턴 진입까지 실행(내부 생성자 — InternalsVisibleTo).</summary>
    private static (GameSession session, SessionOutput output) Rigged(
        Player[] players, Card[] drawPile, Card[] discard, int currentSeat)
    {
        var session = NewSession(players.Length);
        var round = new RoundState(players, drawPile, discard, currentSeat, new SeededRandom(1));
        var output = session.RigRoundForTest(round);
        return (session, output);
    }

    private static T For<T>(SessionOutput output, int seat) =>
        output.Messages.Where(o => o.Seat == seat).Select(o => o.Message).OfType<T>().Last();

    private static bool HasMsg<T>(SessionOutput output) =>
        output.Messages.Any(o => o.Message is T);

    private static Player P(int seat, params Card[] cards) => new(seat, new Hand(cards));

    // ── ① 시작: 딜 + 선 자동 드로우 + 뷰 필터링 ──

    [Test]
    public void StartMatch_deals_and_auto_draws_for_dealer()
    {
        var session = NewSession(3);

        var output = session.StartMatch();

        var myView = For<RoundStartedMsg>(output, 0).view;
        Assert.That(myView.mySeat, Is.EqualTo(0));
        Assert.That(myView.seats, Has.Length.EqualTo(3));

        var drew = For<DrewCardMsg>(output, 0);
        Assert.That(drew.seat, Is.EqualTo(0)); // 첫 판 선 = 0, 자동 드로우
        Assert.That(drew.view.myHand, Has.Length.EqualTo(6)); // 5 + 드로우 1
        Assert.That(drew.view.phase, Is.EqualTo(RoundPhase.WaitingDiscard));
    }

    [Test]
    public void Views_hide_other_players_hands()
    {
        var session = NewSession(3);

        var output = session.StartMatch();

        var otherView = For<DrewCardMsg>(output, 1).view; // seat1 관점
        Assert.That(otherView.mySeat, Is.EqualTo(1));
        Assert.That(otherView.myHand, Has.Length.EqualTo(5)); // 자기 손패만
        Assert.That(otherView.seats[0].handCount, Is.EqualTo(6)); // 선은 장수만 공개
    }

    // ── 버림 가드 ──

    [Test]
    public void Discard_from_wrong_seat_is_rejected()
    {
        var (session, output) = Rigged(
            new[] { P(0, C(1, CardColor.Red), C(2, CardColor.Red)), P(1, C(3, CardColor.Red)) },
            drawPile: new[] { C(4, CardColor.Red), C(5, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        Assert.That(For<DrewCardMsg>(output, 0).view.phase, Is.EqualTo(RoundPhase.WaitingDiscard));

        var result = session.HandleAction(1, new DiscardMsg { card = CardDto.From(C(3, CardColor.Red)) });

        Assert.That(For<ErrorMsg>(result, 1).code, Is.EqualTo("not_your_turn"));
        Assert.That(HasMsg<DiscardedMsg>(result), Is.False);
    }

    [Test]
    public void Discard_of_card_not_in_hand_is_rejected()
    {
        var (session, _) = Rigged(
            new[] { P(0, C(1, CardColor.Red), C(2, CardColor.Red)), P(1, C(3, CardColor.Red)) },
            drawPile: new[] { C(4, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);

        var result = session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(12, CardColor.Blue)) });

        Assert.That(For<ErrorMsg>(result, 0).code, Is.EqualTo("invalid_card"));
    }

    // ── 버림 → 뽕 없음 → 다음 턴 ──

    [Test]
    public void Discard_without_pong_advances_turn()
    {
        var (session, _) = Rigged(
            new[]
            {
                P(0, C(1, CardColor.Red), C(2, CardColor.Red)),
                P(1, C(3, CardColor.Red), C(4, CardColor.Red))
            },
            drawPile: new[] { C(5, CardColor.Red), C(6, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);

        var result = session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(5, CardColor.Red)) });

        Assert.That(For<DiscardedMsg>(result, 1).card.number, Is.EqualTo(5));
        Assert.That(For<TurnBeganMsg>(result, 1).seat, Is.EqualTo(1)); // 다음 턴
        Assert.That(For<DrewCardMsg>(result, 1).view.myHand, Has.Length.EqualTo(3));
        Assert.That(result.Timers, Is.Empty);
    }

    // ── 뽕 창 ──

    private static (GameSession, SessionOutput) PongWindowScenario()
    {
        // seat0이 9를 버리면 seat1(9 두 장)이 뽕 가능. seat2는 불가.
        var (session, _) = Rigged(
            new[]
            {
                P(0, C(9, CardColor.Red), C(1, CardColor.Red)),
                P(1, C(9, CardColor.Green), C(9, CardColor.Yellow), C(2, CardColor.Red), C(3, CardColor.Red)),
                P(2, C(4, CardColor.Red), C(5, CardColor.Red))
            },
            drawPile: new[] { C(6, CardColor.Red), C(7, CardColor.Red), C(8, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);

        var output = session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });
        return (session, output);
    }

    [Test]
    public void Discard_opens_pong_window_with_personalized_flags_and_timer()
    {
        var (_, output) = PongWindowScenario();

        var opened = For<PongWindowOpenedMsg>(output, 1);
        Assert.That(opened.number, Is.EqualTo(9));
        Assert.That(opened.seconds, Is.EqualTo(RealtimeConfig.PongWindowSeconds));
        Assert.That(opened.view.canPong, Is.True);            // seat1만 가능
        Assert.That(For<PongWindowOpenedMsg>(output, 2).view.canPong, Is.False);
        Assert.That(HasMsg<TurnBeganMsg>(output), Is.False);      // 창 열려 있는 동안 턴 진행 없음

        var timer = output.Timers.Single();
        Assert.That(timer.Command, Is.InstanceOf<PongTimeoutCmd>());
        Assert.That(timer.DelayMs, Is.EqualTo(RealtimeConfig.PongWindowSeconds * 1000));
    }

    [Test]
    public void Pong_declare_then_extra_discard_resumes_turn()
    {
        var (session, _) = PongWindowScenario();

        var declared = session.HandleAction(1, new PongDeclareMsg());
        var ponged = For<PongedMsg>(declared, 2);
        Assert.That(ponged.seat, Is.EqualTo(1));
        Assert.That(ponged.laid.Select(c => c.number), Is.All.EqualTo(9));
        Assert.That(For<PongedMsg>(declared, 1).view.phase, Is.EqualTo(RoundPhase.WaitingPongDiscard));

        var tossed = session.HandleAction(1, new PongDiscardMsg { card = CardDto.From(C(2, CardColor.Red)) });
        Assert.That(For<DiscardedMsg>(tossed, 0).card.number, Is.EqualTo(2));
        Assert.That(For<TurnBeganMsg>(tossed, 0).seat, Is.EqualTo(2)); // 뽕 선언자(1) 다음 좌석
    }

    [Test]
    public void Pong_extra_discard_can_open_second_window()
    {
        // seat1 뽕 후 추가 버림(4)을 seat2(4 두 장)가 다시 뽕 가능
        var (session, _) = Rigged(
            new[]
            {
                P(0, C(9, CardColor.Red), C(1, CardColor.Red)),
                P(1, C(9, CardColor.Green), C(9, CardColor.Yellow), C(4, CardColor.Blue), C(3, CardColor.Red)),
                P(2, C(4, CardColor.Red), C(4, CardColor.Green), C(5, CardColor.Red))
            },
            drawPile: new[] { C(6, CardColor.Red), C(7, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });
        session.HandleAction(1, new PongDeclareMsg());

        var tossed = session.HandleAction(1, new PongDiscardMsg { card = CardDto.From(C(4, CardColor.Blue)) });

        var opened = For<PongWindowOpenedMsg>(tossed, 2);
        Assert.That(opened.number, Is.EqualTo(4));
        Assert.That(opened.view.canPong, Is.True);
    }

    [Test]
    public void All_pass_closes_window_and_resumes()
    {
        var (session, _) = PongWindowScenario();

        var result = session.HandleAction(1, new PongPassMsg());

        Assert.That(HasMsg<PongWindowClosedMsg>(result), Is.True);
        Assert.That(For<TurnBeganMsg>(result, 0).seat, Is.EqualTo(1)); // 코어는 버림 시점에 이미 턴 전진
    }

    [Test]
    public void Timeout_with_current_token_closes_window_but_stale_is_ignored()
    {
        var (session, output) = PongWindowScenario();
        var token = ((PongTimeoutCmd)output.Timers.Single().Command).Token;

        var stale = session.HandlePongTimeout(token - 1);
        Assert.That(stale.Messages, Is.Empty);

        var closed = session.HandlePongTimeout(token);
        Assert.That(HasMsg<PongWindowClosedMsg>(closed), Is.True);
        Assert.That(For<TurnBeganMsg>(closed, 0).seat, Is.EqualTo(1));
    }

    [Test]
    public void Late_pong_declare_after_close_is_rejected()
    {
        var (session, output) = PongWindowScenario();
        session.HandlePongTimeout(((PongTimeoutCmd)output.Timers.Single().Command).Token);

        var late = session.HandleAction(1, new PongDeclareMsg());

        Assert.That(For<ErrorMsg>(late, 1).code, Is.EqualTo("pong_too_late"));
    }

    // ── 두 번 뽕 손 털기 종료 ──

    [Test]
    public void Pong_with_two_card_hand_ends_round_by_hand_clear()
    {
        // seat1 손패 = 9,9 두 장뿐 → 뽕 즉시 손 소진, 버린 seat0 박 +20
        var (session, _) = Rigged(
            new[]
            {
                P(0, C(9, CardColor.Red), C(1, CardColor.Red), C(2, CardColor.Blue)),
                new Player(1, new Hand(new[] { C(9, CardColor.Green), C(9, CardColor.Yellow) }), PongCount: 1),
                P(2, C(4, CardColor.Red), C(5, CardColor.Red))
            },
            drawPile: new[] { C(6, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });

        var result = session.HandleAction(1, new PongDeclareMsg());

        var ended = For<RoundEndedMsg>(result, 0);
        Assert.That(ended.enderSeat, Is.EqualTo(1));
        Assert.That(ended.scores[1], Is.EqualTo(0));           // 손 턴 승자
        Assert.That(ended.scores[0], Is.GreaterThanOrEqualTo(20)); // 박 +20 포함
        Assert.That(ended.nextRoundInMs, Is.EqualTo(RealtimeConfig.NextRoundDelayMs));
        Assert.That(result.Timers.Any(t => t.Command is NextRoundCmd), Is.True);
    }

    [Test]
    public void Pong_discard_to_empty_hand_ends_round()
    {
        // seat1 손패 = 9,9,X → 뽕 후 X 버림 → 손 0장 종료
        var (session, _) = Rigged(
            new[]
            {
                P(0, C(9, CardColor.Red), C(1, CardColor.Red), C(2, CardColor.Blue)),
                new Player(1, new Hand(new[] { C(9, CardColor.Green), C(9, CardColor.Yellow), C(7, CardColor.Red) }), PongCount: 1),
                P(2, C(4, CardColor.Red), C(5, CardColor.Red))
            },
            drawPile: new[] { C(6, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });
        session.HandleAction(1, new PongDeclareMsg());

        var result = session.HandleAction(1, new PongDiscardMsg { card = CardDto.From(C(7, CardColor.Red)) });

        Assert.That(For<RoundEndedMsg>(result, 0).enderSeat, Is.EqualTo(1));
    }

    // ── 다음 판 자동 진행 ──

    [Test]
    public void Next_round_timer_starts_new_round_with_ender_as_dealer()
    {
        var (session, _) = Rigged(
            new[]
            {
                P(0, C(9, CardColor.Red), C(1, CardColor.Red), C(2, CardColor.Blue)),
                new Player(1, new Hand(new[] { C(9, CardColor.Green), C(9, CardColor.Yellow) }), PongCount: 1),
                P(2, C(4, CardColor.Red), C(5, CardColor.Red))
            },
            drawPile: new[] { C(6, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });
        var ended = session.HandleAction(1, new PongDeclareMsg());
        var token = ((NextRoundCmd)ended.Timers.Single(t => t.Command is NextRoundCmd).Command).Token;

        Assert.That(session.HandleNextRound(token - 1).Messages, Is.Empty); // stale 무시

        var next = session.HandleNextRound(token);
        var started = For<RoundStartedMsg>(next, 0);
        Assert.That(started.roundIndex, Is.EqualTo(1));
        Assert.That(started.dealerSeat, Is.EqualTo(1)); // 판 끝낸 사람이 선
        Assert.That(For<DrewCardMsg>(next, 1).seat, Is.EqualTo(1));
    }
}
