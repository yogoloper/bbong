using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Ai;
using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Online;
using BbongCore.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 연습(봇전) 게임 테이블 드라이버. 2~6인(사람 P0 + 봇), 서버 불필요.
    /// 렌더/연출은 공용 GameTableView가 담당 — 여기는 코어 게임 루프(봇 코루틴 포함)를 돌리며
    /// 상태를 RoundView로 합성해 뷰에 공급하고, 뷰 입력 이벤트를 코어 액션으로 바꾼다.
    /// 인원·난이도는 로비(LobbyBootstrap)에서 Start 전에 설정.
    /// </summary>
    public sealed class GameTableBootstrap : MonoBehaviour
    {
        private enum UiState { StopDecision, NeedDiscard, MeldDecision, PongWindow, PongDiscardSelect, NaturalPongSelect, Resolving, RoundOver, SetOver }

        private const int MySeat = 0;
        private const float BotDelay = 0.5f;
        private const float TossDelay = 0.35f; // 뽕 내려놓기 → 추가 버림 사이 간격(단계 연출)
        private const float TurnGapDelay = 0.5f; // 버림 → 다음 턴 사이 아무도 포커스 없는 간격(턴 전환 연출)
        private const int PongWindowSeconds = 5;

        public int PlayerCount { get; set; } = 4;

        public BotDifficulty Difficulty { get; set; } = BotDifficulty.Normal;

        // 닉네임 = "형용사 명사" 조합(띄어쓰기 포함 최대 9자, GameConfig.MaxNicknameLength=12 이내).
        // 나 포함 전원 게임마다 중복 없이 무작위 배정.
        private static readonly string[] NickAdjectives =
        {
            "수줍은", "용감한", "날쌘", "졸린", "명랑한",
            "시크한", "엉뚱한", "우아한", "씩씩한", "능청스런"
        };

        private static readonly string[] NickNouns =
        {
            "너구리", "두더지", "고슴도치", "다람쥐", "부엉이",
            "수달", "알파카", "펭귄", "사막여우", "호랑나비"
        };

        private string[] _names;
        private Bot[] _bots;

        private GameTableView _table;
        private GameState _game;
        private RoundState _round;
        private int _roundIndex;
        private int _dealerSeat; // 다음 판 선(직전 판 끝낸 사람). 첫 판 = 0
        private UiState _state;
        private bool _turnGap; // 턴 전환 간격 동안 좌석 포커스 숨김
        private int _pongNumber;
        private int _pongDiscarderSeat;
        private int _naturalPongNumber;
        private int _seed = 1;
        private Coroutine _pongTimer;
        private Coroutine _botLoop;
        private MeldResult _pendingMeld; // 족보 선언 대기 중인 족보(MeldDecision)
        private readonly List<int[]> _roundHistory = new(); // 게임 내 판별 점수
        private Button _nextBtn, _lobbyBtn;
        // 뽕/자연뽕 선언 직후 내려놓은 카드(시각 전용). 코어는 버림 선택 후 한 번에 반영되므로
        // 그 사이 손패 표시에서 숨겨 "선언 → 즉시 내려놓기 → 버림" 흐름을 만든다.
        private readonly List<Card> _pendingLaid = new();

        private void Start()
        {
            _seed = Random.Range(1, 1_000_000); // Play마다 다른 패(고정 시드 버그 수정)
            _bots = Enumerable.Range(0, PlayerCount).Select(_ => new Bot(Difficulty)).ToArray();

            _names = new string[PlayerCount];
            var used = new HashSet<string>();
            for (var s = 0; s < PlayerCount; s++)
            {
                string name;
                do
                {
                    name = $"{NickAdjectives[Random.Range(0, NickAdjectives.Length)]} {NickNouns[Random.Range(0, NickNouns.Length)]}";
                }
                while (!used.Add(name));

                _names[s] = name;
            }

            _table = gameObject.AddComponent<GameTableView>();
            _table.MySeat = MySeat;
            _table.PlayerCount = PlayerCount;
            _table.Nicknames = _names;
            _table.Build();
            _nextBtn = _table.AddBarButton("다음 판", OnNext);
            _lobbyBtn = _table.AddBarButton("로비로", OnLobby);

            _table.CardClicked += OnCardClicked;
            _table.StopClicked += OnStop;
            _table.MeldClicked += OnMeldDeclare;
            _table.NaturalPongClicked += OnNaturalPong;
            _table.PongClicked += OnPong;
            _table.PassClicked += OnPass;

            _game = GameState.Start(PlayerCount);
            StartRound();
        }

        // ── 판 시작/종료 ──

        private void StartRound()
        {
            _round = RoundState.Deal(Deck.CreateStandard(), PlayerCount, new SeededRandom(_seed++),
                dealerSeat: _dealerSeat);
            _state = UiState.NeedDiscard;
            _turnGap = false;
            _table.ClearTimeline();
            _pendingLaid.Clear();
            _table.SetEndReason("");
            _table.HideScorePopup();

            SetLog($"{_roundIndex + 1}판 시작.");
            RunBots();
        }

        private void EndRound(int[] scores, string reason, int enderSeat)
        {
            _table.SetEndReason(reason); // 버림 더미 아래에 종료 사유 표시
            _table.PlayStopSfx();
            _roundHistory.Add(scores);
            _game = _game.ApplyRoundScores(scores);
            _roundIndex++;
            _dealerSeat = enderSeat; // 판 끝낸 사람이 다음 판 선

            var detail = string.Join("  ", Enumerable.Range(0, PlayerCount).Select(s => $"P{s} {scores[s]:+0;-0;0}"));
            var cumulative = string.Join("  ", Enumerable.Range(0, PlayerCount).Select(s => $"P{s}={_game.CumulativeDebts[s]}"));

            string title;
            if (_game.IsSetOver)
            {
                var winners = _game.WinnerSeats();
                var who = string.Join(", ", winners.Select(s => _names[s]));
                SetLog($"━━ 게임 종료(5판) ━━ 사유: {reason} → 다음 선 P{enderSeat} | 1등 {who} | 점수[{detail}] 누적[{cumulative}]");
                title = $"게임 종료 — 1등 {who}";
                _state = UiState.SetOver;
            }
            else
            {
                SetLog($"━━ {_roundIndex}판 종료 ━━ 사유: {reason} → 다음 선 P{enderSeat} | 점수[{detail}] 누적[{cumulative}]");
                title = $"{_roundIndex}판 종료";
                _state = UiState.RoundOver;
            }

            // 판 종료만 페이드+자동 진행. 게임 종료(SetOver)는 '새 게임' 전까지 계속 표시.
            _table.ShowScorePopup(title, _game.CumulativeDebts, _roundHistory, fadeOut: _state == UiState.RoundOver,
                onFadedOut: () =>
                {
                    if (_state == UiState.RoundOver)
                    {
                        OnNext();
                    }
                });
            Refresh();
        }

        // ── 봇 자동 진행 (코루틴, 천천히) ──

        /// <summary>봇 루프를 항상 단일로 유지(이전 코루틴 중지). 중복 실행 → 드로우 2번 버그 방지.</summary>
        private void RunBots()
        {
            if (_botLoop != null)
            {
                StopCoroutine(_botLoop);
            }

            _botLoop = StartCoroutine(BotLoop());
        }

        private IEnumerator BotLoop()
        {
            while (true)
            {
                if (_state == UiState.RoundOver || _state == UiState.SetOver)
                {
                    yield break;
                }

                // 내 버림 등으로 표시된 턴 전환 간격: 잠깐 아무도 턴이 아닌 상태 연출
                if (_turnGap)
                {
                    Refresh();
                    yield return new WaitForSeconds(TurnGapDelay);
                    _turnGap = false;
                    if (_state == UiState.RoundOver || _state == UiState.SetOver)
                    {
                        yield break; // 간격 중 봇 손 털기 등으로 판이 끝났으면 진행 중단
                    }
                }

                var seat = _round.CurrentSeat;
                SetLog($"P{seat} 턴 시작 ({SeatName(seat)}, 남은 더미 {_round.DrawPile.Count})");
                if (seat == MySeat)
                {
                    // 스톱 가능하면 결정(스톱/계속), 아니면 자동 드로우 후 버림 대기
                    if (StopResolver.CanStop(_round, MySeat))
                    {
                        _state = UiState.StopDecision;
                        SetLog("스톱? 또는 계속");
                        Refresh();
                    }
                    else
                    {
                        AutoDrawMe();
                    }

                    yield break;
                }

                if (StopResolver.CanStop(_round, seat) && _bots[seat].ShouldStop(_round, seat))
                {
                    AnnounceStop(seat);
                    EndRound(RoundSettlement.SettleByStop(_round, seat), StopReason(seat), StopEnderSeat(seat));
                    yield break;
                }

                if (!_round.CanDraw)
                {
                    EndRound(RoundSettlement.SettleByExhaustion(_round), "바닥 더미 소진(재셔플 2회 초과) → 강제 종료", seat);
                    yield break;
                }

                DrawCard();
                Refresh();
                yield return new WaitForSeconds(BotDelay);

                var meld = HandEvaluator.Evaluate(_round.CurrentPlayer.Hand);
                if (meld.Type != MeldType.None)
                {
                    _table.ShowMeldSet(_round.CurrentPlayer.Hand.Cards); // 족보: 버림 비우고 표시
                    _table.PongFx($"{SeatName(seat)}\n{MeldName(meld.Type)}!");
                    EndRound(RoundSettlement.SettleByMeld(_round, seat, meld), $"{SeatName(seat)} 족보 완성 [{MeldName(meld.Type)} {meld.Score}점]", seat);
                    yield break;
                }

                if (_round.CanNaturalPong())
                {
                    var number = TripleNumber(_round.CurrentPlayer.Hand);
                    var laid = _round.CurrentPlayer.Hand.Cards.Where(c => c.Number == number).Take(3).ToList();
                    var rest = new Hand(_round.CurrentPlayer.Hand.Cards.Where(c => c.Number != number));

                    if (rest.Count == 0)
                    {
                        // 3장 전부 같은 숫자 → 손 소진 자연뽕 종료
                        _round = _round.NaturalPong(number, null);
                        _table.AddGroup(laid);
                        _table.PongFx($"{SeatName(seat)}\n{number}자연뽕!");
                        EndRound(RoundSettlement.SettleByHandClear(_round, seat), $"{SeatName(seat)} 자연뽕 손 털기", seat);
                        yield break;
                    }

                    var toss = _bots[seat].ChoosePongDiscard(rest);
                    _round = _round.NaturalPong(number, toss);
                    SetLog($"P{seat} 자연뽕! {number} 3장 고정");
                    _table.PongFx($"{SeatName(seat)}\n{number}자연뽕!");
                    _table.AddGroup(laid);
                    Refresh();
                    yield return new WaitForSeconds(TossDelay); // 내려놓기 먼저, 버림은 한 박자 뒤

                    _table.PlayDiscardSfx();
                    _table.AddDiscard(toss);
                    _turnGap = true;
                    Refresh();
                    yield return new WaitForSeconds(TurnGapDelay);
                    _turnGap = false;

                    if (_round.CanPong(MySeat))
                    {
                        OpenPongWindow(seat);
                        yield break;
                    }

                    continue;
                }

                var discard = _bots[seat].ChooseDiscard(_round.CurrentPlayer.Hand);
                _round = _round.Discard(discard);
                SetLog($"P{seat} 버림 {CardLabel(discard)}");
                _table.PlayDiscardSfx();
                _table.AddDiscard(discard);
                _turnGap = true;
                Refresh();
                yield return new WaitForSeconds(TurnGapDelay);
                _turnGap = false;

                if (_round.CanPong(MySeat))
                {
                    OpenPongWindow(seat);
                    yield break;
                }

                var ponger = TryBotPong(seat);
                if (ponger >= 0)
                {
                    _turnGap = true;
                    Refresh();
                    yield return new WaitForSeconds(TurnGapDelay);
                    _turnGap = false;

                    // 봇 뽕의 추가 버림도 내가 뽕(두 번째 뽕) 가능
                    if (_state != UiState.RoundOver && _state != UiState.SetOver && _round.CanPong(MySeat))
                    {
                        OpenPongWindow(ponger);
                        yield break;
                    }
                }
            }
        }

        /// <summary>봇 중 뽕 가능한 첫 번째가 뽕. 뽕한 좌석 반환, 없으면 -1.</summary>
        private int TryBotPong(int discarderSeat)
        {
            for (var s = 0; s < PlayerCount; s++)
            {
                if (s != MySeat && _round.CanPong(s) && _bots[s].ShouldPong())
                {
                    DoBotPong(s, discarderSeat);
                    return s;
                }
            }

            return -1;
        }

        private void DoBotPong(int seat, int discarderSeat)
        {
            var number = TopDiscardNumber();
            var laid = _round.Players[seat].Hand.Cards.Where(c => c.Number == number).Take(2).ToList();
            var rest = new Hand(_round.Players[seat].Hand.Cards.Except(laid)); // 같은 숫자 3장째도 버림 후보
            if (rest.Count == 0)
            {
                _round = _round.Pong(seat, null);
                _table.PongFx($"{SeatName(seat)}\n{number}뽕!");
                _table.AddGroup(laid);
                EndRound(RoundSettlement.SettleByTwoPong(_round, seat, discarderSeat), $"{SeatName(seat)} 손 털기 · {SeatName(discarderSeat)} 박 +20", seat);
                return;
            }

            var toss = _bots[seat].ChoosePongDiscard(rest);
            _round = _round.Pong(seat, toss); // 코어는 즉시 반영, 버림 표시만 지연
            SetLog($"P{seat} 뽕! {number} 3장 고정");
            _table.PongFx($"{SeatName(seat)}\n{number}뽕!");
            _table.AddGroup(laid);
            Refresh();
            if (_round.Players[seat].Hand.Count == 0)
            {
                // 추가 버림으로 손이 비면 토스 연출 후 손 털기 종료
                StartCoroutine(EndAfterToss(toss, seat, discarderSeat));
                return;
            }

            StartCoroutine(TossAfterPong(toss));
        }

        /// <summary>봇 뽕의 추가 버림으로 손이 빈 경우: 토스 표시 후 손 털기 종료.</summary>
        private IEnumerator EndAfterToss(Card toss, int seat, int discarderSeat)
        {
            yield return new WaitForSeconds(TossDelay);
            _table.PlayDiscardSfx();
            _table.AddDiscard(toss);
            EndRound(RoundSettlement.SettleByTwoPong(_round, seat, discarderSeat), $"{SeatName(seat)} 손 털기 · {SeatName(discarderSeat)} 박 +20", seat);
        }

        /// <summary>봇 뽕의 추가 버림을 한 박자 뒤에 표시(내려놓기 → 버림 단계 연출).</summary>
        private IEnumerator TossAfterPong(Card toss)
        {
            yield return new WaitForSeconds(TossDelay);
            if (_state == UiState.RoundOver || _state == UiState.SetOver)
            {
                yield break; // 그 사이 판이 끝났으면 다음 판에서 더미가 리셋됨
            }

            _table.PlayDiscardSfx();
            _table.AddDiscard(toss);
            Refresh();
        }

        // ── 뽕 창 (사람) ──

        private void OpenPongWindow(int discarderSeat)
        {
            _pongNumber = TopDiscardNumber();
            _pongDiscarderSeat = discarderSeat;
            _state = UiState.PongWindow;
            SetLog($"P{discarderSeat}가 {_pongNumber} 버림 — 뽕?");
            Refresh();
            _table.StartPongCountdown(PongWindowSeconds);
            _pongTimer = StartCoroutine(PongAutoPass());
        }

        private IEnumerator PongAutoPass()
        {
            yield return new WaitForSeconds(PongWindowSeconds);
            if (_state == UiState.PongWindow)
            {
                OnPass();
            }
        }

        private void StopPongTimer()
        {
            if (_pongTimer != null)
            {
                StopCoroutine(_pongTimer);
                _pongTimer = null;
            }

            _table.StopPongCountdown();
        }

        // ── 사람 액션 ──

        /// <summary>
        /// 드로우 1장. 바닥 더미가 비어 코어가 재셔플하면(버림 더미가 맨 위 1장만 남음)
        /// 셔플 연출: 더미의 단일 버림을 맨 위 1장만 남기고 정리 + 콜아웃/효과음.
        /// </summary>
        private void DrawCard()
        {
            var discardBefore = _round.DiscardPile.Count;
            _round = _round.Draw();
            SetLog($"P{_round.CurrentSeat} 드로우 {CardLabel(_round.CurrentPlayer.Hand.Cards[^1])} (남은 더미 {_round.DrawPile.Count}, 손패 {_round.CurrentPlayer.Hand.Count})");

            if (discardBefore > 1 && _round.DiscardPile.Count < discardBefore)
            {
                // 고정 패(뽕/자연뽕)는 테이블에 남고, 단일 버림은 맨 위 1장만 유지
                _table.KeepGroupsAndTopDiscard();
                SetLog("바닥 더미 소진 → 버림 더미 재셔플(맨 위 1장 유지)");
                _table.ShuffleFx();
                Refresh();
            }
        }

        /// <summary>내 턴 자동 드로우 → 족보면 선언 대기, 아니면 버림 대기(NeedDiscard).</summary>
        private void AutoDrawMe()
        {
            if (!_round.CanDraw)
            {
                EndRound(RoundSettlement.SettleByExhaustion(_round), "바닥 더미 소진(재셔플 2회 초과) → 강제 종료", MySeat);
                return;
            }

            DrawCard();
            _table.PlayDrawSfx();
            var meld = HandEvaluator.Evaluate(_round.CurrentPlayer.Hand);
            if (meld.Type != MeldType.None)
            {
                // 즉시 종료하지 않고 선언 권한을 플레이어에게: 선언 버튼 또는 카드 버리고 계속
                _pendingMeld = meld;
                _state = UiState.MeldDecision;
                SetLog($"족보 완성 가능 [{MeldName(meld.Type)} {meld.Score}점] — 선언 또는 계속");
                Refresh();
                return;
            }

            _state = UiState.NeedDiscard;
            SetLog(_round.CanNaturalPong() ? "버릴 카드 클릭 (또는 자연뽕)" : "버릴 카드를 클릭하세요.");
            Refresh();
        }

        private string StopReason(int stopSeat) => StopResolver.IsBagaji(_round, stopSeat)
            ? $"{SeatName(stopSeat)} 바가지 (+30)"
            : $"{SeatName(stopSeat)} 스톱";

        private static string MeldName(MeldType type) => MeldNames.Korean(type); // 단일 출처: 코어 MeldNames

        /// <summary>스톱 종료 시 다음 판 선: 바가지면 이긴 자(최저 손합 뽕한 게이머), 아니면 스톱 선언자.</summary>
        private int StopEnderSeat(int stopSeat)
        {
            if (!StopResolver.IsBagaji(_round, stopSeat))
            {
                return stopSeat;
            }

            var winner = stopSeat;
            var min = _round.Players[stopSeat].Hand.Sum();
            for (var s = 0; s < PlayerCount; s++)
            {
                if (_round.Players[s].HasPonged && _round.Players[s].Hand.Sum() < min)
                {
                    min = _round.Players[s].Hand.Sum();
                    winner = s;
                }
            }

            return winner;
        }

        private void OnStop()
        {
            if (_state == UiState.StopDecision && StopResolver.CanStop(_round, MySeat))
            {
                AnnounceStop(MySeat);
                EndRound(RoundSettlement.SettleByStop(_round, MySeat), StopReason(MySeat), StopEnderSeat(MySeat));
            }
        }

        /// <summary>스톱 선언 콜아웃: 성공=스톱, 실패=바가지.</summary>
        private void AnnounceStop(int stopSeat) =>
            _table.ShowCallout(StopResolver.IsBagaji(_round, stopSeat)
                ? $"{SeatName(stopSeat)}\n바가지!"
                : $"{SeatName(stopSeat)}\n스톱!");

        private void OnPong()
        {
            if (_state != UiState.PongWindow)
            {
                return;
            }

            StopPongTimer();
            var pongLaid = _round.Players[MySeat].Hand.Cards.Where(c => c.Number == _pongNumber).Take(2).ToList();
            if (_round.Players[MySeat].Hand.Count == 2)
            {
                // 손 전체가 뽕 2장 → 추가 버림 없이 손 소진
                _round = _round.Pong(MySeat, null);
                _table.PongFx($"{SeatName(MySeat)}\n{_pongNumber}뽕!");
                _table.AddGroup(pongLaid);
                EndRound(RoundSettlement.SettleByTwoPong(_round, MySeat, _pongDiscarderSeat), $"{SeatName(MySeat)} 손 털기 · {SeatName(_pongDiscarderSeat)} 박 +20", MySeat);
                return;
            }

            // 실제 판처럼 "뽕!" 외치는 순간 3장 고정분을 즉시 내려놓고, 버림은 그다음 동작
            _pendingLaid.Clear();
            _pendingLaid.AddRange(pongLaid);
            _table.AddGroup(pongLaid);
            _table.PongFx($"{SeatName(MySeat)}\n{_pongNumber}뽕!");

            _state = UiState.PongDiscardSelect;
            SetLog($"뽕! {_pongNumber} 외 버릴 카드 클릭");
            Refresh();
        }

        private void OnPass()
        {
            if (_state == UiState.StopDecision)
            {
                _state = UiState.Resolving;
                AutoDrawMe(); // 스톱 안 하고 계속 → 자동 드로우
                return;
            }

            if (_state != UiState.PongWindow)
            {
                return;
            }

            _state = UiState.Resolving; // 재진입 방지(코루틴 실행 전 second-fire 차단)
            StopPongTimer();
            SetLog("패스");
            TryBotPong(_pongDiscarderSeat);
            RunBots();
        }

        /// <summary>족보 선언: 판 종료. MeldDecision에서 선언 대신 카드를 버리면 계속 진행.</summary>
        private void OnMeldDeclare()
        {
            if (_state != UiState.MeldDecision)
            {
                return;
            }

            _state = UiState.Resolving;
            _table.ShowMeldSet(_round.Players[MySeat].Hand.Cards); // 족보: 버림 비우고 표시
            _table.PongFx($"{SeatName(MySeat)}\n{MeldName(_pendingMeld.Type)}!");
            EndRound(RoundSettlement.SettleByMeld(_round, MySeat, _pendingMeld), $"{SeatName(MySeat)} 족보 완성 [{MeldName(_pendingMeld.Type)} {_pendingMeld.Score}점]", MySeat);
        }

        private void OnNaturalPong()
        {
            if ((_state != UiState.NeedDiscard && _state != UiState.MeldDecision) || _round.CurrentSeat != MySeat || !_round.CanNaturalPong())
            {
                return;
            }

            _naturalPongNumber = TripleNumber(_round.Players[MySeat].Hand);
            var laid = _round.Players[MySeat].Hand.Cards.Where(c => c.Number == _naturalPongNumber).Take(3).ToList();
            var rest = _round.Players[MySeat].Hand.Cards.Count(c => c.Number != _naturalPongNumber);

            if (rest == 0)
            {
                // 3장 전부 같은 숫자 → 손 소진 자연뽕 종료
                _state = UiState.Resolving;
                _round = _round.NaturalPong(_naturalPongNumber, null);
                _table.AddGroup(laid);
                _table.PongFx($"{SeatName(MySeat)}\n{_naturalPongNumber}자연뽕!");
                EndRound(RoundSettlement.SettleByHandClear(_round, MySeat), $"{SeatName(MySeat)} 자연뽕 손 털기", MySeat);
                return;
            }

            // 선언 즉시 3장 내려놓기(뽕과 동일한 흐름)
            _pendingLaid.Clear();
            _pendingLaid.AddRange(laid);
            _table.AddGroup(laid);
            _table.PongFx($"{SeatName(MySeat)}\n{_naturalPongNumber}자연뽕!");

            _state = UiState.NaturalPongSelect;
            SetLog($"자연뽕! {_naturalPongNumber} 외 버릴 카드 클릭");
            Refresh();
            _table.SetPrompt($"자연뽕! {_naturalPongNumber} 외 버릴 카드 클릭");
        }

        private void OnNext()
        {
            if (_state != UiState.RoundOver && _state != UiState.SetOver)
            {
                return; // 더블클릭 가드
            }

            if (_state == UiState.SetOver)
            {
                _game = GameState.Start(PlayerCount);
                _roundIndex = 0;
                _dealerSeat = 0;
                _roundHistory.Clear();
            }

            StartRound();
        }

        /// <summary>게임 종료 화면에서 로비로 복귀. 테이블 UI 전체 파기 후 로비 재생성.</summary>
        private void OnLobby()
        {
            if (_state != UiState.SetOver)
            {
                return;
            }

            new GameObject("Lobby", typeof(LobbyBootstrap));
            Destroy(_table.CanvasGo);
            Destroy(gameObject);
        }

        private void OnCardClicked(Card card)
        {
            switch (_state)
            {
                case UiState.NeedDiscard:
                case UiState.MeldDecision: // 족보 선언 대신 버리고 계속
                    if (_round.CurrentSeat != MySeat)
                    {
                        return;
                    }

                    _state = UiState.Resolving; // 더블클릭 → 두 장 버림 방지
                    _round = _round.Discard(card);
                    SetLog($"내 버림 {CardLabel(card)}");
                    _table.PlayDiscardSfx();
                    _table.AddDiscard(card);
                    _turnGap = true; // BotLoop 진입 시 간격 소화
                    TryBotPong(MySeat);
                    RunBots();
                    break;

                case UiState.PongDiscardSelect:
                    if (_pendingLaid.Contains(card))
                    {
                        return; // 내려놓은 2장만 금지 — 같은 숫자 3장째는 버릴 수 있음
                    }

                    _state = UiState.Resolving;
                    _round = _round.Pong(MySeat, card); // 내려놓기는 선언 시 이미 표시됨
                    _pendingLaid.Clear();
                    SetLog($"뽕 완료. {_pongNumber} 3장 고정");
                    _table.PlayDiscardSfx();
                    _table.AddDiscard(card);
                    if (_round.Players[MySeat].Hand.Count == 0)
                    {
                        // 추가 버림까지 내고 손이 비면 손 털기 종료
                        EndRound(RoundSettlement.SettleByTwoPong(_round, MySeat, _pongDiscarderSeat), $"{SeatName(MySeat)} 손 털기 · {SeatName(_pongDiscarderSeat)} 박 +20", MySeat);
                        break;
                    }

                    _turnGap = true;
                    RunBots();
                    break;

                case UiState.NaturalPongSelect:
                    if (_pendingLaid.Contains(card))
                    {
                        return; // 내려놓은 3장만 금지 — 같은 숫자 4장째는 버릴 수 있음
                    }

                    _state = UiState.Resolving;
                    _round = _round.NaturalPong(_naturalPongNumber, card); // 내려놓기는 선언 시 이미 표시됨
                    _pendingLaid.Clear();
                    SetLog($"자연뽕 완료. {_naturalPongNumber} 3장 고정");
                    _table.PlayDiscardSfx();
                    _table.AddDiscard(card);
                    _turnGap = true;
                    RunBots();
                    break;
            }
        }

        // ── 뷰 공급 ──

        private void Refresh()
        {
            _table.Render(BuildRoundView(), _pendingLaid, _state == UiState.NaturalPongSelect);
            _nextBtn.gameObject.SetActive(_state == UiState.RoundOver || _state == UiState.SetOver);
            GameTableView.SetButtonLabel(_nextBtn, _state == UiState.SetOver ? "새 게임" : "다음 판");
            _lobbyBtn.gameObject.SetActive(_state == UiState.SetOver);
        }

        /// <summary>로컬 코어 상태 → 공용 뷰 입력(RoundView) 합성. 서버 GameSession.ToView와 같은 의미.</summary>
        private RoundView BuildRoundView()
        {
            var myTurn = _round.CurrentSeat == MySeat;
            var phase = _turnGap ? RoundPhase.TurnGap : _state switch
            {
                UiState.StopDecision => RoundPhase.WaitingStop,
                UiState.PongWindow => RoundPhase.PongWindow,
                UiState.PongDiscardSelect => RoundPhase.WaitingPongDiscard,
                UiState.RoundOver => RoundPhase.RoundOver,
                UiState.SetOver => RoundPhase.SetOver,
                _ => RoundPhase.WaitingDiscard // NeedDiscard/MeldDecision/NaturalPongSelect/Resolving
            };

            var canNatural = (_state == UiState.NeedDiscard || _state == UiState.MeldDecision)
                && myTurn && _round.CanNaturalPong();
            return new RoundView
            {
                mySeat = MySeat,
                currentSeat = _round.CurrentSeat,
                phase = phase,
                actorSeat = _state == UiState.PongDiscardSelect ? MySeat : _round.CurrentSeat,
                drawPileCount = _round.DrawPile.Count,
                reshuffleCount = _round.ReshuffleCount,
                pongNumber = _pongNumber,
                canStop = _state == UiState.StopDecision,
                canMeld = _state == UiState.MeldDecision,
                meldType = _state == UiState.MeldDecision ? _pendingMeld.Type.ToString() : "",
                meldScore = _state == UiState.MeldDecision ? _pendingMeld.Score : 0,
                canNaturalPong = canNatural,
                naturalPongNumber = canNatural ? TripleNumber(_round.Players[MySeat].Hand) : 0,
                canPong = _state == UiState.PongWindow,
                myHand = CardDto.FromAll(_round.Players[MySeat].Hand.Cards),
                seats = Enumerable.Range(0, PlayerCount).Select(s => new SeatView
                {
                    seat = s,
                    nickname = _names[s],
                    handCount = _round.Players[s].Hand.Count,
                    pongCount = _round.Players[s].PongCount,
                    hasPonged = _round.Players[s].HasPonged,
                    cumulativeDebt = _game.CumulativeDebts[s]
                }).ToArray()
            };
        }

        // ── 헬퍼 ──

        private int TripleNumber(Hand hand) => hand.Cards.GroupBy(c => c.Number).First(g => g.Count() >= 3).Key;

        private int TopDiscardNumber() => _round.DiscardPile[_round.DiscardPile.Count - 1].Number;

        private string CardLabel(Card c) => TableArt.CardLabel(c);

        private string SeatName(int seat) => seat == MySeat ? $"{_names[seat]}(나)" : _names[seat];

        private void SetLog(string message) => Debug.Log($"[BBONG {Time.time:F2}] {message.Replace("\n", " | ")}"); // 콘솔 전용, 타이밍 튜닝용 경과초 포함
    }
}
