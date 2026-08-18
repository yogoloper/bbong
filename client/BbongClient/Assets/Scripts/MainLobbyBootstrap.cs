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

            // 모드 카드 — 가로 한 줄(루미큐브식 카드 레이아웃 차용).
            // 프로필은 상단바 아바타가 맡는다: 카드 한 장을 통째로 쓰기엔 자주 들어가는 곳이 아니고,
            // 아바타를 누르는 쪽이 어느 화면에서나 통한다.
            var titles = new[] { "튜토리얼", "연습", "맞춤게임", "친구와 함께", "포인트 얻기" };
            var descs = new[] { "처음이면 여기부터", "봇 상대로 몸풀기", "포인트 걸고 한 판", "초대코드로 친구랑", "광고 보고 포인트" };
            UnityEngine.Events.UnityAction[] actions = { OnTutorial, OnPractice, OnMatch, OnFriend, OnShop };

            // 가운데 70% 폭에 좌우 같은 여백으로 깐다. 높이는 카드 비율(가로:세로 ≈ 1:1.6)에서
            // 역산한 값이다 — 폭만 건드리면 카드가 길쭉해진다.
            const float left = 0.15f, right = 0.85f, pad = 0.012f, top = 0.70f, bottom = 0.24f;
            var count = titles.Length;
            var w = (right - left - pad * (count - 1)) / count;
            for (var i = 0; i < count; i++)
            {
                var x0 = left + i * (w + pad);
                Mode(root, titles[i], descs[i], new Vector2(x0, bottom), new Vector2(x0 + w, top), actions[i]);
            }
        }

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

            // 카드 = 반투명 패널 + 클릭 버튼. 제목은 하단, 위쪽은 아이콘 영역.
            // 모드 구분은 아이콘 도형이 한다 — 카드마다 색을 달리 주면 첫 화면부터 무지개가 된다.
            var btn = UiKit.CreateButton(root, "", min, max, onClick);
            btn.GetComponent<Image>().color = UiTheme.Surface;

            var accentTop = UiKit.CreatePanel(btn.transform, UiTheme.SurfaceDim);
            accentTop.sprite = UiArt.Pill;
            accentTop.type = Image.Type.Sliced;
            UiKit.Anchor(accentTop.rectTransform, new Vector2(0.08f, 0.45f), new Vector2(0.92f, 0.9f));
            UiKit.CreateIcon(accentTop.transform, ModeIcon(i),
                new Vector2(0.12f, 0.1f), new Vector2(0.88f, 0.9f));

            UiKit.CreateText(btn.transform, title, 40, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.25f), new Vector2(1f, 0.42f));

            var d = UiKit.CreateText(btn.transform, desc, 26, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.08f), new Vector2(1f, 0.24f));
            d.color = UiTheme.InkMuted;
        }

        private void OnTutorial() => UiKit.GoTo<TutorialBootstrap>(_canvas, this);
        private void OnMatch() => UiKit.GoTo<MatchSetupBootstrap>(_canvas, this);
        private void OnPractice() => UiKit.GoTo<LobbyBootstrap>(_canvas, this);
        private void OnFriend() => UiKit.GoTo<FriendRoomBootstrap>(_canvas, this);
        private void OnShop() => UiKit.GoTo<ShopBootstrap>(_canvas, this);
    }
}
