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
            UiKit.BackAction = null; // 로그인 화면 — 기기 뒤로가기는 앱 종료
            Build();

            if (Session.HasSavedCredentials)
            {
                _guestBtn.interactable = false;
                _status.text = "이어서 접속 중...";
                StartCoroutine(ServerApi.ResumeLogin(OnLoggedIn, OnResumeFailed));
            }
        }

        /// <summary>
        /// 저장된 자격이 거부됨(계정 삭제·서버 초기화 등). 자동으로 새 게스트를 만들지 않고
        /// 버튼을 돌려준다 — 조용히 새 계정이 생기면 유저는 포인트가 사라진 걸로 본다.
        /// </summary>
        private void OnResumeFailed(string error)
        {
            Session.ForgetCredentials();
            _guestBtn.interactable = true;
            _status.text = "이전 계정을 불러오지 못했습니다.\n새로 시작하려면 아래 버튼을 눌러 주세요.";
            Debug.LogWarning($"[BBONG] 재개 실패: {error}");
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("AuthCanvas");
            _canvas = canvas;

            var title = UiKit.CreateText(root, "나이롱뽕", 110, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.62f), new Vector2(0.9f, 0.82f));
            title.fontStyle = FontStyle.Bold;
            title.color = UiKit.Accent; // 첫 화면부터 테마 골드
            var titleShadow = title.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            titleShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            titleShadow.effectDistance = new Vector2(0f, -8f);

            // 카드게임 첫인상 — 카드 뒷면 3장 부채꼴
            for (var i = -1; i <= 1; i++)
            {
                var back = UiKit.CreateIcon(root, UiArt.CardBack,
                    new Vector2(0.465f + i * 0.035f, 0.48f), new Vector2(0.535f + i * 0.035f, 0.60f));
                back.transform.localRotation = Quaternion.Euler(0f, 0f, -i * 12f);
            }

            _guestBtn = UiKit.CtaButton(root, "게스트로 시작",
                new Vector2(0.34f, 0.32f), new Vector2(0.66f, 0.43f), OnGuest, 44);

            var social = UiKit.CreateButton(root, "소셜 로그인 (준비중)",
                new Vector2(0.34f, 0.20f), new Vector2(0.66f, 0.29f), () => { }, 30);
            social.interactable = false;
            social.GetComponent<Image>().color = UiTheme.SurfaceDim; // 전 화면 유일한 흰색 판이었다 — 네이비 체계 안으로
            social.GetComponentInChildren<Text>().color = UiTheme.InkDisabled;

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.08f), new Vector2(0.9f, 0.17f));
            _status.color = UiTheme.InkMuted;
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
            _status.text = $"접속 실패: {error}\n잠시 후 다시 시도해 주세요.";
        }
    }
}
