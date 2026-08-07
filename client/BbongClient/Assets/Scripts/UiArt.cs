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
        private static Sprite _cardBackSmall;
        private static Sprite _backdrop;
        private static Sprite _greenButton;
        private static Sprite _pill;
        private static Sprite _panel9;
        private static Sprite _coin;
        private static Sprite _vignette;
        private static bool _coinLoaded;

        /// <summary>테이블 펠트 배경: 중앙이 밝은 방사형 그라데이션 + 미세 그레인.</summary>
        public static Sprite Felt => _felt ??= CreateFelt(512);

        /// <summary>버튼 배경: Kenney UI(CC0) 회색 9-slice. Image.color 틴트로 색/상태 표현. 없으면 절차 폴백.</summary>
        public static Sprite Button => _button ??= LoadSliced("UI/btn_grey", 12)
            ?? RoundedGradient(96, 96, 24, Color.white, new Color(0.86f, 0.86f, 0.86f));

        /// <summary>카드 뒷면: 남색 그라데이션 + 다이아 격자 + 금색 안쪽 테두리.</summary>
        public static Sprite CardBack => _cardBack ??= CreateCardBack(90, 126, 14);

        /// <summary>
        /// 좌석 손패 수 표시용 소형 뒷면. 1px대 금테는 서브픽셀 위치에 따라 좌석마다 다르게 뭉개져
        /// (중앙 좌석만 또렷, 측면 좌석은 회색) 도톰한 단일 테두리로 단순화 — 어느 위치서든 균일.
        /// </summary>
        public static Sprite CardBackSmall => _cardBackSmall ??= CreateCardBackSmall(36, 54, 6);

        /// <summary>메뉴 화면 배경: 진한 네이비 세로 그라데이션 + 별 점.</summary>
        public static Sprite Backdrop => _backdrop ??= CreateBackdrop(512);

        /// <summary>화면 가장자리를 은은히 어둡게 하는 비네트 오버레이(중앙 투명 → 모서리 반투명 검정).</summary>
        public static Sprite Vignette => _vignette ??= CreateVignette(256);


        /// <summary>CTA(주요 액션) 버튼: Kenney UI(CC0) 갈색/골드 9-slice. 없으면 초록 절차 폴백.</summary>
        public static Sprite GreenButton => _greenButton ??= LoadSliced("UI/btn_brown", 12)
            ?? RoundedGradient(96, 96, 28, new Color(0.45f, 0.78f, 0.30f), new Color(0.22f, 0.52f, 0.16f));

        /// <summary>둥근 캡슐(상단바·태그용). 반투명 남색.</summary>
        public static Sprite Pill => _pill ??= RoundedGradient(64, 64, 30,
            new Color(0.12f, 0.20f, 0.38f), new Color(0.08f, 0.14f, 0.28f));

        /// <summary>Kenney UI(CC0) 파란 패널 9-slice(상단바·정보 패널). 없으면 null(호출부 단색 폴백).</summary>
        public static Sprite Panel9 => _panel9 ??= LoadSliced("UI/panel", 18);

        /// <summary>Kenney(CC0) 골드 코인 아이콘. 없으면 null.</summary>
        public static Sprite Coin
        {
            get
            {
                if (!_coinLoaded)
                {
                    _coin = Resources.Load<Sprite>("UI/coin");
                    _coinLoaded = true;
                }

                return _coin;
            }
        }

        private static Sprite CreateCardBackSmall(int w, int h, int radius)
        {
            var top = new Color(0.27f, 0.35f, 0.63f);
            var bottom = new Color(0.10f, 0.14f, 0.32f);
            var lattice = new Color(0.44f, 0.52f, 0.80f);
            var border = new Color(0.87f, 0.82f, 0.68f); // 웜 라이트 — 금테 대신 넓은 단일 테두리

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var cx = Mathf.Clamp(x, radius, w - 1 - radius);
                    var cy = Mathf.Clamp(y, radius, h - 1 - radius);
                    var dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    var edge = radius - dist;
                    var alpha = Mathf.Clamp01(edge + 0.5f);

                    var fill = Color.Lerp(bottom, top, y / (float)h);
                    if (edge >= 4f && ((x + y) % 8 < 1 || (x - y + 4096) % 8 < 1))
                    {
                        fill = Color.Lerp(fill, lattice, 0.55f); // 촘촘한 다이아 격자
                    }

                    if (edge < 2.5f)
                    {
                        fill = border; // 리샘플링에도 살아남는 두께
                    }

                    pixels[y * w + x] = new Color(fill.r, fill.g, fill.b, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateCardBack(int w, int h, int radius)
        {
            var top = new Color(0.27f, 0.35f, 0.63f);
            var bottom = new Color(0.10f, 0.14f, 0.32f);
            var lattice = new Color(0.44f, 0.52f, 0.80f);
            var gold = new Color(0.84f, 0.70f, 0.34f);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var cx = Mathf.Clamp(x, radius, w - 1 - radius);
                    var cy = Mathf.Clamp(y, radius, h - 1 - radius);
                    var dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    var edge = radius - dist;
                    var alpha = Mathf.Clamp01(edge + 0.5f);

                    var fill = Color.Lerp(bottom, top, y / (float)h);
                    // 테두리 밴드는 radius 비례 — 소형 스프라이트도 같은 비율로 그려짐(r14 기준 9/5~8/3)
                    if (edge >= radius * 0.643f && ((x + y) % 16 < 2 || (x - y + 4096) % 16 < 2))
                    {
                        fill = Color.Lerp(fill, lattice, 0.55f); // 다이아 격자
                    }

                    if (edge >= radius * 0.357f && edge < radius * 0.571f)
                    {
                        fill = gold; // 안쪽 금테
                    }
                    else if (edge < radius * 0.214f)
                    {
                        fill = Color.Lerp(fill, Color.white, 0.85f); // 바깥 흰 테두리
                    }

                    pixels[y * w + x] = new Color(fill.r, fill.g, fill.b, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Resources의 PNG(Kenney CC0)를 9-slice 스프라이트로 런타임 생성.
        /// 에디터 임포트 설정(스프라이트 border)에 의존하지 않도록 텍스처에서 직접 만든다.
        /// 임포트 안 됐으면 null 반환(호출부가 절차 생성으로 폴백).
        /// </summary>
        private static Sprite LoadSliced(string resourcePath, int border)
        {
            var loaded = Resources.Load<Sprite>(resourcePath);
            var tex = loaded != null ? loaded.texture : Resources.Load<Texture2D>(resourcePath);
            if (tex == null)
            {
                return null;
            }

            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

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

        private static Sprite CreateVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            var half = (size - 1) / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - half) / half;
                    var dy = (y - half) / half;
                    var d = Mathf.Sqrt(dx * dx + dy * dy); // 0=중앙, ~1.41=모서리
                    var t = Mathf.Clamp01(Mathf.InverseLerp(0.75f, 1.35f, d));
                    pixels[y * size + x] = new Color(0f, 0f, 0f, t * t * 0.45f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateBackdrop(int size)
        {
            var top = new Color(0.09f, 0.16f, 0.34f);
            var bottom = new Color(0.03f, 0.06f, 0.16f);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var fill = Color.Lerp(bottom, top, y / (float)size);

                    // 결정적 해시로 드문 별 점
                    var n = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
                    var r = n - Mathf.Floor(n);
                    if (r > 0.9985f)
                    {
                        fill = Color.Lerp(fill, Color.white, 0.7f);
                    }

                    pixels[y * size + x] = new Color(fill.r, fill.g, fill.b, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
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
