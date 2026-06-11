using System.Linq;
using BbongCore.Ai;
using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Rules;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 코드 생성 카드 + 최소 인터랙션 데모(Phase 3).
    /// 빈 GameObject에 이 컴포넌트만 붙이고 Play하면 UI 전체가 코드로 생성됩니다.
    /// 내 손패(P0)를 클릭해 버리고, 드로우 버튼으로 한 장 뽑습니다. 나머지 좌석은 봇이 자동 진행.
    /// </summary>
    public sealed class GameTableBootstrap : MonoBehaviour
    {
        private const int PlayerCount = 3;
        private const int MySeat = 0;

        private static readonly Color[] Palette =
        {
            new Color(0.85f, 0.23f, 0.23f), // Red
            new Color(0.20f, 0.45f, 0.85f), // Blue
            new Color(0.20f, 0.62f, 0.35f), // Green
            new Color(0.90f, 0.75f, 0.15f)  // Yellow
        };

        private static readonly string[] ColorLetter = { "R", "B", "G", "Y" };

        private readonly Bot _bot = new(BotDifficulty.Normal);
        private RoundState _round;
        private bool _roundOver;
        private bool _myTurnDrawn; // 내 턴에 드로우 완료(=버릴 차례)

        private Font _font;
        private Transform _handRow;
        private Text _info;
        private Text _log;
        private Button _drawButton;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildUi();
            StartNewRound();
        }

        // ── 게임 흐름 ──

        private void StartNewRound()
        {
            _round = RoundState.Deal(Deck.CreateStandard(), PlayerCount, new SeededRandom(Random.Range(1, 99999)));
            _roundOver = false;
            _myTurnDrawn = false;
            SetLog("새 판 시작. P0(나)부터. 드로우 누르세요.");
            AdvanceToMyTurn(); // 혹시 선이 내가 아니면 봇 먼저
            Refresh();
        }

        private void OnDrawClicked()
        {
            if (_roundOver || _round.CurrentSeat != MySeat || _myTurnDrawn)
            {
                return;
            }

            _round = _round.Draw();
            _myTurnDrawn = true;

            var meld = HandEvaluator.Evaluate(_round.CurrentPlayer.Hand);
            if (meld.Type != MeldType.None)
            {
                SetLog($"족보 {meld.Type}({meld.Score})! 판 종료. 다시 시작하려면 드로우.");
                _roundOver = true;
            }
            else
            {
                SetLog("버릴 카드를 클릭하세요.");
            }

            Refresh();
        }

        private void OnCardClicked(Card card)
        {
            if (_roundOver)
            {
                StartNewRound();
                return;
            }

            if (_round.CurrentSeat != MySeat || !_myTurnDrawn)
            {
                return;
            }

            _round = _round.Discard(card);
            _myTurnDrawn = false;
            RunBotsUntilMyTurn();
            Refresh();
        }

        /// <summary>다른 좌석(봇)을 내 차례가 돌아올 때까지 자동 진행합니다(드로우→버림, 족보면 종료).</summary>
        private void RunBotsUntilMyTurn()
        {
            var guard = 0;
            while (!_roundOver && _round.CurrentSeat != MySeat && guard++ < 100)
            {
                _round = _round.Draw();
                var seat = _round.CurrentSeat;
                var meld = HandEvaluator.Evaluate(_round.CurrentPlayer.Hand);
                if (meld.Type != MeldType.None)
                {
                    SetLog($"P{seat} 족보 {meld.Type}({meld.Score})! 판 종료. 카드/드로우로 새 판.");
                    _roundOver = true;
                    return;
                }

                var discard = _bot.ChooseDiscard(_round.CurrentPlayer.Hand);
                _round = _round.Discard(discard);
            }

            if (!_roundOver)
            {
                SetLog("내 차례. 드로우 누르세요.");
            }
        }

        private void AdvanceToMyTurn()
        {
            if (_round.CurrentSeat != MySeat)
            {
                RunBotsUntilMyTurn();
            }
        }

        // ── 렌더링 ──

        private void Refresh()
        {
            var p = _round.Players[MySeat];
            var topDiscard = _round.DiscardPile.Count > 0
                ? CardLabel(_round.DiscardPile[_round.DiscardPile.Count - 1])
                : "-";

            _info.text =
                $"턴: P{_round.CurrentSeat}   바닥더미: {_round.DrawPile.Count}   버림: {topDiscard}\n" +
                $"내 손패 {p.Hand.Count}장  합 {p.Hand.Sum()}";

            _drawButton.interactable = !_roundOver && _round.CurrentSeat == MySeat && !_myTurnDrawn;

            RenderHand(p.Hand);
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

            var bg = CreatePanel(canvasGo.transform, new Color(0.12f, 0.30f, 0.20f));
            Stretch(bg.GetComponent<RectTransform>());

            _info = CreateText(canvasGo.transform, "", 34, TextAnchor.UpperCenter);
            Anchor(_info.rectTransform, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.95f));

            _log = CreateText(canvasGo.transform, "", 28, TextAnchor.MiddleCenter);
            Anchor(_log.rectTransform, new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.78f));

            var handPanel = CreatePanel(canvasGo.transform, new Color(0, 0, 0, 0.15f));
            Anchor(handPanel.GetComponent<RectTransform>(), new Vector2(0.03f, 0.20f), new Vector2(0.97f, 0.42f));
            var layout = handPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(20, 20, 20, 20);
            _handRow = handPanel.transform;

            _drawButton = CreateButton(canvasGo.transform, "드로우", OnDrawClicked);
            Anchor(_drawButton.GetComponent<RectTransform>(), new Vector2(0.35f, 0.06f), new Vector2(0.65f, 0.15f));
        }

        private void CreateCardButton(Card card)
        {
            var go = new GameObject($"Card_{card.Number}{ColorLetter[(int)card.Color]}",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));

            go.transform.SetParent(_handRow, false);
            go.GetComponent<Image>().color = Palette[(int)card.Color];

            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 150;
            le.preferredHeight = 220;

            var number = CreateText(go.transform, card.Number.ToString(), 80, TextAnchor.MiddleCenter);
            number.color = Color.white;
            Stretch(number.rectTransform);

            var letter = CreateText(go.transform, ColorLetter[(int)card.Color], 30, TextAnchor.UpperLeft);
            letter.color = new Color(1, 1, 1, 0.85f);
            Anchor(letter.rectTransform, new Vector2(0.08f, 0.78f), new Vector2(0.5f, 0.98f));

            var captured = card;
            go.GetComponent<Button>().onClick.AddListener(() => OnCardClicked(captured));
        }

        private string CardLabel(Card c) => $"{c.Number}{ColorLetter[(int)c.Color]}";

        // ── UI 헬퍼 ──

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            // 새 Input System 사용 프로젝트라 InputSystemUIInputModule 사용
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
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
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.95f, 0.95f, 0.95f);
            var text = CreateText(go.transform, label, 40, TextAnchor.MiddleCenter);
            text.color = Color.black;
            Stretch(text.rectTransform);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go.GetComponent<Button>();
        }

        private void SetLog(string message) => _log.text = message;

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
