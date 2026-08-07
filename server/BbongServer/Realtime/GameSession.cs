using System;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Ai;
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
    private int _turnGapToken;

    // 턴 행동 대기 타이머(§3). 대기 상태 진입마다 증가 — stale 타임아웃 무시용.
    private int _turnToken;
    private Card _drawnCard; // 시간 초과 시 자동으로 버릴 방금 드로우한 카드

    // 이탈/AFK 봇 대체(§9-4): 한 판 내내 직접 입력 없이 턴 타임아웃을 겪은 좌석은 판 종료 시 봇 전환.
    private readonly bool[] _acted;    // 이 판에 직접 입력했는가
    private readonly bool[] _timedOut; // 이 판에 턴 타임아웃을 겪었는가(턴이 안 온 좌석 보호)
    private readonly HashSet<int> _botSeats = new();

    // 다음 타이머 1회에만 가산되는 연출 지연(재셔플 수렴 등) — ArmActorTimer 직전에 설정, 직후 리셋
    private int _timerExtraMs;
    private readonly Bot?[] _bots;
    private int _botToken;

    public GameSession(string[] nicknames, Func<IRandom> rngFactory, int setRounds = 5,
        IEnumerable<int>? botSeats = null)
    {
        _nicknames = nicknames;
        _rngFactory = rngFactory;
        _playerCount = nicknames.Length;
        _game = GameState.Start(_playerCount, setRounds);
        _acted = new bool[_playerCount];
        _timedOut = new bool[_playerCount];
        _bots = new Bot?[_playerCount];

        // 대기실에서 방장이 채운 봇 좌석 — 시작부터 봇 플레이(닉네임은 방에서 "(봇)" 포함 전달)
        foreach (var seat in botSeats ?? Array.Empty<int>())
        {
            _botSeats.Add(seat);
            _bots[seat] = new Bot(BotDifficulty.Normal);
        }
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
        if (_botSeats.Contains(seat))
        {
            Error(output, seat, "seat_replaced", "이탈로 봇이 대신 플레이 중인 자리입니다.");
            return output;
        }

        _acted[seat] = true; // 어떤 입력이든 활동으로 간주(§9-4 AFK 판정)
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
            case StopDeclareMsg:
                HandleStopDeclare(output, seat);
                break;
            case ContinueTurnMsg:
                HandleContinueTurn(output, seat);
                break;
            case MeldDeclareMsg:
                HandleMeldDeclare(output, seat);
                break;
            case NaturalPongMsg naturalPong:
                HandleNaturalPong(output, seat, naturalPong);
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
            BotPongDecisions(output); // 사람이 5초 안에 선언 안 함 → 대기하던 봇 기회
            if (_phase == RoundPhase.PongWindow)
            {
                ClosePongWindow(output);
            }
        }

        return output;
    }

    public SessionOutput HandleTurnTimeout(int token)
    {
        var output = new SessionOutput();
        if (token != _turnToken)
        {
            return output;
        }

        switch (_phase)
        {
            case RoundPhase.WaitingStop:
                _timedOut[_round.CurrentSeat] = true;
                AutoDraw(output); // 미응답 → 자동 계속(§3)
                break;

            case RoundPhase.WaitingDiscard:
                var current = _round.CurrentSeat;
                _timedOut[current] = true;
                var drawn = _drawnCard;
                _round = _round.Discard(drawn);
                AfterDiscard(output, current, drawn); // 방금 드로우한 카드 자동 버림(§3)
                break;

            case RoundPhase.WaitingPongDiscard:
                var declarer = _pongDeclarerSeat;
                _timedOut[declarer] = true;
                var toss = _round.Players[declarer].Hand.Cards.First(c => !_pongLaid.Contains(c));
                ApplyPongDiscard(output, declarer, toss); // 내려놓은 고정 패 제외 자동 버림
                break;
        }

        return output;
    }

    /// <summary>봇 대체 좌석의 행동(§9-4). 상태별로 코어 Bot이 결정한다.</summary>
    public SessionOutput HandleBotAct(int token)
    {
        var output = new SessionOutput();
        if (token != _botToken)
        {
            return output;
        }

        switch (_phase)
        {
            case RoundPhase.WaitingStop when _botSeats.Contains(_round.CurrentSeat):
            {
                var seat = _round.CurrentSeat;
                if (StopResolver.CanStop(_round, seat) && _bots[seat]!.ShouldStop(_round, seat))
                {
                    var bagaji = StopResolver.IsBagaji(_round, seat);
                    var ender = StopEnderSeat(seat, bagaji);
                    output.ToAll(new StopDeclaredMsg
                    {
                        seat = seat, bagaji = bagaji,
                        laidSeat = ender, laid = CardDto.FromAll(_round.Players[ender].Hand.Cards)
                    });
                    var reason = bagaji ? $"{_nicknames[seat]} - 스톱 바가지" : $"{_nicknames[seat]} - 스톱";
                    EndRound(output, RoundSettlement.SettleByStop(_round, seat), reason, ender);
                }
                else
                {
                    AutoDraw(output);
                }

                break;
            }

            case RoundPhase.WaitingDiscard when _botSeats.Contains(_round.CurrentSeat):
            {
                var seat = _round.CurrentSeat;
                var bot = _bots[seat]!;
                if (_meld.Type != MeldType.None)
                {
                    output.ToAll(new MeldDeclaredMsg
                    {
                        seat = seat, meldType = _meld.Type.ToString(), meldScore = _meld.Score,
                        laid = CardDto.FromAll(_round.Players[seat].Hand.Cards)
                    });
                    EndRound(output, RoundSettlement.SettleByMeld(_round, seat, _meld),
                        $"{_nicknames[seat]} - {MeldNames.Korean(_meld.Type)}", seat);
                    break;
                }

                if (_canNaturalPong)
                {
                    BotNaturalPong(output, seat, bot);
                    break;
                }

                var card = bot.ChooseDiscard(_round.CurrentPlayer.Hand);
                _round = _round.Discard(card);
                AfterDiscard(output, seat, card);
                break;
            }

            case RoundPhase.WaitingPongDiscard when _botSeats.Contains(_pongDeclarerSeat):
            {
                var seat = _pongDeclarerSeat;
                if (_round.CanPongThenNaturalPong(seat))
                {
                    PongClear(output, seat); // 남은 3장 자연뽕 손 소진(뽕 바가지) — 봇에게 항상 이득
                    break;
                }

                var toss = _bots[seat]!.ChoosePongDiscard(new Hand(_round.Players[seat].Hand.Cards.Except(_pongLaid)));
                ApplyPongDiscard(output, seat, toss);
                break;
            }

            case RoundPhase.PongWindow:
            {
                if (!HumanPongPending())
                {
                    BotPongDecisions(output); // 사람 우선 — 사람 창이 남아 있으면 봇은 대기
                }

                break;
            }
        }

        return output;
    }

    private void BotNaturalPong(SessionOutput output, int seat, Bot bot)
    {
        var number = _naturalPongNumber;
        var hand = _round.Players[seat].Hand;
        var laid = hand.Cards.Where(c => c.Number == number).Take(3).ToList();
        var rest = hand.Cards.Except(laid).ToList();

        if (rest.Count == 0)
        {
            _round = _round.NaturalPong(number, null);
            EmitEach(output, s => new NaturalPongedMsg
            {
                seat = seat, number = number, laid = CardDto.FromAll(laid), view = BuildView(s)
            });
            EndRound(output, RoundSettlement.SettleByHandClear(_round, seat), $"{_nicknames[seat]} - 자연뽕 손 털기", seat);
            return;
        }

        var toss = bot.ChoosePongDiscard(new Hand(rest));
        _round = _round.NaturalPong(number, toss);
        EmitEach(output, s => new NaturalPongedMsg
        {
            seat = seat, number = number, laid = CardDto.FromAll(laid), view = BuildView(s)
        });
        AfterDiscard(output, seat, toss);
    }

    public SessionOutput HandleTurnGap(int token)
    {
        var output = new SessionOutput();
        if (_phase == RoundPhase.TurnGap && token == _turnGapToken)
        {
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
        Array.Fill(_acted, false);
        Array.Fill(_timedOut, false);
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
            ArmActorTimer(output);
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
        _drawnCard = hand.Cards[^1]; // 시간 초과 시 자동 버림 대상(§3)
        _meld = HandEvaluator.Evaluate(hand);
        _canNaturalPong = _round.CanNaturalPong();
        _naturalPongNumber = _canNaturalPong ? TripleNumber(hand) : 0;
        _phase = RoundPhase.WaitingDiscard;

        EmitEach(output, seat => new DrewCardMsg { seat = current, reshuffled = reshuffled, view = BuildView(seat) });
        _timerExtraMs = reshuffled ? RealtimeConfig.ReshuffleFxMs : 0; // 셔플 연출이 끝난 뒤부터 행동 시간
        ArmActorTimer(output);
        _timerExtraMs = 0;
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
        _turnToken++; // 대기 상태 이탈 — 진행 중 턴 타이머 무효화
        var eligible = Enumerable.Range(0, _playerCount).Where(s => _round.CanPong(s)).ToList();
        if (eligible.Count == 0)
        {
            EnterTurnGap(output);
            EmitEach(output, seat => new DiscardedMsg { seat = discarderSeat, card = CardDto.From(card), view = BuildView(seat) });
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

        if (eligible.Any(s => _botSeats.Contains(s)))
        {
            ArmBotAct(output); // 봇 좌석의 뽕 결정
        }
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

        DeclarePong(output, seat);
    }

    /// <summary>뽕 선언 반영(플레이어/봇 공용). 검증은 호출부 책임.</summary>
    private void DeclarePong(SessionOutput output, int seat)
    {
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
                $"{_nicknames[_pongDiscarderSeat]} - 뽕 바가지", seat);
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
        ArmActorTimer(output);
    }

    private void HandlePongPass(SessionOutput output, int seat)
    {
        if (_phase != RoundPhase.PongWindow || !_pongEligible.Contains(seat))
        {
            return; // 무해한 지각 패스는 조용히 무시
        }

        _pongPassed.Add(seat);
        if (_pongPassed.Count >= _pongEligible.Count)
        {
            _pongToken++;
            ClosePongWindow(output);
            return;
        }

        if (!HumanPongPending() && !_botSeats.Contains(seat))
        {
            ArmBotAct(output); // 마지막 사람이 패스 → 대기하던 봇이 한 박자 뒤 결정
        }
    }

    /// <summary>아직 창이 살아있는 사람 좌석이 있는지 — 봇 뽕은 사람 우선 원칙으로 그동안 대기.</summary>
    private bool HumanPongPending() =>
        _pongEligible.Any(s => !_botSeats.Contains(s) && !_pongPassed.Contains(s));

    private void BotPongDecisions(SessionOutput output)
    {
        foreach (var seat in _pongEligible.Where(s => _botSeats.Contains(s) && !_pongPassed.Contains(s)).ToList())
        {
            if (_phase != RoundPhase.PongWindow)
            {
                break; // 선행 봇의 뽕/마지막 패스로 창이 닫힘
            }

            if (_bots[seat]!.ShouldPong())
            {
                DeclarePong(output, seat);
                break;
            }

            HandlePongPass(output, seat);
        }
    }

    /// <summary>뽕 후 남은 같은 숫자 3장을 자연뽕으로 내려놓아 손 소진 종료. 뽕 준 사람이 뽕 바가지.</summary>
    private void PongClear(SessionOutput output, int seat)
    {
        var rest = _round.Players[seat].Hand.Cards.Where(c => c.Number != _pongNumber).ToList();
        var number = rest[0].Number;
        _round = _round.PongThenNaturalPong(seat);
        _turnToken++;
        EmitEach(output, s => new NaturalPongedMsg
        {
            seat = seat, number = number, laid = CardDto.FromAll(rest), view = BuildView(s)
        });
        EndRound(output, RoundSettlement.SettleByTwoPong(_round, seat, _pongDiscarderSeat),
            $"{_nicknames[_pongDiscarderSeat]} - 뽕 바가지", seat);
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

        ApplyPongDiscard(output, seat, card);
    }

    /// <summary>뽕 추가 버림 반영(플레이어 선택/턴 타임아웃 공용).</summary>
    private void ApplyPongDiscard(SessionOutput output, int seat, Card card)
    {
        _round = _round.Pong(seat, card);
        _pongDeclarerSeat = -1;

        if (_round.Players[seat].Hand.Count == 0)
        {
            EmitEach(output, s => new DiscardedMsg { seat = seat, card = CardDto.From(card), view = BuildView(s) });
            EndRound(output, RoundSettlement.SettleByTwoPong(_round, seat, _pongDiscarderSeat),
                $"{_nicknames[_pongDiscarderSeat]} - 뽕 바가지", seat);
            return;
        }

        AfterDiscard(output, seat, card); // 뽕의 추가 버림도 다시 뽕 대상
    }

    // ── 스톱 / 계속 / 족보 / 자연뽕 ──

    private void HandleStopDeclare(SessionOutput output, int seat)
    {
        if (_phase != RoundPhase.WaitingStop || seat != _round.CurrentSeat || !StopResolver.CanStop(_round, seat))
        {
            Error(output, seat, "cannot_stop", "지금은 스톱할 수 없습니다.");
            return;
        }

        var bagaji = StopResolver.IsBagaji(_round, seat);
        var ender = StopEnderSeat(seat, bagaji); // 정상 스톱=선언자, 바가지=박 먹인 승자
        output.ToAll(new StopDeclaredMsg
        {
            seat = seat, bagaji = bagaji,
            laidSeat = ender, laid = CardDto.FromAll(_round.Players[ender].Hand.Cards)
        });
        var reason = bagaji ? $"{_nicknames[seat]} - 스톱 바가지" : $"{_nicknames[seat]} - 스톱";
        EndRound(output, RoundSettlement.SettleByStop(_round, seat), reason, ender);
    }

    private void HandleContinueTurn(SessionOutput output, int seat)
    {
        if (_phase != RoundPhase.WaitingStop || seat != _round.CurrentSeat)
        {
            Error(output, seat, "invalid_phase", "지금은 계속을 선택할 수 없습니다.");
            return;
        }

        AutoDraw(output);
    }

    private void HandleMeldDeclare(SessionOutput output, int seat)
    {
        if (_phase != RoundPhase.WaitingDiscard || seat != _round.CurrentSeat || _meld.Type == MeldType.None)
        {
            Error(output, seat, "cannot_meld", "선언할 족보가 없습니다.");
            return;
        }

        output.ToAll(new MeldDeclaredMsg
        {
            seat = seat, meldType = _meld.Type.ToString(), meldScore = _meld.Score,
            laid = CardDto.FromAll(_round.Players[seat].Hand.Cards)
        });
        EndRound(output, RoundSettlement.SettleByMeld(_round, seat, _meld),
            $"{_nicknames[seat]} - {MeldNames.Korean(_meld.Type)}", seat);
    }

    private void HandleNaturalPong(SessionOutput output, int seat, NaturalPongMsg msg)
    {
        if (_phase == RoundPhase.WaitingPongDiscard && seat == _pongDeclarerSeat && _round.CanPongThenNaturalPong(seat))
        {
            PongClear(output, seat); // 토스 대신 남은 3장 자연뽕 → 손 소진(뽕 바가지)
            return;
        }

        if (_phase != RoundPhase.WaitingDiscard || seat != _round.CurrentSeat || !_canNaturalPong)
        {
            Error(output, seat, "cannot_natural_pong", "자연뽕할 수 없습니다.");
            return;
        }

        var number = _naturalPongNumber;
        var hand = _round.Players[seat].Hand;
        var laid = hand.Cards.Where(c => c.Number == number).Take(3).ToList();
        var rest = hand.Cards.Except(laid).ToList();

        if (rest.Count == 0)
        {
            // 손패 전부 같은 숫자 → 손 소진 종료
            _round = _round.NaturalPong(number, null);
            EmitEach(output, s => new NaturalPongedMsg
            {
                seat = seat, number = number, laid = CardDto.FromAll(laid), view = BuildView(s)
            });
            EndRound(output, RoundSettlement.SettleByHandClear(_round, seat), $"{_nicknames[seat]} - 자연뽕 손 털기", seat);
            return;
        }

        if (!msg.hasDiscard)
        {
            Error(output, seat, "invalid_card", "자연뽕 후 버릴 카드를 지정해야 합니다.");
            return;
        }

        var card = msg.card.ToCard();
        if (!rest.Contains(card))
        {
            Error(output, seat, "invalid_card", "버릴 수 없는 카드입니다.");
            return;
        }

        _round = _round.NaturalPong(number, card);
        EmitEach(output, s => new NaturalPongedMsg
        {
            seat = seat, number = number, laid = CardDto.FromAll(laid), view = BuildView(s)
        });
        AfterDiscard(output, seat, card); // 추가 버림도 뽕 대상
    }

    /// <summary>스톱 승자: 정상 스톱=선언자, 바가지=가장 낮은 손패 합의 뽕 게이머(클라 로직 이식).</summary>
    private int StopEnderSeat(int stopSeat, bool bagaji)
    {
        if (!bagaji)
        {
            return stopSeat;
        }

        var winner = stopSeat;
        var min = _round.Players[stopSeat].Hand.Sum();
        for (var s = 0; s < _playerCount; s++)
        {
            if (_round.Players[s].HasPonged && _round.Players[s].Hand.Sum() < min)
            {
                min = _round.Players[s].Hand.Sum();
                winner = s;
            }
        }

        return winner;
    }

    private void ClosePongWindow(SessionOutput output)
    {
        EnterTurnGap(output);
        EmitEach(output, seat => new PongWindowClosedMsg { view = BuildView(seat) });
    }

    /// <summary>턴 행동 대기 타이머 예약(§3). 이전 타이머는 토큰 증가로 무효화.</summary>
    private void ArmTurnTimer(SessionOutput output)
    {
        _turnToken++;
        output.After(new TurnTimeoutCmd(_turnToken), RealtimeConfig.TurnTimerSeconds * 1000 + _timerExtraMs);
    }

    /// <summary>행동 주체가 봇이면 봇 행동 예약, 사람이면 5초 턴 타이머 예약.</summary>
    private void ArmActorTimer(SessionOutput output)
    {
        var actor = _phase == RoundPhase.WaitingPongDiscard ? _pongDeclarerSeat : _round.CurrentSeat;
        if (_botSeats.Contains(actor))
        {
            ArmBotAct(output);
        }
        else
        {
            ArmTurnTimer(output);
        }
    }

    private void ArmBotAct(SessionOutput output)
    {
        _botToken++;
        output.After(new BotActCmd(_botToken), RealtimeConfig.BotActDelayMs + _timerExtraMs);
    }

    /// <summary>턴 전환 간격 진입: 잠깐 아무도 턴이 아닌 상태(연습 모드 연출 이식). 만료 시 HandleTurnGap이 턴 진입.</summary>
    private void EnterTurnGap(SessionOutput output)
    {
        _phase = RoundPhase.TurnGap;
        _turnGapToken++;
        output.After(new TurnGapCmd(_turnGapToken), RealtimeConfig.TurnGapMs);
    }

    // ── 판 종료 ──

    private void EndRound(SessionOutput output, int[] scores, string reason, int enderSeat)
    {
        _turnToken++; // 진행 중 턴 타이머 무효화
        ReplaceLeaversWithBots(output);
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
                winnerSeats = WinnerSeatsExcludingBots()
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

    /// <summary>
    /// §9-4: 이 판에 직접 입력 없이 턴 타임아웃만 겪은 좌석(이탈/AFK)을 봇으로 전환.
    /// 턴이 오지 않아 입력 기회가 없던 좌석은 타임아웃 기록이 없어 보호된다.
    /// </summary>
    private void ReplaceLeaversWithBots(SessionOutput output)
    {
        for (var seat = 0; seat < _playerCount; seat++)
        {
            if (_botSeats.Contains(seat) || _acted[seat] || !_timedOut[seat])
            {
                continue;
            }

            BotifySeat(output, seat);
        }
    }

    private void BotifySeat(SessionOutput output, int seat)
    {
        _botSeats.Add(seat);
        _bots[seat] = new Bot(BotDifficulty.Normal); // 이어받는 봇은 중간 난이도
        // 닉네임은 게임 끝까지 원래 게이머 것 유지 — 남은 사람들이 헷갈리지 않게
        output.ToAll(new BotTookOverMsg { seat = seat, nickname = _nicknames[seat] });
    }

    /// <summary>해당 좌석이 현재 봇 플레이 중인지(재접속 복귀 판단용).</summary>
    public bool IsBotSeat(int seat) => _botSeats.Contains(seat);

    /// <summary>좌석 순 닉네임(재접속 시 GameStarted 재전송용).</summary>
    public IReadOnlyList<string> Nicknames => _nicknames;

    /// <summary>
    /// 재접속: 봇이 대신 플레이 중이면 자리를 되돌려주고, 현재 판 상태를 본인에게 재동기화한다.
    /// 닉네임은 이탈 중에도 원본 유지라 되돌릴 것이 없다.
    /// </summary>
    public SessionOutput HandleReconnect(int seat)
    {
        var output = new SessionOutput();
        if (_botSeats.Remove(seat))
        {
            _bots[seat] = null;
            _acted[seat] = false;
            _timedOut[seat] = false; // 복귀자는 AFK 카운트 초기화 — 판 종료 시 재강퇴 방지

            var actor = _phase == RoundPhase.WaitingPongDiscard ? _pongDeclarerSeat : _round.CurrentSeat;
            if (actor == seat && _phase is RoundPhase.WaitingDiscard or RoundPhase.WaitingStop or RoundPhase.WaitingPongDiscard)
            {
                _botToken++; // 예약된 봇 행동 무효화 → 사람 턴 타이머로 교체
                ArmActorTimer(output);
            }
        }

        output.ToSeat(seat, new TurnBeganMsg { seat = _round.CurrentSeat, view = BuildView(seat) }); // 상태 재동기화
        return output;
    }

    /// <summary>
    /// 명시적 나가기(§9-4): 판 종료를 기다리지 않고 즉시 봇으로 전환.
    /// 그 좌석이 지금 행동 차례면(버림/스톱/뽕 추가버림/뽕 창) 봇 행동을 바로 예약한다.
    /// </summary>
    public SessionOutput ReplaceSeatWithBot(int seat)
    {
        var output = new SessionOutput();
        if (seat < 0 || seat >= _playerCount || _botSeats.Contains(seat))
        {
            return output;
        }

        BotifySeat(output, seat);

        var actorPhase = _phase == RoundPhase.WaitingStop || _phase == RoundPhase.WaitingDiscard
            ? _round.CurrentSeat
            : _phase == RoundPhase.WaitingPongDiscard ? _pongDeclarerSeat : -1;
        if (actorPhase == seat)
        {
            _turnToken++; // 사람용 5초 타이머 무효화(봇 행동과 중복 방지)
            ArmBotAct(output);
        }
        else if (_phase == RoundPhase.PongWindow && _pongEligible.Contains(seat) && !_pongPassed.Contains(seat))
        {
            ArmBotAct(output); // 뽕 창 대기 중이면 봇이 대신 선언/패스
        }

        return output;
    }

    /// <summary>§9-4: 이탈(봇 대체) 좌석은 우승 후보 제외 — 남은 사람 중 최저 빚이 우승.</summary>
    private int[] WinnerSeatsExcludingBots()
    {
        var humans = Enumerable.Range(0, _playerCount).Where(s => !_botSeats.Contains(s)).ToList();
        if (humans.Count == 0)
        {
            return _game.WinnerSeats().ToArray();
        }

        var minDebt = humans.Min(s => _game.CumulativeDebts[s]);
        return humans.Where(s => _game.CumulativeDebts[s] == minDebt).ToArray();
    }

    /// <summary>테스트 전용: 좌석을 즉시 봇으로 전환.</summary>
    internal void BotifyForTest(int seat)
    {
        _botSeats.Add(seat);
        _bots[seat] = new Bot(BotDifficulty.Normal);
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
            canNaturalPong = (_phase == RoundPhase.WaitingDiscard && seat == current && _canNaturalPong)
                || (_phase == RoundPhase.WaitingPongDiscard && seat == _pongDeclarerSeat && _round.CanPongThenNaturalPong(seat)),
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
