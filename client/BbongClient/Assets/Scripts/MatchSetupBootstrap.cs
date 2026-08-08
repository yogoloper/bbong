using System;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Config;
using BbongCore.Online;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 맞춤게임: 인원·입장료를 고르고 "시작하기" 한 번으로 빠른매칭 — 조건 맞는 대기방에 자동 배정되고
    /// 없으면 새 방을 열어 대기한다(초대코드·방장 시작 없음, 정원 차면 서버가 자동 시작).
    /// </summary>
    public sealed class MatchSetupBootstrap : MonoBehaviour
    {
        private static readonly Color Selected = UiKit.Accent;
        private static readonly Color Unselected = new(0.95f, 0.95f, 0.95f);

        private GameObject _canvas;
        private int _players = 4;
        private int _stake = 1000;
        private Text _prize;
        private Text _status;
        private readonly List<(int value, Button button)> _playerChoices = new();
        private readonly List<(int value, Button button)> _stakeChoices = new();

        private RoomUpdateMsg _room;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            WsClient.Instance.OnMessage += HandleMessage;
            WsClient.Instance.OnClosed += HandleClosed;
            WsClient.Instance.Paused = false;
            Build();
            RefreshSelection();
        }

        private void OnDestroy()
        {
            if (WsClient.HasInstance)
            {
                WsClient.Instance.OnMessage -= HandleMessage;
                WsClient.Instance.OnClosed -= HandleClosed;
            }
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("MatchSetupCanvas", topBar: true);
            _canvas = canvas;

            UiKit.CreateText(root, "맞춤게임", 56, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.78f), new Vector2(0.9f, 0.87f)).fontStyle = FontStyle.Bold;

            UiKit.CreateText(root, "인원", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.69f), new Vector2(0.9f, 0.74f));
            var playerCount = GameConfig.MaxPlayers - GameConfig.MinPlayers + 1;
            PlaceChoices(root, 0.58f, 0.67f, 0.09f, playerCount,
                i => GameConfig.MinPlayers + i, n => $"{n}", _playerChoices, v => _players = v);

            UiKit.CreateText(root, "입장료", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.55f));
            PlaceChoices(root, 0.39f, 0.48f, 0.1f, GameConfig.StakeOptions.Count,
                i => GameConfig.StakeOptions[i], s => $"{s:N0}", _stakeChoices, v => _stake = v);

            _prize = UiKit.CreateText(root, "", 38, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.3f), new Vector2(0.9f, 0.37f));
            _prize.color = UiKit.Accent;
            _prize.fontStyle = FontStyle.Bold;

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.28f));
            _status.color = new Color(1f, 0.8f, 0.5f);

            UiKit.PrimaryCta(root, "시작하기", OnMatch);
            UiKit.BackButton(root, Back);
        }

        /// <summary>선택지 버튼들을 가로 가운데 정렬로 배치 + 선택 강조 등록.</summary>
        private void PlaceChoices(Transform root, float y0, float y1, float w, int count,
            Func<int, int> valueAt, Func<int, string> format,
            List<(int value, Button button)> registry, Action<int> onPick)
        {
            const float gap = 0.012f;
            var start = 0.5f - (count * w + (count - 1) * gap) / 2f;
            for (var i = 0; i < count; i++)
            {
                var v = valueAt(i);
                var x0 = start + i * (w + gap);
                var btn = UiKit.CreateButton(root, format(v), new Vector2(x0, y0), new Vector2(x0 + w, y1),
                    () => { onPick(v); RefreshSelection(); }, 28);
                registry.Add((v, btn));
            }
        }

        private void RefreshSelection()
        {
            foreach (var (value, button) in _playerChoices)
            {
                button.GetComponent<Image>().color = value == _players ? Selected : Unselected;
            }

            foreach (var (value, button) in _stakeChoices)
            {
                button.GetComponent<Image>().color = value == _stake ? Selected : Unselected;
            }

            // winner-takes-all → 총상금 = 입장료 × 인원
            _prize.text = $"총상금 {(_stake * (long)_players):N0}";
        }

        private void OnMatch() =>
            EnsureConnected(() => WsClient.Instance.Send(new QuickMatchMsg { stake = _stake, players = _players }));

        private void EnsureConnected(Action then)
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

        /// <summary>매칭 대기 화면: 인원 채워지는 상황만 보여주고, 정원이 차면 서버가 자동 시작한다.</summary>
        private void BuildMatching()
        {
            Destroy(_canvas);
            var (canvas, root) = UiKit.CreateScreen("MatchWaitCanvas", topBar: true);
            _canvas = canvas;

            UiKit.CreateText(root, "상대를 찾는 중...", 52, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.84f)).fontStyle = FontStyle.Bold;

            var humans = _room.members.Count(m => !m.isBot);
            var count = UiKit.CreateText(root, $"{_room.members.Length} / {_room.targetPlayers}명", 68, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.58f), new Vector2(0.9f, 0.70f));
            count.fontStyle = FontStyle.Bold;
            count.color = UiKit.Accent;

            UiKit.CreateText(root, $"입장료 {_room.stake:N0} · 현재 총상금 {(long)_room.stake * humans:N0}", 30,
                TextAnchor.MiddleCenter, new Vector2(0.1f, 0.51f), new Vector2(0.9f, 0.57f)).color = UiKit.Accent;

            var lines = string.Join("\n", _room.members.Select(m =>
                m.userId == Session.UserId ? $"{m.nickname} (나)" : m.nickname));
            UiKit.CreateText(root, lines, 34, TextAnchor.UpperCenter,
                new Vector2(0.25f, 0.24f), new Vector2(0.75f, 0.49f));

            UiKit.CreateButton(root, "매칭 취소",
                new Vector2(0.40f, 0.06f), new Vector2(0.60f, 0.15f),
                () =>
                {
                    WsClient.Instance.Send(new LeaveRoomMsg());
                    _room = null;
                    Rebuild();
                }, 32);

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.0f), new Vector2(0.9f, 0.06f));
            _status.color = new Color(1f, 0.8f, 0.5f);
        }

        private void Rebuild()
        {
            Destroy(_canvas);
            _playerChoices.Clear();
            _stakeChoices.Clear();
            Build();
            RefreshSelection();
        }

        private void HandleMessage(string json)
        {
            switch (JsonUtility.FromJson<ServerEnvelope>(json).type)
            {
                case ServerMessageType.RoomUpdate:
                    _room = JsonUtility.FromJson<RoomUpdateMsg>(json);
                    BuildMatching();
                    break;

                case ServerMessageType.GameStarted:
                    var started = JsonUtility.FromJson<GameStartedMsg>(json);
                    WsClient.Instance.Paused = true; // 테이블이 구독을 마칠 때까지 후속 메시지 보존
                    var table = new GameObject("NetGameTable").AddComponent<NetGameTableBootstrap>();
                    table.MySeat = started.yourSeat;
                    table.PlayerCount = started.playerCount;
                    table.Nicknames = started.nicknames;
                    Destroy(_canvas);
                    Destroy(gameObject);
                    break;

                case ServerMessageType.RoomClosed:
                    var closed = JsonUtility.FromJson<RoomClosedMsg>(json);
                    _room = null;
                    Rebuild();
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
            Rebuild();
            _status.text = reason;
        }

        private void Back() => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this);
    }
}
