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
        private static Sprite _cardBackSmallDanger;
        private static Sprite _backdrop;
        private static Sprite _greenButton;
        private static Sprite _pill;
        private static Sprite _panel9;
        private static Sprite _coin;
        private static Sprite _vignette;
        private static bool _coinLoaded;
        private static Sprite _iconBook;
        private static Sprite _iconRobot;
        private static Sprite _iconTrophy;
        private static Sprite _iconFriends;
        private static Sprite _iconCoins;
        private static Sprite _iconAvatar;

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
        public static Sprite CardBackSmall => _cardBackSmall ??= CreateCardBackSmall(36, 54, 6,
            top: new Color(0.27f, 0.35f, 0.63f), bottom: new Color(0.10f, 0.14f, 0.32f),
            lattice: new Color(0.44f, 0.52f, 0.80f), border: new Color(0.87f, 0.82f, 0.68f));

        /// <summary>쌍 공개(§7) 좌석용 붉은 뒷면 — "뽕 바가지 주의"를 색으로 표현.</summary>
        public static Sprite CardBackSmallDanger => _cardBackSmallDanger ??= CreateCardBackSmall(36, 54, 6,
            top: new Color(0.72f, 0.26f, 0.28f), bottom: new Color(0.42f, 0.10f, 0.14f),
            lattice: new Color(0.95f, 0.55f, 0.50f), border: new Color(1f, 0.85f, 0.72f));

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

        /// <summary>펼친 책(튜토리얼) 아이콘.</summary>
        public static Sprite IconBook => _iconBook ??= CreateIconBook();

        /// <summary>로봇 얼굴(봇 연습) 아이콘.</summary>
        public static Sprite IconRobot => _iconRobot ??= CreateIconRobot();

        /// <summary>트로피(맞춤게임) 아이콘.</summary>
        public static Sprite IconTrophy => _iconTrophy ??= CreateIconTrophy();

        /// <summary>두 사람 실루엣(친구와 함께) 아이콘.</summary>
        public static Sprite IconFriends => _iconFriends ??= CreateIconFriends();

        /// <summary>동전 더미(포인트 얻기) 아이콘.</summary>
        public static Sprite IconCoins => _iconCoins ??= CreateIconCoins();

        /// <summary>한 사람 실루엣 + 원형 프레임(프로필) 아이콘.</summary>
        public static Sprite IconAvatar => _iconAvatar ??= CreateIconAvatar();

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

        private static Sprite CreateCardBackSmall(int w, int h, int radius,
            Color top, Color bottom, Color lattice, Color border)
        {

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

        // ── 모드 아이콘 도형 ──
        // 좌표는 텍스처 픽셀(원점 좌하단, y 위쪽). 각 도형을 "안이 음수"인 거리함수로 두면
        // 합집합·교집합·윤곽 부풀리기·AA를 한 가지 방식으로 처리할 수 있다.

        private const int IconSize = 256;
        private const float IconOutline = 6f; // 카드 크기로 축소돼도 남을 두께
        private static readonly Color IconFill = new(0.97f, 0.95f, 0.88f);
        private static readonly Color IconShade = new(0.80f, 0.77f, 0.68f);
        private static readonly Color IconLine = new(0.10f, 0.16f, 0.30f);

        private delegate float Sdf(float x, float y);

        private static Sdf Circle(float cx, float cy, float r) =>
            (x, y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        private static Sdf Ellipse(float cx, float cy, float rx, float ry) => (x, y) =>
        {
            var dx = (x - cx) / rx;
            var dy = (y - cy) / ry;
            return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
        };

        /// <summary>중심이 빈 고리(손잡이·프레임용) — 구멍이 투명하게 남아야 해서 원 두 개 대신 하나로 그린다.</summary>
        private static Sdf Ring(float cx, float cy, float r, float t) =>
            (x, y) => Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) - t;

        private static Sdf RoundRect(float cx, float cy, float hw, float hh, float r) => (x, y) =>
        {
            var qx = Mathf.Abs(x - cx) - (hw - r);
            var qy = Mathf.Abs(y - cy) - (hh - r);
            var ox = Mathf.Max(qx, 0f);
            var oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        };

        /// <summary>양끝이 둥근 굵은 선(책등·안테나·밑줄).</summary>
        private static Sdf Segment(float ax, float ay, float bx, float by, float t) => (x, y) =>
        {
            var pax = x - ax;
            var pay = y - ay;
            var bax = bx - ax;
            var bay = by - ay;
            var h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
            var dx = pax - bax * h;
            var dy = pay - bay * h;
            return Mathf.Sqrt(dx * dx + dy * dy) - t;
        };

        /// <summary>볼록 다각형(반시계 방향). 변별 반평면의 최댓값 — 법선은 픽셀 루프 밖에서 한 번만 구한다.</summary>
        private static Sdf Convex(params Vector2[] pts)
        {
            var n = pts.Length;
            var nx = new float[n];
            var ny = new float[n];
            var offset = new float[n];
            for (var i = 0; i < n; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % n];
                var ex = b.x - a.x;
                var ey = b.y - a.y;
                var len = Mathf.Sqrt(ex * ex + ey * ey);
                nx[i] = ey / len; // 반시계 기준 바깥 법선
                ny[i] = -ex / len;
                offset[i] = nx[i] * a.x + ny[i] * a.y;
            }

            return (x, y) =>
            {
                var d = float.NegativeInfinity;
                for (var i = 0; i < n; i++)
                {
                    d = Mathf.Max(d, nx[i] * x + ny[i] * y - offset[i]);
                }

                return d;
            };
        }

        /// <summary>오각별 = 안쪽 오각형 ∪ 꼭짓점 삼각형 5개. 볼록 다각형 SDF만으로 오목 도형을 만든다.</summary>
        private static Sdf Star5(float cx, float cy, float outer, float inner)
        {
            var tips = new Vector2[5];
            var valleys = new Vector2[5];
            for (var i = 0; i < 5; i++)
            {
                var a = Mathf.PI / 2f + i * Mathf.PI * 2f / 5f;
                var b = a + Mathf.PI / 5f;
                tips[i] = new Vector2(cx + Mathf.Cos(a) * outer, cy + Mathf.Sin(a) * outer);
                valleys[i] = new Vector2(cx + Mathf.Cos(b) * inner, cy + Mathf.Sin(b) * inner);
            }

            var parts = new Sdf[6];
            parts[0] = Convex(valleys);
            for (var i = 0; i < 5; i++)
            {
                parts[i + 1] = Convex(valleys[(i + 4) % 5], tips[i], valleys[i]);
            }

            return Union(parts);
        }

        private static Sdf Union(params Sdf[] parts) => (x, y) =>
        {
            var d = float.PositiveInfinity;
            foreach (var p in parts)
            {
                d = Mathf.Min(d, p(x, y));
            }

            return d;
        };

        private static Sdf Intersect(Sdf a, Sdf b) => (x, y) => Mathf.Max(a(x, y), b(x, y));

        private static Sdf Above(float y0) => (_, y) => y0 - y;

        private static Sdf Below(float y0) => (_, y) => y - y0;

        /// <summary>SDF를 커버리지 알파로 합성(0.5 오프셋 = 픽셀 중심 기준 반 픽셀 AA).</summary>
        private static void Fill(Color[] px, Sdf sdf, Color color)
        {
            for (var y = 0; y < IconSize; y++)
            {
                for (var x = 0; x < IconSize; x++)
                {
                    var d = sdf(x + 0.5f, y + 0.5f);
                    if (d > 0.5f)
                    {
                        // 도형 밖 — 거리만큼 건너뛴다. 모든 SDF가 실제 거리 이하(보수적)라 안전하다.
                        x += (int)(d - 0.5f);
                        continue;
                    }

                    var a = Mathf.Clamp01(0.5f - d) * color.a;
                    if (a <= 0f)
                    {
                        continue;
                    }

                    var i = y * IconSize + x;
                    var dst = px[i];
                    var keep = dst.a * (1f - a);
                    var outA = a + keep;
                    px[i] = new Color(
                        (color.r * a + dst.r * keep) / outA,
                        (color.g * a + dst.g * keep) / outA,
                        (color.b * a + dst.b * keep) / outA,
                        outA);
                }
            }
        }

        /// <summary>면 + 네이비 윤곽. 윤곽은 SDF를 바깥으로 부풀려 그리므로 겹친 도형끼리 자동으로 갈라진다.</summary>
        private static void Shape(Color[] px, Sdf sdf, Color fill)
        {
            Fill(px, (x, y) => sdf(x, y) - IconOutline, IconLine);
            Fill(px, sdf, fill);
        }

        private static Sprite IconSprite(Color[] px)
        {
            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateIconBook()
        {
            var px = new Color[IconSize * IconSize];
            var ruled = Color.Lerp(IconLine, IconFill, 0.3f);

            // 바깥이 들리고 책등이 내려앉은 펼친 책 — 좌/우 페이지를 사다리꼴 두 장으로
            Shape(px, Convex(new Vector2(34, 92), new Vector2(128, 72), new Vector2(128, 166), new Vector2(34, 186)),
                IconFill);
            Shape(px, Convex(new Vector2(128, 72), new Vector2(222, 92), new Vector2(222, 186), new Vector2(128, 166)),
                IconFill);
            Fill(px, Segment(128, 68, 128, 170, 4f), IconLine); // 책등

            for (var i = 0; i < 3; i++)
            {
                var ly = 116 + i * 19;
                Fill(px, Segment(52, ly, 112, ly - 13, 3.5f), ruled);
                Fill(px, Segment(144, ly - 12, 204, ly + 1, 3.5f), ruled);
            }

            return IconSprite(px);
        }

        private static Sprite CreateIconRobot()
        {
            var px = new Color[IconSize * IconSize];

            Fill(px, Segment(128, 154, 128, 190, 5f), IconLine); // 안테나 — 머리 뒤에 먼저
            Shape(px, Circle(128, 198, 13), IconFill);
            Shape(px, RoundRect(46, 98, 10, 24, 8), IconShade); // 좌우 귀
            Shape(px, RoundRect(210, 98, 10, 24, 8), IconShade);
            Shape(px, RoundRect(128, 98, 74, 60, 26), IconFill);

            Fill(px, Circle(102, 116, 16), IconLine);
            Fill(px, Circle(154, 116, 16), IconLine);
            Fill(px, Circle(107, 122, 5.5f), IconFill); // 눈 하이라이트
            Fill(px, Circle(159, 122, 5.5f), IconFill);

            Fill(px, RoundRect(128, 64, 36, 10, 9), IconLine);
            for (var i = -1; i <= 1; i++)
            {
                Fill(px, RoundRect(128 + i * 16, 64, 2.5f, 10, 1), IconFill); // 입 칸막이
            }

            return IconSprite(px);
        }

        private static Sprite CreateIconTrophy()
        {
            var px = new Color[IconSize * IconSize];

            Shape(px, Ring(74, 168, 24, 8), IconFill); // 손잡이 — 컵에 가려지도록 먼저
            Shape(px, Ring(182, 168, 24, 8), IconFill);
            Shape(px, RoundRect(128, 112, 11, 32, 7), IconShade); // 기둥
            Shape(px, Convex(new Vector2(92, 66), new Vector2(164, 66), new Vector2(154, 92), new Vector2(102, 92)),
                IconShade);
            Shape(px, RoundRect(128, 58, 56, 13, 7), IconFill); // 받침

            var bowl = Intersect(Circle(128, 190, 58), Below(190));
            Shape(px, Union(bowl, RoundRect(128, 192, 64, 13, 7)), IconFill);
            Fill(px, Star5(128, 170, 26, 11), IconLine);

            return IconSprite(px);
        }

        private static Sprite CreateIconFriends()
        {
            var px = new Color[IconSize * IconSize];

            // 어깨는 둥근 사각형의 위쪽만 남겨 만든다(아래를 잘라 흉상 실루엣)
            Shape(px, Union(Circle(168, 178, 30), Intersect(RoundRect(168, 98, 54, 62, 44), Above(56))), IconShade);
            Shape(px, Union(Circle(96, 170, 36), Intersect(RoundRect(96, 86, 62, 72, 52), Above(48))), IconFill);

            return IconSprite(px);
        }

        private static Sprite CreateIconCoins()
        {
            var px = new Color[IconSize * IconSize];
            const float rx = 46f, ry = 14f, thick = 22f, cx = 95f;

            // 원기둥 = 위아래 타원 + 그 사이 직사각형. 아래 동전부터 쌓아 윤곽선이 경계가 되게 한다.
            for (var i = 0; i < 3; i++)
            {
                var by = 108f + i * 29f;
                Shape(px, Union(Ellipse(cx, by, rx, ry), Ellipse(cx, by + thick, rx, ry),
                    RoundRect(cx, by + thick / 2f, rx, thick / 2f, 2f)), IconShade);
            }

            const float faceY = 108f + 2 * 29f + thick;
            Shape(px, Ellipse(cx, faceY, rx, ry), IconFill);
            var rim = Ring(cx, faceY, 33, 4);
            Fill(px, (x, y) => rim(x, faceY + (y - faceY) * 3.2f), IconShade); // 원근에 맞춰 눌린 테두리

            Shape(px, Circle(163, 98, 44), IconFill); // 더미 앞에 기대 세운 동전
            Fill(px, Star5(163, 98, 24, 10), IconLine);

            return IconSprite(px);
        }

        private static Sprite CreateIconAvatar()
        {
            var px = new Color[IconSize * IconSize];

            Shape(px, Ring(128, 128, 104, 10), IconFill);
            var bust = Union(Circle(128, 150, 40), Intersect(RoundRect(128, 60, 66, 68, 50), Above(30)));
            Shape(px, Intersect(bust, Circle(128, 128, 84)), IconFill); // 프레임 안쪽으로 잘라 아바타처럼

            return IconSprite(px);
        }
    }
}
