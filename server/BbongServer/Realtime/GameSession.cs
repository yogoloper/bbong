using System;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Online;
using BbongCore.Rules;
using BbongServer.Realtime;

namespace BbongServer.Realtime;

/// <summary>
/// 서버 권위 게임 세션(친구방 1게임). 클라 연습 모드(GameTableBootstrap)의 진행 흐름을
/// 사람 N명 기준으로 이식 — 턴 시작 → (스톱 대기) → 자동 드로우 → 버림 대기 → 뽕 창(5초) → ...
/// WS/전송을 모름: 모든 핸들러가 SessionOutput(송신+타이머 예약)을 반환한다.
/// </summary>
public sealed class GameSession
{
    private readonly string[] _nicknames;
    private readonly Func<IRandom> _rngFactory;
    private readonly int _playerCount;

    private GameState _game;
    private RoundState _round = null!;
    private int _roundIndex;
    private int _dealerSeat;
    private string _phase = RoundPhase.WaitingDiscard;

    private MeldResult _meld = MeldResult.None;
    private bool _canNaturalPong;
    private int _naturalPongNumber;

    // 뽕 창 상태. 토큰은 창/판이 바뀔 때마다 증가 — 뒤늦게 도착한 타이머(stale) 무시용.
    private int _pongToken;
    private int _pongNumber;
    private int _pongDiscarderSeat;
    private readonly List<int> _pongEligible = new();
    private readonly HashSet<int> _pongPassed = new();
    private int _pongDeclarerSeat = -1;
    private List<Card> _pongLaid = new();

    private int _roundToken;

    public GameSession(string[] nicknames, Func<IRandom> rngFactory)
    {
        _nicknames = nicknames;
        _rngFactory = rngFactory;
        _playerCount = nicknames.Length;
        _game = GameState.Start(_playerCount);
    }

    // ── 진입점 ──

    public SessionOutput StartMatch()
    {
        var output = new SessionOutput();
        StartRound(output);
        return output;
    }

    public SessionOutput HandleAction(int seat, object message)
    {
        var output = new SessionOutput();
        switch (message)
        {
            case DiscardMsg discard:
                HandleDiscard(output, seat, discard);
                break;
            case PongDeclareMsg:
                HandlePongDeclare(output, seat);
                break;
            case PongPassMsg:
                HandlePongPass(output, seat);
                break;
            case PongDiscardMsg pongDiscard:
                HandlePongDiscard(output, seat, pongDiscard);
                break;
            default:
                Error(output, seat, "unsupported", "지원하지 않는 요청입니다.");
                break;
        }

        return output;
    }

    public SessionOutput HandlePongTimeout(int token)
    {
        var output = new SessionOutput();
        if (_phase == RoundPhase.PongWindow && token == _pongToken)
        {
            ClosePongWindow(output);
            BeginTurn(output);
        }

        return output;
    }

    public SessionOutput HandleNextRound(int token)
    {
        var output = new SessionOutput();
        if (_phase == RoundPhase.RoundOver && token == _roundToken)
        {
            StartRound(output);
        }

        return output;
    }

    /// <summary>테스트 전용: 조작된 판으로 교체 후 턴 진입까지 실행.</summary>
    internal SessionOutput RigRoundForTest(RoundState round)
    {
        _round = round;
        var output = new SessionOutput();
        BeginTurn(output);
        return output;
    }

    // ── 판/턴 진행 ──

    private void StartRound(SessionOutput output)
    {
        _round = RoundState.Deal(Deck.CreateStandard(), _playerCount, _rngFactory(), _dealerSeat);
        _phase = RoundPhase.WaitingDiscard;
        EmitEach(output, seat => new RoundStartedMsg
        {
            roundIndex = _roundIndex,
            dealerSeat = _dealerSeat,
            view = BuildView(seat)
        });
        BeginTurn(output);
    }

    private void BeginTurn(SessionOutput output)
    {
        _meld = MeldResult.None;
        _canNaturalPong = false;
        var current = _round.CurrentSeat;

        if (StopResolver.CanStop(_round, current))
        {
            _phase = RoundPhase.WaitingStop;
            EmitEach(output, seat => new TurnBeganMsg { seat = current, view = BuildView(seat) });
            return;
        }

        _phase = RoundPhase.WaitingDiscard;
        EmitEach(output, seat => new TurnBeganMsg { seat = current, view = BuildView(seat) });
        AutoDraw(output);
    }

