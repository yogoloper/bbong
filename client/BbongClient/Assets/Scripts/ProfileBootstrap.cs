using System;
using BbongCore.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>프로필: 닉네임 변경, 맞춤게임 전적 요약, 최근 게임 기록.</summary>
    public sealed class ProfileBootstrap : MonoBehaviour
    {
        /// <summary>가로 화면 한 열에 스크롤 없이 들어가는 줄 수. 더 필요해지면 그때 스크롤을 단다.</summary>
        private const int HistoryRows = 8;

        private GameObject _canvas;
        private InputField _nickInput;
        private Text _status;
        private Button _saveBtn;
        private Text _statsText;
        private Transform _historyRoot;
        private Text _historyEmpty;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private void ShowStats(ServerApi.StatsResult stats)
        {
            if (stats.games == 0)
            {
                _statsText.text = "아직 전적이 없어요\n맞춤게임을 한 판 해보세요";
                return;
            }

            _statsText.text =
                $"{stats.games}전 {stats.wins}승 {stats.games - stats.wins}패  ·  승률 {stats.winRate}%\n" +
                $"누적 상금 {stats.totalWinnings:N0}";
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("ProfileCanvas", topBar: true);
            _canvas = canvas;

            UiKit.CreateText(root, "프로필", 52, TextAnchor.MiddleCenter,
                new Vector2(0.06f, 0.79f), new Vector2(0.94f, 0.87f)).fontStyle = FontStyle.Bold;

            BuildLeftColumn(root);
            BuildHistoryColumn(root);

            StartCoroutine(ServerApi.FetchStats(ShowStats, _ => _statsText.text = "전적을 불러오지 못했어요"));
            StartCoroutine(ServerApi.FetchHistory(HistoryRows, ShowHistory,
                _ => _historyEmpty.text = "기록을 불러오지 못했어요"));

            UiKit.BackButton(root, Back);
        }

        private void BuildLeftColumn(Transform root)
        {
            UiKit.CreateText(root, "닉네임", 30, TextAnchor.MiddleLeft,
                new Vector2(0.07f, 0.70f), new Vector2(0.46f, 0.76f));
            // 입력창·버튼 같은 높이(터치 하한 이상 명시) — 폼 한 줄의 위아래 선을 맞춘다
            _nickInput = UiKit.CreateInputField(root, Session.Nickname, GameConfig.MaxNicknameLength,
                new Vector2(0.07f, 0.575f), new Vector2(0.33f, 0.697f));
            _saveBtn = UiKit.CreateButton(root, "저장",
                new Vector2(0.35f, 0.575f), new Vector2(0.46f, 0.697f), OnSave, 30);

            var statsPanel = UiKit.CreatePanel(root, new Color(0f, 0f, 0f, 0.3f));
            if (UiArt.Panel9 != null)
            {
                statsPanel.sprite = UiArt.Panel9;
                statsPanel.type = Image.Type.Sliced;
            }

            UiKit.Anchor(statsPanel.rectTransform, new Vector2(0.07f, 0.33f), new Vector2(0.46f, 0.53f));

            _statsText = UiKit.CreateText(root, "전적 불러오는 중...", 30, TextAnchor.MiddleCenter,
                new Vector2(0.07f, 0.33f), new Vector2(0.46f, 0.53f));
            _statsText.color = new Color(1f, 1f, 1f, 0.85f);

            UiKit.CreateText(root, "맞춤게임 기준 · 친구와 함께는 집계하지 않아요", 22, TextAnchor.MiddleCenter,
                new Vector2(0.07f, 0.27f), new Vector2(0.46f, 0.32f)).color = new Color(1f, 1f, 1f, 0.4f);

            _status = UiKit.CreateText(root, "", 26, TextAnchor.MiddleCenter,
                new Vector2(0.07f, 0.19f), new Vector2(0.46f, 0.26f));
            _status.color = new Color(1f, 0.8f, 0.5f);
        }

        private void BuildHistoryColumn(Transform root)
        {
            var panel = UiKit.CreatePanel(root, new Color(0f, 0f, 0f, 0.3f));
            if (UiArt.Panel9 != null)
            {
                panel.sprite = UiArt.Panel9;
                panel.type = Image.Type.Sliced;
            }

            UiKit.Anchor(panel.rectTransform, new Vector2(0.52f, 0.19f), new Vector2(0.94f, 0.76f));

            UiKit.CreateText(root, "최근 기록", 30, TextAnchor.MiddleLeft,
                new Vector2(0.545f, 0.69f), new Vector2(0.75f, 0.75f)).fontStyle = FontStyle.Bold;
            UiKit.CreateText(root, "친구와 함께 포함", 22, TextAnchor.MiddleRight,
                new Vector2(0.75f, 0.69f), new Vector2(0.915f, 0.75f)).color = new Color(1f, 1f, 1f, 0.4f);

            _historyRoot = root;
            _historyEmpty = UiKit.CreateText(root, "기록 불러오는 중...", 26, TextAnchor.MiddleCenter,
                new Vector2(0.52f, 0.35f), new Vector2(0.94f, 0.45f));
            _historyEmpty.color = new Color(1f, 1f, 1f, 0.5f);
        }

        private void ShowHistory(ServerApi.HistoryEntry[] entries)
        {
            if (entries.Length == 0)
            {
                _historyEmpty.text = "아직 끝낸 판이 없어요";
                return;
            }

            _historyEmpty.gameObject.SetActive(false);

            const float top = 0.665f;
            const float rowHeight = 0.058f;
            for (var i = 0; i < entries.Length && i < HistoryRows; i++)
            {
                HistoryRow(entries[i], top - (i + 1) * rowHeight, i % 2 == 1);
            }
        }

        /// <summary>기록 한 줄: 결과 · 모드/인원 · 정산액 · 시각. 승패는 색으로도 구분한다.</summary>
        private void HistoryRow(ServerApi.HistoryEntry entry, float y, bool striped)
        {
            if (striped)
            {
                UiKit.Anchor(
                    UiKit.CreatePanel(_historyRoot, new Color(1f, 1f, 1f, 0.05f)).rectTransform,
                    new Vector2(0.535f, y), new Vector2(0.925f, y + 0.055f));
            }

            var win = entry.won;
            var result = UiKit.CreateText(_historyRoot, win ? "승" : "패", 28, TextAnchor.MiddleCenter,
                new Vector2(0.545f, y), new Vector2(0.585f, y + 0.055f));
            result.color = win ? UiKit.Accent : new Color(1f, 1f, 1f, 0.45f);
            result.fontStyle = FontStyle.Bold;

            var mode = entry.mode == "Friend" ? "친구와 함께" : "맞춤게임";
            UiKit.CreateText(_historyRoot, $"{mode} · {entry.players}인", 25, TextAnchor.MiddleLeft,
                new Vector2(0.595f, y), new Vector2(0.755f, y + 0.055f)).color = new Color(1f, 1f, 1f, 0.8f);

            // 정산액은 받은 상금 기준. 진 판은 입장료만 나가고 상금이 없어 0으로 남는다.
            var payout = UiKit.CreateText(_historyRoot,
                entry.payout > 0 ? $"+{entry.payout:N0}" : "-", 26, TextAnchor.MiddleRight,
                new Vector2(0.755f, y), new Vector2(0.845f, y + 0.055f));
            payout.color = entry.payout > 0 ? UiKit.Accent : new Color(1f, 1f, 1f, 0.35f);

            UiKit.CreateText(_historyRoot, Ago(entry.endedAt), 23, TextAnchor.MiddleRight,
                new Vector2(0.845f, y), new Vector2(0.915f, y + 0.055f)).color = new Color(1f, 1f, 1f, 0.4f);
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
