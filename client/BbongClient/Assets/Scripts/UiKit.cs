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

        // ── 색 사다리 ──
        // 화면마다 네이비를 조금씩 다르게 섞어 쓰다 보니 같은 판인데 재질이 달라 보였다.
        // 층은 딱 셋이다: 배경 < 판(Panel) < 판 위의 면(Surface). 그 위는 골드뿐.
        public static readonly Color Ink = new(0.07f, 0.11f, 0.22f);                // 골드 바탕 위 글자
        public static readonly Color Panel = new(0.10f, 0.15f, 0.30f, 0.94f);       // 큰 판(상단바·보드·시트)
        public static readonly Color Surface = new(0.15f, 0.22f, 0.42f, 0.92f);     // 판 위 면(카드·타일·배지)
        public static readonly Color SurfaceDim = new(0.13f, 0.19f, 0.36f, 0.75f);  // 안 고른 면
        public static readonly Color ButtonColor = new(0.16f, 0.24f, 0.42f);        // 부차 버튼 단일 네이비

        // 글자 투명도도 세 단이면 충분하다. 0.8·0.7·0.55·0.45·0.38이 섞여 있으면
        // 무엇이 더 중요한 정보인지 눈이 순서를 못 세운다.
        public static readonly Color TextSub = new(1f, 1f, 1f, 0.72f);   // 부연·설명
        public static readonly Color TextFaint = new(1f, 1f, 1f, 0.42f); // 라벨·단위·비활성
        public static readonly Color TextGhost = new(1f, 1f, 1f, 0.22f); // 빈칸 표시("-")
        public static readonly Color Warn = new(1f, 0.80f, 0.50f);       // 안내·에러 한 줄(골드보다 따뜻하게)

        /// <summary>알파만 바꾼 골드(헤어라인·옅은 강조). 화면마다 만들던 지역 헬퍼를 한곳으로.</summary>
        public static Color Gold(float alpha) => new(Accent.r, Accent.g, Accent.b, alpha);

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
        public static (GameObject canvasGo, Transform root) CreateScreen(string name, bool topBar = false,
            bool profileLink = true)
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
                TopBar(root, profileLink);
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
        public static void TopBar(Transform root, bool profileLink = true)
        {
            // 띠 자체는 안전 영역 밖 — 화면 좌우 끝까지 채워야 옆구리에 배경이 비치지 않는다.
            // 캔버스 직속으로 올리되 배경(0번) 바로 위, 안전 영역 루트 아래에 끼워 넣는다.
            // root가 안전 영역일 수도, 캔버스 자신일 수도 있어(화면마다 다르다) 캔버스를 거슬러 찾는다.
            var canvas = root.GetComponentInParent<Canvas>().transform;
            var panel = CreatePanel(canvas, new Color(0, 0, 0, 0.35f));
            if (UiArt.Panel9 != null)
            {
                panel.sprite = UiArt.Panel9;
                panel.type = Image.Type.Sliced;
                panel.color = Panel; // 보드·시트와 같은 네이비 — 판은 어느 화면에서나 한 가지 색
            }

            Anchor(panel.rectTransform, new Vector2(0f, 0.9f), new Vector2(1f, 1f));
            panel.transform.SetSiblingIndex(1);

            var hairline = CreatePanel(canvas, Gold(0.30f)); // 골드 헤어라인
            Anchor(hairline.rectTransform, new Vector2(0f, 0.898f), new Vector2(1f, 0.9f));
            hairline.transform.SetSiblingIndex(2);

            var avatar = CreatePanel(root, new Color(0.3f, 0.55f, 0.9f));
            avatar.sprite = UiArt.Pill;
            avatar.type = Image.Type.Sliced;
            Anchor(avatar.rectTransform, new Vector2(0.006f, 0.915f), new Vector2(0.044f, 0.985f));
            var initial = string.IsNullOrEmpty(Session.Nickname) ? "?" : Session.Nickname[..1];
            CreateText(avatar.transform, initial, 38, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one)
                .fontStyle = FontStyle.Bold;

            CreateText(root, Session.Nickname, 34, TextAnchor.MiddleLeft,
                new Vector2(0.054f, 0.9f), new Vector2(0.5f, 1f));

            // 아바타가 프로필 진입점. 방에 들어가 있는 화면에서는 링크를 안 건다 —
            // 화면을 갈아엎으면 방 연결만 남고 상태가 어긋난다.
            if (profileLink)
            {
                var avatarBtn = avatar.gameObject.AddComponent<Button>();
                avatarBtn.onClick.AddListener(GoToProfile);
                ApplyButtonStates(avatarBtn);
            }

            // 설정은 상단바 안이 아니라 바로 아래 오른쪽 끝에 둔다 — 잔액과 한 줄에 넣으면
            // 정사각 아이콘 자리가 안 나오고, 자릿수가 늘 때마다 위치가 밀린다.
            CornerSettingsButton(root);

            // 코인+숫자를 우측 정렬 레이아웃으로 묶어 자릿수와 무관하게 우측 여백 고정
            var wallet = new GameObject("Wallet", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            wallet.transform.SetParent(root, false);
            Anchor(wallet.GetComponent<RectTransform>(), new Vector2(0.70f, 0.9f), new Vector2(0.994f, 1f));
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
        /// 설정 진입 버튼(톱니바퀴). 상단바가 없는 화면(게임 테이블)도 직접 부를 수 있게 열어 둔다.
        /// 글자 대신 아이콘인 이유는 어느 화면에서나 같은 자리·같은 크기로 놓기 위해서다 —
        /// "설정"이라는 글자는 폭이 화면마다 달라져 정사각 자리에 안 맞는다.
        /// 최소 탭 높이 보정(EnsureTapHeight)은 정사각 비율을 깨서 타지 않게 직접 만든다.
        /// </summary>
        public static Button SettingsButton(Transform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Settings", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.Button;
            img.type = Image.Type.Sliced;
            img.color = new Color(ButtonColor.r, ButtonColor.g, ButtonColor.b, 0.9f); // 부차 버튼과 같은 네이비
            Anchor(go.GetComponent<RectTransform>(), min, max);

            CreateIcon(go.transform, UiArt.IconGear, new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f));

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(OpenSettings);
            ApplyButtonStates(btn);
            return btn;
        }

        // 설정 톱니는 어느 화면에서나 같은 자리·같은 크기여야 한다. 화면을 옮길 때마다 눈이
        // 다시 찾아야 하는 버튼은 없느니만 못하다. 로비 상단바(0.9~1.0) 아래로 내려간 값이라,
        // 상단바가 없는 게임 테이블에서도 같은 높이를 쓴다.
        private const float SettingsTop = 0.895f;
        private const float SettingsHeight = 0.092f;
        private const float SettingsRight = 0.994f;

        /// <summary>
        /// 화면 오른쪽 끝 고정 위치의 정사각 설정 버튼. 1920x1080 기준이라 세로 비율에
        /// 1080/1920을 곱해야 정사각이 된다.
        /// </summary>
        public static Button CornerSettingsButton(Transform parent)
        {
            var width = SettingsHeight * 1080f / 1920f;
            return SettingsButton(parent,
                new Vector2(SettingsRight - width, SettingsTop - SettingsHeight),
                new Vector2(SettingsRight, SettingsTop));
        }

        /// <summary>
        /// 프로필 화면으로 이동. 상단바는 화면마다 새로 그려져 현재 부트스트랩을 모르므로,
        /// 살아 있는 화면을 이름으로 찾아 정리하고 넘어간다.
        /// </summary>
        public static void GoToProfile()
        {
            if (UnityEngine.Object.FindAnyObjectByType<ProfileBootstrap>() != null)
            {
                return; // 이미 프로필
            }

            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.Destroy(canvas.gameObject);
            }

            foreach (var screen in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (screen.GetType().Name.EndsWith("Bootstrap", StringComparison.Ordinal))
                {
                    UnityEngine.Object.Destroy(screen.gameObject);
                }
            }

            new GameObject(nameof(ProfileBootstrap), typeof(ProfileBootstrap));
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

        /// <summary>
        /// 주요 액션(게임 시작 등) CTA 버튼. 앱 전체에서 채운 골드는 이것과 "지금 고른 것"뿐이다 —
        /// 파란 CTA는 이 앱에서 유일한 파란 버튼이라 어디에도 안 물렸고, 골드 선택 칩과 서로 시선을 뺏었다.
        /// </summary>
        public static Button CtaButton(Transform parent, string label, Vector2 min, Vector2 max,
            UnityEngine.Events.UnityAction onClick, int fontSize = 48)
        {
            (min, max) = EnsureTapHeight(min, max);
            var go = new GameObject("Cta", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.Button; // 일반 버튼과 동일한 Kenney 9-slice
            img.type = Image.Type.Sliced;
            img.color = Accent;
            Anchor(go.GetComponent<RectTransform>(), min, max);

            var text = CreateText(go.transform, label, fontSize, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            text.color = Ink;
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

        /// <summary>
        /// 큰 판(프로필 보드·설정 시트). Panel9 스프라이트가 반투명이라 단색 백킹을 먼저 깔아
        /// 뒤 배경의 별이 비치지 않게 한다 — 판마다 따로 하던 처리를 한곳으로 모았다.
        /// </summary>
        public static Image CreateBoard(Transform parent, Vector2 min, Vector2 max)
        {
            var backing = CreatePanel(parent, new Color(Panel.r, Panel.g, Panel.b, 0.995f));
            Anchor(backing.rectTransform, min, max);

            var board = CreatePanel(parent, Panel);
            if (UiArt.Panel9 != null)
            {
                board.sprite = UiArt.Panel9;
                board.type = Image.Type.Sliced;
            }

            Anchor(board.rectTransform, min, max);
            return board;
        }

        /// <summary>판 위에 얹는 둥근 면(타일·배지·세그먼트 트랙). 모서리 반경을 앱 전체에서 하나로 유지한다.</summary>
        public static Image CreateChip(Transform parent, Color color, Vector2 min, Vector2 max)
        {
            var chip = CreatePanel(parent, color);
            chip.sprite = UiArt.Chip;
            chip.type = Image.Type.Sliced;
            Anchor(chip.rectTransform, min, max);
            return chip;
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

        /// <summary>
        /// 설정 화면의 항목 이름(인원·입장료·봇 난이도). 흰 40pt로 세워 두면 이름이 정작 고를
        /// 칩보다 커서 눈이 라벨부터 읽는다 — 한 단 낮춰 아래 칩 무리의 머리말로만 쓰이게 한다.
        /// </summary>
        public static Text SectionLabel(Transform parent, string label, float y0, float y1)
        {
            var text = CreateText(parent, label, 30, TextAnchor.MiddleCenter,
                new Vector2(0.1f, y0), new Vector2(0.9f, y1));
            text.color = TextFaint;
            return text;
        }

        /// <summary>
        /// 가운데 정렬 선택 칩 한 줄. 연습 설정과 맞춤게임 설정이 각자 그리드를 갖고 있어
        /// 칩 크기·간격이 화면마다 미묘하게 달랐다 — 두 화면이 같은 자를 쓰게 한곳으로 모은다.
        /// </summary>
        public static Button[] ChoiceRow(Transform parent, string[] labels, float y0, float y1, float w,
            Action<int> onPick)
        {
            const float gap = 0.012f;
            var start = 0.5f - (labels.Length * w + (labels.Length - 1) * gap) / 2f;
            var buttons = new Button[labels.Length];
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var x0 = start + i * (w + gap);
                buttons[i] = CreateButton(parent, labels[i], new Vector2(x0, y0), new Vector2(x0 + w, y1),
                    () => onPick(index), 28);
            }

            return buttons;
        }

        /// <summary>선택 칩 칠하기 — 고른 것만 골드, 나머지는 부차 네이비(전 화면 공통).</summary>
        public static void PaintChoice(Button btn, bool selected)
        {
            btn.GetComponent<Image>().color = selected ? Accent : ButtonColor;
            var text = btn.GetComponentInChildren<Text>();
            text.color = selected ? Ink : TextSub;
            text.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        /// <summary>
        /// 입력창. 흰 사각형은 네이비 화면에서 제일 밝은 덩어리라 눈이 먼저 거기로 갔다 —
        /// 판 위의 면과 같은 네이비로 낮추고, 커서만 골드로 켜서 "지금 여기 쓴다"를 표시한다.
        /// </summary>
        public static InputField CreateInputField(Transform parent, string initial, int charLimit,
            Vector2 min, Vector2 max)
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.Chip; // 앱 공용 모서리 반경
            img.type = Image.Type.Sliced;
            img.color = Surface;
            Anchor(go.GetComponent<RectTransform>(), min, max);

            var text = CreateText(go.transform, initial, 36, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0f), new Vector2(0.95f, 1f));
            text.supportRichText = false;

            var input = go.AddComponent<InputField>();
            input.textComponent = text;
            input.text = initial;
            input.characterLimit = charLimit;
            input.customCaretColor = true; // 어두운 바탕에선 기본 검정 커서가 안 보인다
            input.caretColor = Accent;
            input.selectionColor = Gold(0.35f);
            return input;
        }

        /// <summary>
        /// 기기 뒤로가기(안드로이드)가 눌렸을 때 실행할 동작. 화면마다 뒤로 동작이 정확히 하나뿐이라
        /// 스택 대신 슬롯 하나로 충분하다. 화면을 새로 만들 때마다 덮어쓴다.
        /// </summary>
        public static System.Action BackAction { get; set; }

        /// <summary>설정 오버레이가 닫힐 때 돌려줄, 오버레이 직전 화면의 뒤로 동작.</summary>
        public static System.Action PreviousBackAction { get; private set; }

        /// <summary>
        /// 게임 테이블이 살아 있는 동안만 채워지는 "판 나가기" 동작. 설정 오버레이가 이걸 보고
        /// 나가기 버튼을 띄운다 — 판을 버리는 동작을 테이블 표면에서 한 겹 안으로 밀어 넣기 위해서다.
        /// </summary>
        public static System.Action ExitGameAction { get; set; }

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