    private void AutoDraw(SessionOutput output)
    {
        var current = _round.CurrentSeat;
        if (!_round.CanDraw)
        {
            EndRound(output, RoundSettlement.SettleByExhaustion(_round),
                "바닥 더미 소진(재셔플 한도 초과) → 강제 종료", current);
            return;
        }

        var reshufflesBefore = _round.ReshuffleCount;
        _round = _round.Draw();
        var reshuffled = _round.ReshuffleCount > reshufflesBefore;

        var hand = _round.CurrentPlayer.Hand;
        _meld = HandEvaluator.Evaluate(hand);
        _canNaturalPong = _round.CanNaturalPong();
        _naturalPongNumber = _canNaturalPong ? TripleNumber(hand) : 0;
        _phase = RoundPhase.WaitingDiscard;

        EmitEach(output, seat => new DrewCardMsg { seat = current, reshuffled = reshuffled, view = BuildView(seat) });
    }

    // ── 버림 ──

    private void HandleDiscard(SessionOutput output, int seat, DiscardMsg msg)
    {
        if (_phase != RoundPhase.WaitingDiscard)
        {
            Error(output, seat, "invalid_phase", "지금은 버릴 수 없습니다.");
            return;
        }

        if (seat != _round.CurrentSeat)
        {
            Error(output, seat, "not_your_turn", "내 턴이 아닙니다.");
            return;
        }

        var card = msg.card.ToCard();
        if (!_round.CurrentPlayer.Hand.Contains(card))
        {
            Error(output, seat, "invalid_card", "손에 없는 카드입니다.");
            return;
        }

        _round = _round.Discard(card);
        AfterDiscard(output, seat, card);
    }

    /// <summary>버림(일반/뽕 추가버림) 공통 후처리: 뽕 창 오픈 또는 다음 턴.</summary>
    private void AfterDiscard(SessionOutput output, int discarderSeat, Card card)
    {
        var eligible = Enumerable.Range(0, _playerCount).Where(s => _round.CanPong(s)).ToList();
        if (eligible.Count == 0)
        {
            _phase = RoundPhase.WaitingDiscard;
            EmitEach(output, seat => new DiscardedMsg { seat = discarderSeat, card = CardDto.From(card), view = BuildView(seat) });
            BeginTurn(output);
            return;
        }

        _phase = RoundPhase.PongWindow;
        _pongToken++;
        _pongNumber = card.Number;
        _pongDiscarderSeat = discarderSeat;
        _pongEligible.Clear();
        _pongEligible.AddRange(eligible);
        _pongPassed.Clear();

        EmitEach(output, seat => new DiscardedMsg { seat = discarderSeat, card = CardDto.From(card), view = BuildView(seat) });
        EmitEach(output, seat => new PongWindowOpenedMsg
        {
            discarderSeat = discarderSeat,
            number = _pongNumber,
            seconds = RealtimeConfig.PongWindowSeconds,
            view = BuildView(seat)
        });
        output.After(new PongTimeoutCmd(_pongToken), RealtimeConfig.PongWindowSeconds * 1000);
    }

    // ── 뽕 ──

    private void HandlePongDeclare(SessionOutput output, int seat)
    {
        if (_phase != RoundPhase.PongWindow)
        {
            Error(output, seat, "pong_too_late", "뽕 선언 창이 이미 닫혔습니다.");
            return;
        }

        if (!_pongEligible.Contains(seat) || _pongPassed.Contains(seat))
        {
            Error(output, seat, "cannot_pong", "뽕할 수 없습니다.");
            return;
        }

        _pongToken++; // 진행 중 타이머 무효화

        var hand = _round.Players[seat].Hand;
        var laid = hand.Cards.Where(c => c.Number == _pongNumber).Take(2).ToList();

        if (hand.Count == 2)
        {
            // 손 전체가 뽕 2장 → 추가 버림 없이 손 소진(두 번 뽕 손 털기, 버린 사람 박 +20)
            _round = _round.Pong(seat, null);
            EmitEach(output, s => new PongedMsg
            {
                seat = seat, number = _pongNumber, laid = CardDto.FromAll(laid), view = BuildView(s)
            });
            EndRound(output, RoundSettlement.SettleByTwoPong(_round, seat, _pongDiscarderSeat),
                $"{_nicknames[seat]} 손 털기 · {_nicknames[_pongDiscarderSeat]} 박 +20", seat);
            return;
        }

        // 내려놓기만 먼저 확정, 코어 반영은 추가 버림 선택 시(클라 연습 모드와 동일한 단계 연출)
        _phase = RoundPhase.WaitingPongDiscard;
        _pongDeclarerSeat = seat;
        _pongLaid = laid;
        EmitEach(output, s => new PongedMsg
        {
            seat = seat, number = _pongNumber, laid = CardDto.FromAll(laid), view = BuildView(s)
        });
    }

    private void HandlePongPass(SessionOutput output, int seat)
    {
        if (_phase != RoundPhase.PongWindow || !_pongEligible.Contains(seat))
        {
            return; // 무해한 지각 패스는 조용히 무시
        }

        _pongPassed.Add(seat);
        if (_pongPassed.Count < _pongEligible.Count)
        {
            return;
        }

        _pongToken++;
        ClosePongWindow(output);
        BeginTurn(output);
    }

