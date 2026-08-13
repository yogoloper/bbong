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
        /// <summary>가로 화면 한 장에 스크롤 없이 들어가는 줄 수. 줄 높이를 키워 아홉에서 여덟로 줄었다.</summary>
        private const int HistoryRows = 8;

        private const string QuickMatch = "QuickMatch";
        private const string Friend = "Friend";

        // 보드(탭이 얹히는 콘텐츠 판)의 네 변. 탭·표·막대 좌표가 전부 여기에 물려 있어 한곳에서 잡는다.
        private const float BoardTop = 0.70f;
        private const float BoardBottom = 0.14f;
        private const float BoardLeft = 0.05f;
        private const float BoardRight = 0.95f;

        /// <summary>표 좌우 여백 — 보드 안쪽으로 한 뼘 들어와야 줄이 판에 담긴 것처럼 보인다.</summary>
        private const float RowLeft = 0.072f;
        private const float RowRight = 0.928f;

        // 탭 높이는 최소 탭 높이와 정확히 같게 잡는다. CreateButton이 높이를 늘려 버리면
        // 탭 아랫변이 보드 윗변에서 어긋나 "얹힌 탭"이 아니라 떠 있는 버튼이 된다.
        private const float TabTop = BoardTop + UiKit.MinTapHeight;

        // 보드 첫 띠: 왼쪽 모드 세그먼트만 놓고 오른쪽은 비워 둔다.
        // 예전엔 여기에 요약 타일 세 장이 더 서 있었는데, 아래 표와 같은 숫자를 두 벌로 보여주는 셈이라
        // 층만 늘었다. 요약은 표 맨 위 "전체" 줄로 내려보냈다.
        private const float BandTop = 0.684f;
        private const float BandBottom = 0.552f;
        private const float SegTop = 0.679f;
        private const float SegBottom = 0.557f;

        // 인원별 표: 열 이름 → 전체 줄 → 인원별 5줄이 보드 바닥 위에 딱 떨어지는 값.
        // 고르개 띠(~0.552)와 열 이름 사이는 줄 간격의 두 배쯤 띄운다 — 붙여 두면 열 이름이
        // 고르개에 딸린 설명처럼 읽힌다.
        private const float HeadTop = 0.526f;
        private const float HeadBottom = 0.488f;
        private const float HeroTop = 0.478f;
        private const float HeroH = 0.086f;
        private const float StatsRowTop = 0.383f;
        private const float StatsRowH = 0.0476f;

        // 승률 막대가 눕는 구간. 전체 줄과 인원별 줄이 같은 x를 써야 길이 비교가 성립한다.
        private const float BarLeft = 0.315f;
        private const float BarRight = 0.615f;

        private const float HistoryRowTop = 0.588f;
        private const float HistoryRowH = 0.0555f;

        private static readonly Color TabOff = new(UiKit.Panel.r, UiKit.Panel.g, UiKit.Panel.b, 0.55f);
        private static readonly Color Trough = new(0f, 0f, 0f, 0.30f);  // 막대 홈
        private static readonly Color Hairline = new(1f, 1f, 1f, 0.12f); // 열 이름 밑줄
        private static readonly Color Divider = new(1f, 1f, 1f, 0.06f);  // 줄 사이 가름선

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

            // 닉네임 입력·저장은 탭과 같은 줄(보드 윗변~탭 윗변)에 세운다 — 헤더가 한 층으로
            // 정리되고, 입력창 높이도 탭 높이(=최소 탭 높이)와 자로 잰 듯 맞는다.
            _nickInput = UiKit.CreateInputField(root, Session.Nickname, GameConfig.MaxNicknameLength,
                new Vector2(BoardLeft, BoardTop), new Vector2(0.235f, TabTop));
            _saveBtn = UiKit.CreateButton(root, "저장",
                new Vector2(0.245f, BoardTop), new Vector2(0.335f, TabTop), OnSave, 28);

            // 한 줄이 두 몫을 한다: 평소엔 입력창 이름표, 저장하면 그 자리에 결과가 뜬다.
            // 라벨과 상태를 따로 세우면 탭 줄 위에 글자 층이 둘 생긴다.
            _status = UiKit.CreateText(root, "닉네임", 22, TextAnchor.MiddleLeft,
                new Vector2(BoardLeft + 0.006f, TabTop + 0.004f), new Vector2(0.44f, TabTop + 0.05f));
            _status.color = UiKit.TextFaint;

            // 보드를 탭보다 먼저 만든다 — uGUI는 나중에 만든 쪽이 위에 그려져서,
            // 순서를 바꾸면 선택된 탭과 보드를 잇는 다리가 판 밑으로 깔린다.
            UiKit.CreateBoard(root, new Vector2(BoardLeft, BoardBottom), new Vector2(BoardRight, BoardTop));

            // 탭이 앉는 레일. 선택 안 된 탭도 이 선 위에 서 있어야 허공에 뜨지 않는다.
            UiKit.Anchor(UiKit.CreatePanel(root, UiKit.Gold(0.22f)).rectTransform,
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
                new Vector2(0.05f, 0.36f), new Vector2(0.95f, 0.44f));
            _loading.color = UiKit.TextFaint;

            StartCoroutine(ServerApi.FetchStats(s => { _stats = s; Redraw(); },
                _ => _loading.text = "전적을 불러오지 못했어요"));
            StartCoroutine(ServerApi.FetchHistory(HistoryRows, h => { _history = h; Redraw(); },
                _ => _loading.text = "기록을 불러오지 못했어요"));

            UiKit.BackButton(root, Back);
        }

        /// <summary>보드 좌표로 바로 놓는 단색 판 — 가름선·승률 막대에 쓴다.</summary>
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
            text.color = on ? UiKit.Ink : UiKit.TextSub;
            text.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
            bridge.enabled = on;
        }

        /// <summary>
        /// 모드 고르개(맞춤게임/친구와 함께). 위쪽 탭과 층을 나누려고 트랙 안에 칸을 넣은
        /// 세그먼트 컨트롤로 만든다 — 같은 모양이면 어느 쪽이 상위인지 안 보인다.
        /// </summary>
        private void ModeSegments()
        {
            UiKit.CreateChip(_content, new Color(0f, 0f, 0f, 0.28f),
                new Vector2(RowLeft, BandBottom), new Vector2(0.442f, BandTop));

            Segment("맞춤게임", 0.0775f, 0.2545f, _mode == QuickMatch,
                () => { _mode = QuickMatch; Redraw(); });
            Segment("친구와 함께", 0.2595f, 0.4365f, _mode == Friend,
                () => { _mode = Friend; Redraw(); });
        }

        private void Segment(string label, float x0, float x1, bool on, Action onClick)
        {
            var btn = UiKit.CreateButton(_content, label,
                new Vector2(x0, SegBottom), new Vector2(x1, SegTop), () => onClick(), 26);

            var img = btn.GetComponent<Image>();
            img.sprite = UiArt.Chip;
            img.color = on ? UiKit.Accent : UiKit.SurfaceDim;

            var text = btn.GetComponentInChildren<Text>();
            text.color = on ? UiKit.Ink : UiKit.TextSub;
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

            // 승·패는 판수와 승률에서 나오는 값이라 표에서 열 두 개를 뺐다. 대신 고르개 옆에
            // 한마디로만 남긴다 — 없으면 아쉽지만, 열로 세울 만큼 자주 보는 숫자는 아니다.
            UiKit.CreateText(_content, $"{mode.wins}승 {mode.games - mode.wins}패", 26, TextAnchor.MiddleRight,
                new Vector2(0.60f, SegBottom), new Vector2(RowRight, SegTop)).color = UiKit.TextFaint;

            StatsHeader();
            HeroRow(mode);

            var rows = mode.byPlayers ?? Array.Empty<ServerApi.SeatCountStats>();
            for (var i = 0; i < rows.Length; i++)
            {
                StatsRow(rows[i], StatsRowTop - (i + 1) * StatsRowH);
            }

            if (_mode == Friend)
            {
                UiKit.CreateText(_content, "친구와 함께는 상대를 고를 수 있어 맞춤게임 승률과 따로 셉니다",
                    22, TextAnchor.MiddleCenter,
                    new Vector2(0.05f, 0.085f), new Vector2(0.95f, 0.13f)).color = UiKit.TextFaint;
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
                new Vector2(0.465f, 0.395f), new Vector2(0.535f, 0.505f));
            icon.color = UiKit.TextGhost;

            UiKit.CreateText(_content, friend ? "아직 친구와 함께한 판이 없어요" : "아직 맞춤게임 전적이 없어요",
                28, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.310f), new Vector2(0.95f, 0.370f)).color = UiKit.TextSub;

            UiKit.CreateText(_content, friend
                    ? "친구와 함께한 판은 맞춤게임 승률과 따로 셉니다"
                    : "포인트를 걸고 한 판 하면 여기부터 쌓여요",
                22, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.255f), new Vector2(0.95f, 0.305f)).color = UiKit.TextFaint;
        }

        /// <summary>
        /// 인원별 표의 열 위치. 헤더·전체 줄·인원별 줄이 같은 값을 써야 줄이 맞는다.
        /// 승/패를 빼고 넷만 남겼다: 인원 · 판수 · 승률 · 누적 상금.
        /// </summary>
        private static readonly (string Title, float Min, float Max, TextAnchor Anchor)[] Columns =
        {
            ("인원", 0.078f, 0.175f, TextAnchor.MiddleLeft),
            ("판수", 0.185f, 0.265f, TextAnchor.MiddleRight),
            ("승률", 0.630f, 0.705f, TextAnchor.MiddleRight),
            ("누적 상금", 0.780f, 0.925f, TextAnchor.MiddleRight)
        };

        private void StatsHeader()
        {
            foreach (var (title, min, max, anchor) in Columns)
            {
                UiKit.CreateText(_content, title, 22, anchor,
                    new Vector2(min, HeadBottom), new Vector2(max, HeadTop)).color = UiKit.TextFaint;
            }

            Fill(Hairline, RowLeft, 0.4825f, RowRight, 0.4845f);
        }

        /// <summary>
        /// 전체 줄. 예전엔 판수·승률·누적 상금을 타일 세 장으로 따로 세웠는데, 바로 아래 표가
        /// 같은 항목을 인원별로 또 보여줘 층이 두 겹이 됐다. 표의 첫 줄로 끌어내리고 글자만 키우면
        /// "전체 → 인원별"이 한 축에서 읽히고, 게이지도 아래 줄들과 같은 눈금 위에 선다.
        /// </summary>
        private void HeroRow(ServerApi.ModeStats mode)
        {
            const float y = HeroTop - HeroH;

            var name = UiKit.CreateText(_content, "전체", 28, Columns[0].Anchor,
                new Vector2(Columns[0].Min, y), new Vector2(Columns[0].Max, HeroTop));
            name.fontStyle = FontStyle.Bold;

            var games = UiKit.CreateText(_content, $"{mode.games}", 32, Columns[1].Anchor,
                new Vector2(Columns[1].Min, y), new Vector2(Columns[1].Max, HeroTop));
            games.fontStyle = FontStyle.Bold;

            Gauge(y + HeroH / 2f, 0.0135f, mode.winRate, lit: true);

            var rate = UiKit.CreateText(_content, $"{mode.winRate}%", 40, Columns[2].Anchor,
                new Vector2(Columns[2].Min, y), new Vector2(Columns[2].Max, HeroTop));
            rate.color = UiKit.Accent; // 게이지의 숫자 — 게이지와 한 몸이라 같은 색을 쓴다
            rate.fontStyle = FontStyle.Bold;

            var won = UiKit.CreateText(_content,
                mode.totalWinnings > 0 ? $"{mode.totalWinnings:N0}" : "-", 30, Columns[3].Anchor,
                new Vector2(Columns[3].Min, y), new Vector2(Columns[3].Max, HeroTop));
            won.fontStyle = FontStyle.Bold;

            // 전체 줄과 인원별 줄을 가르는 선은 열 이름 밑줄보다 한 단 옅게 — 같은 굵기로 두 번 그으면
            // 판이 사다리처럼 칸칸이 나뉘어 보인다
            Fill(Divider, RowLeft, 0.3855f, RowRight, 0.3875f);
        }

        private void StatsRow(ServerApi.SeatCountStats row, float y)
        {
            // 표 자체는 강조 없이 담담하게 — 눈이 갈 곳은 승률 게이지 하나로 좁힌다.
            // 줄무늬 배경도 걷어냈다. 다섯 줄뿐이라 줄 사이 여백만으로 충분히 갈라지고,
            // 회색 띠가 한 줄 건너 깔리면 표가 두 겹으로 접힌 것처럼 보였다.
            var played = row.games > 0;
            var ink = played ? UiKit.TextSub : UiKit.TextGhost;

            Gauge(y + StatsRowH / 2f, 0.009f, row.winRate, played && row.winRate > 0);

            var values = new[]
            {
                $"{row.players}인",
                played ? $"{row.games}" : "-",
                played ? $"{row.winRate}%" : "-",
                row.totalWinnings > 0 ? $"{row.totalWinnings:N0}" : "-"
            };

            for (var i = 0; i < Columns.Length; i++)
            {
                var (_, min, max, anchor) = Columns[i];
                UiKit.CreateText(_content, values[i], 25, anchor,
                    new Vector2(min, y), new Vector2(max, y + StatsRowH)).color = ink;
            }
        }

        /// <summary>
        /// 승률 막대 — 이 화면의 유일한 강조. 퍼센트 숫자만 세로로 늘어놓으면 2인전과 6인전 중
        /// 어느 쪽이 나은지 매번 읽어서 비교해야 한다. 같은 축에 눕혀 길이로 보이게 한다.
        /// 안 해본 인원도 홈은 남긴다 — 축이 끊기면 나머지 줄의 길이가 기준을 잃는다.
        /// </summary>
        private void Gauge(float mid, float half, int percent, bool lit)
        {
            Fill(Trough, BarLeft, mid - half, BarRight, mid + half);

            if (lit && percent > 0)
            {
                Fill(UiKit.Accent, BarLeft, mid - half, BarLeft + (BarRight - BarLeft) * percent / 100f, mid + half);
            }
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
                new Vector2(0.5f, 0.605f), new Vector2(RowRight, 0.677f)).color = UiKit.TextFaint;
            UiKit.CreateText(_content, "최근 기록", 30, TextAnchor.MiddleLeft,
                new Vector2(RowLeft, 0.605f), new Vector2(0.4f, 0.677f)).fontStyle = FontStyle.Bold;
            Fill(Hairline, RowLeft, 0.5955f, RowRight, 0.5975f);

            for (var i = 0; i < _history.Length && i < HistoryRows; i++)
            {
                HistoryRow(_history[i], HistoryRowTop - (i + 1) * HistoryRowH, separator: i > 0);
            }
        }

        /// <summary>기록 한 줄: 등수 배지 · 모드 · 인원/상대 · 정산액 · 시각.</summary>
        private void HistoryRow(ServerApi.HistoryEntry entry, float y, bool separator)
        {
            var win = entry.won;

            // 줄무늬와 금빛 바탕을 걷고 줄 사이 가름선만 남겼다. 이긴 판 하나에 바탕·왼쪽 띠·배지·
            // 정산액까지 금색이 넷이라, 정작 "이겼다"가 어디에 적혀 있는지 흐려졌었다.
            // 이제 골드는 둘뿐이다: 왼쪽 끝 배지와 오른쪽 끝 정산액 — 줄의 시작과 끝.
            if (separator)
            {
                Fill(Divider, RowLeft, y + HistoryRowH, RowRight, y + HistoryRowH + 0.0015f);
            }

            // 사람이 여럿이면 등수가 승패보다 정보량이 많다. 3인전 2등과 6인전 2등은 다른 판이라
            // 사람 수를 붙여 "2/3"으로 읽히게 한다. 봇만 있는 판은 등수가 늘 1/1이라 승패로 쓴다.
            var badge = UiKit.CreateChip(_content, win ? UiKit.Accent : UiKit.SurfaceDim,
                new Vector2(0.078f, y + 0.010f), new Vector2(0.148f, y + HistoryRowH - 0.010f));

            var result = UiKit.CreateText(badge.transform,
                entry.humans >= 2 ? $"{entry.rank}/{entry.humans}" : win ? "승" : "패", 24,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            result.color = win ? UiKit.Ink : UiKit.TextSub;
            result.fontStyle = FontStyle.Bold;

            // 모드는 색이 아니라 글자로 구분한다. 파랑·보라 이름표까지 얹으니 한 줄에 색이 넷이었다.
            UiKit.CreateText(_content, entry.mode == Friend ? "친구와 함께" : "맞춤게임", 24, TextAnchor.MiddleLeft,
                    new Vector2(0.162f, y), new Vector2(0.30f, y + HistoryRowH))
                .color = UiKit.TextSub;

            var with = entry.opponents == null || entry.opponents.Length == 0
                ? $"{entry.players}인"
                : $"{entry.players}인 · {string.Join(", ", entry.opponents)}";
            var label = UiKit.CreateText(_content, with, 23, TextAnchor.MiddleLeft,
                new Vector2(0.305f, y), new Vector2(0.69f, y + HistoryRowH));
            label.color = UiKit.TextFaint;
            label.verticalOverflow = VerticalWrapMode.Truncate; // 닉네임이 길어도 다음 줄을 침범하지 않게

            // 정산액은 받은 상금 기준. 진 판은 입장료만 나가고 상금이 없어 0으로 남는다.
            var payout = UiKit.CreateText(_content,
                entry.payout > 0 ? $"+{entry.payout:N0}" : "-", 26, TextAnchor.MiddleRight,
                new Vector2(0.70f, y), new Vector2(0.82f, y + HistoryRowH));
            payout.color = entry.payout > 0 ? UiKit.Accent : UiKit.TextGhost;
            payout.fontStyle = entry.payout > 0 ? FontStyle.Bold : FontStyle.Normal;

            UiKit.CreateText(_content, Ago(entry.endedAt), 22, TextAnchor.MiddleRight,
                    new Vector2(0.83f, y), new Vector2(RowRight, y + HistoryRowH))
                .color = UiKit.TextFaint;
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
                Notice($"닉네임은 1~{GameConfig.MaxNicknameLength}자로 지어주세요.");
                return;
            }

            _saveBtn.interactable = false;
            Notice("저장 중...");
            StartCoroutine(ServerApi.Rename(nick,
                () => { Notice("저장 완료"); _saveBtn.interactable = true; },
                err => { Notice(err); _saveBtn.interactable = true; }));
        }

        /// <summary>이름표 자리에 저장 결과를 띄운다 — 골드로 바꿔 "방금 뭔가 일어났다"를 표시한다.</summary>
        private void Notice(string message)
        {
            _status.text = message;
            _status.color = UiKit.Accent;
        }

        private void Back() => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this);
    }
}
