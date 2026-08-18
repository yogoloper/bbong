using System.Collections.Generic;
using BbongCore.Ai;
using BbongCore.Config;
using UnityEngine;
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
        private static readonly Color SelectedColor = UiTheme.Accent;
        private static readonly Color UnselectedColor = UiTheme.Control; // 어두운 네이비 — 밝은 것은 선택/CTA뿐

        // 봇 난이도 표시명(쉬움/보통/어려움)
        private static readonly (BotDifficulty value, string label)[] Difficulties =
        {
            (BotDifficulty.Easy, "쉬움"), (BotDifficulty.Normal, "보통"), (BotDifficulty.Hard, "어려움")
        };

        private int _playerCount = 4;
        private BotDifficulty _difficulty = BotDifficulty.Normal;

        private GameObject _canvasGo;
        private Text _summary;
        private Button _startBtn;
        private readonly List<(int value, Button button)> _playerChoices = new();
        private readonly List<(int value, Button button)> _difficultyChoices = new();

        private void Start()
        {
            UiKit.EnsureEventSystem();
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
            var (canvas, root) = UiKit.CreateScreen("LobbyCanvas", topBar: true);
            _canvasGo = canvas;

            var title = UiKit.CreateText(root, "연습 게임", 56, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.79f), new Vector2(0.95f, 0.875f));
            title.fontStyle = FontStyle.Bold;

            var subtitle = UiKit.CreateText(root, "봇 상대로 부담 없이 한 판, 포인트는 안 걸어요", 30,
                TextAnchor.MiddleCenter, new Vector2(0.05f, 0.73f), new Vector2(0.95f, 0.78f));
            subtitle.color = UiTheme.InkMuted;

            UiKit.CreateText(root, "인원", 36, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.585f), new Vector2(0.95f, 0.635f));

            var playerRow = CreateRow(root, new Vector2(0.25f, 0.46f), new Vector2(0.75f, 0.575f), 14).transform;
            for (var n = GameConfig.MinPlayers; n <= GameConfig.MaxPlayers; n++)
            {
                CreateChoice(playerRow, $"{n}명", n, _playerChoices, v => _playerCount = v);
            }

            UiKit.CreateText(root, "봇 난이도", 36, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.38f), new Vector2(0.95f, 0.43f));

            var diffRow = CreateRow(root, new Vector2(0.30f, 0.255f), new Vector2(0.70f, 0.37f), 14).transform;
            foreach (var (value, label) in Difficulties)
            {
                CreateChoice(diffRow, label, (int)value, _difficultyChoices, v => _difficulty = (BotDifficulty)v);
            }

            // 요약은 확인용 문구 — 선택 칩(골드)·CTA(파랑)에 이어 세 번째 강조가 되면 안 된다
            _summary = UiKit.CreateText(root, "", 36, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.165f), new Vector2(0.95f, 0.23f));
            _summary.color = UiTheme.InkMuted;

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
            text.color = selected ? UiTheme.Ink : UiTheme.InkOn;
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

        // ── UI 헬퍼 (레이아웃 그룹 기반이라 앵커 기반 UiKit.CreateButton을 못 쓴다) ──

        private static Image CreateRow(Transform parent, Vector2 min, Vector2 max, float spacing)
        {
            var panel = UiKit.CreatePanel(parent, UiTheme.Trough);
            UiKit.Anchor(panel.rectTransform, min, max);
            var layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(14, 14, 10, 10);
            return panel;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
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
            UiKit.CreateText(go.transform, label, 28, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
            UiKit.ApplyButtonStates(btn); // 이 화면만 hover/press 반응이 없던 버그 수정
            return btn;
        }
    }
}