    private void HandlePongDiscard(SessionOutput output, int seat, PongDiscardMsg msg)
    {
        if (_phase != RoundPhase.WaitingPongDiscard || seat != _pongDeclarerSeat)
        {
            Error(output, seat, "invalid_phase", "지금은 뽕 추가 버림 차례가 아닙니다.");
            return;
        }

        var card = msg.card.ToCard();
        if (!_round.Players[seat].Hand.Contains(card) || _pongLaid.Contains(card))
        {
            Error(output, seat, "invalid_card", "버릴 수 없는 카드입니다.");
            return;
        }

        _round = _round.Pong(seat, card);
        _pongDeclarerSeat = -1;

        if (_round.Players[seat].Hand.Count == 0)
        {
            EmitEach(output, s => new DiscardedMsg { seat = seat, card = CardDto.From(card), view = BuildView(s) });
            EndRound(output, RoundSettlement.SettleByTwoPong(_round, seat, _pongDiscarderSeat),
                $"{_nicknames[seat]} 손 털기 · {_nicknames[_pongDiscarderSeat]} 박 +20", seat);
            return;
        }

        AfterDiscard(output, seat, card); // 뽕의 추가 버림도 다시 뽕 대상
    }

    private void ClosePongWindow(SessionOutput output)
    {
        _phase = RoundPhase.WaitingDiscard; // 다음 턴 진입 전 임시 — BeginTurn이 확정
        EmitEach(output, seat => new PongWindowClosedMsg { view = BuildView(seat) });
    }

    // ── 판 종료 ──

    private void EndRound(SessionOutput output, int[] scores, string reason, int enderSeat)
    {
        _game = _game.ApplyRoundScores(scores);
        _roundIndex++;
        _dealerSeat = enderSeat;

        if (_game.IsSetOver)
        {
            _phase = RoundPhase.SetOver;
            output.ToAll(new SetEndedMsg
            {
                reason = reason,
                enderSeat = enderSeat,
                scores = scores,
                cumulativeDebts = _game.CumulativeDebts.ToArray(),
                winnerSeats = _game.WinnerSeats().ToArray()
            });
            return;
        }

        _phase = RoundPhase.RoundOver;
        _roundToken++;
        EmitEach(output, seat => new RoundEndedMsg
        {
            reason = reason,
            enderSeat = enderSeat,
            scores = scores,
            cumulativeDebts = _game.CumulativeDebts.ToArray(),
            roundIndex = _roundIndex - 1,
            nextRoundInMs = RealtimeConfig.NextRoundDelayMs,
            view = BuildView(seat)
        });
        output.After(new NextRoundCmd(_roundToken), RealtimeConfig.NextRoundDelayMs);
    }

    // ── 뷰/헬퍼 ──

    private RoundView BuildView(int seat)
    {
        var current = _round.CurrentSeat;
        return new RoundView
        {
            mySeat = seat,
            currentSeat = current,
            phase = _phase,
            actorSeat = _phase == RoundPhase.WaitingPongDiscard ? _pongDeclarerSeat
                : _phase == RoundPhase.PongWindow ? _pongDiscarderSeat
                : current,
            drawPileCount = _round.DrawPile.Count,
            reshuffleCount = _round.ReshuffleCount,
            pongNumber = _phase is RoundPhase.PongWindow or RoundPhase.WaitingPongDiscard ? _pongNumber : 0,
            canStop = _phase == RoundPhase.WaitingStop && seat == current && StopResolver.CanStop(_round, seat),
            canMeld = _phase == RoundPhase.WaitingDiscard && seat == current && _meld.Type != MeldType.None,
            meldType = _meld.Type.ToString(),
            meldScore = _meld.Score,
            canNaturalPong = _phase == RoundPhase.WaitingDiscard && seat == current && _canNaturalPong,
            naturalPongNumber = _naturalPongNumber,
            canPong = _phase == RoundPhase.PongWindow && _pongEligible.Contains(seat) && !_pongPassed.Contains(seat),
            myHand = CardDto.FromAll(_round.Players[seat].Hand.Cards),
            seats = _round.Players.Select(p => new SeatView
            {
                seat = p.Seat,
                nickname = _nicknames[p.Seat],
                handCount = p.Hand.Count,
                pongCount = p.PongCount,
                hasPonged = p.HasPonged,
                cumulativeDebt = _game.CumulativeDebts[p.Seat]
            }).ToArray()
        };
    }

    private void EmitEach(SessionOutput output, Func<int, object> make)
    {
        for (var seat = 0; seat < _playerCount; seat++)
        {
            output.ToSeat(seat, make(seat));
        }
    }

    private static void Error(SessionOutput output, int seat, string code, string message) =>
        output.ToSeat(seat, new ErrorMsg { code = code, message = message });

    private static int TripleNumber(Hand hand) =>
        hand.Cards.GroupBy(c => c.Number).First(g => g.Count() >= 3).Key;
}
