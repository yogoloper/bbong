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

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("ProfileCanvas");
            _canvas = canvas;

            UiKit.CreateText(root, "프로필", 64, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.86f), new Vector2(0.9f, 0.97f)).fontStyle = FontStyle.Bold;

            UiKit.CreateText(root, "닉네임", 32, TextAnchor.MiddleLeft,
                new Vector2(0.2f, 0.7f), new Vector2(0.8f, 0.76f));
            _nickInput = UiKit.CreateInputField(root, Session.Nickname, GameConfig.MaxNicknameLength,
                new Vector2(0.2f, 0.6f), new Vector2(0.65f, 0.69f));
            _saveBtn = UiKit.CreateButton(root, "저장", new Vector2(0.67f, 0.6f), new Vector2(0.8f, 0.69f), OnSave, 32);

            // 통계 placeholder
            var statsPanel = UiKit.CreatePanel(root, new Color(0, 0, 0, 0.3f));
            UiKit.Anchor(statsPanel.rectTransform, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.52f));
            UiKit.CreateText(root, "전적\n0전 0승 0패  ·  최고 기록 -", 32, TextAnchor.MiddleCenter,
                new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.52f)).color = new Color(1, 1, 1, 0.6f);
            UiKit.CreateText(root, "(통계·게임 기록은 온라인 플레이 후 집계 — Phase 5)", 24, TextAnchor.MiddleCenter,
                new Vector2(0.2f, 0.24f), new Vector2(0.8f, 0.29f)).color = new Color(1, 1, 1, 0.4f);

            UiKit.CreateButton(root, "뒤로", new Vector2(0.03f, 0.03f), new Vector2(0.15f, 0.11f), Back, 32);

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.13f), new Vector2(0.9f, 0.21f));
            _status.color = new Color(1f, 0.8f, 0.5f);
        }

        private void OnSave()
        {
            var nick = _nickInput.text;
            if (!GameConfig.IsValidNickname(nick))
            {
                _status.text = $"닉네임은 1~{GameConfig.MaxNicknameLength}자여야 합니다.";
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
