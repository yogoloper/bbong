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
        private static readonly Color DisabledColor = new(0.45f, 0.45f, 0.45f);

        private int _playerCount = 4;
        private int _stake = 1000;

        private Font _font;
        private GameObject _canvasGo;
        private Text _wallet;
        private Text _summary;
        private Button _startBtn;
        private readonly List<(int value, Button button)> _playerChoices = new();
        private readonly List<(int value, Button button)> _stakeChoices = new();

        private void Start()
        {
            _font = Resources.Load<Font>("Fonts/Pretendard-SemiBold")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildUi();

            // 잔액으로 못 거는 판돈이 선택돼 있으면 감당 가능한 최대 판돈으로 조정
            if (!PlayerWallet.CanAfford(_stake))
            {
                foreach (var stake in GameConfig.StakeOptions)
                {
                    if (PlayerWallet.CanAfford(stake))
                    {
                        _stake = stake;
                    }
                }
            }

            RefreshSelection();
        }

        private void OnStartGame()
        {
            if (!GameConfig.IsValidPlayerCount(_playerCount) || !GameConfig.IsValidStake(_stake)
                || !PlayerWallet.CanAfford(_stake))
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
            scaler.referenceResolution = new Vector2(1920, 1080); // 폰 가로(16:9 기준, 넓은 화면은 여유 확장)
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // 화면비 달라도 글씨 비대 방지
            var root = _canvasGo.transform;

            var felt = CreatePanel(root, Color.white);
            felt.sprite = UiArt.Felt;
            Stretch(felt.rectTransform);

            var title = CreateText(root, "나이롱뽕", 96, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.95f));

            var subtitle = CreateText(root, "방 만들기", 40, TextAnchor.MiddleCenter);
            subtitle.color = new Color(1f, 1f, 1f, 0.75f);
            Anchor(subtitle.rectTransform, new Vector2(0.05f, 0.73f), new Vector2(0.95f, 0.785f));

            _wallet = CreateText(root, "", 36, TextAnchor.MiddleCenter);
            _wallet.fontStyle = FontStyle.Bold;
            _wallet.color = new Color(0.5f, 0.95f, 0.6f);
            Anchor(_wallet.rectTransform, new Vector2(0.05f, 0.665f), new Vector2(0.95f, 0.725f));

            var playerLabel = CreateText(root, "인원", 40, TextAnchor.MiddleCenter);
            Anchor(playerLabel.rectTransform, new Vector2(0.05f, 0.585f), new Vector2(0.95f, 0.635f));

            var playerRow = CreateRow(root, new Vector2(0.25f, 0.46f), new Vector2(0.75f, 0.575f), 14).transform;
            for (var n = GameConfig.MinPlayers; n <= GameConfig.MaxPlayers; n++)
            {
                CreateChoice(playerRow, $"{n}명", n, _playerChoices, v => _playerCount = v);
            }

            var stakeLabel = CreateText(root, "입장료", 40, TextAnchor.MiddleCenter);
            Anchor(stakeLabel.rectTransform, new Vector2(0.05f, 0.38f), new Vector2(0.95f, 0.43f));

            var stakeRow = CreateRow(root, new Vector2(0.25f, 0.255f), new Vector2(0.75f, 0.37f), 14).transform;
            foreach (var stake in GameConfig.StakeOptions)
            {
                CreateChoice(stakeRow, $"{stake:N0}", stake, _stakeChoices, v => _stake = v);
            }

            _summary = CreateText(root, "", 36, TextAnchor.MiddleCenter);
            _summary.color = new Color(1f, 0.92f, 0.4f);
            Anchor(_summary.rectTransform, new Vector2(0.05f, 0.165f), new Vector2(0.95f, 0.23f));

            _startBtn = CreateButton(root, "게임 시작", OnStartGame);
            Anchor((RectTransform)_startBtn.transform, new Vector2(0.40f, 0.025f), new Vector2(0.60f, 0.145f));
            _startBtn.GetComponentInChildren<Text>().fontSize = 48;

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

        private void RefreshSelection()
        {
            _wallet.text = $"{PlayerWallet.Balance:N0} 포인트";

            foreach (var (value, button) in _playerChoices)
            {
                button.GetComponent<Image>().color = value == _playerCount ? SelectedColor : UnselectedColor;
            }

            // 잔액으로 못 거는 판돈은 비활성(회색)
            foreach (var (value, button) in _stakeChoices)
            {
                var affordable = PlayerWallet.CanAfford(value);
                button.interactable = affordable;
                button.GetComponent<Image>().color = !affordable ? DisabledColor
                    : value == _stake ? SelectedColor : UnselectedColor;
            }

            var canStart = PlayerWallet.CanAfford(_stake);
            _startBtn.interactable = canStart;
            _summary.text = canStart
                ? $"나 + 봇 {_playerCount - 1}  ·  입장료 {_stake:N0}"
                : "재화가 부족합니다 (Play를 다시 시작하면 충전)";
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
