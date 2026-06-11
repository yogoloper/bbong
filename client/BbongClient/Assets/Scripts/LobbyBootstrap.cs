using System.Collections.Generic;
using BbongCore.Config;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 로비/방 만들기 화면(architecture §4-2의 로컬 싱글 축소판, §4-4).
    /// 인원(2~6)·판돈(GameConfig.StakeOptions)을 고르고 게임 시작 → GameTableBootstrap 생성.
    /// 빈 GameObject에 이 컴포넌트 하나 붙이고 Play. 게임 종료 화면의 '로비로'로 복귀.
    /// </summary>
    public sealed class LobbyBootstrap : MonoBehaviour
    {
        private static readonly Color SelectedColor = new(1f, 0.85f, 0.3f);
        private static readonly Color UnselectedColor = new(0.95f, 0.95f, 0.95f);

        private int _playerCount = 4;
        private int _stake = 1000;

        private Font _font;
        private GameObject _canvasGo;
        private Text _summary;
        private readonly List<(int value, Image bg)> _playerChoices = new();
        private readonly List<(int value, Image bg)> _stakeChoices = new();

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildUi();
            RefreshSelection();
        }

        private void OnStartGame()
        {
            if (!GameConfig.IsValidPlayerCount(_playerCount) || !GameConfig.IsValidStake(_stake))
            {
                return;
            }

            var table = new GameObject("GameTable").AddComponent<GameTableBootstrap>();
            table.PlayerCount = _playerCount;
            table.Stake = _stake;
            Destroy(_canvasGo);
            Destroy(gameObject);
        }

        // ── UI 생성 ──

        private void BuildUi()
        {
            _canvasGo = new GameObject("LobbyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            var root = _canvasGo.transform;

            Stretch(CreatePanel(root, new Color(0.12f, 0.30f, 0.20f)).rectTransform);

            var title = CreateText(root, "나이롱뽕", 96, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.92f));

            var subtitle = CreateText(root, "방 만들기", 40, TextAnchor.MiddleCenter);
            subtitle.color = new Color(1f, 1f, 1f, 0.75f);
            Anchor(subtitle.rectTransform, new Vector2(0.05f, 0.755f), new Vector2(0.95f, 0.795f));

            var playerLabel = CreateText(root, "인원", 40, TextAnchor.MiddleCenter);
            Anchor(playerLabel.rectTransform, new Vector2(0.05f, 0.675f), new Vector2(0.95f, 0.715f));

            var playerRow = CreateRow(root, new Vector2(0.05f, 0.575f), new Vector2(0.95f, 0.665f), 14).transform;
            for (var n = GameConfig.MinPlayers; n <= GameConfig.MaxPlayers; n++)
            {
                CreateChoice(playerRow, $"{n}명", n, _playerChoices, v => _playerCount = v);
            }

            var stakeLabel = CreateText(root, "판돈", 40, TextAnchor.MiddleCenter);
            Anchor(stakeLabel.rectTransform, new Vector2(0.05f, 0.51f), new Vector2(0.95f, 0.55f));

            var stakeRow = CreateRow(root, new Vector2(0.05f, 0.41f), new Vector2(0.95f, 0.50f), 14).transform;
            foreach (var stake in GameConfig.StakeOptions)
            {
                CreateChoice(stakeRow, $"{stake:N0}", stake, _stakeChoices, v => _stake = v);
            }

            _summary = CreateText(root, "", 36, TextAnchor.MiddleCenter);
            _summary.color = new Color(1f, 0.92f, 0.4f);
            Anchor(_summary.rectTransform, new Vector2(0.05f, 0.325f), new Vector2(0.95f, 0.385f));

            var startBtn = CreateButton(root, "게임 시작", OnStartGame);
            Anchor((RectTransform)startBtn.transform, new Vector2(0.28f, 0.18f), new Vector2(0.72f, 0.27f));
            startBtn.GetComponentInChildren<Text>().fontSize = 48;
        }

        /// <summary>선택지 버튼 1개 생성 + 선택 강조용 배경 등록.</summary>
        private void CreateChoice(Transform parent, string label, int value,
            List<(int value, Image bg)> registry, System.Action<int> onPick)
        {
            var button = CreateButton(parent, label, () =>
            {
                onPick(value);
                RefreshSelection();
            });
            registry.Add((value, button.GetComponent<Image>()));
        }

        private void RefreshSelection()
        {
            foreach (var (value, bg) in _playerChoices)
            {
                bg.color = value == _playerCount ? SelectedColor : UnselectedColor;
            }

            foreach (var (value, bg) in _stakeChoices)
            {
                bg.color = value == _stake ? SelectedColor : UnselectedColor;
            }

            _summary.text = $"나 + 봇 {_playerCount - 1}  ·  판돈 {_stake:N0}";
        }

        // ── UI 헬퍼 (GameTableBootstrap과 동일 패턴) ──

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
            go.GetComponent<Image>().color = UnselectedColor;
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 160;
            le.preferredHeight = 90;
            var text = CreateText(go.transform, label, 34, TextAnchor.MiddleCenter);
            text.color = Color.black;
            Stretch(text.rectTransform);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go.GetComponent<Button>();
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
