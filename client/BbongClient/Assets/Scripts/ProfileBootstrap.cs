using BbongCore.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>프로필: 닉네임 변경(서버 연동). 통계/기록은 Phase 5 데이터 — placeholder.</summary>
    public sealed class ProfileBootstrap : MonoBehaviour
    {
        private GameObject _canvas;
        private InputField _nickInput;
        private Text _status;
        private Button _saveBtn;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private Text _statsText;

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

            UiKit.CreateText(root, "프로필", 56, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.78f), new Vector2(0.9f, 0.87f)).fontStyle = FontStyle.Bold;

            UiKit.CreateText(root, "닉네임", 32, TextAnchor.MiddleLeft,
                new Vector2(0.2f, 0.715f), new Vector2(0.8f, 0.775f));
            // 입력창·버튼 같은 높이(터치 하한 이상 명시) — 폼 한 줄의 위아래 선을 맞춘다
            _nickInput = UiKit.CreateInputField(root, Session.Nickname, GameConfig.MaxNicknameLength,
                new Vector2(0.2f, 0.584f), new Vector2(0.65f, 0.706f));
            _saveBtn = UiKit.CreateButton(root, "저장", new Vector2(0.67f, 0.584f), new Vector2(0.8f, 0.706f), OnSave, 32);

            var statsPanel = UiKit.CreatePanel(root, new Color(0, 0, 0, 0.3f));
            UiKit.Anchor(statsPanel.rectTransform, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.52f));

            _statsText = UiKit.CreateText(root, "전적 불러오는 중...", 32, TextAnchor.MiddleCenter,
                new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.52f));
            _statsText.color = new Color(1, 1, 1, 0.85f);

            UiKit.CreateText(root, "맞춤게임 기준 · 친구와 함께는 집계하지 않아요", 24, TextAnchor.MiddleCenter,
                new Vector2(0.2f, 0.24f), new Vector2(0.8f, 0.29f)).color = new Color(1, 1, 1, 0.4f);

            StartCoroutine(ServerApi.FetchStats(ShowStats, _ => _statsText.text = "전적을 불러오지 못했어요"));

            UiKit.BackButton(root, Back);

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.13f), new Vector2(0.9f, 0.21f));
            _status.color = new Color(1f, 0.8f, 0.5f);
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
