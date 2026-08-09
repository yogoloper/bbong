using System.Linq;
using BbongCore.Config;
using BbongCore.Online;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 친구방(초대코드) — Phase 5 M1. 방 만들기/코드 입장/대기실, 호스트가 시작하면
    /// NetGameTableBootstrap으로 전환. 무료(판돈 없음), 통신은 WsClient(/ws).
    /// </summary>
    public sealed class FriendRoomBootstrap : MonoBehaviour
    {
        private GameObject _canvas;
        private Text _status;
        private RoomUpdateMsg _room;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            WsClient.Instance.OnMessage += HandleMessage;
            WsClient.Instance.OnClosed += HandleClosed;
            WsClient.Instance.Paused = false; // 전환 중 보존된 메시지 수신 재개
            if (_room == null)
            {
                BuildEntry();
            }
            else
            {
                BuildWaitingRoom(); // 게임 종료 후 대기실 복귀 경로
            }
        }

        /// <summary>게임 종료 후 대기실로 되돌아올 때 방 상태를 미리 주입.</summary>
        public void ResumeInRoom(RoomUpdateMsg room) => _room = room;

        private void OnDestroy()
        {
            if (WsClient.HasInstance)
            {
                WsClient.Instance.OnMessage -= HandleMessage;
                WsClient.Instance.OnClosed -= HandleClosed;
            }
        }

        // ── 화면 ──

        private void BuildEntry()
        {
            Rebuild(out var root);
            var title = UiKit.CreateText(root, "친구와 함께", 56, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.74f), new Vector2(0.9f, 0.86f));
            title.fontStyle = FontStyle.Bold;
            UiKit.CreateText(root, "포인트 없이 친구들과 한 판 (입장료 없음)", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.66f), new Vector2(0.9f, 0.73f)).color = new Color(1f, 1f, 1f, 0.7f);

            UiKit.CtaButton(root, "방 만들기 (호스트)",
                new Vector2(0.34f, 0.50f), new Vector2(0.66f, 0.62f), OnCreateRoom, 38);
            UiKit.CreateButton(root, "초대코드로 입장",
                new Vector2(0.34f, 0.36f), new Vector2(0.66f, 0.48f), BuildCodeInput, 38);

            UiKit.CreateText(root, "방을 만들면 6자리 초대코드가 나옵니다.\n친구에게 코드를 알려주고 함께 시작하세요.", 28,
                TextAnchor.MiddleCenter, new Vector2(0.1f, 0.20f), new Vector2(0.9f, 0.32f))
                .color = new Color(1f, 1f, 1f, 0.7f);

            BuildStatus(root);
            UiKit.BackButton(root, () => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this));
        }

        private void BuildCodeInput()
        {
            Rebuild(out var root);
            var title = UiKit.CreateText(root, "초대코드 입력", 56, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.74f), new Vector2(0.9f, 0.86f));
            title.fontStyle = FontStyle.Bold;

            var input = UiKit.CreateInputField(root, "", 6,
                new Vector2(0.38f, 0.52f), new Vector2(0.62f, 0.62f));
            input.contentType = InputField.ContentType.IntegerNumber;

            UiKit.CreateButton(root, "입장",
                new Vector2(0.40f, 0.38f), new Vector2(0.60f, 0.48f),
                () => JoinWithCode(input.text), 38);

            BuildStatus(root);
            UiKit.BackButton(root, BuildEntry);
        }

        private void BuildWaitingRoom()
        {
            Rebuild(out var root);
            var title = UiKit.CreateText(root, "대기실", 48, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.84f), new Vector2(0.9f, 0.94f));
            title.fontStyle = FontStyle.Bold;

            // 초대코드 크게 — 친구에게 알려줄 값
            var code = UiKit.CreateText(root, $"초대코드  {_room.code}", 72, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.82f));
            code.fontStyle = FontStyle.Bold;
            code.color = UiKit.Accent;

            if (_room.stake > 0)
            {
                var humans = _room.members.Count(m => !m.isBot); // 봇은 입장료가 없어 상금에서 제외
                UiKit.CreateText(root, $"입장료 {_room.stake:N0} · 현재 총상금 {(long)_room.stake * humans:N0}", 30,
                    TextAnchor.MiddleCenter, new Vector2(0.1f, 0.63f), new Vector2(0.9f, 0.68f)).color = UiKit.Accent;
            }

            var lines = "";
            foreach (var member in _room.members)
            {
                var host = member.userId == _room.hostUserId ? " ★" : "";
                var me = member.userId == Session.UserId ? " (나)" : "";
                lines += $"{member.nickname}{host}{me}\n";
            }

            UiKit.CreateText(root, lines.TrimEnd(), 34, TextAnchor.UpperCenter,
                new Vector2(0.25f, 0.30f), new Vector2(0.75f, 0.64f));

            if (Session.UserId == _room.hostUserId)
            {
                // 방장 전용 봇 관리 — 둘이서만 하면 루즈하니 봇으로 자리를 채운다(사람+봇 최대 정원)
                var botCount = _room.members.Count(m => m.isBot);
                var addBot = UiKit.CreateButton(root, "봇 추가",
                    new Vector2(0.30f, 0.19f), new Vector2(0.48f, 0.27f),
                    () => WsClient.Instance.Send(new AddBotMsg()), 30);
                addBot.interactable = _room.members.Length < GameConfig.MaxPlayers;
                var removeBot = UiKit.CreateButton(root, "봇 빼기",
                    new Vector2(0.52f, 0.19f), new Vector2(0.70f, 0.27f),
                    () => WsClient.Instance.Send(new RemoveBotMsg()), 30);
                removeBot.interactable = botCount > 0;

                var start = UiKit.PrimaryCta(root, "게임 시작", () => WsClient.Instance.Send(new StartGameMsg()));
                start.interactable = _room.members.Length >= 2; // 봇 포함 2명 이상
            }
            else
            {
                UiKit.CreateText(root, "호스트가 시작하길 기다리는 중...", 30, TextAnchor.MiddleCenter,
                    new Vector2(0.1f, 0.13f), new Vector2(0.9f, 0.20f)).color = new Color(1f, 1f, 1f, 0.7f);
            }

            BuildStatus(root);
            UiKit.BackButton(root, () =>
            {
                WsClient.Instance.Send(new LeaveRoomMsg());
                _room = null;
                BuildEntry();
            });
        }

        private void Rebuild(out Transform root)
        {
            if (_canvas != null)
            {
                Destroy(_canvas);
            }

            (_canvas, root) = UiKit.CreateScreen("FriendRoomCanvas", topBar: true);
        }

        private void BuildStatus(Transform root)
        {
            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.02f), new Vector2(0.9f, 0.10f));
            _status.color = new Color(1f, 0.8f, 0.5f);
        }

        // ── 액션 ──

        private void OnCreateRoom() => EnsureConnected(() => WsClient.Instance.Send(new CreateRoomMsg()));

        private void JoinWithCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 6)
            {
                _status.text = "6자리 코드를 입력하세요.";
                return;
            }

            EnsureConnected(() => WsClient.Instance.Send(new JoinRoomMsg { code = code }));
        }

        private void EnsureConnected(System.Action then)
        {
            if (WsClient.Instance.IsConnected)
            {
                then();
                return;
            }

            _status.text = "서버 연결 중...";
            WsClient.Instance.Connect(
                () =>
                {
                    _status.text = "";
                    then();
                },
                err => _status.text = err);
        }

        // ── 수신 ──

        private void HandleMessage(string json)
        {
            var type = JsonUtility.FromJson<ServerEnvelope>(json).type;
            switch (type)
            {
                case ServerMessageType.RoomUpdate:
                    _room = JsonUtility.FromJson<RoomUpdateMsg>(json);
                    BuildWaitingRoom();
                    break;

                case ServerMessageType.GameStarted:
                    var started = JsonUtility.FromJson<GameStartedMsg>(json);
                    WsClient.Instance.Paused = true; // 테이블이 구독을 마칠 때까지 후속 메시지 보존
                    var table = new GameObject("NetGameTable").AddComponent<NetGameTableBootstrap>();
                    table.MySeat = started.yourSeat;
                    table.PlayerCount = started.playerCount;
                    table.Nicknames = started.nicknames;
                    table.Stake = started.stake;
                    Destroy(_canvas);
                    Destroy(gameObject);
                    break;

                case ServerMessageType.RoomClosed:
                    var closed = JsonUtility.FromJson<RoomClosedMsg>(json);
                    _room = null;
                    BuildEntry();
                    _status.text = closed.reason;
                    break;

                case ServerMessageType.Error:
                    var error = JsonUtility.FromJson<ErrorMsg>(json);
                    if (_status != null)
                    {
                        _status.text = error.message;
                    }

                    break;
            }
        }

        private void HandleClosed(string reason)
        {
            _room = null;
            BuildEntry();
            _status.text = reason;
        }
    }
}
