using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Ai;
using BbongCore.Cards;
using BbongCore.Config;
using BbongCore.Game;
using BbongCore.Rules;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 코드 생성 카드 테이블 + 게임 흐름(Phase 3). 2~6인(사람 P0 + 봇).
    /// 드로우/버림 · 뽕/자연뽕 · 스톱 · 게임(5판) 점수/판돈. 봇 턴은 코루틴으로 천천히 진행.
    /// 인원·판돈은 로비(LobbyBootstrap)에서 Start 전에 설정. 단독 사용 시 기본 4인·판돈 1000.
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

        public int Stake { get; set; } = 1000;

        public BotDifficulty Difficulty { get; set; } = BotDifficulty.Normal;

        // 색약 안전 팔레트(Okabe-Ito 기반). 색은 보조, 도형이 주 구분 수단.
        private static readonly Color[] Palette =
        {
            new Color(0.835f, 0.369f, 0.000f), // Red  → 주황빨강(vermillion)
            new Color(0.000f, 0.447f, 0.698f), // Blue → 진파랑
            new Color(0.000f, 0.620f, 0.451f), // Green→ 청록
            new Color(0.902f, 0.624f, 0.000f)  // Yellow→ 호박색(amber)
        };

        private static readonly string[] ColorLetter = { "R", "B", "G", "Y" };

        // 정렬 색 순위: 빨(0)·파(1)·노(2)·초(3). enum 순서(Red0,Blue1,Green2,Yellow3) → 순위.
        private static readonly int[] ColorRank = { 0, 1, 3, 2 };

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

        private Font _font;
        private GameObject _canvasGo;
        private Transform _seatsArea;        // 좌석 패널(타원 배치) 컨테이너
        private Transform _discardRow;
        private Transform _handRow;
        private Text _prompt;                // 내 차례 안내 문구(손패 위)
        private Text _endReason;             // 판 종료 사유(버림 더미 아래)
        private GameObject _scorePopup;      // 판 종료 중앙 전광판(표)
        private Text _scoreTitle;
        private Transform _scoreGrid;        // 표 셀 컨테이너(GridLayout)
        private CanvasGroup _scorePopupGroup;
        private Coroutine _scoreFade;
        private readonly List<int[]> _roundHistory = new(); // 게임 내 판별 점수
        private Button _stopBtn, _pongBtn, _passBtn, _naturalBtn, _meldBtn, _nextBtn, _lobbyBtn;
        private MeldResult _pendingMeld; // 족보 선언 대기 중인 족보(MeldDecision)

        private AudioSource _audio;
        private AudioClip _sfxDraw, _sfxDiscard, _sfxPong, _sfxStop, _sfxShuffle;
        private Image _flash;
        private Text _callout;               // 뽕/자연뽕 콜아웃("8뽕!")
        private CanvasGroup _calloutGroup;
        private Coroutine _calloutFx;
        // 버림 타임라인: 발생 순서대로. group=true면 뽕/자연뽕 고정 패(위쪽 정렬), false면 단일 버림(중앙 더미).
        // pos/rot은 버릴 때 1회 추첨(매 Refresh마다 더미가 들썩이지 않도록 고정).
        private readonly List<(List<Card> cards, bool group, Vector2 pos, float rot)> _timeline = new();
        private int _timelineShown;
        private List<Card> _meldSet; // 족보 완성 시 6장(버림 비우고 표시)
        // 뽕/자연뽕 선언 직후 내려놓은 카드(시각 전용). 코어는 버림 선택 후 한 번에 반영되므로
        // 그 사이 손패 표시에서 숨겨 "선언 → 즉시 내려놓기 → 버림" 흐름을 만든다.
        private readonly List<Card> _pendingLaid = new();

        private readonly Sprite[] _cardBg = new Sprite[4];  // 색별 둥근 그라데이션 카드 배경
        private Sprite _haloSprite;                          // 마지막 버림 강조용 흰 둥근 스프라이트

        private void Start()
        {
            _seed = Random.Range(1, 1_000_000); // Play마다 다른 패(고정 시드 버그 수정)
            _font = Resources.Load<Font>("Fonts/Pretendard-SemiBold")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            EnsureEventSystem();
            BuildUi();
            PlayerWallet.Pay(Stake); // 입장 시 판돈 에스크로(rules.md §9)
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
            _timeline.Clear();
            _timelineShown = 0;
            _meldSet = null;
            _pendingLaid.Clear();
            _endReason.text = "";
            if (_scorePopup != null)
            {
                _scorePopup.SetActive(false);
            }

            SetLog($"{_roundIndex + 1}판 시작.");
            RunBots();
        }

        private void EndRound(int[] scores, string reason, int enderSeat)
        {
            _endReason.text = reason; // 버림 더미 아래에 종료 사유 표시
            PlayStop();
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
                var payouts = StakePot.Distribute(Stake, PlayerCount, winners); // 공동 1등은 균등 분배(나머지 절사)
                PlayerWallet.Receive(payouts[MySeat]);
                var who = string.Join(", ", winners.Select(s => _names[s]));
                SetLog($"━━ 게임 종료(5판) ━━ 사유: {reason} → 다음 선 P{enderSeat} | 1등 {who} 판돈 P{MySeat}={payouts[MySeat]} 보유 {PlayerWallet.Balance:N0} | 점수[{detail}] 누적[{cumulative}]");
                title = $"게임 종료 — 1등 {who}";
                _state = UiState.SetOver;
            }
            else
            {
                SetLog($"━━ {_roundIndex}판 종료 ━━ 사유: {reason} → 다음 선 P{enderSeat} | 점수[{detail}] 누적[{cumulative}]");
                title = $"{_roundIndex}판 종료";
                _state = UiState.RoundOver;
            }

            ShowScorePopup(title);
            Refresh();
        }

        /// <summary>중앙 점수표: 헤더 + 판별 점수 행 + 합계 행. 내용 크기에 맞춰 정중앙 배치, 잠시 후 페이드아웃.</summary>
        private void ShowScorePopup(string title)
        {
            _scoreTitle.text = title;

            var cols = PlayerCount + 1;
            var rows = _roundHistory.Count + 2; // 헤더 + 판별 + 합계
            ((RectTransform)_scorePopup.transform).sizeDelta = new Vector2(
                cols * 180f + (cols - 1) * 6f + 40f,
                rows * 48f + (rows - 1) * 6f + 80f + 32f);

            foreach (Transform child in _scoreGrid)
            {
                Destroy(child.gameObject);
            }

            // 헤더(닉네임은 셀에 맞게 작은 글씨)
            AddCell("판수", true);
            for (var s = 0; s < PlayerCount; s++)
            {
                AddCell($"{_names[s]}{(s == MySeat ? "*" : "")}", true, 22, fit: true);
            }

            // 판별 점수
            for (var r = 0; r < _roundHistory.Count; r++)
            {
                AddCell($"{r + 1}", true);
                for (var s = 0; s < PlayerCount; s++)
                {
                    AddCell($"{_roundHistory[r][s]}", false);
                }
            }

            // 합계
            AddCell("계", true);
            for (var s = 0; s < PlayerCount; s++)
            {
                AddCell($"{_game.CumulativeDebts[s]}", true);
            }

            _scorePopup.SetActive(true);
            _scorePopupGroup.alpha = 1f;

            if (_scoreFade != null)
            {
                StopCoroutine(_scoreFade);
                _scoreFade = null;
            }

            // 판 종료만 페이드+자동 진행. 게임 종료(SetOver)는 '새 게임' 전까지 계속 표시.
            if (_state == UiState.RoundOver)
            {
                _scoreFade = StartCoroutine(FadeScorePopup());
            }
        }

        private void AddCell(string text, bool emphasize, int size = 30, bool fit = false)
        {
            var t = CreateText(_scoreGrid, text, size, TextAnchor.MiddleCenter); // 셀 중앙 정렬(박스와 표 정렬 일치)
            t.color = emphasize ? new Color(1f, 0.9f, 0.4f) : Color.white;
            if (emphasize)
            {
                t.fontStyle = FontStyle.Bold;
            }

            if (fit)
            {
                FitText(t, 12, size);
            }
        }

        /// <summary>긴 닉네임 대응: 영역에 맞게 글씨 자동 축소.</summary>
        private static void FitText(Text t, int min, int max)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = min;
            t.resizeTextMaxSize = max;
        }

        private IEnumerator FadeScorePopup()
        {
            yield return new WaitForSeconds(5f); // 충분히 보이도록
            for (var t = 0f; t < 1f; t += Time.deltaTime / 1.2f)
            {
                _scorePopupGroup.alpha = 1f - t;
                yield return null;
            }

            _scorePopup.SetActive(false);

            // 전광판이 닫히면 다음 판 자동 시작(게임 종료 시엔 '새 게임' 버튼 유지)
            if (_state == UiState.RoundOver)
            {
                OnNext();
            }
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
                    _meldSet = Sorted(_round.CurrentPlayer.Hand.Cards); // 족보: 버림 비우고 표시
                    ShowCallout($"{SeatName(seat)}\n{MeldName(meld.Type)}!");
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
                        AddGroup(laid);
                        PlayPong($"{SeatName(seat)}\n{number}자연뽕!");
                        EndRound(RoundSettlement.SettleByHandClear(_round, seat), $"{SeatName(seat)} 자연뽕 손 털기", seat);
                        yield break;
                    }

                    var toss = _bots[seat].ChoosePongDiscard(rest);
                    _round = _round.NaturalPong(number, toss);
                    SetLog($"P{seat} 자연뽕! {number} 3장 고정");
                    PlayPong($"{SeatName(seat)}\n{number}자연뽕!");
                    AddGroup(laid);
                    Refresh();
                    yield return new WaitForSeconds(TossDelay); // 내려놓기 먼저, 버림은 한 박자 뒤

                    PlayDiscard();
                    AddDiscard(toss);
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
                PlayDiscard();
                AddDiscard(discard);
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
            var rest = new Hand(_round.Players[seat].Hand.Cards.Where(c => c.Number != number));
            if (rest.Count == 0)
            {
                _round = _round.Pong(seat, null);
                PlayPong($"{SeatName(seat)}\n{number}뽕!");
                AddGroup(laid);
                EndRound(RoundSettlement.SettleByTwoPong(_round, seat, discarderSeat), $"{SeatName(seat)} 손 털기 · {SeatName(discarderSeat)} 박 +20", seat);
                return;
            }

            var toss = _bots[seat].ChoosePongDiscard(rest);
            _round = _round.Pong(seat, toss); // 코어는 즉시 반영, 버림 표시만 지연
            SetLog($"P{seat} 뽕! {number} 3장 고정");
            PlayPong($"{SeatName(seat)}\n{number}뽕!");
            AddGroup(laid);
            Refresh();
            StartCoroutine(TossAfterPong(toss));
        }

        /// <summary>봇 뽕의 추가 버림을 한 박자 뒤에 표시(내려놓기 → 버림 단계 연출).</summary>
        private IEnumerator TossAfterPong(Card toss)
        {
            yield return new WaitForSeconds(TossDelay);
            if (_state == UiState.RoundOver || _state == UiState.SetOver)
            {
                yield break; // 그 사이 판이 끝났으면 다음 판에서 더미가 리셋됨
            }

            PlayDiscard();
            AddDiscard(toss);
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
            _pongTimer = StartCoroutine(PongCountdown());
        }

        private IEnumerator PongCountdown()
        {
            for (var t = PongWindowSeconds; t > 0; t--)
            {
                SetButtonLabel(_pongBtn, $"뽕! ({t})");
                yield return new WaitForSeconds(1f);
            }

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

            SetButtonLabel(_pongBtn, "뽕!");
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
                var kept = new List<(List<Card> cards, bool group, Vector2 pos, float rot)>();
                (List<Card> cards, bool group, Vector2 pos, float rot)? lastSingle = null;
                foreach (var entry in _timeline)
                {
                    if (entry.group)
                    {
                        kept.Add(entry);
                    }
                    else
                    {
                        lastSingle = entry;
                    }
                }

                if (lastSingle != null)
                {
                    kept.Add(lastSingle.Value);
                }

                _timeline.Clear();
                _timeline.AddRange(kept);
                _timelineShown = _timeline.Count;

                SetLog("바닥 더미 소진 → 버림 더미 재셔플(맨 위 1장 유지)");
                ShowCallout("더미 셔플!");
                Flash(new Color(1f, 1f, 1f, 0.3f));
                _audio.PlayOneShot(_sfxShuffle, 0.7f);
                Refresh();
            }
        }

        /// <summary>내 턴 자동 드로우 → 족보면 종료, 아니면 버림 대기(NeedDiscard).</summary>
        private void AutoDrawMe()
        {
            if (!_round.CanDraw)
            {
                EndRound(RoundSettlement.SettleByExhaustion(_round), "바닥 더미 소진(재셔플 2회 초과) → 강제 종료", MySeat);
                return;
            }

            DrawCard();
            PlayDraw();
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

        private static string MeldName(MeldType type) => type switch
        {
            MeldType.Ttoittoi => "또이또이",
            MeldType.Straight => "스트레이트",
            MeldType.TenOrUnder => "10이하",
            MeldType.SixtySixOrOver => "66이상",
            MeldType.Chongtong => "총통",
            _ => type.ToString()
        };

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
            ShowCallout(StopResolver.IsBagaji(_round, stopSeat)
                ? $"{SeatName(stopSeat)}\n바가지!"
                : $"{SeatName(stopSeat)}\n스톱!");

        private void OnPong()
        {
            if (_state != UiState.PongWindow)
            {
                return;
            }

            StopPongTimer();
            var rest = new Hand(_round.Players[MySeat].Hand.Cards.Where(c => c.Number != _pongNumber));
            if (rest.Count == 0)
            {
                var laid = _round.Players[MySeat].Hand.Cards.Where(c => c.Number == _pongNumber).Take(2).ToList();
                _round = _round.Pong(MySeat, null);
                PlayPong($"{SeatName(MySeat)}\n{_pongNumber}뽕!");
                AddGroup(laid);
                EndRound(RoundSettlement.SettleByTwoPong(_round, MySeat, _pongDiscarderSeat), $"{SeatName(MySeat)} 손 털기 · {SeatName(_pongDiscarderSeat)} 박 +20", MySeat);
                return;
            }

            // 실제 판처럼 "뽕!" 외치는 순간 3장 고정분을 즉시 내려놓고, 버림은 그다음 동작
            var pongLaid = _round.Players[MySeat].Hand.Cards.Where(c => c.Number == _pongNumber).Take(2).ToList();
            _pendingLaid.Clear();
            _pendingLaid.AddRange(pongLaid);
            AddGroup(pongLaid);
            PlayPong($"{SeatName(MySeat)}\n{_pongNumber}뽕!");

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
            _meldSet = Sorted(_round.Players[MySeat].Hand.Cards); // 족보: 버림 비우고 표시
            ShowCallout($"{SeatName(MySeat)}\n{MeldName(_pendingMeld.Type)}!");
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
                AddGroup(laid);
                PlayPong($"{SeatName(MySeat)}\n{_naturalPongNumber}자연뽕!");
                EndRound(RoundSettlement.SettleByHandClear(_round, MySeat), $"{SeatName(MySeat)} 자연뽕 손 털기", MySeat);
                return;
            }

            // 선언 즉시 3장 내려놓기(뽕과 동일한 흐름)
            var naturalLaid = _round.Players[MySeat].Hand.Cards.Where(c => c.Number == _naturalPongNumber).Take(3).ToList();
            _pendingLaid.Clear();
            _pendingLaid.AddRange(naturalLaid);
            AddGroup(naturalLaid);
            PlayPong($"{SeatName(MySeat)}\n{_naturalPongNumber}자연뽕!");

            _state = UiState.NaturalPongSelect;
            SetLog($"자연뽕! {_naturalPongNumber} 외 버릴 카드 클릭");
            Refresh();
        }

        private void OnNext()
        {
            if (_state != UiState.RoundOver && _state != UiState.SetOver)
            {
                return; // 더블클릭 가드
            }

            if (_state == UiState.SetOver)
            {
                if (!PlayerWallet.CanAfford(Stake))
                {
                    return; // 버튼 비활성과 이중 방어
                }

                PlayerWallet.Pay(Stake); // 새 게임도 판돈 다시 걸기
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
            Destroy(_canvasGo);
            Destroy(gameObject);
        }

        private void OnCardClicked(Card card)
        {
            switch (_state)
            {
                case UiState.NeedDiscard:
                case UiState.MeldDecision: // 족보 선언 대신 버리고 계속
                    _state = UiState.Resolving; // 더블클릭 → 두 장 버림 방지
                    _round = _round.Discard(card);
                    SetLog($"내 버림 {CardLabel(card)}");
                    PlayDiscard();
                    AddDiscard(card);
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
                    PlayDiscard();
                    AddDiscard(card);
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
                    PlayDiscard();
                    AddDiscard(card);
                    _turnGap = true;
                    RunBots();
                    break;
            }
        }

        // ── 렌더링 ──

        private void Refresh()
        {
            RenderSeats();
            RenderDiscard();
            RenderHand(_round.Players[MySeat].Hand);

            _prompt.text = _state switch
            {
                // 내 차례(드로우 완료)일 때만. 봇 턴 중 stale NeedDiscard 방지.
                UiState.NeedDiscard when _round.CurrentSeat == MySeat =>
                    _round.CanNaturalPong() ? "버릴 카드를 선택하세요 (또는 자연뽕)" : "버릴 카드를 선택하세요",
                UiState.MeldDecision => $"족보 완성! [{MeldName(_pendingMeld.Type)}] 선언하거나 버리고 계속하세요",
                UiState.PongDiscardSelect => "뽕! 버릴 카드를 선택하세요",
                UiState.NaturalPongSelect => "자연뽕! 버릴 카드를 선택하세요",
                UiState.StopDecision => "스톱 또는 계속을 선택하세요",
                UiState.PongWindow => "뽕 하시겠습니까?",
                _ => ""
            };

            _stopBtn.gameObject.SetActive(_state == UiState.StopDecision);
            _meldBtn.gameObject.SetActive(_state == UiState.MeldDecision);
            _naturalBtn.gameObject.SetActive((_state == UiState.NeedDiscard || _state == UiState.MeldDecision)
                && _round.CurrentSeat == MySeat && _round.CanNaturalPong());
            _pongBtn.gameObject.SetActive(_state == UiState.PongWindow);
            _passBtn.gameObject.SetActive(_state == UiState.PongWindow || _state == UiState.StopDecision);
            SetButtonLabel(_passBtn, _state == UiState.StopDecision ? "계속" : "패스");
            // 새 게임은 판돈을 다시 걸 수 있을 때만
            _nextBtn.gameObject.SetActive(_state == UiState.RoundOver
                || (_state == UiState.SetOver && PlayerWallet.CanAfford(Stake)));
            SetButtonLabel(_nextBtn, _state == UiState.SetOver ? "새 게임" : "다음 판");
            _lobbyBtn.gameObject.SetActive(_state == UiState.SetOver);
        }

        /// <summary>
        /// 좌석 패널: 테이블 중심 타원 위에 360°/인원수 간격으로 배치.
        /// 나(P0)=아래, 좌석 순서대로 반시계 방향(2인: P1 위 / 4인: P1 오른쪽·P2 위·P3 왼쪽).
        /// 이름 옆 누적 점수, 현재 턴은 노란 배경.
        /// </summary>
        private void RenderSeats()
        {
            foreach (Transform child in _seatsArea)
            {
                Destroy(child.gameObject);
            }

            var center = new Vector2(0.50f, 0.58f);
            var radius = new Vector2(0.40f, 0.30f);

            // 뽕 대기 중엔 아무도 포커싱하지 않고, 뽕을 실제 선언했을 때만 선언자를 포커싱.
            // 창이 시간 초과로 닫히면 상태가 바뀌면서 원래 턴 대상자에게 포커스가 넘어간다.
            var focusSeat = _turnGap || _state == UiState.PongWindow ? -1
                : _state == UiState.PongDiscardSelect ? MySeat
                : _round.CurrentSeat;

            for (var seat = 0; seat < PlayerCount; seat++)
            {
                var angle = (-90f + seat * 360f / PlayerCount) * Mathf.Deg2Rad;
                var anchor = center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y);

                var mine = seat == MySeat;
                var highlight = focusSeat == seat;
                var panel = CreatePanel(_seatsArea, highlight ? new Color(0.9f, 0.8f, 0.2f, 0.55f) : new Color(0, 0, 0, 0.35f));
                var rt = panel.rectTransform;
                rt.anchorMin = rt.anchorMax = anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(250f, mine ? 80f : 150f); // 내 손패는 아래에 보이므로 이름표만

                // 닉네임 1줄 + 빚 1줄(긴 닉네임의 어색한 개행 방지)
                var t = CreateText(panel.transform, $"{SeatName(seat)}\n빚: {_game.CumulativeDebts[seat]}", 24, TextAnchor.MiddleCenter);
                FitText(t, 16, 24);
                if (mine)
                {
                    Stretch(t.rectTransform);
                    continue;
                }

                Anchor(t.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 1f));

                // 닉네임 아래 뒤집힌 카드로 손패 수 표현(겹침 없이 나란히, 6장까지 수용)
                var count = _round.Players[seat].Hand.Count;
                const float bw = 36f, bh = 50f, step = bw + 3f;
                var total = (count - 1) * step + bw;
                for (var j = 0; j < count; j++)
                {
                    var back = CreatePanel(panel.transform, Color.white);
                    back.sprite = UiArt.CardBack;
                    back.type = Image.Type.Simple; // 격자 패턴 왜곡 없이 통째로 축소
                    var brt = back.rectTransform;
                    brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.31f);
                    brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.sizeDelta = new Vector2(bw, bh);
                    brt.anchoredPosition = new Vector2(-total / 2f + bw / 2f + j * step, 0f);
                }
            }
        }

        private void RenderHand(Hand hand)
        {
            foreach (Transform child in _handRow)
            {
                Destroy(child.gameObject);
            }

            foreach (var card in Sorted(hand.Cards))
            {
                if (_pendingLaid.Contains(card))
                {
                    continue; // 뽕/자연뽕 선언으로 이미 내려놓은 카드(코어 반영 전)
                }

                var go = CreateCardFace(_handRow, card, 130, 200);
                var captured = card;
                go.AddComponent<Button>().onClick.AddListener(() => OnCardClicked(captured));
            }
        }

        // ── UI 생성 ──

        private void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // 폰 가로(16:9 기준, 넓은 화면은 여유 확장)
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // 화면비 달라도 글씨/카드 비대 방지
            var root = canvasGo.transform;

            GenerateArt();

            var felt = CreatePanel(root, Color.white);
            felt.sprite = UiArt.Backdrop; // 메뉴 화면과 톤 통일(네이비). 카드/더미는 그 위에.
            Stretch(felt.rectTransform);

            // 좌석 패널: 테이블 중심 기준 타원 배치(나=아래, 반시계 방향)
            var seatsGo = new GameObject("SeatsArea", typeof(RectTransform));
            seatsGo.transform.SetParent(root, false);
            _seatsArea = seatsGo.transform;
            Stretch((RectTransform)_seatsArea);

            // 버림 더미/내려놓은 패 영역(수동 배치 — 겹침/펼침 제어). 테이블 중앙.
            var discardGo = new GameObject("DiscardArea", typeof(RectTransform));
            discardGo.transform.SetParent(root, false);
            _discardRow = discardGo.transform;
            Anchor((RectTransform)_discardRow, new Vector2(0.20f, 0.42f), new Vector2(0.80f, 0.72f));

            // 판 종료 사유(내 좌석 패널 바로 위, 다음 판 시작 시 비움)
            _endReason = CreateText(root, "", 32, TextAnchor.MiddleCenter);
            _endReason.fontStyle = FontStyle.Bold;
            _endReason.color = new Color(1f, 1f, 1f, 0.9f);
            FitText(_endReason, 20, 32);
            Anchor(_endReason.rectTransform, new Vector2(0.25f, 0.325f), new Vector2(0.75f, 0.39f));

            // 내 차례 안내 문구(버튼 위)
            _prompt = CreateText(root, "", 44, TextAnchor.MiddleCenter);
            _prompt.fontStyle = FontStyle.Bold;
            _prompt.color = new Color(1f, 0.92f, 0.4f);
            Anchor(_prompt.rectTransform, new Vector2(0.20f, 0.335f), new Vector2(0.80f, 0.41f));

            // 액션 버튼: 손패 우측(시기에 따라 표시, 동시 노출 최대 2개)
            var bar = CreateRow(root, new Vector2(0.80f, 0.04f), new Vector2(0.995f, 0.21f), 14).transform;
            _stopBtn = CreateButton(bar, "스톱", OnStop);
            _meldBtn = CreateButton(bar, "족보 선언!", OnMeldDeclare);
            _naturalBtn = CreateButton(bar, "자연뽕", OnNaturalPong);
            _pongBtn = CreateButton(bar, "뽕!", OnPong);
            _passBtn = CreateButton(bar, "패스", OnPass);
            _nextBtn = CreateButton(bar, "다음 판", OnNext);
            _lobbyBtn = CreateButton(bar, "로비로", OnLobby);

            // 내 손패: 화면 가로 정중앙 기준 가운데 정렬(카드 수에 따라 유동).
            // 전폭 컨테이너라 배경·클릭 차단은 제거(우측 버튼 영역과 겹치므로).
            var handPanel = CreateRow(root, new Vector2(0f, 0.02f), new Vector2(1f, 0.225f), 12);
            handPanel.color = new Color(0, 0, 0, 0);
            handPanel.raycastTarget = false;
            _handRow = handPanel.transform;

            // 판 종료 점수표(이름+점수 한 그룹). 화면 정중앙, 크기는 내용에 맞춰 ShowScorePopup에서 설정.
            var popupBg = CreatePanel(root, new Color(0.05f, 0.05f, 0.08f, 0.97f)); // 뒤 카드가 비치지 않게 거의 불투명
            _scorePopup = popupBg.gameObject;
            var popupRt = popupBg.rectTransform;
            popupRt.anchorMin = popupRt.anchorMax = new Vector2(0.5f, 0.60f); // 살짝 위 — 아래 종료 사유와 분리
            popupRt.pivot = new Vector2(0.5f, 0.5f);
            popupBg.raycastTarget = false;
            _scorePopupGroup = _scorePopup.AddComponent<CanvasGroup>();
            _scorePopupGroup.blocksRaycasts = false;
            _scorePopupGroup.interactable = false;

            _scoreTitle = CreateText(popupBg.transform, "", 36, TextAnchor.MiddleCenter);
            _scoreTitle.fontStyle = FontStyle.Bold;
            FitText(_scoreTitle, 20, 36);
            var titleRt = _scoreTitle.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(0f, -80f);
            titleRt.offsetMax = Vector2.zero;

            var gridGo = new GameObject("ScoreGrid", typeof(RectTransform));
            gridGo.transform.SetParent(popupBg.transform, false);
            _scoreGrid = gridGo.transform;
            var gridRt = (RectTransform)_scoreGrid;
            gridRt.anchorMin = Vector2.zero;
            gridRt.anchorMax = Vector2.one;
            gridRt.offsetMin = new Vector2(20f, 16f);
            gridRt.offsetMax = new Vector2(-20f, -80f);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(6, 6);
            grid.cellSize = new Vector2(180, 48); // 닉네임(최대 8자) 수용 폭
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = PlayerCount + 1; // 라벨 칸 + 플레이어

            _scorePopup.SetActive(false);

            // 뽕/자연뽕/족보 콜아웃("P1 8뽕!") — 더미 위 중앙에 크게 떴다 사라짐
            _callout = CreateText(root, "", 100, TextAnchor.MiddleCenter);
            _callout.fontStyle = FontStyle.Bold;
            _callout.color = new Color(1f, 0.92f, 0.35f);
            _callout.raycastTarget = false;
            FitText(_callout, 40, 100); // 긴 닉네임도 한 화면에
            AddOutline(_callout);
            Anchor(_callout.rectTransform, new Vector2(0.25f, 0.43f), new Vector2(0.75f, 0.62f));
            _calloutGroup = _callout.gameObject.AddComponent<CanvasGroup>();
            _calloutGroup.alpha = 0f;
            _calloutGroup.blocksRaycasts = false;
            _calloutGroup.interactable = false;

            // 전체 화면 플래시(연출용, 클릭 막지 않음)
            _flash = CreatePanel(root, new Color(1, 1, 1, 0));
            Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;
            _flash.transform.SetAsLastSibling();

            // 오디오 + 절차적 효과음
            _audio = canvasGo.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _sfxDraw = Tone("draw", 880f, 0.06f, 24f);
            _sfxDiscard = Tone("discard", 300f, 0.12f, 16f);
            _sfxPong = Noise("pong", 0.16f, 42f);   // "찰싹" 노이즈 버스트
            _sfxStop = Tone("stop", 520f, 0.28f, 7f);
            _sfxShuffle = Noise("shuffle", 0.35f, 10f); // "쏴아" 카드 섞는 소리
        }

        // ── 연출/사운드 ──

        private void PlayDraw() => _audio.PlayOneShot(_sfxDraw, 0.5f);
        private void PlayDiscard() => _audio.PlayOneShot(_sfxDiscard, 0.5f);
        private void PlayStop() => _audio.PlayOneShot(_sfxStop, 0.6f);

        private void PlayPong(string callout)
        {
            _audio.PlayOneShot(_sfxPong, 0.8f);
            Flash(new Color(1f, 0.95f, 0.4f, 0.5f));
            ShowCallout(callout);
        }

        private void ShowCallout(string message)
        {
            _callout.text = message;
            if (_calloutFx != null)
            {
                StopCoroutine(_calloutFx);
            }

            _calloutFx = StartCoroutine(CalloutFx());
        }

        /// <summary>콜아웃 연출: 크게 등장 → 잠시 유지 → 페이드아웃.</summary>
        private IEnumerator CalloutFx()
        {
            _calloutGroup.alpha = 1f;
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.15f)
            {
                _callout.transform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, t);
                yield return null;
            }

            _callout.transform.localScale = Vector3.one;
            yield return new WaitForSeconds(0.7f);

            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                _calloutGroup.alpha = 1f - t;
                yield return null;
            }

            _calloutGroup.alpha = 0f;
        }

        /// <summary>숫자 오름차순 → 같은 숫자는 빨·파·노·초 순으로 정렬.</summary>
        private static List<Card> Sorted(IEnumerable<Card> cards) =>
            cards.OrderBy(c => c.Number).ThenBy(c => ColorRank[(int)c.Color]).ToList();

        /// <summary>중앙 밀집 무작위(삼각분포). 균등분포보다 자연스러운 무더기를 만듭니다.</summary>
        private static float Tri(float range) => (Random.Range(-range, range) + Random.Range(-range, range)) / 2f;

        /// <summary>단일 버림: 중앙 더미에 무작위 위치·기울기로 던져 놓기(실제 카드판 느낌).</summary>
        private void AddDiscard(Card card) => _timeline.Add((new List<Card> { card }, false,
            new Vector2(Tri(150f), Tri(50f)), Tri(28f)));

        /// <summary>뽕/자연뽕 고정 패: 더미 위로 같이 던짐(한 지점에 살짝 펼쳐서).</summary>
        private void AddGroup(IEnumerable<Card> cards) => _timeline.Add((Sorted(cards), true,
            new Vector2(Tri(120f), Tri(45f)), Tri(16f)));

        private void Flash(Color color)
        {
            _flash.color = color;
            StartCoroutine(FadeFlash());
        }

        private IEnumerator FadeFlash()
        {
            var start = _flash.color;
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
            {
                _flash.color = new Color(start.r, start.g, start.b, Mathf.Lerp(start.a, 0f, t));
                yield return null;
            }

            _flash.color = new Color(start.r, start.g, start.b, 0f);
        }

        private IEnumerator ScalePop(Transform target)
        {
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.15f)
            {
                var s = Mathf.Lerp(1.4f, 1f, t);
                if (target != null)
                {
                    target.localScale = new Vector3(s, s, 1f);
                }

                yield return null;
            }

            if (target != null)
            {
                target.localScale = Vector3.one;
            }
        }

        private AudioClip Tone(string name, float freq, float duration, float decay)
        {
            var rate = 44100;
            var count = Mathf.RoundToInt(rate * duration);
            var data = new float[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)rate;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-decay * t);
            }

            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip Noise(string name, float duration, float decay)
        {
            var rate = 44100;
            var count = Mathf.RoundToInt(rate * duration);
            var data = new float[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)rate;
                data[i] = (Random.value * 2f - 1f) * Mathf.Exp(-decay * t);
            }

            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// 카드 한 장(색 배경 그라데이션 + 흰 테두리 + 흰 숫자(외곽선) + 양 모서리 흰 도형).
        /// 색약 대응: 도형이 주 구분 수단, 색은 보조. 손패·버림 공용.
        /// </summary>
        private GameObject CreateCardFace(Transform parent, Card card, float width, float height)
        {
            var colorIndex = (int)card.Color;

            var go = new GameObject($"Card_{card.Number}{ColorLetter[colorIndex]}",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var bg = go.GetComponent<Image>();
            bg.sprite = _cardBg[colorIndex];
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;

            // 카드가 테이블에 떠 있는 느낌의 부드러운 그림자
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(5f, -5f);

            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            var num = card.Number.ToString();
            var letter = ColorLetter[colorIndex];
            var pip = Mathf.RoundToInt(height * 0.15f);

            // 중앙 큰 숫자(흰색 + 검은 외곽선)
            var center = CreateText(go.transform, num, Mathf.RoundToInt(height * 0.5f), TextAnchor.MiddleCenter);
            center.color = Color.white;
            center.fontStyle = FontStyle.Bold;
            Stretch(center.rectTransform);
            AddOutline(center);

            // 네 모서리: 숫자/이니셜 (대각 대칭)
            Pip(go.transform, num, pip, TextAnchor.UpperLeft, new Vector2(0.10f, 0.78f), new Vector2(0.5f, 0.97f));
            Pip(go.transform, letter, pip, TextAnchor.UpperRight, new Vector2(0.5f, 0.78f), new Vector2(0.90f, 0.97f));
            Pip(go.transform, letter, pip, TextAnchor.LowerLeft, new Vector2(0.10f, 0.03f), new Vector2(0.5f, 0.22f));
            Pip(go.transform, num, pip, TextAnchor.LowerRight, new Vector2(0.5f, 0.03f), new Vector2(0.90f, 0.22f));

            return go;
        }

        private void Pip(Transform parent, string content, int size, TextAnchor anchor, Vector2 min, Vector2 max)
        {
            var t = CreateText(parent, content, size, anchor);
            t.color = Color.white;
            t.fontStyle = FontStyle.Bold;
            Anchor(t.rectTransform, min, max);
            AddOutline(t);
        }

        private static void AddOutline(Text text)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.65f);
            outline.effectDistance = new Vector2(2, -2);
        }

        // ── 절차적 카드 아트 ──

        private void GenerateArt()
        {
            for (var i = 0; i < 4; i++)
            {
                var c = Palette[i];
                var top = Color.Lerp(c, Color.white, 0.22f);     // 위쪽 밝게
                var bottom = Color.Lerp(c, Color.black, 0.20f);  // 아래쪽 어둡게
                _cardBg[i] = UiArt.RoundedGradient(120, 168, 22, top, bottom);
            }

            _haloSprite = UiArt.RoundedGradient(120, 168, 22, Color.white, Color.white);
        }

        private void RenderDiscard()
        {
            foreach (Transform child in _discardRow)
            {
                Destroy(child.gameObject);
            }

            // 족보 완성: 버림 타임라인 비우고 족보 6장만 영역 정중앙에 펼쳐 표시
            if (_meldSet != null)
            {
                for (var i = 0; i < _meldSet.Count; i++)
                {
                    var offset = (i - (_meldSet.Count - 1) / 2f) * 136f;
                    PlaceCard(_meldSet[i], 128, 192, new Vector2(0.5f, 0.5f), new Vector2(offset, 0f), 0f);
                }

                return;
            }

            // 전부 중앙 무작위 더미: 발생 순서대로 쌓아 마지막 던진 카드가 맨 위.
            // 뽕/자연뽕 3장도 같은 지점에 살짝 펼쳐 던짐(j별 고정 오프셋 → Refresh마다 동일).
            const float w = 120f, h = 180f;
            var heapAnchor = new Vector2(0.5f, 0.45f);
            GameObject last = null;
            foreach (var (cards, group, pos, rot) in _timeline)
            {
                if (group)
                {
                    for (var j = 0; j < cards.Count; j++)
                    {
                        var fan = j - (cards.Count - 1) / 2f;
                        last = PlaceCard(cards[j], w, h, heapAnchor,
                            pos + new Vector2(fan * 38f, j * 3f), rot + fan * 9f);
                    }
                }
                else
                {
                    last = PlaceCard(cards[0], w, h, heapAnchor, pos, rot);
                }
            }

            if (last != null)
            {
                HighlightTop(last);

                if (_timeline.Count > _timelineShown)
                {
                    StartCoroutine(ScalePop(last.transform));
                }
            }

            _timelineShown = _timeline.Count;
        }

        /// <summary>맨 위(마지막 버림) 카드 강조: 카드 바로 아래에 노란 헤일로를 깔아 테두리처럼 보이게.</summary>
        private void HighlightTop(GameObject top)
        {
            var topRt = (RectTransform)top.transform;
            var halo = new GameObject("TopHalo", typeof(RectTransform), typeof(Image));
            halo.transform.SetParent(_discardRow, false);

            var img = halo.GetComponent<Image>();
            img.sprite = _haloSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(1f, 0.92f, 0.3f, 0.95f);
            img.raycastTarget = false;

            var rt = (RectTransform)halo.transform;
            rt.anchorMin = rt.anchorMax = topRt.anchorMin;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = topRt.sizeDelta + new Vector2(14f, 14f);
            rt.anchoredPosition = topRt.anchoredPosition;
            rt.localRotation = topRt.localRotation;

            halo.transform.SetSiblingIndex(top.transform.GetSiblingIndex()); // 카드 바로 아래로
        }

        /// <summary>버림 영역에 카드 1장 배치. anchorRel=영역 내 기준점(0~1), pos=기준점에서의 오프셋(px).</summary>
        private GameObject PlaceCard(Card card, float w, float h, Vector2 anchorRel, Vector2 pos, float rot)
        {
            var face = CreateCardFace(_discardRow, card, w, h);
            var rt = face.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchorRel;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = pos;
            rt.localRotation = Quaternion.Euler(0, 0, rot);
            return face;
        }

        // ── UI 헬퍼 ──

        private void EnsureEventSystem()
        {
            if (EventSystem.current == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }
        }

        private Image CreateRow(Transform parent, Vector2 min, Vector2 max, float spacing)
        {
            var panel = CreatePanel(parent, new Color(0, 0, 0, 0.12f));
            Anchor(panel.rectTransform, min, max);
            var layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(14, 14, 10, 10);
            return panel;
        }

        private static Image CreatePanel(Transform parent, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<Image>();
        }

        private Text CreateText(Transform parent, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = content;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.Button;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.95f, 0.95f, 0.95f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 160;
            le.preferredHeight = 90;
            var text = CreateText(go.transform, label, 34, TextAnchor.MiddleCenter);
            text.color = Color.black;
            Stretch(text.rectTransform);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go.GetComponent<Button>();
        }

        private void SetButtonLabel(Button button, string label) => button.GetComponentInChildren<Text>().text = label;

        private int TripleNumber(Hand hand) => hand.Cards.GroupBy(c => c.Number).First(g => g.Count() >= 3).Key;

        private int TopDiscardNumber() => _round.DiscardPile[_round.DiscardPile.Count - 1].Number;

        private string CardLabel(Card c) => $"{c.Number}{ColorLetter[(int)c.Color]}";

        private string SeatName(int seat) => seat == MySeat ? $"{_names[seat]}(나)" : _names[seat];

        private void SetLog(string message) => Debug.Log($"[BBONG {Time.time:F2}] {message.Replace("\n", " | ")}"); // 콘솔 전용, 타이밍 튜닝용 경과초 포함

        private static void Stretch(RectTransform rt) => Anchor(rt, Vector2.zero, Vector2.one);

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
