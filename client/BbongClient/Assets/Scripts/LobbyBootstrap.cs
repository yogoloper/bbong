using System.Collections.Generic;
using BbongCore.Ai;
using BbongCore.Config;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 로비/방 만들기 화면(architecture §4-2의 로컬 싱글 축소판, §4-4).
    /// 인원(2~6)·봇 난이도를 고르고 게임 시작 → GameTableBootstrap 생성. 연습은 판돈 없음(무료).
    /// 빈 GameObject에 이 컴포넌트 하나 붙이고 Play. 게임 종료 화면의 '로비로'로 복귀.
    /// </summary>
    public sealed class LobbyBootstrap : MonoBehaviour
    {
        private static readonly Color SelectedColor = UiKit.Accent;
        private static readonly Color UnselectedColor = new(0.16f, 0.24f, 0.42f); // 어두운 네이비 — 밝은 것은 선택/CTA뿐

        // 봇 난이도 표시명(쉬움/보통/어려움)
        private static readonly (BotDifficulty value, string label)[] Difficulties =
        {
            (BotDifficulty.Easy, "쉬움"), (BotDifficulty.Normal, "보통"), (BotDifficulty.Hard, "어려움")
        };

        private int _playerCount = 4;
        private BotDifficulty _difficulty = BotDifficulty.Normal;

        private Font _font;
        private GameObject _canvasGo;
        private Text _summary;
        private Button _startBtn;
        private readonly List<(int value, Button button)> _playerChoices = new();
        private readonly List<(int value, Button button)> _difficultyChoices = new();

        private void Start()
        {
            _font = Resources.Load<Font>("Fonts/Pretendard-SemiBold")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            UiKit.DestroyStrayTables(); // 연습 설정에 들어온 시점엔 이전 판이 남아 있으면 안 됨
            BuildUi();
            RefreshSelection();
        }

        private bool _started;

        private void OnStartGame()
        {
            if (!GameConfig.IsValidPlayerCount(_playerCount) || _started)
            {
                return;
            }

            _started = true; // 더블클릭으로 테이블이 2개 생기는 것 방지

            // 연습은 판돈 없음(무료). 봇 인원·난이도만 적용.
            var table = new GameObject("GameTable").AddComponent<GameTableBootstrap>();
            table.PlayerCount = _playerCount;
            table.Difficulty = _difficulty;
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
            scaler.referenceResolution = new Vector2(1920, 1080); // 폰 가로(16:9 기준, 넓은 화면은 여유 확장)
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // 화면비 달라도 글씨 비대 방지
            var root = _canvasGo.transform;

            var bg = CreatePanel(root, Color.white);
            bg.sprite = UiArt.Backdrop;
            Stretch(bg.rectTransform);

            UiKit.TopBar(root); // 공통 상단바(다른 화면과 통일)

            var title = CreateText(root, "연습 게임", 60, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(0.05f, 0.79f), new Vector2(0.95f, 0.875f));

            var subtitle = CreateText(root, "컴퓨터와 연습 — 우승해도 포인트 없음", 30, TextAnchor.MiddleCenter);
            subtitle.color = new Color(1f, 1f, 1f, 0.7f);
            Anchor(subtitle.rectTransform, new Vector2(0.05f, 0.73f), new Vector2(0.95f, 0.78f));

            var playerLabel = CreateText(root, "인원", 40, TextAnchor.MiddleCenter);
            Anchor(playerLabel.rectTransform, new Vector2(0.05f, 0.585f), new Vector2(0.95f, 0.635f));

            var playerRow = CreateRow(root, new Vector2(0.25f, 0.46f), new Vector2(0.75f, 0.575f), 14).transform;
            for (var n = GameConfig.MinPlayers; n <= GameConfig.MaxPlayers; n++)
            {
                CreateChoice(playerRow, $"{n}명", n, _playerChoices, v => _playerCount = v);
            }

            var diffLabel = CreateText(root, "봇 난이도", 40, TextAnchor.MiddleCenter);
            Anchor(diffLabel.rectTransform, new Vector2(0.05f, 0.38f), new Vector2(0.95f, 0.43f));

            var diffRow = CreateRow(root, new Vector2(0.30f, 0.255f), new Vector2(0.70f, 0.37f), 14).transform;
            foreach (var (value, label) in Difficulties)
            {
                CreateChoice(diffRow, label, (int)value, _difficultyChoices, v => _difficulty = (BotDifficulty)v);
            }

            _summary = CreateText(root, "", 36, TextAnchor.MiddleCenter);
            _summary.color = UiKit.Accent;
            Anchor(_summary.rectTransform, new Vector2(0.05f, 0.165f), new Vector2(0.95f, 0.23f));

            _startBtn = UiKit.PrimaryCta(root, "게임 시작", OnStartGame);

            UiKit.BackButton(root, OnBack);
        }

        /// <summary>메인 로비로 돌아가기(연습 설정 취소).</summary>
        private void OnBack() => UiKit.GoTo<MainLobbyBootstrap>(_canvasGo, this);

        /// <summary>선택지 버튼 1개 생성 + 선택 강조/비활성 표시용 등록.</summary>
        private void CreateChoice(Transform parent, string label, int value,
            List<(int value, Button button)> registry, System.Action<int> onPick)
        {
            var button = CreateButton(parent, label, () =>
            {
                onPick(value);
                RefreshSelection();
            });
            registry.Add((value, button));
        }

        private static void Paint(Button button, bool selected)
        {
            button.GetComponent<Image>().color = selected ? SelectedColor : UnselectedColor;
            var text = button.GetComponentInChildren<Text>();
            text.color = selected ? Color.black : Color.white;
            text.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        private void RefreshSelection()
        {
            foreach (var (value, button) in _playerChoices)
            {
                Paint(button, value == _playerCount);
            }

            foreach (var (value, button) in _difficultyChoices)
            {
                Paint(button, value == (int)_difficulty);
            }

            var label = Difficulties[System.Array.FindIndex(Difficulties, d => d.value == _difficulty)].label;
            _summary.text = $"나 + 봇 {_playerCount - 1}  ·  난이도 {label}";
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
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.Button;
            img.type = Image.Type.Sliced;
            img.color = UnselectedColor;
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 172;  // 맞춤게임 칩 폭(0.09)과 동일 그리드
            le.preferredHeight = 132; // 터치 하한(UiKit.MinTapHeight ≒ 132유닛)과 통일
            var text = CreateText(go.transform, label, 28, TextAnchor.MiddleCenter);
            text.color = Color.white;
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
