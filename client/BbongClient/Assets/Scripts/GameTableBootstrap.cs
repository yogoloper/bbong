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
    /// 드로우/버림 · 뽕/자연뽕 · 스톱 · 5판 세트 점수/판돈. 봇 턴은 코루틴으로 천천히 진행.
    /// </summary>
    public sealed class GameTableBootstrap : MonoBehaviour
    {
        private enum UiState { NeedDraw, NeedDiscard, PongWindow, PongDiscardSelect, NaturalPongSelect, Resolving, RoundOver, SetOver }

        private const int PlayerCount = 4;
        private const int MySeat = 0;
        private const int Stake = 1000;
        private const float BotDelay = 0.5f;
        private const int PongWindowSeconds = 4;

        private static readonly Color[] Palette =
        {
            new Color(0.85f, 0.23f, 0.23f), new Color(0.20f, 0.45f, 0.85f),
            new Color(0.20f, 0.62f, 0.35f), new Color(0.90f, 0.75f, 0.15f)
        };

        private static readonly string[] ColorLetter = { "R", "B", "G", "Y" };
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
        private bool _drawnThisTurn; // 턴당 드로우 1회 보장(버튼 이중 발화 방어)
        private int _seed = 1;
        private Coroutine _pongTimer;
        private Coroutine _botLoop;

        private Font _font;
        private Transform _opponentsRow;
        private Transform _discardRow;
        private Transform _handRow;
        private Text _info;
        private Text _log;
        private Button _drawBtn, _stopBtn, _pongBtn, _passBtn, _naturalBtn, _nextBtn;
        private readonly List<string> _events = new();

        private AudioSource _audio;
        private AudioClip _sfxDraw, _sfxDiscard, _sfxPong, _sfxStop;
        private Image _flash;
        private int _discardShownCount;

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
            _state = UiState.NeedDraw;
            _discardShownCount = 0;
            SetLog($"{_roundIndex + 1}판 시작.");
            RunBots();
        }

        private void EndRound(int[] scores, string reason)
        {
            PlayStop();
            _game = _game.ApplyRoundScores(scores);
            _roundIndex++;

            var detail = string.Join("  ", Enumerable.Range(0, PlayerCount).Select(s => $"P{s} {scores[s]:+0;-0;0}"));
            var cumulative = string.Join("  ", Enumerable.Range(0, PlayerCount).Select(s => $"P{s}={_game.CumulativeDebts[s]}"));

            if (_game.IsSetOver)
            {
                var winners = _game.WinnerSeats();
                var payouts = StakePot.Distribute(Stake, PlayerCount, winners);
                var who = string.Join(", ", winners.Select(s => $"P{s}"));
                SetLog($"{reason}\n[{detail}]\n누적 {cumulative}\n=== 세트 종료 === 1등 {who}  판돈 P{MySeat}={payouts[MySeat]}");
                _state = UiState.SetOver;
            }
            else
            {
                SetLog($"{reason}\n[{detail}]\n누적 {cumulative}");
                _state = UiState.RoundOver;
            }

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

                var seat = _round.CurrentSeat;
                if (seat == MySeat)
                {
                    _state = UiState.NeedDraw;
                    _drawnThisTurn = false; // 새 내 턴 → 드로우 허용
                    Refresh();
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
                    EndRound(RoundSettlement.SettleByMeld(_round, seat, meld), $"P{seat} 족보 {meld.Type}({meld.Score})");
                    yield break;
                }

                if (_round.CanNaturalPong())
                {
                    var number = TripleNumber(_round.CurrentPlayer.Hand);
                    var rest = new Hand(_round.CurrentPlayer.Hand.Cards.Where(c => c.Number != number));
                    _round = _round.NaturalPong(number, _bots[seat].ChoosePongDiscard(rest));
                    SetLog($"P{seat} 자연뽕! {number} 3장 고정");
                    PlayPong();
                    Refresh();
                    yield return new WaitForSeconds(BotDelay);
                    continue;
                }

                var discard = _bots[seat].ChooseDiscard(_round.CurrentPlayer.Hand);
                _round = _round.Discard(discard);
                SetLog($"P{seat} 버림 {CardLabel(discard)}");
                PlayDiscard();
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
            var rest = new Hand(_round.Players[seat].Hand.Cards.Where(c => c.Number != number));
            if (rest.Count == 0)
            {
                _round = _round.Pong(seat, null);
                EndRound(RoundSettlement.SettleByTwoPong(_round, seat, discarderSeat), $"P{seat} 두 번 뽕 (P{discarderSeat} 박)");
                return;
            }

            _round = _round.Pong(seat, _bots[seat].ChoosePongDiscard(rest));
            SetLog($"P{seat} 뽕! {number} 3장 고정");
            PlayPong();
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

        private void OnDraw()
        {
            if (_state != UiState.NeedDraw || _drawnThisTurn)
            {
                return;
            }

            _drawnThisTurn = true;
            _round = _round.Draw();
            PlayDraw();
            var meld = HandEvaluator.Evaluate(_round.CurrentPlayer.Hand);
            if (meld.Type != MeldType.None)
            {
                EndRound(RoundSettlement.SettleByMeld(_round, MySeat, meld), $"내 족보 {meld.Type}({meld.Score})");
                return;
            }

            _state = UiState.NeedDiscard;
            SetLog(_round.CanNaturalPong() ? "버릴 카드 클릭 (또는 자연뽕)" : "버릴 카드를 클릭하세요.");
            Refresh();
        }

        private void OnStop()
        {
            if (_state == UiState.NeedDraw && StopResolver.CanStop(_round, MySeat))
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
                _round = _round.Pong(MySeat, null);
                EndRound(RoundSettlement.SettleByTwoPong(_round, MySeat, _pongDiscarderSeat), $"내 두 번 뽕 (P{_pongDiscarderSeat} 박)");
                return;
            }

            _state = UiState.PongDiscardSelect;
            SetLog($"뽕! {_pongNumber} 외 버릴 카드 클릭");
            Refresh();
        }

        private void OnPass()
        {
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
                    TryBotPong(MySeat);
                    RunBots();
                    break;

                case UiState.PongDiscardSelect:
                    if (card.Number == _pongNumber)
                    {
                        return;
                    }

                    _state = UiState.Resolving;
                    _round = _round.Pong(MySeat, card);
                    SetLog($"뽕 완료. {_pongNumber} 3장 고정");
                    PlayPong();
                    RunBots();
                    break;

                case UiState.NaturalPongSelect:
                    if (card.Number == _naturalPongNumber)
                    {
                        return;
                    }

                    _state = UiState.Resolving;
                    _round = _round.NaturalPong(_naturalPongNumber, card);
                    SetLog($"자연뽕 완료. {_naturalPongNumber} 3장 고정");
                    PlayPong();
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
                $"내 손패 {me.Hand.Count}장 합 {me.Hand.Sum()}   내 누적빚 {_game.CumulativeDebts[MySeat]}";

            _drawBtn.gameObject.SetActive(_state == UiState.NeedDraw);
            _stopBtn.gameObject.SetActive(_state == UiState.NeedDraw && StopResolver.CanStop(_round, MySeat));
            _naturalBtn.gameObject.SetActive(_state == UiState.NeedDiscard && _round.CanNaturalPong());
            _pongBtn.gameObject.SetActive(_state == UiState.PongWindow);
            _passBtn.gameObject.SetActive(_state == UiState.PongWindow);
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

            foreach (var card in hand.Cards.OrderBy(c => c.Number).ThenBy(c => c.Color))
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

            Stretch(CreatePanel(root, new Color(0.12f, 0.30f, 0.20f)).rectTransform);

            _opponentsRow = CreateRow(root, new Vector2(0.02f, 0.85f), new Vector2(0.98f, 0.99f), 12).transform;

            _info = CreateText(root, "", 28, TextAnchor.UpperCenter);
            Anchor(_info.rectTransform, new Vector2(0.04f, 0.795f), new Vector2(0.96f, 0.845f));

            var discardLabel = CreateText(root, "버림 더미 (오른쪽이 최신)", 24, TextAnchor.MiddleLeft);
            discardLabel.color = new Color(1, 1, 1, 0.7f);
            Anchor(discardLabel.rectTransform, new Vector2(0.05f, 0.755f), new Vector2(0.96f, 0.79f));

            _discardRow = CreateRow(root, new Vector2(0.03f, 0.60f), new Vector2(0.97f, 0.75f), 8).transform;

            _log = CreateText(root, "", 24, TextAnchor.UpperCenter);
            Anchor(_log.rectTransform, new Vector2(0.04f, 0.44f), new Vector2(0.96f, 0.59f));

            var bar = CreateRow(root, new Vector2(0.03f, 0.30f), new Vector2(0.97f, 0.42f), 14).transform;
            _drawBtn = CreateButton(bar, "드로우", OnDraw);
            _stopBtn = CreateButton(bar, "스톱", OnStop);
            _naturalBtn = CreateButton(bar, "자연뽕", OnNaturalPong);
            _pongBtn = CreateButton(bar, "뽕!", OnPong);
            _passBtn = CreateButton(bar, "패스", OnPass);
            _nextBtn = CreateButton(bar, "다음 판", OnNext);

            _handRow = CreateRow(root, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.28f), 12).transform;

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
            Flash(new Color(1f, 0.95f, 0.4f, 0.5f)); // 노란 섬광
        }

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

        /// <summary>카드 한 장의 시각 표현(색 채운 패널 + 숫자 + 색문자). 손패·버림 더미 공용.</summary>
        private GameObject CreateCardFace(Transform parent, Card card, float width, float height)
        {
            var go = new GameObject($"Card_{card.Number}{ColorLetter[(int)card.Color]}",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Palette[(int)card.Color];

            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            var number = CreateText(go.transform, card.Number.ToString(), Mathf.RoundToInt(height * 0.35f), TextAnchor.MiddleCenter);
            number.color = Color.white;
            Stretch(number.rectTransform);

            var letter = CreateText(go.transform, ColorLetter[(int)card.Color], Mathf.RoundToInt(height * 0.14f), TextAnchor.UpperLeft);
            letter.color = new Color(1, 1, 1, 0.85f);
            Anchor(letter.rectTransform, new Vector2(0.08f, 0.74f), new Vector2(0.62f, 0.98f));

            return go;
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

            var pile = _round.DiscardPile;
            if (pile.Count == 0)
            {
                var empty = CreateText(_discardRow, "(아직 버린 카드 없음)", 28, TextAnchor.MiddleCenter);
                empty.color = new Color(1, 1, 1, 0.6f);
                return;
            }

            var show = Mathf.Min(7, pile.Count);
            GameObject newest = null;
            for (var i = pile.Count - show; i < pile.Count; i++)
            {
                newest = CreateCardFace(_discardRow, pile[i], 90, 140); // 맨 오른쪽 = 최신(맨 위)
            }

            if (pile.Count > _discardShownCount && newest != null)
            {
                StartCoroutine(ScalePop(newest.transform)); // 새로 버려진 카드 팝
            }

            _discardShownCount = pile.Count;
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
