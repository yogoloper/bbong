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
            var (canvas, root) = UiKit.CreateScreen("MainLobbyCanvas");
            _canvas = canvas;

            // 상단 바: 닉네임 + 포인트
            var bar = UiKit.CreatePanel(root, new Color(0, 0, 0, 0.35f));
            UiKit.Anchor(bar.rectTransform, new Vector2(0.02f, 0.88f), new Vector2(0.98f, 0.98f));
            UiKit.CreateText(root, Session.Nickname, 40, TextAnchor.MiddleLeft,
                new Vector2(0.05f, 0.88f), new Vector2(0.6f, 0.98f));
            var points = UiKit.CreateText(root, $"{Session.Balance:N0} 포인트", 40, TextAnchor.MiddleRight,
                new Vector2(0.4f, 0.88f), new Vector2(0.95f, 0.98f));
            points.color = new Color(0.5f, 0.95f, 0.6f);

            // 5개 모드 — 큰 버튼 그리드
            Mode(root, "맞춤게임", "실유저와 포인트 대결", new Vector2(0.06f, 0.50f), new Vector2(0.48f, 0.82f), OnMatch);
            Mode(root, "연습", "컴퓨터와 연습", new Vector2(0.52f, 0.50f), new Vector2(0.94f, 0.82f), OnPractice);
            Mode(root, "친구와 함께", "초대코드로 방 만들기", new Vector2(0.06f, 0.16f), new Vector2(0.37f, 0.46f), OnFriend);
            Mode(root, "상점", "광고로 포인트 받기", new Vector2(0.40f, 0.16f), new Vector2(0.60f, 0.46f), OnShop);
            Mode(root, "프로필", "닉네임·통계", new Vector2(0.63f, 0.16f), new Vector2(0.94f, 0.46f), OnProfile);
        }

        private void Mode(Transform root, string title, string desc, Vector2 min, Vector2 max,
            UnityEngine.Events.UnityAction onClick)
        {
            var btn = UiKit.CreateButton(root, "", min, max, onClick);
            var t = UiKit.CreateText(btn.transform, title, 48, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.45f), new Vector2(1f, 0.95f));
            t.color = Color.black;
            t.fontStyle = FontStyle.Bold;
            var d = UiKit.CreateText(btn.transform, desc, 26, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.08f), new Vector2(1f, 0.42f));
            d.color = new Color(0.25f, 0.25f, 0.25f);
        }

        private void OnMatch() => UiKit.GoTo<MatchSetupBootstrap>(_canvas, this);
        private void OnPractice() => UiKit.GoTo<LobbyBootstrap>(_canvas, this);
        private void OnFriend() => UiKit.GoTo<FriendRoomBootstrap>(_canvas, this);
        private void OnShop() => UiKit.GoTo<ShopBootstrap>(_canvas, this);
        private void OnProfile() => UiKit.GoTo<ProfileBootstrap>(_canvas, this);
    }
}
