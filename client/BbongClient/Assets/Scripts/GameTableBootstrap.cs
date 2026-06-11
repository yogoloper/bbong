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
    /// 코드 생성 카드 테이블 + 게임 흐름(Phase 3). 4인(사람 P0 + 봇 3).
    /// 드로우/버림 · 뽕/자연뽕 · 스톱 · 게임(5판) 점수/판돈. 봇 턴은 코루틴으로 천천히 진행.
    /// </summary>
    public sealed class GameTableBootstrap : MonoBehaviour
    {
        private enum UiState { StopDecision, NeedDiscard, PongWindow, PongDiscardSelect, NaturalPongSelect, Resolving, RoundOver, SetOver }

        private const int PlayerCount = 4;
        private const int MySeat = 0;
        private const int Stake = 1000;
        private const float BotDelay = 0.5f;
        private const int PongWindowSeconds = 4;

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
        private readonly Bot[] _bots =
        {
            new(BotDifficulty.Normal), new(BotDifficulty.Normal),
            new(BotDifficulty.Normal), new(BotDifficulty.Normal)
        };

        private GameState _game;
        private RoundState _round;
        private int _roundIndex;
        private UiState _state;
        private int _pongNumber;
        private int _pongDiscarderSeat;
        private int _naturalPongNumber;
        private int _seed = 1;
        private Coroutine _pongTimer;
        private Coroutine _botLoop;

        private Font _font;
        private Transform _opponentsRow;
        private Transform _discardRow;
        private Transform _handRow;
        private Text _info;
        private Text _log;
        private Text _scoreboard;            // 상시 점수 전광판(누적)
        private GameObject _scorePopup;      // 판 종료 중앙 전광판(표)
        private Text _scoreTitle;
        private Transform _scoreGrid;        // 표 셀 컨테이너(GridLayout)
        private CanvasGroup _scorePopupGroup;
        private Coroutine _scoreFade;
        private readonly List<int[]> _roundHistory = new(); // 게임 내 판별 점수
        private Button _stopBtn, _pongBtn, _passBtn, _naturalBtn, _nextBtn;
        private readonly List<string> _events = new();

        private AudioSource _audio;
        private AudioClip _sfxDraw, _sfxDiscard, _sfxPong, _sfxStop;
        private Image _flash;
        // 버림 타임라인: 발생 순서대로. group=true면 뽕/자연뽕(겹쳐 표시), false면 단일 버림.
        private readonly List<(List<Card> cards, bool group)> _timeline = new();
        private int _timelineShown;
        private List<Card> _meldSet; // 족보 완성 시 6장(버림 비우고 표시)

        private readonly Sprite[] _cardBg = new Sprite[4];  // 색별 둥근 그라데이션 카드 배경

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildUi();
            _game = GameState.Start(PlayerCount);
            StartRound();
        }

        // ── 판 시작/종료 ──

        private void StartRound()
        {
            _round = RoundState.Deal(Deck.CreateStandard(), PlayerCount, new SeededRandom(_seed++),
                dealerSeat: _roundIndex % PlayerCount);
            _state = UiState.NeedDiscard;
            _timeline.Clear();
            _timelineShown = 0;
            _meldSet = null;
            if (_scorePopup != null)
            {
                _scorePopup.SetActive(false);
            }

            SetLog($"{_roundIndex + 1}판 시작.");
            RunBots();
        }

        private void EndRound(int[] scores, string reason)
        {
            PlayStop();
            _roundHistory.Add(scores);
            _game = _game.ApplyRoundScores(scores);
            _roundIndex++;

            var detail = string.Join("  ", Enumerable.Range(0, PlayerCount).Select(s => $"P{s} {scores[s]:+0;-0;0}"));
            var cumulative = string.Join("  ", Enumerable.Range(0, PlayerCount).Select(s => $"P{s}={_game.CumulativeDebts[s]}"));

            string title;
            if (_game.IsSetOver)
            {
                var winners = _game.WinnerSeats();
                var payouts = StakePot.Distribute(Stake, PlayerCount, winners);
                var who = string.Join(", ", winners.Select(s => $"P{s}"));
                SetLog($"{reason}\n[{detail}]\n누적 {cumulative}\n=== 게임 종료(5판) === 1등 {who}  판돈 P{MySeat}={payouts[MySeat]}");
                title = $"게임 종료 — 1등 {who}";
                _state = UiState.SetOver;
            }
            else
            {
                SetLog($"{reason}\n[{detail}]\n누적 {cumulative}");
                title = $"{_roundIndex}판 종료";
                _state = UiState.RoundOver;
            }

            ShowScorePopup(title);
            Refresh();
        }

        /// <summary>중앙 전광판(표): 헤더 + 판별 점수 행 + 합계 행. 잠시 후 페이드아웃.</summary>
        private void ShowScorePopup(string title)
        {
            _scoreTitle.text = title;

            foreach (Transform child in _scoreGrid)
            {
                Destroy(child.gameObject);
            }

            // 헤더
            AddCell("판", true);
            for (var s = 0; s < PlayerCount; s++)
            {
                AddCell($"P{s}{(s == MySeat ? "*" : "")}", true);
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
            }

            _scoreFade = StartCoroutine(FadeScorePopup());
        }

        private void AddCell(string text, bool emphasize)
        {
            var t = CreateText(_scoreGrid, text, 30, TextAnchor.MiddleRight);
            t.color = emphasize ? new Color(1f, 0.9f, 0.4f) : Color.white;
            if (emphasize)
            {
                t.fontStyle = FontStyle.Bold;
            }
        }

        private IEnumerator FadeScorePopup()
        {
            yield return new WaitForSeconds(4.5f); // 충분히 보이도록
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

                var seat = _round.CurrentSeat;
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
                    EndRound(RoundSettlement.SettleByStop(_round, seat), $"P{seat} 스톱");
                    yield break;
                }

                _round = _round.Draw();
                Refresh();
                yield return new WaitForSeconds(BotDelay);

                var meld = HandEvaluator.Evaluate(_round.CurrentPlayer.Hand);
                if (meld.Type != MeldType.None)
                {
                    _meldSet = Sorted(_round.CurrentPlayer.Hand.Cards); // 족보: 버림 비우고 표시
                    EndRound(RoundSettlement.SettleByMeld(_round, seat, meld), $"P{seat} 족보 {meld.Type}({meld.Score})");
                    yield break;
                }

                if (_round.CanNaturalPong())
                {
                    var number = TripleNumber(_round.CurrentPlayer.Hand);
                    var laid = _round.CurrentPlayer.Hand.Cards.Where(c => c.Number == number).Take(3).ToList();
                    var rest = new Hand(_round.CurrentPlayer.Hand.Cards.Where(c => c.Number != number));
                    var toss = _bots[seat].ChoosePongDiscard(rest);
                    _round = _round.NaturalPong(number, toss);
                    SetLog($"P{seat} 자연뽕! {number} 3장 고정");
                    PlayPong();
                    AddGroup(laid);
                    AddDiscard(toss);
                    Refresh();
                    yield return new WaitForSeconds(BotDelay);
                    continue;
                }

                var discard = _bots[seat].ChooseDiscard(_round.CurrentPlayer.Hand);
                _round = _round.Discard(discard);
                SetLog($"P{seat} 버림 {CardLabel(discard)}");
                PlayDiscard();
                AddDiscard(discard);
                Refresh();
                yield return new WaitForSeconds(BotDelay);

                if (_round.CanPong(MySeat))
                {
                    OpenPongWindow(seat);
                    yield break;
                }

                if (TryBotPong(seat))
                {
                    Refresh();
                    yield return new WaitForSeconds(BotDelay);
                }
            }
        }

        private bool TryBotPong(int discarderSeat)
        {
            for (var s = 0; s < PlayerCount; s++)
            {
                if (s != MySeat && _round.CanPong(s) && _bots[s].ShouldPong())
                {
                    DoBotPong(s, discarderSeat);
                    return true;
                }
            }

            return false;
        }

        private void DoBotPong(int seat, int discarderSeat)
        {
            var number = TopDiscardNumber();
            var laid = _round.Players[seat].Hand.Cards.Where(c => c.Number == number).Take(2).ToList();
            var rest = new Hand(_round.Players[seat].Hand.Cards.Where(c => c.Number != number));
            if (rest.Count == 0)
            {
                _round = _round.Pong(seat, null);
                PlayPong();
                AddGroup(laid);
                EndRound(RoundSettlement.SettleByTwoPong(_round, seat, discarderSeat), $"P{seat} 두 번 뽕 (P{discarderSeat} 박)");
                return;
            }

            var toss = _bots[seat].ChoosePongDiscard(rest);
            _round = _round.Pong(seat, toss);
            SetLog($"P{seat} 뽕! {number} 3장 고정");
            PlayPong();
            AddGroup(laid);
            AddDiscard(toss);
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

        /// <summary>내 턴 자동 드로우 → 족보면 종료, 아니면 버림 대기(NeedDiscard).</summary>
        private void AutoDrawMe()
        {
            _round = _round.Draw();
            PlayDraw();
            var meld = HandEvaluator.Evaluate(_round.CurrentPlayer.Hand);
            if (meld.Type != MeldType.None)
            {
                _meldSet = Sorted(_round.CurrentPlayer.Hand.Cards); // 족보: 버림 비우고 표시
                EndRound(RoundSettlement.SettleByMeld(_round, MySeat, meld), $"내 족보 {meld.Type}({meld.Score})");
                return;
            }

            _state = UiState.NeedDiscard;
            SetLog(_round.CanNaturalPong() ? "버릴 카드 클릭 (또는 자연뽕)" : "버릴 카드를 클릭하세요.");
            Refresh();
        }

        private void OnStop()
        {
            if (_state == UiState.StopDecision && StopResolver.CanStop(_round, MySeat))
            {
                EndRound(RoundSettlement.SettleByStop(_round, MySeat), "내 스톱");
            }
        }

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
                PlayPong();
                AddGroup(laid);
                EndRound(RoundSettlement.SettleByTwoPong(_round, MySeat, _pongDiscarderSeat), $"내 두 번 뽕 (P{_pongDiscarderSeat} 박)");
                return;
            }

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

        private void OnNaturalPong()
        {
            if (_state != UiState.NeedDiscard || !_round.CanNaturalPong())
            {
                return;
            }

            _naturalPongNumber = TripleNumber(_round.Players[MySeat].Hand);
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
                _game = GameState.Start(PlayerCount);
                _roundIndex = 0;
                _roundHistory.Clear();
            }

            StartRound();
        }

        private void OnCardClicked(Card card)
        {
            switch (_state)
            {
                case UiState.NeedDiscard:
                    _state = UiState.Resolving; // 더블클릭 → 두 장 버림 방지
                    _round = _round.Discard(card);
                    SetLog($"내 버림 {CardLabel(card)}");
                    PlayDiscard();
                    AddDiscard(card);
                    TryBotPong(MySeat);
                    RunBots();
                    break;

                case UiState.PongDiscardSelect:
                    if (card.Number == _pongNumber)
                    {
                        return;
                    }

                    _state = UiState.Resolving;
                    var pongLaid = _round.Players[MySeat].Hand.Cards.Where(c => c.Number == _pongNumber).Take(2).ToList();
                    _round = _round.Pong(MySeat, card);
                    SetLog($"뽕 완료. {_pongNumber} 3장 고정");
                    PlayPong();
                    AddGroup(pongLaid);
                    AddDiscard(card);
                    RunBots();
                    break;

                case UiState.NaturalPongSelect:
                    if (card.Number == _naturalPongNumber)
                    {
                        return;
                    }

                    _state = UiState.Resolving;
                    var naturalLaid = _round.Players[MySeat].Hand.Cards.Where(c => c.Number == _naturalPongNumber).Take(3).ToList();
                    _round = _round.NaturalPong(_naturalPongNumber, card);
                    SetLog($"자연뽕 완료. {_naturalPongNumber} 3장 고정");
                    PlayPong();
                    AddGroup(naturalLaid);
                    AddDiscard(card);
                    RunBots();
                    break;
            }
        }

        // ── 렌더링 ──

        private void Refresh()
        {
            RenderOpponents();
            RenderDiscard();
            RenderHand(_round.Players[MySeat].Hand);

            var top = _round.DiscardPile.Count > 0 ? CardLabel(_round.DiscardPile[_round.DiscardPile.Count - 1]) : "-";
            var me = _round.Players[MySeat];
            _info.text =
                $"{_roundIndex + 1}/{GameConfig.DefaultSetRounds}판   턴 P{_round.CurrentSeat}   더미 {_round.DrawPile.Count}   버림 {top}\n" +
                $"내 손패 {me.Hand.Count}장 합 {me.Hand.Sum()}";

            _scoreboard.text = "누적  " + string.Join("   ", Enumerable.Range(0, PlayerCount)
                .Select(s => $"P{s}{(s == MySeat ? "(나)" : "")} {_game.CumulativeDebts[s]}"));

            _stopBtn.gameObject.SetActive(_state == UiState.StopDecision);
            _naturalBtn.gameObject.SetActive(_state == UiState.NeedDiscard && _round.CanNaturalPong());
            _pongBtn.gameObject.SetActive(_state == UiState.PongWindow);
            _passBtn.gameObject.SetActive(_state == UiState.PongWindow || _state == UiState.StopDecision);
            SetButtonLabel(_passBtn, _state == UiState.StopDecision ? "계속" : "패스");
            _nextBtn.gameObject.SetActive(_state == UiState.RoundOver || _state == UiState.SetOver);
            SetButtonLabel(_nextBtn, _state == UiState.SetOver ? "새 게임" : "다음 판");
        }

        private void RenderOpponents()
        {
            foreach (Transform child in _opponentsRow)
            {
                Destroy(child.gameObject);
            }

            for (var seat = 0; seat < PlayerCount; seat++)
            {
                if (seat == MySeat)
                {
                    continue;
                }

                var p = _round.Players[seat];
                var highlight = _round.CurrentSeat == seat;
                var panel = CreatePanel(_opponentsRow, highlight ? new Color(0.9f, 0.8f, 0.2f, 0.55f) : new Color(0, 0, 0, 0.25f));
                panel.gameObject.AddComponent<LayoutElement>().preferredWidth = 210;
                var t = CreateText(panel.transform, $"P{seat}\n손 {p.Hand.Count}\n뽕 {p.PongCount}", 28, TextAnchor.MiddleCenter);
                Stretch(t.rectTransform);
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
                CreateCardButton(card);
            }
        }

        // ── UI 생성 ──

        private void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            var root = canvasGo.transform;

            GenerateArt();

            Stretch(CreatePanel(root, new Color(0.12f, 0.30f, 0.20f)).rectTransform);

            // 상시 점수 전광판(최상단)
            var boardBg = CreatePanel(root, new Color(0, 0, 0, 0.35f));
            Anchor(boardBg.rectTransform, new Vector2(0.02f, 0.955f), new Vector2(0.98f, 0.998f));
            _scoreboard = CreateText(root, "", 26, TextAnchor.MiddleCenter);
            Anchor(_scoreboard.rectTransform, new Vector2(0.02f, 0.955f), new Vector2(0.98f, 0.998f));

            _opponentsRow = CreateRow(root, new Vector2(0.02f, 0.83f), new Vector2(0.98f, 0.95f), 12).transform;

            _info = CreateText(root, "", 28, TextAnchor.UpperCenter);
            Anchor(_info.rectTransform, new Vector2(0.04f, 0.795f), new Vector2(0.96f, 0.845f));

            var discardLabel = CreateText(root, "버림 더미 (오른쪽이 최신)", 24, TextAnchor.MiddleLeft);
            discardLabel.color = new Color(1, 1, 1, 0.7f);
            Anchor(discardLabel.rectTransform, new Vector2(0.05f, 0.755f), new Vector2(0.96f, 0.79f));

            // 버림 더미/내려놓은 패 영역(수동 배치 — 겹침/펼침 제어)
            var discardGo = new GameObject("DiscardArea", typeof(RectTransform));
            discardGo.transform.SetParent(root, false);
            _discardRow = discardGo.transform;
            Anchor((RectTransform)_discardRow, new Vector2(0.03f, 0.595f), new Vector2(0.97f, 0.755f));

            _log = CreateText(root, "", 24, TextAnchor.UpperCenter);
            Anchor(_log.rectTransform, new Vector2(0.04f, 0.44f), new Vector2(0.96f, 0.59f));

            var bar = CreateRow(root, new Vector2(0.03f, 0.30f), new Vector2(0.97f, 0.42f), 14).transform;
            _stopBtn = CreateButton(bar, "스톱", OnStop);
            _naturalBtn = CreateButton(bar, "자연뽕", OnNaturalPong);
            _pongBtn = CreateButton(bar, "뽕!", OnPong);
            _passBtn = CreateButton(bar, "패스", OnPass);
            _nextBtn = CreateButton(bar, "다음 판", OnNext);

            _handRow = CreateRow(root, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.28f), 12).transform;

            // 판 종료 중앙 전광판(표). 떴다가 사라짐, 클릭 통과.
            var popupBg = CreatePanel(root, new Color(0.05f, 0.05f, 0.08f, 0.92f));
            _scorePopup = popupBg.gameObject;
            Anchor(popupBg.rectTransform, new Vector2(0.08f, 0.40f), new Vector2(0.92f, 0.82f));
            popupBg.raycastTarget = false;
            _scorePopupGroup = _scorePopup.AddComponent<CanvasGroup>();
            _scorePopupGroup.blocksRaycasts = false;
            _scorePopupGroup.interactable = false;

            _scoreTitle = CreateText(popupBg.transform, "", 36, TextAnchor.MiddleCenter);
            _scoreTitle.fontStyle = FontStyle.Bold;
            Anchor(_scoreTitle.rectTransform, new Vector2(0.04f, 0.85f), new Vector2(0.96f, 0.99f));

            var gridGo = new GameObject("ScoreGrid", typeof(RectTransform));
            gridGo.transform.SetParent(popupBg.transform, false);
            _scoreGrid = gridGo.transform;
            Anchor((RectTransform)_scoreGrid, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.84f));
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(150, 48);
            grid.spacing = new Vector2(6, 6);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = PlayerCount + 1; // 라벨 칸 + 플레이어

            _scorePopup.SetActive(false);

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
        }

        // ── 연출/사운드 ──

        private void PlayDraw() => _audio.PlayOneShot(_sfxDraw, 0.5f);
        private void PlayDiscard() => _audio.PlayOneShot(_sfxDiscard, 0.5f);
        private void PlayStop() => _audio.PlayOneShot(_sfxStop, 0.6f);

        private void PlayPong()
        {
            _audio.PlayOneShot(_sfxPong, 0.8f);
            Flash(new Color(1f, 0.95f, 0.4f, 0.5f));
        }

        /// <summary>숫자 오름차순 → 같은 숫자는 빨·파·노·초 순으로 정렬.</summary>
        private static List<Card> Sorted(IEnumerable<Card> cards) =>
            cards.OrderBy(c => c.Number).ThenBy(c => ColorRank[(int)c.Color]).ToList();

        private void AddDiscard(Card card) => _timeline.Add((new List<Card> { card }, false));

        private void AddGroup(IEnumerable<Card> cards) => _timeline.Add((Sorted(cards), true));

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
                _cardBg[i] = RoundedGradientSprite(120, 168, 22, top, bottom);
            }
        }

        /// <summary>둥근 그라데이션 카드 스프라이트(흰 테두리 포함, 9-slice).</summary>
        private static Sprite RoundedGradientSprite(int w, int h, int radius, Color top, Color bottom)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * h];
            const float borderW = 4f;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var cx = Mathf.Clamp(x, radius, w - 1 - radius);
                    var cy = Mathf.Clamp(y, radius, h - 1 - radius);
                    var dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    var edge = radius - dist;                 // 가장자리까지 거리(코너·직선 공통)
                    var alpha = Mathf.Clamp01(edge + 0.5f);   // 모서리 AA

                    var fill = Color.Lerp(bottom, top, y / (float)h);
                    if (edge < borderW)
                    {
                        fill = Color.Lerp(fill, Color.white, 0.8f); // 흰 테두리
                    }

                    pixels[y * w + x] = new Color(fill.r, fill.g, fill.b, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            var border = new Vector4(radius, radius, radius, radius);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        private void CreateCardButton(Card card)
        {
            var go = CreateCardFace(_handRow, card, 130, 200);
            var captured = card;
            go.AddComponent<Button>().onClick.AddListener(() => OnCardClicked(captured));
        }

        private void RenderDiscard()
        {
            foreach (Transform child in _discardRow)
            {
                Destroy(child.gameObject);
            }

            // 족보 완성: 버림 타임라인 비우고 족보 6장만 펼쳐 표시
            if (_meldSet != null)
            {
                var mx = 12f;
                foreach (var card in _meldSet)
                {
                    PlaceCard(card, 128, 192, mx, 0f);
                    mx += 136f;
                }

                return;
            }

            // 하나의 타임라인: 단일 버림 + 뽕/자연뽕(겹침)을 발생 순서대로.
            // 영역 폭에 맞춰 오른쪽(최신)부터 들어가는 만큼만 표시.
            const float w = 78f, h = 117f, gap = 8f, gGap = 16f, gStep = 24f;
            var area = ((RectTransform)_discardRow).rect.width;
            if (area <= 1f)
            {
                area = 1000f; // 첫 프레임 레이아웃 전 대비
            }

            float EntryWidth(int idx)
            {
                var e = _timeline[idx];
                return e.group ? (e.cards.Count - 1) * gStep + w + gGap : w + gap;
            }

            var startIdx = _timeline.Count;
            var sum = 0f;
            for (var i = _timeline.Count - 1; i >= 0; i--)
            {
                var ew = EntryWidth(i);
                if (sum + ew > area - 16f && i != _timeline.Count - 1)
                {
                    break;
                }

                sum += ew;
                startIdx = i;
            }

            var px = 8f;
            GameObject last = null;
            for (var i = startIdx; i < _timeline.Count; i++)
            {
                var (cards, group) = _timeline[i];
                if (group)
                {
                    for (var j = 0; j < cards.Count; j++)
                    {
                        last = PlaceCard(cards[j], w, h, px + j * gStep, (j - (cards.Count - 1) / 2f) * -6f);
                    }

                    px += (cards.Count - 1) * gStep + w + gGap;
                }
                else
                {
                    last = PlaceCard(cards[0], w, h, px, 0f);
                    px += w + gap;
                }
            }

            if (last != null && _timeline.Count > _timelineShown)
            {
                StartCoroutine(ScalePop(last.transform));
            }

            _timelineShown = _timeline.Count;
        }

        /// <summary>버림 영역에 카드 1장을 좌측 기준 x 위치에 배치(회전 가능).</summary>
        private GameObject PlaceCard(Card card, float w, float h, float leftX, float rot)
        {
            var face = CreateCardFace(_discardRow, card, w, h);
            var rt = face.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(leftX + w / 2f, 0f);
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
            go.GetComponent<Image>().color = new Color(0.95f, 0.95f, 0.95f);
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

        private void SetLog(string message)
        {
            foreach (var line in message.Split('\n'))
            {
                _events.Add(line);
            }

            while (_events.Count > 9)
            {
                _events.RemoveAt(0);
            }

            _log.text = string.Join("\n", _events);
            Debug.Log($"[BBONG] {message.Replace("\n", " | ")}");
        }

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
