using UnityEngine;

namespace Bbong.Client
{
    /// <summary>
    /// 절차 생성 UI 스프라이트 모음(로비·게임 공용). 실제 아트 에셋 도입 전까지의 단일 출처.
    /// </summary>
    internal static class UiArt
    {
        private static Sprite _felt;
        private static Sprite _button;
        private static Sprite _cardBack;

        /// <summary>테이블 펠트 배경: 중앙이 밝은 방사형 그라데이션 + 미세 그레인.</summary>
        public static Sprite Felt => _felt ??= CreateFelt(512);

        /// <summary>버튼 배경: 흰색 둥근 그라데이션(Image.color 틴트로 상태 표현).</summary>
        public static Sprite Button => _button ??= RoundedGradient(96, 96, 24,
            Color.white, new Color(0.86f, 0.86f, 0.86f));

        /// <summary>카드 뒷면: 남색 그라데이션(상대 손패 수 표현용).</summary>
        public static Sprite CardBack => _cardBack ??= RoundedGradient(60, 84, 12,
            new Color(0.24f, 0.32f, 0.58f), new Color(0.11f, 0.15f, 0.33f));

        /// <summary>둥근 모서리 세로 그라데이션 스프라이트(흰 테두리 포함, 9-slice).</summary>
        public static Sprite RoundedGradient(int w, int h, int radius, Color top, Color bottom)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * h];
            const float borderW = 4f;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var cx = Mathf.Clamp(x, radius, w - 1 - radius);
                    var cy = Mathf.Clamp(y, radius, h - 1 - radius);
                    var dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    var edge = radius - dist;                 // 가장자리까지 거리(코너·직선 공통)
                    var alpha = Mathf.Clamp01(edge + 0.5f);   // 모서리 AA

                    var fill = Color.Lerp(bottom, top, y / (float)h);
                    if (edge < borderW)
                    {
                        fill = Color.Lerp(fill, Color.white, 0.8f); // 흰 테두리
                    }

                    pixels[y * w + x] = new Color(fill.r, fill.g, fill.b, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            var border = new Vector4(radius, radius, radius, radius);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        private static Sprite CreateFelt(int size)
        {
            var center = new Color(0.16f, 0.43f, 0.27f);
            var edge = new Color(0.06f, 0.19f, 0.12f);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            var half = size / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    var t = Mathf.Pow(Mathf.Clamp01(d * 0.78f), 1.5f); // 중앙 평탄, 가장자리 비네트
                    var fill = Color.Lerp(center, edge, t);

                    // 펠트 그레인(결정적 해시 노이즈)
                    var n = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
                    var grain = (n - Mathf.Floor(n) - 0.5f) * 0.035f;
                    pixels[y * size + x] = new Color(fill.r + grain, fill.g + grain, fill.b + grain, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
