using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>친구와 함께(방 생성/초대코드 입장). 포인트 없음. 게임서버는 Phase 5 — 흐름 placeholder.</summary>
    public sealed class FriendRoomBootstrap : MonoBehaviour
    {
        private GameObject _canvas;
        private Text _status;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("FriendRoomCanvas");
            _canvas = canvas;

            UiKit.CreateText(root, "친구와 함께", 64, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.84f), new Vector2(0.9f, 0.96f)).fontStyle = FontStyle.Bold;
            UiKit.CreateText(root, "포인트 없이 친구들과 한 판", 30, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.76f), new Vector2(0.9f, 0.82f)).color = new Color(1, 1, 1, 0.7f);

            UiKit.CreateButton(root, "방 만들기 (호스트)",
                new Vector2(0.3f, 0.56f), new Vector2(0.7f, 0.66f),
                () => _status.text = "방 생성 → 초대코드 발급 (Phase 5)", 38);
            UiKit.CreateButton(root, "초대코드로 입장",
                new Vector2(0.3f, 0.43f), new Vector2(0.7f, 0.53f),
                () => _status.text = "초대코드 입력 → 대기실 입장 (Phase 5)", 38);

            UiKit.CreateButton(root, "뒤로", new Vector2(0.03f, 0.03f), new Vector2(0.15f, 0.11f), Back, 32);

            _status = UiKit.CreateText(root, "", 30, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.26f), new Vector2(0.9f, 0.36f));
            _status.color = new Color(1f, 0.8f, 0.5f);
        }

        private void Back() => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this);
    }
}
