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
        private static readonly Color Unselected = new(0.16f, 0.24f, 0.42f); // 어두운 네이비 — 밝은 것은 선택/CTA뿐

        private GameObject _canvas;
        private int _players = 4;
        private int _stake = 1000;
        private Text _prize;
        private Text _status;
        private readonly List<(int value, Button button)> _playerChoices = new();
        private readonly List<(int value, Button button)> _stakeChoices = new();

        private RoomUpdateMsg _room;
        private Coroutine _searchPulse;
        private bool _matching;   // 시작하기 이후 대기 상태 — 레이스 에러 시 자동 재매칭
        private int _retries;
        private Text _searchLabel;

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

            var subtitle = UiKit.CreateText(root, "1등이 총상금을 전부 가져갑니다", 26, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.735f), new Vector2(0.9f, 0.775f));
            subtitle.color = new Color(1f, 1f, 1f, 0.8f);

            // 라벨-칩 간격 < 그룹 간 간격(약 1:2) — 라벨이 아래 칩 무리로 묶여 읽히게 한다
            UiKit.CreateText(root, "인원", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.66f), new Vector2(0.9f, 0.71f));
            var playerCount = GameConfig.MaxPlayers - GameConfig.MinPlayers + 1;
            PlaceChoices(root, 0.515f, 0.637f, 0.09f, playerCount,
                i => GameConfig.MinPlayers + i, n => $"{n}명", _playerChoices, v => _players = v);

            UiKit.CreateText(root, "입장료", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.425f), new Vector2(0.9f, 0.475f));
            PlaceChoices(root, 0.28f, 0.402f, 0.09f, GameConfig.StakeOptions.Count,
                i => GameConfig.StakeOptions[i], s => $"{s:N0}", _stakeChoices, v => _stake = v);

            _prize = UiKit.CreateText(root, "", 38, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.268f));
            _prize.color = UiKit.Accent;
            _prize.fontStyle = FontStyle.Bold;

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.168f), new Vector2(0.9f, 0.212f));
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

        private static void Paint(Button button, bool selected)
        {
            button.GetComponent<Image>().color = selected ? Selected : Unselected;
            var text = button.GetComponentInChildren<Text>();
            text.color = selected ? Color.black : Color.white;
            text.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        private void RefreshSelection()
        {
            foreach (var (value, button) in _playerChoices)
            {
                Paint(button, value == _players);
            }

            foreach (var (value, button) in _stakeChoices)
            {
                Paint(button, value == _stake);
            }

            // winner-takes-all → 총상금 = 입장료 × 인원
            _prize.text = $"총상금 {(_stake * (long)_players):N0}";
        }

        private void OnMatch()
        {
            _matching = true;
            _retries = 0;
            EnsureConnected(() => WsClient.Instance.Send(new QuickMatchMsg { stake = _stake, players = _players }));
        }

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

            _searchLabel = UiKit.CreateText(root, "", 52, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.84f));
            _searchLabel.fontStyle = FontStyle.Bold;
            if (_searchPulse != null)
            {
                StopCoroutine(_searchPulse);
            }

            _searchPulse = StartCoroutine(SearchPulse(_searchLabel));

            var count = UiKit.CreateText(root, $"{_room.members.Length} / {_room.targetPlayers}", 68, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.58f), new Vector2(0.9f, 0.70f));
            count.fontStyle = FontStyle.Bold;
            count.color = UiKit.Accent;

            // 총상금 = 입장료 × 목표 인원 — 다 차면 받을 확정 금액을 바로 보여준다
            UiKit.CreateText(root, $"입장료 {_room.stake:N0} · 총상금 {(long)_room.stake * _room.targetPlayers:N0}", 30,
                TextAnchor.MiddleCenter, new Vector2(0.1f, 0.51f), new Vector2(0.9f, 0.57f)).color = UiKit.Accent;

            var lines = string.Join("\n", _room.members.Select(m =>
                m.userId == Session.UserId ? $"{m.nickname} (나)" : m.nickname));
            for (var i = _room.members.Length; i < _room.targetPlayers; i++)
            {
                lines += "\n<color=#FFFFFF40>- 빈 자리 -</color>"; // 목표 인원만큼 줄 유지 — 진행률이 목록으로 읽힘
            }

            UiKit.CreateText(root, lines, 34, TextAnchor.UpperCenter,
                new Vector2(0.25f, 0.24f), new Vector2(0.75f, 0.49f));

            UiKit.CreateButton(root, "매칭 취소",
                new Vector2(0.40f, 0.06f), new Vector2(0.60f, 0.15f),
                () =>
                {
                    WsClient.Instance.Send(new LeaveRoomMsg());
                    _room = null;
                    _matching = false;
                    Rebuild();
                }, 32);

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.0f), new Vector2(0.9f, 0.06f));
            _status.color = new Color(1f, 0.8f, 0.5f);
        }

        /// <summary>
        /// "상대를 찾는 중" 뒤의 점 3개가 파도처럼 밝아졌다 어두워진다.
        /// 점을 지우지 않고 투명도만 바꿔 글자 폭이 고정 — 가운데 정렬이어도 텍스트가 흔들리지 않는다.
        /// </summary>
        private System.Collections.IEnumerator SearchPulse(Text label)
        {
            const string bright = "FFF5E0FF";
            const string dim = "FFF5E033";
            var step = 0;
            while (label != null)
            {
                var dots = "";
                for (var i = 0; i < 3; i++)
                {
                    dots += $"<color=#{(i == step % 3 ? bright : dim)}>.</color>";
                }

                label.text = $"상대를 찾는 중 {dots}";
                step++;
                yield return new WaitForSeconds(0.4f);
            }
        }

        /// <summary>정원 충족: "곧 시작합니다 (5)" 카운트다운. 이탈자가 생기면 roomUpdate가 대기 화면으로 되돌린다.</summary>
        private System.Collections.IEnumerator StartCountdown(int seconds)
        {
            for (var remain = seconds; remain > 0 && _searchLabel != null; remain--)
            {
                _searchLabel.text = $"곧 시작합니다 ({remain})";
                yield return new WaitForSeconds(1f);
            }
        }

        private System.Collections.IEnumerator RetryMatch()
        {
            yield return new WaitForSeconds(0.5f);
            WsClient.Instance.Send(new QuickMatchMsg { stake = _stake, players = _players });
        }

        private void Rebuild()
        {
            _matching = false;
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
                    _retries = 0;
                    BuildMatching();
                    break;

                case ServerMessageType.MatchStarting:
                    var starting = JsonUtility.FromJson<MatchStartingMsg>(json);
                    if (_searchPulse != null)
                    {
                        StopCoroutine(_searchPulse);
                        _searchPulse = null;
                    }

                    StartCoroutine(StartCountdown(starting.seconds));
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
                    Rebuild();
                    _status.text = closed.reason;
                    break;

                case ServerMessageType.Error:
                    var error = JsonUtility.FromJson<ErrorMsg>(json);
                    // 매칭 레이스(방이 그 사이 차거나 시작됨) — 자동으로 다른 방을 다시 찾는다
                    if (_matching && _retries < 3
                        && error.code is "room_full" or "room_playing" or "room_not_found")
                    {
                        _retries++;
                        StartCoroutine(RetryMatch());
                        break;
                    }

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
