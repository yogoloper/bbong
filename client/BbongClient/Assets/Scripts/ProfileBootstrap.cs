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

            Tab(root, "통계", new Vector2(0.46f, 0.775f), new Vector2(0.63f, 0.875f),
                () => _statsTab, () => { _statsTab = true; Redraw(); });
            Tab(root, "게임 기록", new Vector2(0.645f, 0.775f), new Vector2(0.815f, 0.875f),
                () => !_statsTab, () => { _statsTab = false; Redraw(); });

            var board = UiKit.CreatePanel(root, new Color(0f, 0f, 0f, 0.3f));
            if (UiArt.Panel9 != null)
            {
                board.sprite = UiArt.Panel9;
                board.type = Image.Type.Sliced;
            }

            UiKit.Anchor(board.rectTransform, new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.70f));

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

        /// <summary>화면 내내 살아 있는 탭(통계/게임 기록).</summary>
        private readonly List<(Button btn, Func<bool> selected)> _tabs = new();

        /// <summary>선택된 탭만 골드로 채워 현재 위치를 드러낸다.</summary>
        private void Tab(Transform parent, string label, Vector2 min, Vector2 max,
            Func<bool> selected, Action onClick)
        {
            var btn = UiKit.CreateButton(parent, label, min, max, () => onClick(), 30);
            if (parent != _content)
            {
                _tabs.Add((btn, selected)); // 콘텐츠 탭은 매번 다시 그려지니 추적할 필요가 없다
            }

            Paint(btn, selected());
        }

        private static void Paint(Button btn, bool on)
        {
            btn.GetComponent<Image>().color = on ? UiKit.Accent : new Color(0.16f, 0.24f, 0.42f);
            var text = btn.GetComponentInChildren<Text>();
            text.color = on ? Color.black : new Color(1f, 1f, 1f, 0.75f);
            text.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
        }

        private void Redraw()
        {
            foreach (var (btn, selected) in _tabs)
            {
                Paint(btn, selected());
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

            Tab(_content, "맞춤게임", new Vector2(0.07f, 0.60f), new Vector2(0.24f, 0.685f),
                () => _mode == QuickMatch, () => { _mode = QuickMatch; Redraw(); });
            Tab(_content, "친구와 함께", new Vector2(0.25f, 0.60f), new Vector2(0.44f, 0.685f),
                () => _mode == Friend, () => { _mode = Friend; Redraw(); });

            var mode = Array.Find(_stats.modes, m => m.mode == _mode);
            if (mode == null || mode.games == 0)
            {
                UiKit.CreateText(_content, _mode == QuickMatch
                        ? "아직 맞춤게임 전적이 없어요"
                        : "아직 친구와 함께한 전적이 없어요",
                    28, TextAnchor.MiddleCenter,
                    new Vector2(0.05f, 0.36f), new Vector2(0.95f, 0.46f)).color = new Color(1f, 1f, 1f, 0.5f);
                return;
            }

            var summary = UiKit.CreateText(_content,
                $"{mode.games}전 {mode.wins}승 {mode.games - mode.wins}패  ·  승률 {mode.winRate}%" +
                $"  ·  누적 상금 {mode.totalWinnings:N0}",
                30, TextAnchor.MiddleRight, new Vector2(0.46f, 0.60f), new Vector2(0.93f, 0.685f));
            summary.color = new Color(1f, 1f, 1f, 0.9f);

            StatsHeader();
            // 5줄(2~6인)이 패널 안에 다 들어가야 한다 — 0.465에서 시작해 0.062씩 내려가면 바닥이 0.155
            var rows = mode.byPlayers ?? Array.Empty<ServerApi.SeatCountStats>();
            for (var i = 0; i < rows.Length; i++)
            {
                StatsRow(rows[i], 0.465f - (i + 1) * 0.062f, i % 2 == 1);
            }

            if (_mode == Friend)
            {
                UiKit.CreateText(_content, "친구와 함께는 상대를 고를 수 있어 맞춤게임 승률과 따로 셉니다",
                    22, TextAnchor.MiddleCenter,
                    new Vector2(0.05f, 0.085f), new Vector2(0.95f, 0.13f)).color = new Color(1f, 1f, 1f, 0.4f);
            }
        }

        /// <summary>인원별 표의 열 위치. 헤더와 각 행이 같은 값을 써야 줄이 맞는다.</summary>
        private static readonly (string Title, float Min, float Max, TextAnchor Anchor)[] Columns =
        {
            ("인원", 0.07f, 0.19f, TextAnchor.MiddleLeft),
            ("판수", 0.24f, 0.38f, TextAnchor.MiddleRight),
            ("승", 0.40f, 0.52f, TextAnchor.MiddleRight),
            ("패", 0.54f, 0.66f, TextAnchor.MiddleRight),
            ("승률", 0.68f, 0.79f, TextAnchor.MiddleRight),
            ("누적 상금", 0.81f, 0.93f, TextAnchor.MiddleRight)
        };

        private void StatsHeader()
        {
            foreach (var (title, min, max, anchor) in Columns)
            {
                UiKit.CreateText(_content, title, 24, anchor,
                    new Vector2(min, 0.485f), new Vector2(max, 0.545f)).color = new Color(1f, 1f, 1f, 0.45f);
            }

            UiKit.Anchor(UiKit.CreatePanel(_content, new Color(1f, 1f, 1f, 0.14f)).rectTransform,
                new Vector2(0.07f, 0.478f), new Vector2(0.93f, 0.481f));
        }

        private void StatsRow(ServerApi.SeatCountStats row, float y, bool striped)
        {
            if (striped)
            {
                UiKit.Anchor(UiKit.CreatePanel(_content, new Color(1f, 1f, 1f, 0.05f)).rectTransform,
                    new Vector2(0.06f, y), new Vector2(0.94f, y + 0.062f));
            }

            // 안 해본 인원도 줄을 남기되 흐리게 — 표가 들쭉날쭉하지 않아야 비교가 쉽다
            var played = row.games > 0;
            var bright = played ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.28f);

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
                    new Vector2(min, y), new Vector2(max, y + 0.062f));
                text.color = i == Columns.Length - 1 && row.totalWinnings > 0 ? UiKit.Accent : bright;
                if (i == 0)
                {
                    text.fontStyle = FontStyle.Bold;
                }
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
                new Vector2(0.5f, 0.625f), new Vector2(0.93f, 0.685f)).color = new Color(1f, 1f, 1f, 0.4f);
            UiKit.CreateText(_content, "최근 기록", 30, TextAnchor.MiddleLeft,
                new Vector2(0.07f, 0.625f), new Vector2(0.4f, 0.685f)).fontStyle = FontStyle.Bold;

            for (var i = 0; i < _history.Length && i < HistoryRows; i++)
            {
                HistoryRow(_history[i], 0.60f - (i + 1) * 0.052f, i % 2 == 1);
            }
        }

        /// <summary>기록 한 줄: 등수 · 모드/인원/상대 · 정산액 · 시각.</summary>
        private void HistoryRow(ServerApi.HistoryEntry entry, float y, bool striped)
        {
            if (striped)
            {
                UiKit.Anchor(UiKit.CreatePanel(_content, new Color(1f, 1f, 1f, 0.05f)).rectTransform,
                    new Vector2(0.06f, y), new Vector2(0.94f, y + 0.05f));
            }

            // 사람이 여럿이면 등수가 승패보다 정보량이 많다. 3인전 2등과 6인전 2등은 다른 판이라
            // 사람 수를 붙여 "2/3"으로 읽히게 한다. 봇만 있는 판은 등수가 늘 1/1이라 승패로 쓴다.
            var win = entry.won;
            var result = UiKit.CreateText(_content,
                entry.humans >= 2 ? $"{entry.rank}/{entry.humans}" : win ? "승" : "패", 26,
                TextAnchor.MiddleCenter, new Vector2(0.07f, y), new Vector2(0.15f, y + 0.05f));
            result.color = win ? UiKit.Accent : new Color(1f, 1f, 1f, 0.5f);
            result.fontStyle = FontStyle.Bold;

            var mode = entry.mode == Friend ? "친구와 함께" : "맞춤게임";
            var with = entry.opponents == null || entry.opponents.Length == 0
                ? $"{mode} · {entry.players}인"
                : $"{mode} · {entry.players}인 · {string.Join(", ", entry.opponents)}";
            var label = UiKit.CreateText(_content, with, 24, TextAnchor.MiddleLeft,
                new Vector2(0.17f, y), new Vector2(0.70f, y + 0.05f));
            label.color = new Color(1f, 1f, 1f, 0.8f);
            label.verticalOverflow = VerticalWrapMode.Truncate; // 닉네임이 길어도 다음 줄을 침범하지 않게

            // 정산액은 받은 상금 기준. 진 판은 입장료만 나가고 상금이 없어 0으로 남는다.
            var payout = UiKit.CreateText(_content,
                entry.payout > 0 ? $"+{entry.payout:N0}" : "-", 25, TextAnchor.MiddleRight,
                new Vector2(0.70f, y), new Vector2(0.82f, y + 0.05f));
            payout.color = entry.payout > 0 ? UiKit.Accent : new Color(1f, 1f, 1f, 0.35f);

            UiKit.CreateText(_content, Ago(entry.endedAt), 23, TextAnchor.MiddleRight,
                new Vector2(0.82f, y), new Vector2(0.93f, y + 0.05f)).color = new Color(1f, 1f, 1f, 0.4f);
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
