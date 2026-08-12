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
            UiKit.DestroyStrayTables(); // 메인 로비가 떴다 = 어떤 게임도 진행 중이면 안 됨
            UiKit.BackAction = null;    // 최상위 화면 — 기기 뒤로가기는 앱 종료
            Build();
            if (Session.IsLoggedIn)
            {
                // 게임 정산(상금/입장료) 반영 — 로비로 돌아올 때마다 서버 기준으로 갱신
                StartCoroutine(ServerApi.RefreshMe(UiKit.SyncBalanceLabel, _ => { }));
            }
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("MainLobbyCanvas", topBar: true);
            _canvas = canvas;

            // 6개 모드 카드 — 가로 한 줄(루미큐브식 카드 레이아웃 차용)
            var titles = new[] { "튜토리얼", "연습", "맞춤게임", "친구와 함께", "포인트 얻기", "프로필" };
            var descs = new[] { "처음이면 여기부터", "봇 상대로 몸풀기", "포인트 걸고 한 판", "초대코드로 친구랑", "광고 보고 포인트", "닉네임과 전적" };
            UnityEngine.Events.UnityAction[] actions = { OnTutorial, OnPractice, OnMatch, OnFriend, OnShop, OnProfile };

            const float pad = 0.012f, top = 0.80f, bottom = 0.13f;
            var w = (1f - pad * 7f) / 6f;
            for (var i = 0; i < 6; i++)
            {
                var x0 = pad + i * (w + pad);
                Mode(root, titles[i], descs[i], new Vector2(x0, bottom), new Vector2(x0 + w, top), actions[i]);
            }
        }

        // 모드별 강조색 — 카드 얼굴이자 하위 화면까지 이어지는 색 체계
        private static readonly Color[] ModeTint =
        {
            new(0.35f, 0.62f, 0.95f), // 튜토리얼 — 하늘
            new(0.36f, 0.72f, 0.52f), // 연습 — 초록
            new(0.94f, 0.83f, 0.55f), // 맞춤게임 — 골드
            new(0.78f, 0.48f, 0.86f), // 친구와 함께 — 보라
            new(0.95f, 0.66f, 0.32f), // 포인트 얻기 — 주황
            new(0.60f, 0.66f, 0.80f), // 프로필 — 중성 회청
        };

        private static Sprite ModeIcon(int i) => i switch
        {
            0 => UiArt.IconBook,
            1 => UiArt.IconRobot,
            2 => UiArt.IconTrophy,
            3 => UiArt.IconFriends,
            4 => UiArt.IconCoins,
            _ => UiArt.IconAvatar,
        };

        private int _modeIndex;

        private void Mode(Transform root, string title, string desc, Vector2 min, Vector2 max,
            UnityEngine.Events.UnityAction onClick)
        {
            var i = _modeIndex++;

            // 카드 = 반투명 패널 + 클릭 버튼. 제목은 하단, 위쪽은 모드 색 아이콘 영역.
            var btn = UiKit.CreateButton(root, "", min, max, onClick);
            btn.GetComponent<Image>().color = new Color(0.12f, 0.22f, 0.42f, 0.95f);
            var colors = btn.colors;
            colors.pressedColor = new Color(1.2f, 1.2f, 1.2f); // 눌림 피드백
            btn.colors = colors;

            var accentTop = UiKit.CreatePanel(btn.transform, ModeTint[i]);
            accentTop.sprite = UiArt.Pill;
            accentTop.type = Image.Type.Sliced;
            UiKit.Anchor(accentTop.rectTransform, new Vector2(0.08f, 0.45f), new Vector2(0.92f, 0.9f));
            UiKit.CreateIcon(accentTop.transform, ModeIcon(i),
                new Vector2(0.12f, 0.1f), new Vector2(0.88f, 0.9f));

            var t = UiKit.CreateText(btn.transform, title, 40, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.25f), new Vector2(1f, 0.42f));
            t.color = Color.white;
            t.fontStyle = FontStyle.Bold;
            var d = UiKit.CreateText(btn.transform, desc, 26, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.08f), new Vector2(1f, 0.24f));
            d.color = new Color(1f, 1f, 1f, 0.8f);
        }

        private void OnTutorial() => UiKit.GoTo<TutorialBootstrap>(_canvas, this);
        private void OnMatch() => UiKit.GoTo<MatchSetupBootstrap>(_canvas, this);
        private void OnPractice() => UiKit.GoTo<LobbyBootstrap>(_canvas, this);
        private void OnFriend() => UiKit.GoTo<FriendRoomBootstrap>(_canvas, this);
        private void OnShop() => UiKit.GoTo<ShopBootstrap>(_canvas, this);
        private void OnProfile() => UiKit.GoTo<ProfileBootstrap>(_canvas, this);
    }
}
