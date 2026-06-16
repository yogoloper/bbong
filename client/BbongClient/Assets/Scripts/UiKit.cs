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
        public static readonly Color Accent = new(1f, 0.85f, 0.3f);
        public static readonly Color ButtonColor = new(0.95f, 0.95f, 0.95f);

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

            var bg = CreatePanel(canvasGo.transform, Color.white);
            bg.sprite = UiArt.Backdrop;
            Stretch(bg.rectTransform);

            if (topBar)
            {
                TopBar(canvasGo.transform);
            }

            return (canvasGo, canvasGo.transform);
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
                panel.color = new Color(1f, 1f, 1f, 0.92f);
            }

            Anchor(panel.rectTransform, new Vector2(0f, 0.9f), new Vector2(1f, 1f));

            var avatar = CreatePanel(root, new Color(0.3f, 0.55f, 0.9f));
            avatar.sprite = UiArt.Pill;
            avatar.type = Image.Type.Sliced;
            Anchor(avatar.rectTransform, new Vector2(0.012f, 0.915f), new Vector2(0.05f, 0.985f));
            var initial = string.IsNullOrEmpty(Session.Nickname) ? "?" : Session.Nickname[..1];
            CreateText(avatar.transform, initial, 38, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one)
                .fontStyle = FontStyle.Bold;

            CreateText(root, Session.Nickname, 34, TextAnchor.MiddleLeft,
                new Vector2(0.06f, 0.9f), new Vector2(0.5f, 1f));

            if (UiArt.Coin != null)
            {
                CreateIcon(root, UiArt.Coin, new Vector2(0.78f, 0.915f), new Vector2(0.815f, 0.985f));
            }

            var pts = CreateText(root, $"{Session.Balance:N0}", 38, TextAnchor.MiddleLeft,
                new Vector2(0.82f, 0.9f), new Vector2(0.98f, 1f));
            pts.color = Accent;
            pts.fontStyle = FontStyle.Bold;
        }

        /// <summary>화면 하단 중앙 고정 주요 CTA(게임 시작/매칭 시작 등 전 화면 통일).</summary>
        public static Button PrimaryCta(Transform root, string label, UnityEngine.Events.UnityAction onClick) =>
            CtaButton(root, label, new Vector2(0.34f, 0.05f), new Vector2(0.66f, 0.16f), onClick, 46);

        /// <summary>주요 액션(게임 시작 등) CTA 버튼. 일반 버튼과 같은 모양 + 골드 강조색(테마 일치).</summary>
        public static Button CtaButton(Transform parent, string label, Vector2 min, Vector2 max,
            UnityEngine.Events.UnityAction onClick, int fontSize = 48)
        {
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

            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go.GetComponent<Button>();
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

        public static Button CreateButton(Transform parent, string label, Vector2 min, Vector2 max,
            UnityEngine.Events.UnityAction onClick, int fontSize = 40)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.Button;
            img.type = Image.Type.Sliced;
            img.color = ButtonColor;
            Anchor(go.GetComponent<RectTransform>(), min, max);

            var text = CreateText(go.transform, label, fontSize, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            text.color = Color.black;

            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go.GetComponent<Button>();
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

        /// <summary>화면 왼쪽 하단 고정 뒤로가기 버튼(루미큐브식, 상단바와 안 겹침).</summary>
        public static Button BackButton(Transform root, UnityEngine.Events.UnityAction onBack)
        {
            var btn = CreateButton(root, "← 뒤로", new Vector2(0.015f, 0.02f), new Vector2(0.12f, 0.095f), onBack, 32);
            btn.transform.SetAsLastSibling(); // 항상 최상위(다른 패널에 안 가림)
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
