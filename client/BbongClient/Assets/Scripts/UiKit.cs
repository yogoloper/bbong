using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 코드 생성 화면 공용 UI 헬퍼(로비·인증·상점·프로필 등). 가로 1920x1080 기준.
    /// 기존 LobbyBootstrap/GameTableBootstrap의 화면 생성 패턴을 추출.
    /// </summary>
    internal static class UiKit
    {
        public static readonly Color Accent = new(0.94f, 0.83f, 0.55f); // 전 화면 공용 소프트 골드(노란 텍스트 단일 출처)
        public static readonly Color ButtonColor = new(0.20f, 0.28f, 0.48f); // 네이비 계열 — 부차 버튼이 CTA보다 밝지 않게

        private static Font _font;

        public static Font Font => _font ??=
            Resources.Load<Font>("Fonts/Pretendard-SemiBold")
            ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static void EnsureEventSystem()
        {
            if (EventSystem.current == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }
        }

        /// <summary>화면용 전체 캔버스 + 네이비 배경 생성. topBar=true면 공통 상단바(닉네임·포인트).</summary>
        public static (GameObject canvasGo, Transform root) CreateScreen(string name, bool topBar = false)
        {
            var canvasGo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // 배경은 안전 영역 밖 — 노치 주변까지 채워야 검은 띠가 안 생긴다
            var bg = CreatePanel(canvasGo.transform, Color.white);
            bg.sprite = UiArt.Backdrop;
            Stretch(bg.rectTransform);

            var root = SafeArea.Wrap(canvasGo.transform);

            if (topBar)
            {
                TopBar(root);
            }

            return (canvasGo, root);
        }

        /// <summary>스프라이트 아이콘(클릭 통과). 비율 유지.</summary>
        public static Image CreateIcon(Transform parent, Sprite sprite, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            Anchor(go.GetComponent<RectTransform>(), min, max);
            return img;
        }

        /// <summary>공통 상단바: 좌측 아바타+닉네임, 우측 코인 + 보유 포인트.</summary>
        public static void TopBar(Transform root)
        {
            var panel = CreatePanel(root, new Color(0, 0, 0, 0.35f));
            if (UiArt.Panel9 != null)
            {
                panel.sprite = UiArt.Panel9;
                panel.type = Image.Type.Sliced;
                panel.color = new Color(0.10f, 0.16f, 0.32f, 0.95f); // 네이비 틴트 — 배경 테마와 통일
            }

            Anchor(panel.rectTransform, new Vector2(0f, 0.9f), new Vector2(1f, 1f));

            var hairline = CreatePanel(root, new Color(Accent.r, Accent.g, Accent.b, 0.30f)); // 골드 헤어라인
            Anchor(hairline.rectTransform, new Vector2(0f, 0.898f), new Vector2(1f, 0.9f));

            var avatar = CreatePanel(root, new Color(0.3f, 0.55f, 0.9f));
            avatar.sprite = UiArt.Pill;
            avatar.type = Image.Type.Sliced;
            Anchor(avatar.rectTransform, new Vector2(0.012f, 0.915f), new Vector2(0.05f, 0.985f));
            var initial = string.IsNullOrEmpty(Session.Nickname) ? "?" : Session.Nickname[..1];
            CreateText(avatar.transform, initial, 38, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one)
                .fontStyle = FontStyle.Bold;

            CreateText(root, Session.Nickname, 34, TextAnchor.MiddleLeft,
                new Vector2(0.06f, 0.9f), new Vector2(0.5f, 1f));

            // 설정 진입점. 오버레이로 열려 뒤 화면을 건드리지 않으므로 어느 화면에서든 안전하다.
            // 상단바 높이에 맞춰야 해서 최소 탭 높이 보정(EnsureTapHeight)을 타지 않게 직접 만든다.
            SettingsButton(root, new Vector2(0.575f, 0.912f), new Vector2(0.68f, 0.988f));

            // 코인+숫자를 우측 정렬 레이아웃으로 묶어 자릿수와 무관하게 우측 여백 고정
            var wallet = new GameObject("Wallet", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            wallet.transform.SetParent(root, false);
            Anchor(wallet.GetComponent<RectTransform>(), new Vector2(0.70f, 0.9f), new Vector2(0.988f, 1f));
            var lay = wallet.GetComponent<HorizontalLayoutGroup>();
            lay.childAlignment = TextAnchor.MiddleRight;
            lay.spacing = 10f;
            lay.childControlWidth = true;
            lay.childControlHeight = false;
            lay.childForceExpandWidth = false;
            lay.childForceExpandHeight = false;

            if (UiArt.Coin != null)
            {
                var coin = CreateIcon(wallet.transform, UiArt.Coin, Vector2.zero, Vector2.one);
                var coinLe = coin.gameObject.AddComponent<LayoutElement>();
                coinLe.preferredWidth = 52f;
                var coinRt = coin.rectTransform;
                coinRt.sizeDelta = new Vector2(52f, 52f);
            }

            var pts = CreateText(wallet.transform, $"{Session.Balance:N0}", 38, TextAnchor.MiddleRight,
                Vector2.zero, Vector2.one);
            pts.color = Accent;
            pts.fontStyle = FontStyle.Bold;
            var ptsRt = pts.rectTransform;
            ptsRt.sizeDelta = new Vector2(ptsRt.sizeDelta.x, 108f);
            BalanceLabel = pts; // 서버 재조회(RefreshMe) 후 갱신용 — 최신 화면의 상단바
        }

        /// <summary>
        /// 설정 화면으로 이동. 상단바는 화면마다 새로 그려지므로, 현재 살아 있는 화면 캔버스를
        /// 찾아 정리하고 넘어간다.
        /// </summary>
        /// <summary>
        /// 설정 진입 버튼. 상단바가 없는 화면(게임 테이블)도 직접 부를 수 있게 열어 둔다.
        /// 좁은 띠에 맞춰야 해서 최소 탭 높이 보정(EnsureTapHeight)을 타지 않게 직접 만든다.
        /// </summary>
        public static Button SettingsButton(Transform parent, Vector2 min, Vector2 max, int fontSize = 30)
        {
            var go = new GameObject("Settings", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.Button;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.16f, 0.24f, 0.42f, 0.9f);
            Anchor(go.GetComponent<RectTransform>(), min, max);
            CreateText(go.transform, "설정", fontSize, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one)
                .color = Color.white;
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(OpenSettings);
            ApplyButtonStates(btn);
            return btn;
        }

        public static void OpenSettings()
        {
            if (UnityEngine.Object.FindAnyObjectByType<SettingsBootstrap>() != null)
            {
                return; // 이미 열려 있다
            }

            PreviousBackAction = BackAction;
            new GameObject(nameof(SettingsBootstrap), typeof(SettingsBootstrap));
        }

        /// <summary>현재 화면 상단바의 잔액 라벨(게임 정산 후 재조회 반영용).</summary>
        public static Text BalanceLabel { get; private set; }

        public static void SyncBalanceLabel()
        {
            if (BalanceLabel != null)
            {
                BalanceLabel.text = $"{Session.Balance:N0}";
            }
        }

        /// <summary>화면 하단 중앙 고정 주요 CTA(게임 시작/매칭 시작 등 전 화면 통일).</summary>
        public static Button PrimaryCta(Transform root, string label, UnityEngine.Events.UnityAction onClick) =>
            CtaButton(root, label, new Vector2(0.34f, 0.05f), new Vector2(0.66f, 0.16f), onClick, 46);

        /// <summary>주요 액션(게임 시작 등) CTA 버튼. 일반 버튼과 같은 모양 + 골드 강조색(테마 일치).</summary>
        public static Button CtaButton(Transform parent, string label, Vector2 min, Vector2 max,
            UnityEngine.Events.UnityAction onClick, int fontSize = 48)
        {
            (min, max) = EnsureTapHeight(min, max);
            var go = new GameObject("Cta", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.Button; // 일반 버튼과 동일한 Kenney 9-slice
            img.type = Image.Type.Sliced;
            img.color = new Color(0.24f, 0.5f, 0.88f); // 우주 배경과 같은 블루 계열(배경보다 밝게 강조)
            Anchor(go.GetComponent<RectTransform>(), min, max);

            var text = CreateText(go.transform, label, fontSize, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            text.color = Color.white;
            text.fontStyle = FontStyle.Bold;

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
            ApplyButtonStates(btn);
            return btn;
        }

        public static Image CreatePanel(Transform parent, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<Image>();
        }

        public static Text CreateText(Transform parent, string content, int size, TextAnchor anchor,
            Vector2 min, Vector2 max)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Anchor(text.rectTransform, min, max);
            return text;
        }

        /// <summary>모바일 최소 터치 높이(≈48dp @ 1080유닛 캔버스). 버튼 팩토리가 하한을 강제한다.</summary>
        public const float MinTapHeight = 0.122f;

        /// <summary>앵커 높이가 터치 하한보다 작으면 중심 기준으로 늘리고 화면 안으로 클램프.</summary>
        private static (Vector2 min, Vector2 max) EnsureTapHeight(Vector2 min, Vector2 max)
        {
            var h = max.y - min.y;
            if (h >= MinTapHeight || h >= 0.99f) // 레이아웃 그룹용 stretch(0~1)는 그대로
            {
                return (min, max);
            }

            var c = (min.y + max.y) / 2f;
            var half = MinTapHeight / 2f;
            var lo = Mathf.Clamp(c - half, 0f, 1f - MinTapHeight);
            return (new Vector2(min.x, lo), new Vector2(max.x, lo + MinTapHeight));
        }

        /// <summary>hover/pressed/disabled 상태색 — 기본값(0.96 하이라이트)은 화면에서 무변화라 명시한다.</summary>
        private static void ApplyButtonStates(Button btn)
        {
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
        }

        public static Button CreateButton(Transform parent, string label, Vector2 min, Vector2 max,
            UnityEngine.Events.UnityAction onClick, int fontSize = 40)
        {
            (min, max) = EnsureTapHeight(min, max);
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.Button;
            img.type = Image.Type.Sliced;
            img.color = ButtonColor;
            Anchor(go.GetComponent<RectTransform>(), min, max);

            var text = CreateText(go.transform, label, fontSize, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            text.color = Color.white;

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
            ApplyButtonStates(btn);
            return btn;
        }

        public static InputField CreateInputField(Transform parent, string initial, int charLimit,
            Vector2 min, Vector2 max)
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.white;
            Anchor(go.GetComponent<RectTransform>(), min, max);

            var text = CreateText(go.transform, initial, 36, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0f), new Vector2(0.95f, 1f));
            text.color = Color.black;
            text.supportRichText = false;

            var input = go.AddComponent<InputField>();
            input.textComponent = text;
            input.text = initial;
            input.characterLimit = charLimit;
            return input;
        }

        /// <summary>
        /// 기기 뒤로가기(안드로이드)가 눌렸을 때 실행할 동작. 화면마다 뒤로 동작이 정확히 하나뿐이라
        /// 스택 대신 슬롯 하나로 충분하다. 화면을 새로 만들 때마다 덮어쓴다.
        /// </summary>
        public static System.Action BackAction { get; set; }

        /// <summary>설정 오버레이가 닫힐 때 돌려줄, 오버레이 직전 화면의 뒤로 동작.</summary>
        public static System.Action PreviousBackAction { get; private set; }

        public static void InvokeBack()
        {
            if (BackAction != null)
            {
                BackAction();
                return;
            }

            Application.Quit(); // 최상위 화면에서 뒤로 = 앱 종료(기기 관례)
        }

        /// <summary>화면 왼쪽 하단 고정 뒤로가기 버튼(루미큐브식, 상단바와 안 겹침).</summary>
        public static Button BackButton(Transform root, UnityEngine.Events.UnityAction onBack)
        {
            var btn = CreateButton(root, "← 뒤로", new Vector2(0.015f, 0.02f), new Vector2(0.12f, 0.095f), onBack, 32);
            btn.transform.SetAsLastSibling(); // 항상 최상위(다른 패널에 안 가림)
            BackAction = () => onBack(); // 기기 뒤로가기도 같은 동작
            return btn;
        }

        public static void Stretch(RectTransform rt) => Anchor(rt, Vector2.zero, Vector2.one);

        public static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>남아 있는 게임 테이블 전부 폭파(캔버스 포함) — 로비 화면이 뜰 때 이전 판이 잔존하는 버그 방지.</summary>
        public static void DestroyStrayTables()
        {
            foreach (var stray in UnityEngine.Object.FindObjectsByType<GameTableView>(FindObjectsSortMode.None))
            {
                if (stray.CanvasGo != null)
                {
                    UnityEngine.Object.Destroy(stray.CanvasGo);
                }

                UnityEngine.Object.Destroy(stray.gameObject);
            }
        }

        /// <summary>화면 전환: 현재 화면 파기 후 새 부트스트랩 컴포넌트 생성.</summary>
        public static void GoTo<T>(GameObject currentCanvas, MonoBehaviour current) where T : MonoBehaviour
        {
            new GameObject(typeof(T).Name, typeof(T));
            if (currentCanvas != null)
            {
                UnityEngine.Object.Destroy(currentCanvas);
            }

            UnityEngine.Object.Destroy(current.gameObject);
        }
    }
}
