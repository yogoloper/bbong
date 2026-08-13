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

            // 가운데 79% 폭에 좌우 같은 여백으로 깐다. 카드 다섯 장이 화면 한가운데 좁게 몰려 있어
            // 아래쪽이 통째로 비어 보였다 — 폭과 높이를 함께 키워 여백을 카드 주위로 고르게 돌렸다.
            // 높이는 카드 비율(가로:세로 ≈ 1:1.8)에서 역산한 값이라 폭만 건드리면 카드가 길쭉해진다.
            const float left = 0.105f, right = 0.895f, pad = 0.014f, top = 0.715f, bottom = 0.25f;
            var count = titles.Length;
            var w = (right - left - pad * (count - 1)) / count;
            for (var i = 0; i < count; i++)
            {
                var x0 = left + i * (w + pad);
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
            btn.GetComponent<Image>().color = UiKit.Surface; // 판 위의 면 — 전 화면 공용 네이비
            var colors = btn.colors;
            colors.pressedColor = new Color(1.2f, 1.2f, 1.2f); // 눌림 피드백
            btn.colors = colors;

            // 아이콘 판만 캡슐(Pill)이라 카드 모서리보다 훨씬 둥글어 두 겹으로 보였다.
            // 앱 공용 칩과 같은 반경으로 맞춰 카드 안에 얌전히 앉게 한다.
            var accentTop = UiKit.CreateChip(btn.transform, ModeTint[i],
                new Vector2(0.09f, 0.455f), new Vector2(0.91f, 0.90f));
            UiKit.CreateIcon(accentTop.transform, ModeIcon(i),
                new Vector2(0.12f, 0.1f), new Vector2(0.88f, 0.9f));

            var t = UiKit.CreateText(btn.transform, title, 40, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.26f), new Vector2(1f, 0.42f));
            t.color = Color.white;
            t.fontStyle = FontStyle.Bold;

            // 설명은 제목을 거들 뿐이다. 0.8이면 제목과 거의 같은 무게라 카드마다 글자 두 줄이
            // 나란히 소리쳤다 — 한 단 낮춰 제목 → 설명 순서가 보이게 한다.
            var d = UiKit.CreateText(btn.transform, desc, 25, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.09f), new Vector2(1f, 0.24f));
            d.color = UiKit.TextFaint;
        }

        private void OnTutorial() => UiKit.GoTo<TutorialBootstrap>(_canvas, this);
        private void OnMatch() => UiKit.GoTo<MatchSetupBootstrap>(_canvas, this);
        private void OnPractice() => UiKit.GoTo<LobbyBootstrap>(_canvas, this);
        private void OnFriend() => UiKit.GoTo<FriendRoomBootstrap>(_canvas, this);
        private void OnShop() => UiKit.GoTo<ShopBootstrap>(_canvas, this);
    }
}
