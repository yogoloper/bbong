using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 진입점: 로그인 화면. 게스트 로그인(서버) → 메인 로비.
    /// 빈 GameObject에 이 컴포넌트 하나 붙이고 Play.
    /// </summary>
    public sealed class AuthBootstrap : MonoBehaviour
    {
        private GameObject _canvas;
        private Text _status;
        private Button _guestBtn;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("AuthCanvas");
            _canvas = canvas;

            var title = UiKit.CreateText(root, "나이롱뽕", 110, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.62f), new Vector2(0.9f, 0.82f));
            title.fontStyle = FontStyle.Bold;

            _guestBtn = UiKit.CreateButton(root, "게스트로 시작",
                new Vector2(0.34f, 0.4f), new Vector2(0.66f, 0.5f), OnGuest, 44);

            var social = UiKit.CreateButton(root, "소셜 로그인 (준비중)",
                new Vector2(0.34f, 0.28f), new Vector2(0.66f, 0.37f), () => { }, 34);
            social.interactable = false;

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.12f), new Vector2(0.9f, 0.24f));
            _status.color = new Color(1f, 0.8f, 0.5f);
        }

        private void OnGuest()
        {
            _guestBtn.interactable = false;
            _status.text = "로그인 중...";
            StartCoroutine(ServerApi.GuestLogin(OnLoggedIn, OnError));
        }

        private void OnLoggedIn() =>
            StartCoroutine(ServerApi.RefreshMe(() => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this), OnError));

        private void OnError(string error)
        {
            _guestBtn.interactable = true;
            _status.text = $"실패: {error}\n서버(localhost:5080)가 켜져 있는지 확인하세요.";
        }
    }
}
