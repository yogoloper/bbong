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

        /// <summary>화면용 전체 캔버스 + 펠트 배경 생성. 루트 Transform 반환.</summary>
        public static (GameObject canvasGo, Transform root) CreateScreen(string name)
        {
            var canvasGo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var felt = CreatePanel(canvasGo.transform, Color.white);
            felt.sprite = UiArt.Felt;
            Stretch(felt.rectTransform);

            return (canvasGo, canvasGo.transform);
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

        /// <summary>화면 왼쪽 상단 고정 뒤로가기 버튼(모든 화면 통일 위치).</summary>
        public static Button BackButton(Transform root, UnityEngine.Events.UnityAction onBack)
        {
            var btn = CreateButton(root, "← 뒤로", new Vector2(0.015f, 0.90f), new Vector2(0.12f, 0.975f), onBack, 32);
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
