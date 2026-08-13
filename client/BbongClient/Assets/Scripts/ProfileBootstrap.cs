using System;
using System.Collections.Generic;
using BbongCore.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 프로필: 닉네임 변경, 전적 통계, 최근 게임 기록.
    /// 통계는 맞춤게임/친구와 함께를 탭으로 가르고 인원별로도 나눠 보여준다 —
    /// 상대를 고를 수 있는 친구방과 2인전·6인전을 한 승률로 읽으면 곤란하기 때문이다.
    /// </summary>
    public sealed class ProfileBootstrap : MonoBehaviour
    {
        /// <summary>가로 화면 한 장에 스크롤 없이 들어가는 줄 수. 더 필요해지면 그때 스크롤을 단다.</summary>
        private const int HistoryRows = 9;

        private const string QuickMatch = "QuickMatch";
        private const string Friend = "Friend";

        // 보드(탭이 얹히는 콘텐츠 판)의 네 변. 탭·표·막대 좌표가 전부 여기에 물려 있어 한곳에서 잡는다.
        private const float BoardTop = 0.70f;
        private const float BoardBottom = 0.14f;
        private const float BoardLeft = 0.05f;
        private const float BoardRight = 0.95f;

        /// <summary>표 좌우 여백 — 보드 안쪽으로 한 뼘 들어와야 줄무늬가 판에 갇힌 것처럼 보인다.</summary>
        private const float RowLeft = 0.065f;
        private const float RowRight = 0.935f;

        // 탭 높이는 최소 탭 높이와 정확히 같게 잡는다. CreateButton이 높이를 늘려 버리면
        // 탭 아랫변이 보드 윗변에서 어긋나 "얹힌 탭"이 아니라 떠 있는 버튼이 된다.
        private const float TabTop = BoardTop + UiKit.MinTapHeight;

        // 보드 첫 띠: 왼쪽 모드 세그먼트 + 오른쪽 요약 타일이 같은 높이에 선다.
        private const float BandTop = 0.684f;
        private const float BandBottom = 0.552f;
        private const float SegTop = 0.679f;
        private const float SegBottom = 0.557f;

        // 인원별 표: 헤더 아래 첫 줄부터 5줄(2~6인)이 보드 바닥 위에 딱 떨어지는 값
        private const float StatsRowTop = 0.458f;
        private const float StatsRowH = 0.062f;

        // 승률 막대가 눕는 구간(오른쪽 끝의 퍼센트 숫자와 한 묶음)
        private const float BarLeft = 0.42f;
        private const float BarRight = 0.665f;

        private const float HistoryRowTop = 0.59f;
        private const float HistoryRowH = 0.05f;

        private static readonly Color Ink = new(0.07f, 0.11f, 0.22f);        // 골드 바탕 위 글자
        private static readonly Color TabOff = new(0.10f, 0.16f, 0.32f, 0.6f);

        // 판 위에 얹는 면은 흰색 알파가 아니라 네이비로 칠한다. 흰색을 깔면 어두운 배경에서
        // 회색으로 떠서, 네이비+골드로 짠 다른 화면과 재질이 달라 보인다.
        private static readonly Color Surface = new(0.15f, 0.22f, 0.42f, 0.92f);   // 타일·칸 바탕
        private static readonly Color SurfaceDim = new(0.13f, 0.19f, 0.36f, 0.75f); // 안 고른 칸
        private static readonly Color Trough = new(0f, 0f, 0f, 0.34f);              // 막대 홈

        // 로비 모드 카드의 색을 기록 줄까지 잇는다. 다만 맞춤게임의 골드는 여기서 이미
        // "이김/상금"이 쓰고 있어, 모드 이름에는 부딪히지 않는 차가운 색을 쓴다.
        private static readonly Color QuickTint = new(0.66f, 0.79f, 0.98f);
        private static readonly Color FriendTint = new(0.82f, 0.62f, 0.93f);

        private GameObject _canvas;
        private InputField _nickInput;
        private Text _status;
        private Button _saveBtn;

        /// <summary>탭을 바꿀 때 통째로 지우고 다시 그리는 영역.</summary>
        private Transform _content;
        private Text _loading;

        private bool _statsTab = true;
        private string _mode = QuickMatch;
        private ServerApi.StatsResult _stats;
        private ServerApi.HistoryEntry[] _history;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("ProfileCanvas", topBar: true);
            _canvas = canvas;

            UiKit.CreateText(root, "닉네임", 26, TextAnchor.MiddleLeft,
                new Vector2(0.06f, 0.845f), new Vector2(0.16f, 0.885f));
            // 입력창·버튼 같은 높이(터치 하한 이상 명시) — 폼 한 줄의 위아래 선을 맞춘다
            _nickInput = UiKit.CreateInputField(root, Session.Nickname, GameConfig.MaxNicknameLength,
                new Vector2(0.06f, 0.745f), new Vector2(0.26f, 0.845f));
            _saveBtn = UiKit.CreateButton(root, "저장",
                new Vector2(0.275f, 0.745f), new Vector2(0.375f, 0.845f), OnSave, 28);

            _status = UiKit.CreateText(root, "", 24, TextAnchor.MiddleLeft,
                new Vector2(0.06f, 0.70f), new Vector2(0.42f, 0.745f));
            _status.color = new Color(1f, 0.8f, 0.5f);

            // 보드를 탭보다 먼저 만든다 — uGUI는 나중에 만든 쪽이 위에 그려져서,
            // 순서를 바꾸면 선택된 탭과 보드를 잇는 다리가 판 밑으로 깔린다.
            var board = UiKit.CreatePanel(root, new Color(0f, 0f, 0f, 0.3f));
            if (UiArt.Panel9 != null)
            {
                board.sprite = UiArt.Panel9;
                board.type = Image.Type.Sliced;
                board.color = new Color(0.10f, 0.15f, 0.30f, 0.90f); // 상단바와 같은 네이비 계열
            }

            UiKit.Anchor(board.rectTransform, new Vector2(BoardLeft, BoardBottom), new Vector2(BoardRight, BoardTop));

            // 탭이 앉는 레일. 선택 안 된 탭도 이 선 위에 서 있어야 허공에 뜨지 않는다.
            UiKit.Anchor(UiKit.CreatePanel(root, Gold(0.22f)).rectTransform,
                new Vector2(BoardLeft, BoardTop - 0.0015f), new Vector2(BoardRight, BoardTop + 0.0015f));

            Tab(root, "통계", 0.46f, 0.63f,
                () => _statsTab, () => { _statsTab = true; Redraw(); });
            Tab(root, "게임 기록", 0.645f, 0.815f,
                () => !_statsTab, () => { _statsTab = false; Redraw(); });

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root, false);
            UiKit.Stretch(content.GetComponent<RectTransform>());
            _content = content.transform;

            _loading = UiKit.CreateText(root, "불러오는 중...", 26, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.38f), new Vector2(0.95f, 0.46f));
            _loading.color = new Color(1f, 1f, 1f, 0.5f);

            StartCoroutine(ServerApi.FetchStats(s => { _stats = s; Redraw(); },
                _ => _loading.text = "전적을 불러오지 못했어요"));
            StartCoroutine(ServerApi.FetchHistory(HistoryRows, h => { _history = h; Redraw(); },
                _ => _loading.text = "기록을 불러오지 못했어요"));

            UiKit.BackButton(root, Back);
        }

        private static Color Gold(float alpha) =>
            new(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, alpha);

        /// <summary>보드 좌표로 바로 놓는 단색 판 — 줄무늬·구분선·승률 막대에 쓴다.</summary>
        private Image Fill(Color color, float x0, float y0, float x1, float y1)
        {
            var img = UiKit.CreatePanel(_content, color);
            UiKit.Anchor(img.rectTransform, new Vector2(x0, y0), new Vector2(x1, y1));
            return img;
        }

        // ── 탭 ──

        /// <summary>화면 내내 살아 있는 탭(통계/게임 기록). 다시 칠하려면 다리까지 같이 쥐고 있어야 한다.</summary>
        private readonly List<(Button btn, Image bridge, Func<bool> selected)> _tabs = new();

        /// <summary>
        /// 보드 위에 얹히는 탭. 선택된 쪽만 골드로 채우고, 보드 윗변을 덮는 다리를 켜서
        /// 탭과 판이 한 장으로 이어지게 한다 — 색만 맞바꾸면 그냥 버튼 두 개로 읽힌다.
        /// </summary>
        private void Tab(Transform parent, string label, float x0, float x1, Func<bool> selected, Action onClick)
        {
            var btn = UiKit.CreateButton(parent, label,
                new Vector2(x0, BoardTop), new Vector2(x1, TabTop), () => onClick(), 30);

            var bridge = UiKit.CreatePanel(parent, UiKit.Accent);
            UiKit.Anchor(bridge.rectTransform,
                new Vector2(x0 + 0.004f, BoardTop - 0.006f), new Vector2(x1 - 0.004f, BoardTop + 0.004f));

            _tabs.Add((btn, bridge, selected));
            PaintTab(btn, bridge, selected());
        }

        private static void PaintTab(Button btn, Image bridge, bool on)
        {
            btn.GetComponent<Image>().color = on ? UiKit.Accent : TabOff;
            var text = btn.GetComponentInChildren<Text>();
            text.color = on ? Ink : new Color(1f, 1f, 1f, 0.55f);
            text.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
            bridge.enabled = on;
        }

        /// <summary>
        /// 모드 고르개(맞춤게임/친구와 함께). 위쪽 탭과 층을 나누려고 트랙 안에 칸을 넣은
        /// 세그먼트 컨트롤로 만든다 — 같은 모양이면 어느 쪽이 상위인지 안 보인다.
        /// </summary>
        private void ModeSegments()
        {
            var track = UiKit.CreatePanel(_content, new Color(0f, 0f, 0f, 0.30f));
            track.sprite = UiArt.Chip;
            track.type = Image.Type.Sliced;
            UiKit.Anchor(track.rectTransform, new Vector2(0.07f, BandBottom), new Vector2(0.44f, BandTop));

            Segment("맞춤게임", 0.0755f, 0.2525f, _mode == QuickMatch,
                () => { _mode = QuickMatch; Redraw(); });
            Segment("친구와 함께", 0.2575f, 0.4345f, _mode == Friend,
                () => { _mode = Friend; Redraw(); });
        }

        private void Segment(string label, float x0, float x1, bool on, Action onClick)
        {
            var btn = UiKit.CreateButton(_content, label,
                new Vector2(x0, SegBottom), new Vector2(x1, SegTop), () => onClick(), 26);

            var img = btn.GetComponent<Image>();
            img.sprite = UiArt.Chip;
            img.color = on ? UiKit.Accent : SurfaceDim;

            var text = btn.GetComponentInChildren<Text>();
            text.color = on ? Ink : new Color(1f, 1f, 1f, 0.6f);
            text.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
        }

        private void Redraw()
        {
            foreach (var (btn, bridge, selected) in _tabs)
            {
                PaintTab(btn, bridge, selected());
            }

            // Destroy는 프레임 끝에 실행돼 한 프레임 동안 옛 위젯이 겹치고 클릭까지 먹는다.
            // 부모에서 먼저 떼어내 그 사이를 없앤다.
            for (var i = _content.childCount - 1; i >= 0; i--)
            {
                var stale = _content.GetChild(i).gameObject;
                stale.transform.SetParent(null, false);
                Destroy(stale);
            }

            if (_statsTab)
            {
                DrawStats();
            }
            else
            {
                DrawHistory();
            }
        }

        // ── 통계 탭 ──

        private void DrawStats()
        {
            if (_stats?.modes == null || _stats.modes.Length == 0)
            {
                _loading.text = "아직 전적이 없어요";
                return;
            }

            _loading.gameObject.SetActive(false);

            ModeSegments();

            var mode = Array.Find(_stats.modes, m => m.mode == _mode);
            if (mode == null || mode.games == 0)
            {
                EmptyStats();
                return;
            }

            SummaryTiles(mode);
            StatsHeader();

            var rows = mode.byPlayers ?? Array.Empty<ServerApi.SeatCountStats>();
            var best = BestRow(rows);
            for (var i = 0; i < rows.Length; i++)
            {
                StatsRow(rows[i], StatsRowTop - (i + 1) * StatsRowH, i % 2 == 1, i == best);
            }

            if (_mode == Friend)
            {
                UiKit.CreateText(_content, "친구와 함께는 상대를 고를 수 있어 맞춤게임 승률과 따로 셉니다",
                    22, TextAnchor.MiddleCenter,
                    new Vector2(0.05f, 0.085f), new Vector2(0.95f, 0.13f)).color = new Color(1f, 1f, 1f, 0.4f);
            }
        }

        /// <summary>
        /// 요약 타일: 한 줄로 흘려 쓴 문장 대신 판수·승률·누적 상금을 따로 세운다.
        /// 프로필에 들어와 제일 먼저 확인하는 세 숫자라 표보다 위, 글자보다 크게 둔다.
        /// </summary>
        private void SummaryTiles(ServerApi.ModeStats mode)
        {
            Tile(0, "판수", $"{mode.games}", $"{mode.wins}승 {mode.games - mode.wins}패", Color.white, -1);
            Tile(1, "승률", $"{mode.winRate}%", null, UiKit.Accent, mode.winRate);
            Tile(2, "누적 상금", $"{mode.totalWinnings:N0}", null, UiKit.Accent, -1);
        }

        private void Tile(int index, string label, string value, string sub, Color valueColor, int barPercent)
        {
            // 모드 고르개(~0.44)와 한 뼘 떼어 놓는다 — 붙여 두면 타일이 고르개의 연장으로 읽힌다
            const float left = 0.47f, right = 0.93f, gap = 0.014f;
            var w = (right - left - gap * 2f) / 3f;
            var x0 = left + index * (w + gap);

            var card = UiKit.CreatePanel(_content, Surface);
            card.sprite = UiArt.Chip;
            card.type = Image.Type.Sliced;
            UiKit.Anchor(card.rectTransform, new Vector2(x0, BandBottom), new Vector2(x0 + w, BandTop));

            UiKit.CreateText(card.transform, label, 20, TextAnchor.UpperCenter,
                new Vector2(0f, 0.60f), new Vector2(1f, 0.94f)).color = new Color(1f, 1f, 1f, 0.5f);

            var v = UiKit.CreateText(card.transform, value, 42, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.24f), new Vector2(1f, 0.66f));
            v.color = valueColor;
            v.fontStyle = FontStyle.Bold;

            if (sub != null)
            {
                UiKit.CreateText(card.transform, sub, 20, TextAnchor.LowerCenter,
                    new Vector2(0f, 0.06f), new Vector2(1f, 0.26f)).color = new Color(1f, 1f, 1f, 0.45f);
            }

            if (barPercent < 0)
            {
                return;
            }

            // 승률 타일에만 막대를 깔아 아래 표의 인원별 막대와 같은 눈금으로 읽히게 한다
            var bar = UiKit.CreatePanel(card.transform, Trough);
            UiKit.Anchor(bar.rectTransform, new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.17f));
            if (barPercent > 0)
            {
                var lit = UiKit.CreatePanel(card.transform, UiKit.Accent);
                UiKit.Anchor(lit.rectTransform, new Vector2(0.12f, 0.10f),
                    new Vector2(0.12f + 0.76f * barPercent / 100f, 0.17f));
            }
        }

        /// <summary>
        /// 비어 있는 모드도 화면이 고장 난 게 아니라 "아직 없다"로 읽혀야 한다.
        /// 흐린 아이콘 + 한 줄 설명으로, 채워진 탭과 같은 자리에 같은 무게로 세운다.
        /// </summary>
        private void EmptyStats()
        {
            var friend = _mode == Friend;

            var icon = UiKit.CreateIcon(_content, friend ? UiArt.IconFriends : UiArt.IconTrophy,
                new Vector2(0.46f, 0.335f), new Vector2(0.54f, 0.465f));
            icon.color = new Color(1f, 1f, 1f, 0.20f);

            UiKit.CreateText(_content, friend ? "아직 친구와 함께한 판이 없어요" : "아직 맞춤게임 전적이 없어요",
                28, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.255f), new Vector2(0.95f, 0.315f)).color = new Color(1f, 1f, 1f, 0.7f);

            UiKit.CreateText(_content, friend
                    ? "친구와 함께한 판은 맞춤게임 승률과 따로 셉니다"
                    : "포인트를 걸고 한 판 하면 여기부터 쌓여요",
                22, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.20f), new Vector2(0.95f, 0.25f)).color = new Color(1f, 1f, 1f, 0.4f);
        }

        /// <summary>인원별 표의 열 위치. 헤더와 각 행이 같은 값을 써야 줄이 맞는다.</summary>
        private static readonly (string Title, float Min, float Max, TextAnchor Anchor)[] Columns =
        {
            ("인원", 0.075f, 0.16f, TextAnchor.MiddleLeft),
            ("판수", 0.17f, 0.25f, TextAnchor.MiddleRight),
            ("승", 0.26f, 0.32f, TextAnchor.MiddleRight),
            ("패", 0.33f, 0.39f, TextAnchor.MiddleRight),
            ("승률", 0.68f, 0.755f, TextAnchor.MiddleRight),
            ("누적 상금", 0.77f, 0.925f, TextAnchor.MiddleRight)
        };

        private void StatsHeader()
        {
            foreach (var (title, min, max, anchor) in Columns)
            {
                UiKit.CreateText(_content, title, 24, anchor,
                    new Vector2(min, 0.470f), new Vector2(max, 0.515f)).color = new Color(1f, 1f, 1f, 0.45f);
            }

            Fill(new Color(1f, 1f, 1f, 0.14f), RowLeft, 0.464f, RowRight, 0.467f);
        }

        /// <summary>
        /// 가장 잘 나온 인원 수(승률 기준). 해본 인원이 하나뿐이면 견줄 게 없어 강조를 접는다 —
        /// 비교 대상 없는 "최고"는 정보가 아니라 장식이다.
        /// </summary>
        private static int BestRow(ServerApi.SeatCountStats[] rows)
        {
            var played = 0;
            var best = -1;
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i].games == 0)
                {
                    continue;
                }

                played++;
                if (best < 0 || rows[i].winRate > rows[best].winRate)
                {
                    best = i;
                }
            }

            return played >= 2 ? best : -1;
        }

        private void StatsRow(ServerApi.SeatCountStats row, float y, bool striped, bool best)
        {
            if (best)
            {
                // 최고 승률 줄은 금빛 바탕 + 왼쪽 띠. 줄무늬보다 확실히 진해야 강조로 읽힌다.
                Fill(Gold(0.17f), RowLeft, y, RowRight, y + StatsRowH);
                Fill(UiKit.Accent, RowLeft, y + 0.008f, RowLeft + 0.007f, y + StatsRowH - 0.008f);
            }
            else if (striped)
            {
                Fill(new Color(1f, 1f, 1f, 0.045f), RowLeft, y, RowRight, y + StatsRowH);
            }

            // 안 해본 인원도 줄을 남기되 흐리게 — 표가 들쭉날쭉하지 않아야 비교가 쉽다
            var played = row.games > 0;
            var bright = played ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.28f);

            WinRateBar(row, y, played, best);

            var values = new[]
            {
                $"{row.players}인",
                played ? $"{row.games}" : "-",
                played ? $"{row.wins}" : "-",
                played ? $"{row.games - row.wins}" : "-",
                played ? $"{row.winRate}%" : "-",
                row.totalWinnings > 0 ? $"{row.totalWinnings:N0}" : "-"
            };

            for (var i = 0; i < Columns.Length; i++)
            {
                var (_, min, max, anchor) = Columns[i];
                var text = UiKit.CreateText(_content, values[i], 26, anchor,
                    new Vector2(min, y), new Vector2(max, y + StatsRowH));
                text.color = i == Columns.Length - 1 && row.totalWinnings > 0 ? UiKit.Accent : bright;
                if (i == 0)
                {
                    text.color = best ? UiKit.Accent : text.color;
                    text.fontStyle = FontStyle.Bold;
                }
            }
        }

        /// <summary>
        /// 인원별 승률 막대. 퍼센트 숫자만 세로로 늘어놓으면 2인전과 6인전 중 어느 쪽이 나은지
        /// 매번 읽어서 비교해야 한다. 같은 축에 눕혀 길이로 보이게 하고, 절반 자리에 눈금을 남긴다.
        /// </summary>
        private void WinRateBar(ServerApi.SeatCountStats row, float y, bool played, bool best)
        {
            var mid = y + StatsRowH / 2f;
            const float half = 0.011f;

            Fill(Trough, BarLeft, mid - half, BarRight, mid + half);

            if (played && row.winRate > 0)
            {
                Fill(best ? UiKit.Accent : Gold(0.7f),
                    BarLeft, mid - half, BarLeft + (BarRight - BarLeft) * row.winRate / 100f, mid + half);
            }

            // 눈금은 막대 위에 그린다 — 아래에 깔면 채워진 구간에서 사라져 기준 노릇을 못 한다
            var tick = (BarLeft + BarRight) / 2f;
            Fill(new Color(1f, 1f, 1f, 0.22f), tick - 0.0012f, mid - half, tick + 0.0012f, mid + half);
        }

        // ── 게임 기록 탭 ──

        private void DrawHistory()
        {
            if (_history == null)
            {
                return; // 아직 응답 전 — 로딩 문구가 그대로 남는다
            }

            if (_history.Length == 0)
            {
                _loading.gameObject.SetActive(true);
                _loading.text = "아직 끝낸 판이 없어요";
                return;
            }

            _loading.gameObject.SetActive(false);

            UiKit.CreateText(_content, "맞춤게임과 친구와 함께를 모두 보여줍니다", 22, TextAnchor.MiddleRight,
                new Vector2(0.5f, 0.605f), new Vector2(0.93f, 0.677f)).color = new Color(1f, 1f, 1f, 0.4f);
            UiKit.CreateText(_content, "최근 기록", 30, TextAnchor.MiddleLeft,
                new Vector2(0.07f, 0.605f), new Vector2(0.4f, 0.677f)).fontStyle = FontStyle.Bold;
            Fill(new Color(1f, 1f, 1f, 0.12f), RowLeft, 0.5945f, RowRight, 0.5965f);

            for (var i = 0; i < _history.Length && i < HistoryRows; i++)
            {
                HistoryRow(_history[i], HistoryRowTop - (i + 1) * HistoryRowH, i % 2 == 1);
            }
        }

        /// <summary>기록 한 줄: 등수 배지 · 모드 · 인원/상대 · 정산액 · 시각.</summary>
        private void HistoryRow(ServerApi.HistoryEntry entry, float y, bool striped)
        {
            var win = entry.won;

            if (win)
            {
                // 이긴 판은 바탕을 금빛으로 깔고 왼쪽에 띠를 세운다. 아홉 줄을 다 읽지 않아도
                // 어느 판을 이겼는지 스치듯 보이는 게 기록 탭에서 제일 자주 하는 일이다.
                Fill(Gold(0.11f), RowLeft, y, RowRight, y + HistoryRowH);
                Fill(UiKit.Accent, RowLeft, y + 0.006f, RowLeft + 0.004f, y + HistoryRowH - 0.006f);
            }
            else if (striped)
            {
                Fill(new Color(1f, 1f, 1f, 0.035f), RowLeft, y, RowRight, y + HistoryRowH);
            }

            // 사람이 여럿이면 등수가 승패보다 정보량이 많다. 3인전 2등과 6인전 2등은 다른 판이라
            // 사람 수를 붙여 "2/3"으로 읽히게 한다. 봇만 있는 판은 등수가 늘 1/1이라 승패로 쓴다.
            var badge = UiKit.CreatePanel(_content, win ? UiKit.Accent : SurfaceDim);
            badge.sprite = UiArt.Chip;
            badge.type = Image.Type.Sliced;
            UiKit.Anchor(badge.rectTransform,
                new Vector2(0.078f, y + 0.007f), new Vector2(0.145f, y + HistoryRowH - 0.007f));

            var result = UiKit.CreateText(badge.transform,
                entry.humans >= 2 ? $"{entry.rank}/{entry.humans}" : win ? "승" : "패", 24,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            result.color = win ? Ink : new Color(1f, 1f, 1f, 0.55f);
            result.fontStyle = FontStyle.Bold;

            var friend = entry.mode == Friend;
            UiKit.CreateText(_content, friend ? "친구와 함께" : "맞춤게임", 24, TextAnchor.MiddleLeft,
                    new Vector2(0.158f, y), new Vector2(0.30f, y + HistoryRowH))
                .color = friend ? FriendTint : QuickTint;

            var with = entry.opponents == null || entry.opponents.Length == 0
                ? $"{entry.players}인"
                : $"{entry.players}인 · {string.Join(", ", entry.opponents)}";
            var label = UiKit.CreateText(_content, with, 23, TextAnchor.MiddleLeft,
                new Vector2(0.305f, y), new Vector2(0.69f, y + HistoryRowH));
            label.color = new Color(1f, 1f, 1f, 0.55f);
            label.verticalOverflow = VerticalWrapMode.Truncate; // 닉네임이 길어도 다음 줄을 침범하지 않게

            // 정산액은 받은 상금 기준. 진 판은 입장료만 나가고 상금이 없어 0으로 남는다.
            var payout = UiKit.CreateText(_content,
                entry.payout > 0 ? $"+{entry.payout:N0}" : "-", 25, TextAnchor.MiddleRight,
                new Vector2(0.70f, y), new Vector2(0.82f, y + HistoryRowH));
            payout.color = entry.payout > 0 ? UiKit.Accent : new Color(1f, 1f, 1f, 0.3f);
            payout.fontStyle = entry.payout > 0 ? FontStyle.Bold : FontStyle.Normal;

            UiKit.CreateText(_content, Ago(entry.endedAt), 22, TextAnchor.MiddleRight,
                    new Vector2(0.83f, y), new Vector2(0.925f, y + HistoryRowH))
                .color = new Color(1f, 1f, 1f, 0.38f);
        }

        /// <summary>절대 시각보다 "얼마 전"이 판을 떠올리기 쉽다. 하루가 넘으면 날짜로 바꾼다.</summary>
        private static string Ago(string isoUtc)
        {
            if (!DateTimeOffset.TryParse(isoUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var at))
            {
                return "";
            }

            var span = DateTimeOffset.UtcNow - at;
            if (span.TotalMinutes < 1)
            {
                return "방금";
            }

            if (span.TotalHours < 1)
            {
                return $"{(int)span.TotalMinutes}분 전";
            }

            if (span.TotalDays < 1)
            {
                return $"{(int)span.TotalHours}시간 전";
            }

            return at.ToLocalTime().ToString("M/d");
        }

        private void OnSave()
        {
            var nick = _nickInput.text;
            if (!GameConfig.IsValidNickname(nick))
            {
                _status.text = $"닉네임은 1~{GameConfig.MaxNicknameLength}자로 지어주세요.";
                return;
            }

            _saveBtn.interactable = false;
            _status.text = "저장 중...";
            StartCoroutine(ServerApi.Rename(nick,
                () => { _status.text = "저장 완료"; _saveBtn.interactable = true; },
                err => { _status.text = err; _saveBtn.interactable = true; }));
        }

        private void Back() => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this);
    }
}
