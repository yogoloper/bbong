using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 게임 테이블 공용 순수 연출 조각(로컬 연습·온라인 공용): 카드 아트/정렬/절차 효과음.
    /// 게임 상태를 읽지 않는 부분만 — 상태 기반 렌더(Refresh 등)는 각 테이블이 소유.
    /// </summary>
    internal static class TableArt
    {
        // 색약 안전 팔레트(Okabe-Ito 기반). 색은 보조, 도형이 주 구분 수단.
        public static readonly Color[] Palette =
        {
            new Color(0.835f, 0.369f, 0.000f), // Red  → 주황빨강(vermillion)
            new Color(0.000f, 0.447f, 0.698f), // Blue → 진파랑
            new Color(0.000f, 0.620f, 0.451f), // Green→ 청록
            new Color(0.902f, 0.624f, 0.000f)  // Yellow→ 호박색(amber)
        };

        public static readonly string[] ColorLetter = { "R", "B", "G", "Y" };

        // 정렬 색 순위: 빨(0)·파(1)·노(2)·초(3). enum 순서(Red0,Blue1,Green2,Yellow3) → 순위.
        public static readonly int[] ColorRank = { 0, 1, 3, 2 };

        private static Sprite[] _cardBg;
        private static Sprite _halo;

        public static Sprite Halo => _halo ??= UiArt.RoundedGradient(120, 168, 22, Color.white, Color.white);

        public static Sprite CardBg(int colorIndex)
        {
            if (_cardBg == null)
            {
                _cardBg = new Sprite[4];
                for (var i = 0; i < 4; i++)
                {
                    var c = Palette[i];
                    var top = Color.Lerp(c, Color.white, 0.22f);     // 위쪽 밝게
                    var bottom = Color.Lerp(c, Color.black, 0.20f);  // 아래쪽 어둡게
                    _cardBg[i] = UiArt.RoundedGradient(120, 168, 22, top, bottom);
                }
            }

            return _cardBg[colorIndex];
        }

        public static string CardLabel(Card c) => $"{c.Number}{ColorLetter[(int)c.Color]}";

        /// <summary>숫자 오름차순 → 같은 숫자는 빨·파·노·초 순으로 정렬.</summary>
        public static List<Card> Sorted(IEnumerable<Card> cards) =>
            cards.OrderBy(c => c.Number).ThenBy(c => ColorRank[(int)c.Color]).ToList();

        /// <summary>중앙 밀집 무작위(삼각분포). 균등분포보다 자연스러운 무더기를 만듭니다.</summary>
        public static float Tri(float range) => (Random.Range(-range, range) + Random.Range(-range, range)) / 2f;

        /// <summary>
        /// 카드 한 장(색 배경 그라데이션 + 흰 테두리 + 흰 숫자(외곽선) + 양 모서리 도형).
        /// 색약 대응: 이니셜이 주 구분 수단, 색은 보조. 손패·버림 공용.
        /// </summary>
        public static GameObject CreateCardFace(Transform parent, Card card, float width, float height, Font font)
        {
            var colorIndex = (int)card.Color;

            var go = new GameObject($"Card_{card.Number}{ColorLetter[colorIndex]}",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var bg = go.GetComponent<Image>();
            bg.sprite = CardBg(colorIndex);
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;

            // 카드가 테이블에 떠 있는 느낌의 부드러운 그림자
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(5f, -5f);

            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            var num = card.Number.ToString();
            var letter = ColorLetter[colorIndex];
            var pip = Mathf.RoundToInt(height * 0.15f);

            // 중앙 큰 숫자(흰색 + 검은 외곽선)
            var center = CardText(go.transform, num, Mathf.RoundToInt(height * 0.5f), TextAnchor.MiddleCenter, font);
            var rt = center.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // 네 모서리: 숫자/이니셜 (대각 대칭)
            Pip(go.transform, num, pip, TextAnchor.UpperLeft, new Vector2(0.10f, 0.78f), new Vector2(0.5f, 0.97f), font);
            Pip(go.transform, letter, pip, TextAnchor.UpperRight, new Vector2(0.5f, 0.78f), new Vector2(0.90f, 0.97f), font);
            Pip(go.transform, letter, pip, TextAnchor.LowerLeft, new Vector2(0.10f, 0.03f), new Vector2(0.5f, 0.22f), font);
            Pip(go.transform, num, pip, TextAnchor.LowerRight, new Vector2(0.5f, 0.03f), new Vector2(0.90f, 0.22f), font);

            return go;
        }

        private static void Pip(Transform parent, string content, int size, TextAnchor anchor, Vector2 min, Vector2 max, Font font)
        {
            var t = CardText(parent, content, size, anchor, font);
            var rt = t.rectTransform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static Text CardText(Transform parent, string content, int size, TextAnchor anchor, Font font)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            AddOutline(t);
            return t;
        }

        public static void AddOutline(Text text)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.65f);
            outline.effectDistance = new Vector2(2, -2);
        }

        // ── 절차적 효과음 ──

        public static AudioClip Tone(string name, float freq, float duration, float decay)
        {
            var rate = 44100;
            var count = Mathf.RoundToInt(rate * duration);
            var data = new float[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)rate;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-decay * t);
            }

            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioClip Noise(string name, float duration, float decay)
        {
            var rate = 44100;
            var count = Mathf.RoundToInt(rate * duration);
            var data = new float[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)rate;
                data[i] = (Random.value * 2f - 1f) * Mathf.Exp(-decay * t);
            }

            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
