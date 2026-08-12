using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Online;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 온라인(친구방) 게임 테이블 드라이버 — 서버 권위. 렌더/연출은 공용 GameTableView가 담당하고,
    /// 여기는 WS 메시지 → 뷰 호출, 입력 이벤트 → 의도 전송, 낙관적 상태(뽕/자연뽕 내려놓기)만 관리한다.
    /// FriendRoomBootstrap이 gameStarted에서 생성·필드 주입.
    /// </summary>
    public sealed class NetGameTableBootstrap : MonoBehaviour
    {
        public int MySeat { get; set; }

        public int PlayerCount { get; set; }

        public string[] Nicknames { get; set; }

        /// <summary>입장료(0 = 무료). 판돈 방은 세트 종료 시 서버가 방을 폭파하므로 종료 화면 유지에 필요.</summary>
        public int Stake { get; set; }

        private GameTableView _table;
        private RoundView _view;
        private readonly List<Card> _pendingLaid = new(); // 내 뽕/자연뽕 내려놓기(서버 반영 전 손패 숨김)
        private bool _naturalSelecting;                    // 자연뽕 추가 버림 선택 중
        private bool _naturalLaidLocally;                  // 자연뽕 선언 즉시 내려놓기 연출(서버 확정 전)
        private RoomUpdateMsg _pendingRoom;                // 세트 종료 후 대기실 복귀 대기(무료방)
        private bool _gameOver;                            // 세트 종료 — 방 폭파/소켓 종료에도 점수판 유지

        /// <summary>재접속에 쓰는 방 코드(gameStarted로 받음). 없으면 복귀를 시도하지 않는다.</summary>
        public string RoomCode = "";

        private Coroutine _reconnect;
        private bool _rejoining;   // 재입장 요청을 보내고 응답을 기다리는 중
        private readonly List<int[]> _roundHistory = new(); // 게임(세트) 내 판별 점수
        private Button _roomBtn;
        private Button _lobbyBtn;

        private void Start()
        {
            _table = gameObject.AddComponent<GameTableView>();
            _table.MySeat = MySeat;
            _table.PlayerCount = PlayerCount;
            _table.Nicknames = Nicknames;
            _table.Build();
            _roomBtn = _table.AddBarButton("대기실로", ReturnToRoom);
            _lobbyBtn = _table.AddBarButton("로비로", () => LeaveToLobby("게임 종료"));
            _table.ScorePopupShown += Render; // 종료 점수판이 뜬 뒤에 이동 버튼 노출

            _table.CardClicked += OnCardClicked;
            _table.StopClicked += () => WsClient.Instance.Send(new StopDeclareMsg());
            _table.MeldClicked += () => WsClient.Instance.Send(new MeldDeclareMsg());
            _table.NaturalPongClicked += OnNaturalPong;
            _table.PongClicked += () => WsClient.Instance.Send(new PongDeclareMsg());
            _table.PassClicked += OnPass;
            _table.ExitConfirmText = "게임에서 나가시겠습니까?\n내 자리는 봇이 이어받습니다.";
            _table.ExitConfirmed += OnExitConfirmed;

            WsClient.Instance.OnMessage += HandleMessage;
            WsClient.Instance.OnClosed += HandleClosed;
            SetLog($"게임 테이블 진입 (좌석 {MySeat}, {PlayerCount}인)"); // 자동 테스트 진입 판정용
            WsClient.Instance.Paused = false; // 전환 중 보존된 메시지(roundStarted...) 수신 재개
        }

        private void OnDestroy()
        {
            if (WsClient.HasInstance)
            {
                WsClient.Instance.OnMessage -= HandleMessage;
                WsClient.Instance.OnClosed -= HandleClosed;
            }
        }

        // ── 수신/상태 ──

        private void HandleMessage(string json)
        {
            switch (JsonUtility.FromJson<ServerEnvelope>(json).type)
            {
                case ServerMessageType.GameStarted:
                    // 재입장 성공. 좌석이 바뀌어 있을 수 있어 뷰 기준값을 다시 맞춘다.
                    var restarted = JsonUtility.FromJson<GameStartedMsg>(json);
                    _rejoining = false;
                    RoomCode = restarted.code;
                    MySeat = restarted.yourSeat;
                    _table.MySeat = restarted.yourSeat;
                    _table.Nicknames = restarted.nicknames;
                    _table.SetPrompt("다시 연결됐습니다");
                    break;

                case ServerMessageType.RoundStarted:
                    var round = JsonUtility.FromJson<RoundStartedMsg>(json);
                    _table.ClearTimeline();
                    _pendingLaid.Clear();
                    _naturalSelecting = false;
                    _naturalLaidLocally = false;
                    _table.SetEndReason("");
                    _table.HideScorePopup();
                    ApplyView(round.view);
                    break;

                case ServerMessageType.TurnBegan:
                    ApplyView(JsonUtility.FromJson<TurnBeganMsg>(json).view);
                    break;

                case ServerMessageType.DrewCard:
                    var drew = JsonUtility.FromJson<DrewCardMsg>(json);
                    if (drew.reshuffled)
                    {
                        // 셔플 수렴이 끝난 뒤에 비행·손패 반영 — 카드가 연출보다 먼저 손에 들어오지 않게
                        _table.ClearTimeline(); // 버림 + 나간 패 전부 덱으로 복귀
                        _table.ShuffleFx();
                        SetLog("재셔플 — 수렴 연출 대기");
                        StartCoroutine(ApplyDrawAfterShuffle(drew));
                        break;
                    }

                    _table.DrawFx(drew.seat);
                    ApplyView(drew.view);
                    break;

                case ServerMessageType.Discarded:
                    var discarded = JsonUtility.FromJson<DiscardedMsg>(json);
                    if (discarded.seat == MySeat)
                    {
                        _pendingLaid.Clear(); // 내 뽕 추가 버림까지 서버 반영 완료
                    }

                    _table.DiscardFx(discarded.seat, discarded.card.ToCard()); // 좌석 → 더미 비행 후 쌓임
                    ApplyView(discarded.view);
                    break;

                case ServerMessageType.PongWindowOpened:
                    var openedMsg = JsonUtility.FromJson<PongWindowOpenedMsg>(json);
                    ApplyView(openedMsg.view);
                    if (_view is { canPong: true })
                    {
                        _table.StartPongCountdown(openedMsg.seconds);
                    }

                    break;

                case ServerMessageType.PongWindowClosed:
                    _table.StopPongCountdown();
                    ApplyView(JsonUtility.FromJson<PongWindowClosedMsg>(json).view);
                    break;

                case ServerMessageType.Ponged:
                    var ponged = JsonUtility.FromJson<PongedMsg>(json);
                    _table.StopPongCountdown();
                    OnLaid(ponged.seat, ponged.number, ponged.laid, "뽕!");
                    ApplyView(ponged.view);
                    break;

                case ServerMessageType.NaturalPonged:
                    var natural = JsonUtility.FromJson<NaturalPongedMsg>(json);
                    if (natural.seat == MySeat && _naturalLaidLocally)
                    {
                        // 선언 순간 이미 내려놓기 연출을 했으므로 서버 확정 구성으로 치환만(콜아웃/효과음 중복 방지)
                        _naturalLaidLocally = false;
                        _table.ReplaceLastGroup(natural.laid.Select(c => c.ToCard()));
                        _pendingLaid.Clear();
                        _pendingLaid.AddRange(natural.laid.Select(c => c.ToCard()));
                    }
                    else
                    {
                        OnLaid(natural.seat, natural.number, natural.laid, "자연뽕!");
                    }

                    ApplyView(natural.view);
                    break;

                case ServerMessageType.StopDeclared:
                    var stop = JsonUtility.FromJson<StopDeclaredMsg>(json);
                    _table.PlayStopSfx();
                    // 정상 스톱=선언자, 바가지=박 먹인 승자의 손패를 테이블에 펼침
                    _table.ShowMeldSet(stop.laid.Select(c => c.ToCard()), stop.laidSeat);
                    _table.ShowCallout($"{Nicknames[stop.seat]}\n{(stop.bagaji ? "스톱 바가지!" : "스톱!")}",
                        stop.bagaji ? new Color(1f, 0.4f, 0.35f) : new Color(0.55f, 0.85f, 1f));
                    break;

                case ServerMessageType.BotTookOver:
                    // 이탈/무응답 좌석을 봇이 이어받음(§9-4). 닉네임은 원래 게이머 것 유지.
                    var bot = JsonUtility.FromJson<BotTookOverMsg>(json);
                    Nicknames[bot.seat] = bot.nickname;
                    _table.ShowCallout($"{bot.nickname}\n봇으로 교체");
                    break;

                case ServerMessageType.MeldDeclared:
                    var meld = JsonUtility.FromJson<MeldDeclaredMsg>(json);
                    _table.ShowMeldSet(meld.laid.Select(c => c.ToCard()), meld.seat);
                    _table.PongFx($"{Nicknames[meld.seat]}\n{GameTableView.MeldKorean(meld.meldType)}!");
                    break;

                case ServerMessageType.RoundEnded:
                    var ended = JsonUtility.FromJson<RoundEndedMsg>(json);
                    _pendingLaid.Clear();
                    _table.SetEndReason(ended.reason);
                    _roundHistory.Add(ended.scores);
                    _table.ShowScorePopup($"{ended.roundIndex + 1}라운드 종료", ended.cumulativeDebts, _roundHistory, fadeOut: true);
                    ApplyView(ended.view);
                    break;

                case ServerMessageType.SetEnded:
                    var set = JsonUtility.FromJson<SetEndedMsg>(json);
                    _gameOver = true;
                    _pendingLaid.Clear();
                    _table.SetEndReason(set.reason);
                    _roundHistory.Add(set.scores);
                    var winners = string.Join(", ", set.winnerSeats.Select(s => Nicknames[s]));
                    var title = $"게임 끝! 1등 {winners}";
                    if (Stake > 0 && set.winnerSeats.Length > 0)
                    {
                        title += $" · 상금 {(long)Stake * PlayerCount / set.winnerSeats.Length:N0}";
                    }

                    _table.ShowScorePopup(title, set.cumulativeDebts, _roundHistory, fadeOut: false);
                    if (_view != null)
                    {
                        _view.phase = RoundPhase.SetOver;
                    }

                    Render();
                    break;

                case ServerMessageType.RoomUpdate:
                    // 세트 종료 후 서버가 방을 대기실로 복귀시킴 — 전광판 확인 후 버튼으로 이동
                    _pendingRoom = JsonUtility.FromJson<RoomUpdateMsg>(json);
                    Render();
                    break;

                case ServerMessageType.RoomClosed:
                    if (_gameOver)
                    {
                        break; // 판돈 방 정산 후 폭파 — 점수판을 보고 "로비로"로 나간다
                    }

                    var closed = JsonUtility.FromJson<RoomClosedMsg>(json);
                    LeaveToLobby(closed.reason);
                    break;

                case ServerMessageType.Error:
                    var error = JsonUtility.FromJson<ErrorMsg>(json);
                    if (_naturalLaidLocally)
                    {
                        // 자연뽕이 서버에서 거부됨 — 낙관적으로 내려놓은 3장 원복
                        _naturalLaidLocally = false;
                        _naturalSelecting = false;
                        _table.RemoveLastTimelineEntry();
                        _pendingLaid.Clear();
                        Render();
                    }

                    // 재입장이 거부되면(방이 이미 닫혔거나 서버가 재기동됨) 죽은 테이블에 남기지 않는다
                    if (_rejoining)
                    {
                        _rejoining = false;
                        LeaveToLobby(error.message);
                        break;
                    }

                    _table.SetPrompt(error.message);
                    break;
            }
        }

        private void HandleClosed(string reason)
        {
            if (_gameOver)
            {
                return;
            }

            // 모바일은 백그라운드 전환·통신 전환으로 소켓이 쉽게 끊긴다. 서버는 좌석을 들고
            // 기다리므로(봇이 대신 두는 중) 바로 로비로 보내지 말고 자리 복귀를 시도한다.
            if (string.IsNullOrEmpty(RoomCode) || _reconnect != null)
            {
                LeaveToLobby(reason);
                return;
            }

            _reconnect = StartCoroutine(Reconnect());
        }

        /// <summary>끊긴 소켓을 다시 붙이고 같은 방에 재입장. 서버가 좌석을 돌려주면 게임이 이어진다.</summary>
        private IEnumerator Reconnect()
        {
            for (var attempt = 1; attempt <= ReconnectAttempts; attempt++)
            {
                _table.SetPrompt($"연결이 끊겼습니다. 다시 연결 중... ({attempt}/{ReconnectAttempts})");

                var settled = false;
                var ok = false;
                WsClient.Instance.Connect(() => { ok = true; settled = true; }, _ => settled = true);

                // 콜백이 영영 오지 않는 경우(소켓이 매달림)에도 다음 시도로 넘어가게 한다
                var waited = 0f;
                while (!settled && waited < ConnectWaitSeconds)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (ok)
                {
                    _rejoining = true;
                    WsClient.Instance.Send(new JoinRoomMsg { code = RoomCode });
                    _reconnect = null;
                    yield break; // 성공 여부는 서버 응답(gameStarted / error)이 알려준다
                }

                yield return new WaitForSeconds(ReconnectDelaySeconds);
            }

            _reconnect = null;
            LeaveToLobby("서버에 다시 연결하지 못했습니다");
        }

        private const int ReconnectAttempts = 3;
        private const float ReconnectDelaySeconds = 2f;
        private const float ConnectWaitSeconds = 10f;

        private void ApplyView(RoundView view)
        {
            _view = view;
            Render();
        }

        private void Render()
        {
            _table.Render(_view, _pendingLaid, _naturalSelecting);
            _roomBtn.gameObject.SetActive(_pendingRoom != null);
            // 판돈 방(방 폭파형): 종료 점수판이 뜬 뒤 로비 이동 버튼만 노출
            _lobbyBtn.gameObject.SetActive(_gameOver && _pendingRoom == null && _table.ScorePopupVisible);
        }

        private void OnLaid(int seat, int number, CardDto[] laid, string suffix)
        {
            var cards = laid.Select(c => c.ToCard()).ToList();
            _table.GroupFx(seat, cards); // 좌석 → 무더기 비행
            if (seat == MySeat)
            {
                _pendingLaid.Clear();
                _pendingLaid.AddRange(cards);
            }

            _table.PongFx($"{Nicknames[seat]}\n{number}{suffix}");
        }

        private void LeaveToLobby(string reason)
        {
            SetLog($"방 종료: {reason}");
            UiKit.GoTo<MainLobbyBootstrap>(_table.CanvasGo, this);
        }

        /// <summary>자발적 나가기: leaveRoom 전송 — 서버가 내 자리를 즉시 봇으로 대체(§9-4). 소켓은 로비에서 재사용.</summary>
        private void OnExitConfirmed()
        {
            WsClient.Instance.Send(new LeaveRoomMsg());
            LeaveToLobby("게임에서 나갔습니다.");
        }

        // ── 입력 ──

        private void OnCardClicked(Card card)
        {
            if (_view == null)
            {
                return;
            }

            if (_naturalSelecting)
            {
                _naturalSelecting = false;
                WsClient.Instance.Send(new NaturalPongMsg { hasDiscard = true, card = CardDto.From(card) });
                return;
            }

            if (_view.phase == RoundPhase.WaitingDiscard && _view.currentSeat == MySeat)
            {
                WsClient.Instance.Send(new DiscardMsg { card = CardDto.From(card) });
            }
            else if (_view.phase == RoundPhase.WaitingPongDiscard && _view.actorSeat == MySeat)
            {
                WsClient.Instance.Send(new PongDiscardMsg { card = CardDto.From(card) });
            }
        }

        private void OnNaturalPong()
        {
            if (_view is not { canNaturalPong: true })
            {
                return;
            }

            if (_view.phase == RoundPhase.WaitingPongDiscard)
            {
                WsClient.Instance.Send(new NaturalPongMsg { hasDiscard = false }); // 뽕 후 남은 3장 자연뽕 → 손 소진
                return;
            }

            var number = _view.naturalPongNumber;
            if (_view.myHand.All(c => c.number == number))
            {
                WsClient.Instance.Send(new NaturalPongMsg { hasDiscard = false }); // 손 전부 같은 숫자 → 즉시 손 털기
                return;
            }

            // 선언 즉시 3장 내려놓기(로컬·일반 뽕과 동일한 흐름) — 서버 확정 전 낙관적 연출
            var laid = _view.myHand.Select(c => c.ToCard()).Where(c => c.Number == number).Take(3).ToList();
            _pendingLaid.Clear();
            _pendingLaid.AddRange(laid);
            _table.GroupFx(MySeat, laid);
            _naturalLaidLocally = true;
            _table.PongFx($"{Nicknames[MySeat]}\n{number}자연뽕!");

            _naturalSelecting = true;
            Render();
            _table.SetPrompt("버릴 카드를 클릭하세요");
        }

        private System.Collections.IEnumerator ApplyDrawAfterShuffle(DrewCardMsg drew)
        {
            yield return new WaitForSeconds(0.9f); // ShuffleFx 수렴 시간
            SetLog($"재셔플 반영 — P{drew.seat} 드로우");
            _table.DrawFx(drew.seat);
            ApplyView(drew.view);
        }

        private void OnPass() =>
            WsClient.Instance.Send(_view != null && _view.phase == RoundPhase.WaitingStop
                ? new ContinueTurnMsg()
                : (object)new PongPassMsg());

        private void ReturnToRoom()
        {
            WsClient.Instance.Paused = true; // FriendRoom.Start가 구독 후 해제
            var friend = new GameObject("FriendRoom").AddComponent<FriendRoomBootstrap>();
            friend.ResumeInRoom(_pendingRoom);
            Destroy(_table.CanvasGo);
            Destroy(gameObject);
        }

        private static void SetLog(string message) => Debug.Log($"[BBONG-NET] {message}");
    }
}
