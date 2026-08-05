using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>메인 로비 허브: 5개 모드 진입 + 상단 닉네임/잔액(Session).</summary>
    public sealed class MainLobbyBootstrap : MonoBehaviour
    {
        private GameObject _canvas;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("MainLobbyCanvas", topBar: true);
            _canvas = canvas;

            // 6개 모드 카드 — 가로 한 줄(루미큐브식 카드 레이아웃 차용)
            var titles = new[] { "튜토리얼", "연습", "맞춤게임", "친구와 함께", "포인트 얻기", "프로필" };
            var descs = new[] { "규칙을 처음부터", "컴퓨터와 연습", "실유저와 포인트 대결", "포인트 없이 친구끼리", "광고 보고 포인트", "닉네임·통계" };
            UnityEngine.Events.UnityAction[] actions = { OnTutorial, OnPractice, OnMatch, OnFriend, OnShop, OnProfile };

            const float pad = 0.012f, top = 0.7f, bottom = 0.2f;
            var w = (1f - pad * 7f) / 6f;
            for (var i = 0; i < 6; i++)
            {
                var x0 = pad + i * (w + pad);
                Mode(root, titles[i], descs[i], new Vector2(x0, bottom), new Vector2(x0 + w, top), actions[i]);
            }
        }

        private void Mode(Transform root, string title, string desc, Vector2 min, Vector2 max,
            UnityEngine.Events.UnityAction onClick)
        {
            // 카드 = 반투명 패널 + 클릭 버튼. 제목은 하단, 위쪽은 색 강조 영역.
            var btn = UiKit.CreateButton(root, "", min, max, onClick);
            btn.GetComponent<Image>().color = new Color(0.12f, 0.22f, 0.42f, 0.95f);

            var accentTop = UiKit.CreatePanel(btn.transform, new Color(0.2f, 0.4f, 0.75f, 0.5f));
            UiKit.Anchor(accentTop.rectTransform, new Vector2(0.08f, 0.45f), new Vector2(0.92f, 0.9f));

            var t = UiKit.CreateText(btn.transform, title, 40, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.25f), new Vector2(1f, 0.42f));
            t.color = Color.white;
            t.fontStyle = FontStyle.Bold;
            var d = UiKit.CreateText(btn.transform, desc, 22, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.08f), new Vector2(1f, 0.24f));
            d.color = new Color(1f, 1f, 1f, 0.6f);
        }

        private void OnTutorial() => UiKit.GoTo<TutorialBootstrap>(_canvas, this);
        private void OnMatch() => UiKit.GoTo<MatchSetupBootstrap>(_canvas, this);
        private void OnPractice() => UiKit.GoTo<LobbyBootstrap>(_canvas, this);
        private void OnFriend() => UiKit.GoTo<FriendRoomBootstrap>(_canvas, this);
        private void OnShop() => UiKit.GoTo<ShopBootstrap>(_canvas, this);
        private void OnProfile() => UiKit.GoTo<ProfileBootstrap>(_canvas, this);
    }
}
