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

    private static GameSession NewSession(int playerCount = 3, int setRounds = 5) =>
        new(Enumerable.Range(0, playerCount).Select(i => $"P{i}").ToArray(), () => new SeededRandom(1), setRounds);

    /// <summary>rigged 상태로 턴 진입까지 실행(내부 생성자 — InternalsVisibleTo).</summary>
    private static (GameSession session, SessionOutput output) Rigged(
        Player[] players, Card[] drawPile, Card[] discard, int currentSeat,
        int reshuffles = 0, int setRounds = 5)
    {
        var session = NewSession(players.Length, setRounds);
        var round = new RoundState(players, drawPile, discard, currentSeat, new SeededRandom(1), reshuffles);
        var output = session.RigRoundForTest(round);
        return (session, output);
    }

    private static T For<T>(SessionOutput output, int seat) =>
        output.Messages.Where(o => o.Seat is null || o.Seat == seat) // 브로드캐스트 포함
            .Select(o => o.Message).OfType<T>().Last();

    private static bool HasMsg<T>(SessionOutput output) =>
        output.Messages.Any(o => o.Message is T);

    private static Player P(int seat, params Card[] cards) => new(seat, new Hand(cards));

    /// <summary>턴 전환 간격 타이머를 꺼내 만료시켜 다음 턴 진입.</summary>
    private static SessionOutput AdvanceTurnGap(GameSession session, SessionOutput output)
    {
        var token = ((TurnGapCmd)output.Timers.Single(t => t.Command is TurnGapCmd).Command).Token;
        return session.HandleTurnGap(token);
    }

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

    // ── 버림 → 뽕 없음 → 턴 전환 간격(0.5초) → 다음 턴 ──

    private static (GameSession, SessionOutput) DiscardWithoutPongScenario()
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

        var output = session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(5, CardColor.Red)) });
        return (session, output);
    }

    [Test]
    public void Discard_without_pong_schedules_turn_gap()
    {
        var (_, result) = DiscardWithoutPongScenario();

        Assert.That(For<DiscardedMsg>(result, 1).card.number, Is.EqualTo(5));
        Assert.That(For<DiscardedMsg>(result, 1).view.phase, Is.EqualTo(RoundPhase.TurnGap));
        Assert.That(HasMsg<TurnBeganMsg>(result), Is.False); // 간격 동안 턴 진행 없음

        var timer = result.Timers.Single();
        Assert.That(timer.Command, Is.InstanceOf<TurnGapCmd>());
        Assert.That(timer.DelayMs, Is.EqualTo(RealtimeConfig.TurnGapMs));
    }

    [Test]
    public void Turn_gap_timeout_begins_next_turn()
    {
        var (session, result) = DiscardWithoutPongScenario();

        var next = AdvanceTurnGap(session, result);

        Assert.That(For<TurnBeganMsg>(next, 1).seat, Is.EqualTo(1));
        Assert.That(For<DrewCardMsg>(next, 1).view.myHand, Has.Length.EqualTo(3));
    }

    [Test]
    public void Stale_turn_gap_timeout_is_ignored()
    {
        var (session, result) = DiscardWithoutPongScenario();
        var token = ((TurnGapCmd)result.Timers.Single().Command).Token;

        Assert.That(session.HandleTurnGap(token - 1).Messages, Is.Empty);
    }

    [Test]
    public void Discard_during_turn_gap_is_rejected()
    {
        var (session, _) = DiscardWithoutPongScenario();

        var result = session.HandleAction(1, new DiscardMsg { card = CardDto.From(C(3, CardColor.Red)) });

        Assert.That(For<ErrorMsg>(result, 1).code, Is.EqualTo("invalid_phase"));
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

        var next = AdvanceTurnGap(session, tossed);
        Assert.That(For<TurnBeganMsg>(next, 0).seat, Is.EqualTo(2)); // 뽕 선언자(1) 다음 좌석
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
        Assert.That(HasMsg<TurnBeganMsg>(result), Is.False); // 창 닫힘 → 턴 전환 간격 먼저

        var next = AdvanceTurnGap(session, result);
        Assert.That(For<TurnBeganMsg>(next, 0).seat, Is.EqualTo(1)); // 코어는 버림 시점에 이미 턴 전진
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

        var next = AdvanceTurnGap(session, closed);
        Assert.That(For<TurnBeganMsg>(next, 0).seat, Is.EqualTo(1));
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

    // ── 스톱 / 계속 ──

    private static (GameSession, SessionOutput) StopScenario(int stopperSum, int rivalSum)
    {
        // 뽕 2명(0,1) — seat0 턴 시작 시 스톱 가능. 손패 합으로 바가지 여부 제어.
        var stopper = new Player(0, new Hand(new[] { C(stopperSum, CardColor.Red) }), PongCount: 1);
        var rival = new Player(1, new Hand(new[] { C(rivalSum, CardColor.Blue) }), PongCount: 1);
        var bystander = P(2, C(11, CardColor.Red), C(12, CardColor.Red));
        return Rigged(new[] { stopper, rival, bystander },
            drawPile: new[] { C(6, CardColor.Green) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
    }

    [Test]
    public void Turn_with_stop_available_waits_for_decision()
    {
        var (_, output) = StopScenario(stopperSum: 5, rivalSum: 8);

        var began = For<TurnBeganMsg>(output, 0);
        Assert.That(began.view.phase, Is.EqualTo(RoundPhase.WaitingStop));
        Assert.That(began.view.canStop, Is.True);
        Assert.That(For<TurnBeganMsg>(output, 1).view.canStop, Is.False);
        Assert.That(HasMsg<DrewCardMsg>(output), Is.False); // 결정 전 드로우 없음
    }

    [Test]
    public void Stop_declare_ends_round_with_stopper_as_ender()
    {
        var (session, _) = StopScenario(stopperSum: 5, rivalSum: 8);

        var result = session.HandleAction(0, new StopDeclareMsg());

        var declared = For<StopDeclaredMsg>(result, 1);
        Assert.That(declared.bagaji, Is.False);
        Assert.That(declared.laidSeat, Is.EqualTo(0)); // 정상 스톱 → 선언자 손패 공개
        Assert.That(declared.laid.Select(c => c.ToCard()), Is.EqualTo(new[] { C(5, CardColor.Red) }));
        Assert.That(For<RoundEndedMsg>(result, 0).enderSeat, Is.EqualTo(0));
    }

    [Test]
    public void Stop_bagaji_makes_lowest_ponged_hand_the_ender()
    {
        var (session, _) = StopScenario(stopperSum: 8, rivalSum: 3); // 더 낮은 뽕 손패 존재 → 바가지

        var result = session.HandleAction(0, new StopDeclareMsg());

        var declared = For<StopDeclaredMsg>(result, 1);
        Assert.That(declared.bagaji, Is.True);
        Assert.That(declared.laidSeat, Is.EqualTo(1)); // 바가지 → 박 먹인 승자 손패 공개
        Assert.That(declared.laid.Select(c => c.ToCard()), Is.EqualTo(new[] { C(3, CardColor.Blue) }));
        Assert.That(For<RoundEndedMsg>(result, 0).enderSeat, Is.EqualTo(1));
    }

    [Test]
    public void Continue_turn_proceeds_to_auto_draw()
    {
        var (session, _) = StopScenario(stopperSum: 5, rivalSum: 8);

        var result = session.HandleAction(0, new ContinueTurnMsg());

        Assert.That(For<DrewCardMsg>(result, 0).view.phase, Is.EqualTo(RoundPhase.WaitingDiscard));
    }

    // ── 족보 ──

    [Test]
    public void Meld_declare_ends_round()
    {
        // 드로우 후 1,1,2,2,3,3 = 또이또이
        var (session, output) = Rigged(
            new[]
            {
                P(0, C(1, CardColor.Red), C(1, CardColor.Blue), C(2, CardColor.Red), C(2, CardColor.Blue), C(3, CardColor.Red)),
                P(1, C(7, CardColor.Red), C(8, CardColor.Red))
            },
            drawPile: new[] { C(3, CardColor.Blue) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        Assert.That(For<DrewCardMsg>(output, 0).view.canMeld, Is.True);

        var result = session.HandleAction(0, new MeldDeclareMsg());

        var declared = For<MeldDeclaredMsg>(result, 1);
        Assert.That(declared.seat, Is.EqualTo(0));
        Assert.That(declared.laid, Has.Length.EqualTo(6)); // 족보 6장 전부 공개(테이블 펼침 연출용)
        Assert.That(For<RoundEndedMsg>(result, 0).enderSeat, Is.EqualTo(0));
    }

    // ── 자연뽕 ──

    [Test]
    public void Natural_pong_with_extra_discard_continues_round()
    {
        // 드로우 후 9 세 장 + 2,3 → 자연뽕 선언, 2 추가 버림
        var (session, output) = Rigged(
            new[]
            {
                P(0, C(9, CardColor.Red), C(9, CardColor.Green), C(2, CardColor.Red), C(3, CardColor.Red)),
                P(1, C(7, CardColor.Red), C(8, CardColor.Red))
            },
            drawPile: new[] { C(9, CardColor.Yellow), C(5, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        Assert.That(For<DrewCardMsg>(output, 0).view.canNaturalPong, Is.True);

        var result = session.HandleAction(0, new NaturalPongMsg { hasDiscard = true, card = CardDto.From(C(2, CardColor.Red)) });

        Assert.That(For<NaturalPongedMsg>(result, 1).laid, Has.Length.EqualTo(3));
        Assert.That(For<DiscardedMsg>(result, 1).card.number, Is.EqualTo(2));

        var next = AdvanceTurnGap(session, result);
        Assert.That(For<TurnBeganMsg>(next, 1).seat, Is.EqualTo(1)); // 다음 턴
    }

    [Test]
    public void Natural_pong_hand_clear_ends_round()
    {
        // 뽕 후 2장 + 드로우 = 3장 전부 같은 숫자 → 손 소진 종료
        var (session, _) = Rigged(
            new[]
            {
                new Player(0, new Hand(new[] { C(9, CardColor.Red), C(9, CardColor.Green) }), PongCount: 1),
                P(1, C(7, CardColor.Red), C(8, CardColor.Red))
            },
            drawPile: new[] { C(9, CardColor.Yellow) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);

        var result = session.HandleAction(0, new NaturalPongMsg { hasDiscard = false });

        Assert.That(For<NaturalPongedMsg>(result, 1).seat, Is.EqualTo(0));
        Assert.That(For<RoundEndedMsg>(result, 0).enderSeat, Is.EqualTo(0));
    }

    // ── 더미 소진 ──

    [Test]
    public void Exhausted_draw_pile_force_ends_round()
    {
        var (_, output) = Rigged(
            new[]
            {
                P(0, C(1, CardColor.Red), C(2, CardColor.Red)),
                P(1, C(3, CardColor.Red), C(4, CardColor.Red))
            },
            drawPile: Array.Empty<Card>(),
            discard: new[] { C(5, CardColor.Red) },
            currentSeat: 0,
            reshuffles: 2); // 재셔플 한도 소진 → CanDraw false

        Assert.That(For<RoundEndedMsg>(output, 0).reason, Does.Contain("소진"));
    }

    // ── 세트 종료 ──

    [Test]
    public void Last_round_end_emits_set_ended_with_winners()
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
            currentSeat: 0,
            setRounds: 1); // 이 판이 마지막
        session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });

        var result = session.HandleAction(1, new PongDeclareMsg());

        var ended = result.Messages.Select(o => o.Message).OfType<SetEndedMsg>().Single();
        Assert.That(ended.winnerSeats, Does.Contain(1)); // 손 턴 승자(빚 0)
        Assert.That(result.Timers.Any(t => t.Command is NextRoundCmd), Is.False); // 다음 판 없음
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

    // ── 턴 타이머 (rules.md §3: 5초 미행동 → 자동 진행) ──

    private static int TurnTimerToken(SessionOutput output) =>
        ((TurnTimeoutCmd)output.Timers.Last(t => t.Command is TurnTimeoutCmd).Command).Token;

    [Test]
    public void Turn_timeout_auto_discards_drawn_card()
    {
        var original = new[] { C(1, CardColor.Red), C(2, CardColor.Red), C(3, CardColor.Blue), C(4, CardColor.Red), C(5, CardColor.Blue) };
        var (session, output) = Rigged(
            new[] { P(0, original), P(1, C(11, CardColor.Red), C(12, CardColor.Red)) },
            drawPile: new[] { C(9, CardColor.Red), C(6, CardColor.Green) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        var drew = For<DrewCardMsg>(output, 0);
        var drawn = drew.view.myHand.Select(c => c.ToCard()).Except(original).Single();

        var result = session.HandleTurnTimeout(TurnTimerToken(output));

        var discarded = For<DiscardedMsg>(result, 0);
        Assert.That(discarded.seat, Is.EqualTo(0));
        Assert.That(discarded.card.ToCard(), Is.EqualTo(drawn)); // 방금 드로우한 카드 자동 버림
    }

    [Test]
    public void Turn_timeout_on_stop_decision_auto_continues()
    {
        var (session, output) = StopScenario(stopperSum: 5, rivalSum: 8);
        Assert.That(For<TurnBeganMsg>(output, 0).view.phase, Is.EqualTo(RoundPhase.WaitingStop));

        var result = session.HandleTurnTimeout(TurnTimerToken(output));

        Assert.That(For<DrewCardMsg>(result, 0).view.phase, Is.EqualTo(RoundPhase.WaitingDiscard)); // 자동 계속
    }

    [Test]
    public void Turn_timeout_after_pong_declare_auto_discards()
    {
        var (session, _) = Rigged(
            new[]
            {
                P(0, C(9, CardColor.Red), C(1, CardColor.Red), C(2, CardColor.Blue)),
                P(1, C(9, CardColor.Green), C(9, CardColor.Yellow), C(7, CardColor.Red), C(8, CardColor.Red)),
                P(2, C(4, CardColor.Red), C(5, CardColor.Red))
            },
            drawPile: new[] { C(6, CardColor.Red), C(10, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });
        var ponged = session.HandleAction(1, new PongDeclareMsg());
        Assert.That(For<PongedMsg>(ponged, 0).view.phase, Is.EqualTo(RoundPhase.WaitingPongDiscard));

        var result = session.HandleTurnTimeout(TurnTimerToken(ponged));

        var discarded = For<DiscardedMsg>(result, 1);
        Assert.That(discarded.seat, Is.EqualTo(1)); // 내려놓은 뽕 카드 제외 자동 버림
        Assert.That(discarded.card.ToCard().Number, Is.Not.EqualTo(9));
    }

    // ── 이탈/AFK 봇 대체 (rules.md §9-4) ──

    private static int BotActToken(SessionOutput output) =>
        ((BotActCmd)output.Timers.Last(t => t.Command is BotActCmd).Command).Token;

    [Test]
    public void Silent_seat_with_timeout_becomes_bot_at_round_end()
    {
        var (session, output) = Rigged(
            new[]
            {
                P(0, C(1, CardColor.Red), C(2, CardColor.Red), C(3, CardColor.Red)),
                new Player(1, new Hand(new[] { C(9, CardColor.Green), C(9, CardColor.Yellow) }), PongCount: 1)
            },
            drawPile: new[] { C(9, CardColor.Red), C(9, CardColor.Blue) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        // seat0 무입력 5초 → 드로우한 9 자동 버림(타임아웃 기록)
        var afterTimeout = session.HandleTurnTimeout(TurnTimerToken(output));
        Assert.That(For<DiscardedMsg>(afterTimeout, 0).card.ToCard().Number, Is.EqualTo(9));

        // seat1 뽕 손 털기로 판 종료 → 무입력+타임아웃 좌석 0 봇 전환
        var ended = session.HandleAction(1, new PongDeclareMsg());

        Assert.That(HasMsg<RoundEndedMsg>(ended), Is.True);
        var bot = For<BotTookOverMsg>(ended, 0);
        Assert.That(bot.seat, Is.EqualTo(0));
        Assert.That(bot.nickname, Is.EqualTo("P0")); // 이탈해도 게임 끝까지 원래 닉네임 유지
    }

    [Test]
    public void Acting_seat_is_not_replaced_by_bot()
    {
        var (session, _) = Rigged(
            new[]
            {
                P(0, C(1, CardColor.Red), C(2, CardColor.Red), C(3, CardColor.Red)),
                new Player(1, new Hand(new[] { C(9, CardColor.Green), C(9, CardColor.Yellow) }), PongCount: 1)
            },
            drawPile: new[] { C(9, CardColor.Red), C(9, CardColor.Blue) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        // seat0이 직접 버림(입력 있음) → 판이 끝나도 봇 전환 없음
        session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });

        var ended = session.HandleAction(1, new PongDeclareMsg());

        Assert.That(HasMsg<RoundEndedMsg>(ended), Is.True);
        Assert.That(HasMsg<BotTookOverMsg>(ended), Is.False);
    }

    [Test]
    public void Bot_seat_auto_discards_on_its_turn()
    {
        var session = NewSession(2);
        session.BotifyForTest(0);
        var round = new RoundState(
            new[] { P(0, C(1, CardColor.Red), C(2, CardColor.Red), C(3, CardColor.Blue)), P(1, C(11, CardColor.Red), C(12, CardColor.Red)) },
            new[] { C(5, CardColor.Green), C(6, CardColor.Red) },
            Array.Empty<Card>(), currentSeat: 0, new SeededRandom(1), 0);

        var output = session.RigRoundForTest(round);

        Assert.That(output.Timers.Any(t => t.Command is TurnTimeoutCmd), Is.False); // 봇에겐 5초 타이머 없음
        var result = session.HandleBotAct(BotActToken(output));
        Assert.That(For<DiscardedMsg>(result, 1).seat, Is.EqualTo(0)); // 봇이 알아서 버림
    }

    [Test]
    public void Set_winners_exclude_bot_seats()
    {
        var session = NewSession(2, setRounds: 1);
        session.BotifyForTest(0);
        var round = new RoundState(
            new[]
            {
                new Player(0, new Hand(new[] { C(9, CardColor.Red), C(9, CardColor.Green) }), PongCount: 1),
                P(1, C(9, CardColor.Yellow), C(1, CardColor.Red), C(2, CardColor.Red))
            },
            new[] { C(6, CardColor.Red), C(10, CardColor.Red) },
            Array.Empty<Card>(), currentSeat: 1, new SeededRandom(1), 0);
        session.RigRoundForTest(round);

        // seat1이 9 버림 → 봇(0)이 뽕 손 털기로 최저 빚이 되어도 우승 후보 제외
        var discarded = session.HandleAction(1, new DiscardMsg { card = CardDto.From(C(9, CardColor.Yellow)) });
        var set = session.HandleBotAct(BotActToken(discarded));

        var endMsg = For<SetEndedMsg>(set, 1);
        Assert.That(endMsg.winnerSeats, Does.Not.Contain(0));
        Assert.That(endMsg.winnerSeats, Does.Contain(1));
    }

    [Test]
    public void Voluntary_leave_hands_seat_to_bot_immediately()
    {
        // seat0이 버림 대기 중 나가기 → 즉시 봇 전환 + 봇 행동 예약 → 봇이 알아서 버림
        var (session, _) = Rigged(
            new[] { P(0, C(1, CardColor.Red), C(2, CardColor.Red), C(3, CardColor.Blue)), P(1, C(11, CardColor.Red), C(12, CardColor.Red)) },
            drawPile: new[] { C(5, CardColor.Green), C(6, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);

        var replaced = session.ReplaceSeatWithBot(0);

        var took = For<BotTookOverMsg>(replaced, 1);
        Assert.That(took.seat, Is.EqualTo(0));
        Assert.That(took.nickname, Is.EqualTo("P0")); // 이탈해도 게임 끝까지 원래 닉네임 유지

        var acted = session.HandleBotAct(BotActToken(replaced));
        Assert.That(For<DiscardedMsg>(acted, 1).seat, Is.EqualTo(0));
    }

    [Test]
    public void Initial_bot_seat_acts_automatically_and_rejects_player_input()
    {
        // 대기실에서 추가된 봇 좌석(ctor botSeats) — 자기 턴에 봇 타이머, 사람 입력은 거부
        var session = new GameSession(new[] { "P0", "너구리 (봇)" }, () => new SeededRandom(1), botSeats: new[] { 1 });
        var round = new RoundState(
            new[] { P(0, C(1, CardColor.Red), C(2, CardColor.Red)), P(1, C(11, CardColor.Red), C(12, CardColor.Red), C(5, CardColor.Blue)) },
            new[] { C(9, CardColor.Green), C(6, CardColor.Yellow) },
            Array.Empty<Card>(),
            currentSeat: 1,
            new SeededRandom(1));
        var output = session.RigRoundForTest(round);

        Assert.That(output.Timers.Any(t => t.Command is BotActCmd), Is.True); // 사람용 5초 타이머 대신 봇 예약
        Assert.That(HasMsg<BotTookOverMsg>(output), Is.False); // 교대가 아니므로 안내 없음

        var rejected = session.HandleAction(1, new DiscardMsg { card = CardDto.From(C(11, CardColor.Red)) });
        Assert.That(For<ErrorMsg>(rejected, 1).code, Is.EqualTo("seat_replaced"));

        var acted = session.HandleBotAct(BotActToken(output));
        Assert.That(For<DiscardedMsg>(acted, 0).seat, Is.EqualTo(1)); // 봇이 알아서 버림
    }

    // ── 뽕 + 자연뽕 손 소진 (뽕 바가지) ──

    /// <summary>seat0가 2를 버림. seat1 손패 = 2,2,5,5,5 → 뽕 후 5,5,5 자연뽕으로 손 소진 가능.</summary>
    private static (GameSession, SessionOutput) PongClearScenario()
    {
        var (session, _) = Rigged(
            new[]
            {
                P(0, C(2, CardColor.Red), C(9, CardColor.Red), C(10, CardColor.Red)),
                P(1, C(2, CardColor.Green), C(2, CardColor.Yellow), C(5, CardColor.Red), C(5, CardColor.Green), C(5, CardColor.Blue))
            },
            drawPile: new[] { C(7, CardColor.Red), C(8, CardColor.Red) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        var output = session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(2, CardColor.Red)) });
        return (session, output);
    }

    [Test]
    public void Pong_then_natural_pong_clears_hand_and_bags_discarder()
    {
        var (session, _) = PongClearScenario();
        var ponged = session.HandleAction(1, new PongDeclareMsg());
        Assert.That(For<PongedMsg>(ponged, 0).view.canNaturalPong, Is.False); // 상대 뷰엔 아님
        Assert.That(For<PongedMsg>(ponged, 1).view.canNaturalPong, Is.True);  // 선언자만 자연뽕 제안

        var cleared = session.HandleAction(1, new NaturalPongMsg()); // 토스 대신 5,5,5 자연뽕

        Assert.That(For<NaturalPongedMsg>(cleared, 0).seat, Is.EqualTo(1));
        var ended = For<RoundEndedMsg>(cleared, 0);
        Assert.That(ended.reason, Is.EqualTo("P0 - 뽕 바가지")); // 2를 버린 seat0이 바가지
        Assert.That(ended.scores[0], Is.GreaterThan(0));         // 박 벌점
        Assert.That(ended.scores[1], Is.EqualTo(0));             // 손 소진 승자
    }

    [Test]
    public void Bot_pong_seat_auto_clears_when_possible()
    {
        var (session, _) = PongClearScenario();
        var replaced = session.ReplaceSeatWithBot(1);
        var ponged = session.HandleBotAct(BotActToken(replaced)); // 봇이 뽕 선언
        Assert.That(HasMsg<PongedMsg>(ponged), Is.True);

        var acted = session.HandleBotAct(BotActToken(ponged)); // 봇 추가 행동 = 자연뽕 손 소진

        Assert.That(For<RoundEndedMsg>(acted, 0).reason, Is.EqualTo("P0 - 뽕 바가지"));
    }

    [Test]
    public void Pong_declarer_hand_count_drops_immediately_in_views()
    {
        var (session, _) = PongClearScenario(); // seat0이 2 버림, seat1 = 2,2,5,5,5
        var ponged = session.HandleAction(1, new PongDeclareMsg());

        // 코어 반영 전(추가 버림 대기)이라도 내려놓은 2장을 뺀 3장으로 보여야 한다
        Assert.That(For<PongedMsg>(ponged, 0).view.seats[1].handCount, Is.EqualTo(3));
    }

    [Test]
    public void Bot_natural_pong_lays_first_and_discards_on_next_beat()
    {
        // 봇 seat0: 드로우 후 4,4,4,7,8,9 — 자연뽕 가능
        var session = new GameSession(new[] { "봇닉", "P1" }, () => new SeededRandom(1), botSeats: new[] { 0 });
        var round = new RoundState(
            new[]
            {
                P(0, C(4, CardColor.Red), C(4, CardColor.Green), C(7, CardColor.Red), C(8, CardColor.Red), C(9, CardColor.Red)),
                P(1, C(1, CardColor.Red), C(2, CardColor.Red))
            },
            new[] { C(4, CardColor.Blue), C(6, CardColor.Red) },
            Array.Empty<Card>(),
            currentSeat: 0,
            new SeededRandom(1));
        var rig = session.RigRoundForTest(round); // 자동 드로우로 4B 획득 → 트리플

        var laid = session.HandleBotAct(BotActToken(rig));

        Assert.That(HasMsg<NaturalPongedMsg>(laid), Is.True);
        Assert.That(HasMsg<DiscardedMsg>(laid), Is.False); // 내려놓기와 버림이 한 번에 나가면 안 됨
        Assert.That(laid.Timers.Any(t => t.Command is BotActCmd), Is.True);

        var tossed = session.HandleBotAct(BotActToken(laid));
        Assert.That(HasMsg<DiscardedMsg>(tossed), Is.True); // 한 박자 뒤 버림
    }

    [Test]
    public void Bot_waits_while_human_pong_window_is_open()
    {
        // seat0 버림 9 → 사람(1)과 봇(2) 모두 뽕 가능. 봇은 사람의 5초 창을 가로채면 안 됨.
        var session = new GameSession(new[] { "P0", "P1", "너구리 봇" }, () => new SeededRandom(1), botSeats: new[] { 2 });
        var round = new RoundState(
            new[]
            {
                P(0, C(9, CardColor.Red), C(1, CardColor.Red)),
                P(1, C(9, CardColor.Green), C(9, CardColor.Yellow), C(2, CardColor.Red), C(3, CardColor.Red)),
                P(2, C(9, CardColor.Blue), C(9, CardColor.Red), C(4, CardColor.Red), C(5, CardColor.Red))
            },
            new[] { C(6, CardColor.Red), C(7, CardColor.Red), C(8, CardColor.Red) },
            Array.Empty<Card>(),
            currentSeat: 0,
            new SeededRandom(1));
        session.RigRoundForTest(round);
        var opened = session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });

        var acted = session.HandleBotAct(BotActToken(opened));
        Assert.That(HasMsg<PongedMsg>(acted), Is.False); // 사람 창이 살아있는 동안 봇은 대기

        // 사람이 5초 내 미선언 → 창 만료 시점에 봇이 기회를 가져감
        var token = ((PongTimeoutCmd)opened.Timers.Single(t => t.Command is PongTimeoutCmd).Command).Token;
        var timedOut = session.HandlePongTimeout(token);
        Assert.That(For<PongedMsg>(timedOut, 0).seat, Is.EqualTo(2));
    }

    [Test]
    public void Bot_gets_pong_chance_after_human_passes()
    {
        var session = new GameSession(new[] { "P0", "P1", "너구리 봇" }, () => new SeededRandom(1), botSeats: new[] { 2 });
        var round = new RoundState(
            new[]
            {
                P(0, C(9, CardColor.Red), C(1, CardColor.Red)),
                P(1, C(9, CardColor.Green), C(9, CardColor.Yellow), C(2, CardColor.Red), C(3, CardColor.Red)),
                P(2, C(9, CardColor.Blue), C(9, CardColor.Red), C(4, CardColor.Red), C(5, CardColor.Red))
            },
            new[] { C(6, CardColor.Red), C(7, CardColor.Red), C(8, CardColor.Red) },
            Array.Empty<Card>(),
            currentSeat: 0,
            new SeededRandom(1));
        session.RigRoundForTest(round);
        session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });

        var passed = session.HandleAction(1, new PongPassMsg()); // 마지막 사람 패스 → 봇 결정 예약
        Assert.That(passed.Timers.Any(t => t.Command is BotActCmd), Is.True);

        var acted = session.HandleBotAct(BotActToken(passed));
        Assert.That(For<PongedMsg>(acted, 0).seat, Is.EqualTo(2));
    }

    [Test]
    public void Reshuffle_extends_turn_timer_by_fx_duration()
    {
        // 바닥 0장 + 버림 있음 → 다음 턴 드로우가 재셔플 → 사람 턴 타이머는 연출 시간만큼 가산
        var (session, _) = Rigged(
            new[] { P(0, C(1, CardColor.Red), C(2, CardColor.Red)), P(1, C(11, CardColor.Red), C(12, CardColor.Red)) },
            drawPile: new[] { C(9, CardColor.Red) },
            discard: new[] { C(5, CardColor.Blue), C(6, CardColor.Green) },
            currentSeat: 0);

        var discarded = session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(9, CardColor.Red)) });
        var next = AdvanceTurnGap(session, discarded); // seat1 턴 — 바닥 0 → 재셔플 드로우

        var timer = next.Timers.Single(t => t.Command is TurnTimeoutCmd);
        Assert.That(timer.DelayMs, Is.EqualTo(RealtimeConfig.TurnTimerSeconds * 1000 + RealtimeConfig.ReshuffleFxMs));
    }

    [Test]
    public void Stale_turn_timeout_is_ignored()
    {
        var (session, output) = Rigged(
            new[] { P(0, C(1, CardColor.Red), C(2, CardColor.Red)), P(1, C(11, CardColor.Red), C(12, CardColor.Red)) },
            drawPile: new[] { C(9, CardColor.Red), C(6, CardColor.Green) },
            discard: Array.Empty<Card>(),
            currentSeat: 0);
        var staleToken = TurnTimerToken(output);
        session.HandleAction(0, new DiscardMsg { card = CardDto.From(C(1, CardColor.Red)) }); // 제때 행동

        Assert.That(session.HandleTurnTimeout(staleToken).Messages, Is.Empty);
    }
}
