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
    /// 코드 생성 카드 테이블 + 게임 흐름(Phase 3).
    /// 빈 GameObject에 붙이고 Play하면 UI 전체가 코드로 생성됩니다.
    /// 내 손패(P0) 조작 + 봇 좌석 자동 진행 + 뽕/스톱 + 5판 세트 점수/판돈.
    /// </summary>
    public sealed class GameTableBootstrap : MonoBehaviour
    {
        private enum UiState { NeedDraw, NeedDiscard, PongWindow, PongDiscardSelect, RoundOver, SetOver }

        private const int PlayerCount = 3;
        private const int MySeat = 0;
        private const int Stake = 1000;

        private static readonly Color[] Palette =
        {
            new Color(0.85f, 0.23f, 0.23f), new Color(0.20f, 0.45f, 0.85f),
            new Color(0.20f, 0.62f, 0.35f), new Color(0.90f, 0.75f, 0.15f)
        };

        private static readonly string[] ColorLetter = { "R", "B", "G", "Y" };
        private readonly Bot[] _bots =
        {
            new(BotDifficulty.Normal), new(BotDifficulty.Normal), new(BotDifficulty.Normal)
        };

        private GameState _game;
        private RoundState _round;
        private int _roundIndex;       // 0-based, 세트 내 판 번호(딜러 회전용)
        private UiState _state;
        private int _pongNumber;       // 대기 중 뽕 대상 숫자
        private int _pongDiscarderSeat;// 마지막 버린 좌석(박 귀속)
        private int _seed = 1;

        private Font _font;
        private Transform _opponentsRow;
        private Transform _handRow;
        private Text _info;
        private Text _log;
        private Button _drawBtn, _stopBtn, _pongBtn, _passBtn, _nextBtn;

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
            _state = UiState.NeedDraw; // ProcessTurns가 실제 턴에 맞게 보정
            SetLog($"{_roundIndex + 1}판 시작.");
            ProcessTurns();
        }

        private void EndRound(int[] scores, string reason)
        {
            _game = _game.ApplyRoundScores(scores);
            _roundIndex++;

            var detail = string.Join("  ", Enumerable.Range(0, PlayerCount).Select(s => $"P{s} {scores[s]:+0;-0;0}"));
            var cumulative = string.Join("  ", Enumerable.Range(0, PlayerCount).Select(s => $"P{s}={_game.CumulativeDebts[s]}"));

            if (_game.IsSetOver)
            {
                var winners = _game.WinnerSeats();
                var payouts = StakePot.Distribute(Stake, PlayerCount, winners);
                var who = string.Join(", ", winners.Select(s => $"P{s}"));
                SetLog($"{reason}\n[{detail}]\n누적 {cumulative}\n\n=== 세트 종료 ===\n1등: {who}\n판돈 분배: P{MySeat}={payouts[MySeat]}");
                _state = UiState.SetOver;
            }
            else
            {
                SetLog($"{reason}\n[{detail}]\n누적 {cumulative}");
                _state = UiState.RoundOver;
            }

            Refresh();
        }

        // ── 턴 처리 (봇 자동 진행, 사람 액션 필요 시 멈춤) ──

        private void ProcessTurns()
        {
            var guard = 0;
            while (guard++ < 200)
            {
                if (_state == UiState.RoundOver || _state == UiState.SetOver)
                {
                    return; // 봇 뽕/스톱 등으로 판이 이미 끝남
                }

                var seat = _round.CurrentSeat;
                if (seat == MySeat)
                {
                    _state = UiState.NeedDraw;
                    Refresh();
                    return;
                }

                // 봇 턴: 스톱 → 드로우 → 족보 → 버림 → 뽕 체크
                if (StopResolver.CanStop(_round, seat) && _bots[seat].ShouldStop(_round, seat))
                {
                    EndRound(RoundSettlement.SettleByStop(_round, seat), $"P{seat} 스톱");
                    return;
                }

                _round = _round.Draw();
                var meld = HandEvaluator.Evaluate(_round.CurrentPlayer.Hand);
                if (meld.Type != MeldType.None)
                {
                    EndRound(RoundSettlement.SettleByMeld(_round, seat, meld), $"P{seat} 족보 {meld.Type}({meld.Score})");
                    return;
                }

                var discard = _bots[seat].ChooseDiscard(_round.CurrentPlayer.Hand);
                _round = _round.Discard(discard);
                SetLog($"P{seat} 버림 {CardLabel(discard)}");

                if (AfterDiscard(seat, allowHumanPong: true))
                {
                    return; // 사람 뽕 창에서 대기
                }
            }
        }

        /// <summary>버림 직후 뽕 처리. 사람이 뽕 가능하면 창을 열고 true 반환(대기). 봇 뽕은 자동 처리.</summary>
        private bool AfterDiscard(int discarderSeat, bool allowHumanPong)
        {
            if (allowHumanPong && _round.CanPong(MySeat))
            {
                _pongNumber = TopDiscardNumber();
                _pongDiscarderSeat = discarderSeat;
                _state = UiState.PongWindow;
                SetLog($"P{discarderSeat}가 {_pongNumber} 버림. 뽕 하시겠습니까?");
                Refresh();
                return true;
            }

            for (var s = 0; s < PlayerCount; s++)
            {
                if (s != MySeat && _round.CanPong(s) && _bots[s].ShouldPong())
                {
                    DoBotPong(s, discarderSeat);
                    return false; // 턴 바뀜, 호출부 루프 계속
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
            SetLog($"P{seat} 뽕! {number} 3장 고정.");
        }

        // ── 사람 액션 ──

        private void OnDraw()
        {
            if (_state != UiState.NeedDraw)
            {
                return;
            }

            _round = _round.Draw();
            var meld = HandEvaluator.Evaluate(_round.CurrentPlayer.Hand);
            if (meld.Type != MeldType.None)
            {
                EndRound(RoundSettlement.SettleByMeld(_round, MySeat, meld), $"내 족보 {meld.Type}({meld.Score})");
                return;
            }

            _state = UiState.NeedDiscard;
            SetLog("버릴 카드를 클릭하세요.");
            Refresh();
        }

        private void OnStop()
        {
            if (_state != UiState.NeedDraw || !StopResolver.CanStop(_round, MySeat))
            {
                return;
            }

            EndRound(RoundSettlement.SettleByStop(_round, MySeat), "내 스톱");
        }

        private void OnPong()
        {
            if (_state != UiState.PongWindow)
            {
                return;
            }

            var rest = new Hand(_round.Players[MySeat].Hand.Cards.Where(c => c.Number != _pongNumber));
            if (rest.Count == 0)
            {
                _round = _round.Pong(MySeat, null);
                EndRound(RoundSettlement.SettleByTwoPong(_round, MySeat, _pongDiscarderSeat), $"내 두 번 뽕 (P{_pongDiscarderSeat} 박)");
                return;
            }

            _state = UiState.PongDiscardSelect;
            SetLog($"뽕! {_pongNumber} 외에 버릴 카드를 클릭하세요.");
            Refresh();
        }

        private void OnPass()
        {
            if (_state != UiState.PongWindow)
            {
                return;
            }

            // 사람 패스 → 봇 뽕 기회만 확인 후 진행
            AfterDiscard(_pongDiscarderSeat, allowHumanPong: false);
            ProcessTurns();
        }

        private void OnNext()
        {
            if (_state == UiState.SetOver)
            {
                _game = GameState.Start(PlayerCount);
                _roundIndex = 0;
            }

            StartRound();
        }

        private void OnCardClicked(Card card)
        {
            if (_state == UiState.NeedDiscard)
            {
                _round = _round.Discard(card);
                if (!AfterDiscard(MySeat, allowHumanPong: false))
                {
                    ProcessTurns();
                }
                else
                {
                    Refresh();
                }
            }
            else if (_state == UiState.PongDiscardSelect)
            {
                if (card.Number == _pongNumber)
                {
                    return; // 뽕 짝은 못 버림
                }

                _round = _round.Pong(MySeat, card);
                SetLog($"뽕 완료. {_pongNumber} 3장 고정.");
                ProcessTurns();
            }
        }

        // ── 렌더링 ──

        private void Refresh()
        {
            RenderOpponents();
            RenderHand(_round.Players[MySeat].Hand);

            var top = _round.DiscardPile.Count > 0 ? CardLabel(_round.DiscardPile[_round.DiscardPile.Count - 1]) : "-";
            var me = _round.Players[MySeat];
            _info.text =
                $"{_roundIndex + 1}/{GameConfig.DefaultSetRounds}판   턴 P{_round.CurrentSeat}   더미 {_round.DrawPile.Count}   버림 {top}\n" +
                $"내 손패 {me.Hand.Count}장 합 {me.Hand.Sum()}   내 누적빚 {_game.CumulativeDebts[MySeat]}";

            var canStop = _state == UiState.NeedDraw && StopResolver.CanStop(_round, MySeat);
            _drawBtn.gameObject.SetActive(_state == UiState.NeedDraw);
            _stopBtn.gameObject.SetActive(canStop);
            _pongBtn.gameObject.SetActive(_state == UiState.PongWindow);
            _passBtn.gameObject.SetActive(_state == UiState.PongWindow);
            _nextBtn.gameObject.SetActive(_state == UiState.RoundOver || _state == UiState.SetOver);
            SetNextLabel(_state == UiState.SetOver ? "새 게임" : "다음 판");
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
                var panel = CreatePanel(_opponentsRow, highlight ? new Color(0.9f, 0.8f, 0.2f, 0.5f) : new Color(0, 0, 0, 0.25f));
                panel.gameObject.AddComponent<LayoutElement>().preferredWidth = 240;
                var t = CreateText(panel.transform, $"P{seat}\n손 {p.Hand.Count}장\n뽕 {p.PongCount}", 30, TextAnchor.MiddleCenter);
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

            _opponentsRow = CreateRow(root, new Vector2(0.03f, 0.84f), new Vector2(0.97f, 0.98f), 16).transform;

            _info = CreateText(root, "", 30, TextAnchor.UpperCenter);
            Anchor(_info.rectTransform, new Vector2(0.04f, 0.74f), new Vector2(0.96f, 0.83f));

            _log = CreateText(root, "", 28, TextAnchor.UpperCenter);
            Anchor(_log.rectTransform, new Vector2(0.04f, 0.46f), new Vector2(0.96f, 0.73f));

            var bar = CreateRow(root, new Vector2(0.05f, 0.30f), new Vector2(0.95f, 0.42f), 20).transform;
            _drawBtn = CreateButton(bar, "드로우", OnDraw);
            _stopBtn = CreateButton(bar, "스톱", OnStop);
            _pongBtn = CreateButton(bar, "뽕!", OnPong);
            _passBtn = CreateButton(bar, "패스", OnPass);
            _nextBtn = CreateButton(bar, "다음 판", OnNext);

            _handRow = CreateRow(root, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.28f), 14).transform;
        }

        private void CreateCardButton(Card card)
        {
            var go = new GameObject($"Card_{card.Number}{ColorLetter[(int)card.Color]}",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_handRow, false);
            go.GetComponent<Image>().color = Palette[(int)card.Color];

            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 130;
            le.preferredHeight = 200;

            var number = CreateText(go.transform, card.Number.ToString(), 70, TextAnchor.MiddleCenter);
            number.color = Color.white;
            Stretch(number.rectTransform);

            var letter = CreateText(go.transform, ColorLetter[(int)card.Color], 28, TextAnchor.UpperLeft);
            letter.color = new Color(1, 1, 1, 0.85f);
            Anchor(letter.rectTransform, new Vector2(0.08f, 0.76f), new Vector2(0.6f, 0.98f));

            var captured = card;
            go.GetComponent<Button>().onClick.AddListener(() => OnCardClicked(captured));
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
            layout.padding = new RectOffset(16, 16, 12, 12);
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
            le.preferredWidth = 170;
            le.preferredHeight = 90;
            var text = CreateText(go.transform, label, 36, TextAnchor.MiddleCenter);
            text.color = Color.black;
            Stretch(text.rectTransform);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go.GetComponent<Button>();
        }

        private void SetNextLabel(string label) => _nextBtn.GetComponentInChildren<Text>().text = label;

        private int TopDiscardNumber() => _round.DiscardPile[_round.DiscardPile.Count - 1].Number;

        private string CardLabel(Card c) => $"{c.Number}{ColorLetter[(int)c.Color]}";

        private readonly List<string> _events = new();

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
