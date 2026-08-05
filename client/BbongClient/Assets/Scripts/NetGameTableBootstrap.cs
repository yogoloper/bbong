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

        private GameTableView _table;
        private RoundView _view;
        private readonly List<Card> _pendingLaid = new(); // 내 뽕/자연뽕 내려놓기(서버 반영 전 손패 숨김)
        private bool _naturalSelecting;                    // 자연뽕 추가 버림 선택 중
        private bool _naturalLaidLocally;                  // 자연뽕 선언 즉시 내려놓기 연출(서버 확정 전)
        private RoomUpdateMsg _pendingRoom;                // 세트 종료 후 대기실 복귀 대기
        private readonly List<int[]> _roundHistory = new(); // 게임(세트) 내 판별 점수
        private Button _roomBtn;

        private void Start()
        {
            _table = gameObject.AddComponent<GameTableView>();
            _table.MySeat = MySeat;
            _table.PlayerCount = PlayerCount;
            _table.Nicknames = Nicknames;
            _table.Build();
            _roomBtn = _table.AddBarButton("대기실로", ReturnToRoom);

            _table.CardClicked += OnCardClicked;
            _table.StopClicked += () => WsClient.Instance.Send(new StopDeclareMsg());
            _table.MeldClicked += () => WsClient.Instance.Send(new MeldDeclareMsg());
            _table.NaturalPongClicked += OnNaturalPong;
            _table.PongClicked += () => WsClient.Instance.Send(new PongDeclareMsg());
            _table.PassClicked += OnPass;

            WsClient.Instance.OnMessage += HandleMessage;
            WsClient.Instance.OnClosed += HandleClosed;
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
                        _table.KeepGroupsAndTopDiscard();
                        _table.ShuffleFx();
                    }

                    _table.PlayDrawSfx();
                    ApplyView(drew.view);
                    break;

                case ServerMessageType.Discarded:
                    var discarded = JsonUtility.FromJson<DiscardedMsg>(json);
                    _table.AddDiscard(discarded.card.ToCard());
                    if (discarded.seat == MySeat)
                    {
                        _pendingLaid.Clear(); // 내 뽕 추가 버림까지 서버 반영 완료
                    }

                    _table.PlayDiscardSfx();
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
                    _table.ShowCallout($"{Nicknames[stop.seat]}\n{(stop.bagaji ? "바가지!" : "스톱!")}",
                        stop.bagaji ? new Color(1f, 0.4f, 0.35f) : new Color(0.55f, 0.85f, 1f));
                    break;

                case ServerMessageType.BotTookOver:
                    // 이탈/무응답 좌석을 봇이 이어받음(§9-4). 닉네임 "(봇)" 갱신 — 점수판/콜아웃에 반영.
                    var bot = JsonUtility.FromJson<BotTookOverMsg>(json);
                    Nicknames[bot.seat] = bot.nickname;
                    _table.ShowCallout($"{bot.nickname}\n자리 교대");
                    break;

                case ServerMessageType.MeldDeclared:
                    var meld = JsonUtility.FromJson<MeldDeclaredMsg>(json);
                    _table.PongFx($"{Nicknames[meld.seat]}\n{GameTableView.MeldKorean(meld.meldType)}!");
                    break;

                case ServerMessageType.RoundEnded:
                    var ended = JsonUtility.FromJson<RoundEndedMsg>(json);
                    _pendingLaid.Clear();
                    _table.SetEndReason(ended.reason);
                    _roundHistory.Add(ended.scores);
                    _table.ShowScorePopup($"{ended.roundIndex + 1}판 종료", ended.cumulativeDebts, _roundHistory, fadeOut: true);
                    ApplyView(ended.view);
                    break;

                case ServerMessageType.SetEnded:
                    var set = JsonUtility.FromJson<SetEndedMsg>(json);
                    _pendingLaid.Clear();
                    _table.SetEndReason(set.reason);
                    _roundHistory.Add(set.scores);
                    var winners = string.Join(", ", set.winnerSeats.Select(s => Nicknames[s]));
                    _table.ShowScorePopup($"게임 종료 — 1등 {winners}", set.cumulativeDebts, _roundHistory, fadeOut: false);
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

                    _table.SetPrompt(error.message);
                    break;
            }
        }

        private void HandleClosed(string reason) => LeaveToLobby(reason);

        private void ApplyView(RoundView view)
        {
            _view = view;
            Render();
        }

        private void Render()
        {
            _table.Render(_view, _pendingLaid, _naturalSelecting);
            _roomBtn.gameObject.SetActive(_pendingRoom != null);
        }

        private void OnLaid(int seat, int number, CardDto[] laid, string suffix)
        {
            var cards = laid.Select(c => c.ToCard()).ToList();
            _table.AddGroup(cards);
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
            _table.AddGroup(laid);
            _naturalLaidLocally = true;
            _table.PongFx($"{Nicknames[MySeat]}\n{number}자연뽕!");

            _naturalSelecting = true;
            Render();
            _table.SetPrompt($"자연뽕! {number} 외 버릴 카드 클릭");
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
