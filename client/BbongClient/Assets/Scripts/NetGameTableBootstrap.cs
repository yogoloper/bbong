using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Online;
using BbongCore.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 온라인(친구방) 게임 테이블 — 서버 권위. 서버가 보내는 좌석별 RoundView를 렌더하고
    /// 입력 의도만 전송한다(낙관적 반영 없음). 연출(타임라인/콜아웃/사운드)은 이벤트로 구축.
    /// FriendRoomBootstrap이 gameStarted에서 생성·필드 주입.
    /// </summary>
    public sealed class NetGameTableBootstrap : MonoBehaviour
    {
        public int MySeat { get; set; }

        public int PlayerCount { get; set; }

        public string[] Nicknames { get; set; }

        private RoundView _view;
        private readonly List<(List<Card> cards, bool group, Vector2 pos, float rot)> _timeline = new();
        private int _timelineShown;
        private readonly List<Card> _pendingLaid = new(); // 내 뽕/자연뽕 내려놓기(서버 반영 전 손패 숨김)
        private bool _naturalSelecting;                    // 자연뽕 추가 버림 선택 중
        private bool _naturalLaidLocally;                  // 자연뽕 선언 즉시 내려놓기 연출(서버 확정 전)
        private RoomUpdateMsg _pendingRoom;                // 세트 종료 후 대기실 복귀 대기

        private Font _font;
        private GameObject _canvasGo;
        private Transform _seatsArea;
        private Transform _discardRow;
        private Transform _handRow;
        private Text _prompt;
        private Text _endReason;
        private GameObject _scorePopup;
        private Text _scoreTitle;
        private Transform _scoreGrid;
        private CanvasGroup _scorePopupGroup;
        private Coroutine _scoreFade;
        private readonly List<int[]> _roundHistory = new(); // 게임(세트) 내 판별 점수
        private Button _stopBtn, _pongBtn, _passBtn, _naturalBtn, _meldBtn, _roomBtn;
        private Text _callout;
        private CanvasGroup _calloutGroup;
        private Coroutine _calloutFx;
        private Coroutine _pongCountdown;

        private AudioSource _audio;
        private AudioClip _sfxDraw, _sfxDiscard, _sfxPong, _sfxStop;

        private void Start()
        {
            _font = Resources.Load<Font>("Fonts/Pretendard-SemiBold")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            UiKit.EnsureEventSystem();
            BuildUi();
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
                    _timeline.Clear();
                    _timelineShown = 0;
                    _pendingLaid.Clear();
                    _naturalSelecting = false;
                    _naturalLaidLocally = false;
                    _endReason.text = "";
                    HideScorePopup();
                    ApplyView(round.view);
                    break;

                case ServerMessageType.TurnBegan:
                    ApplyView(JsonUtility.FromJson<TurnBeganMsg>(json).view);
                    break;

                case ServerMessageType.DrewCard:
                    var drew = JsonUtility.FromJson<DrewCardMsg>(json);
                    if (drew.reshuffled)
                    {
                        KeepGroupsAndTopDiscard();
                        ShowCallout("더미 셔플!");
                    }

                    _audio.PlayOneShot(_sfxDraw, 0.5f);
                    ApplyView(drew.view);
                    break;

                case ServerMessageType.Discarded:
                    var discarded = JsonUtility.FromJson<DiscardedMsg>(json);
                    AddDiscard(discarded.card.ToCard());
                    if (discarded.seat == MySeat)
                    {
                        _pendingLaid.Clear(); // 내 뽕 추가 버림까지 서버 반영 완료
                    }

                    _audio.PlayOneShot(_sfxDiscard, 0.5f);
                    ApplyView(discarded.view);
                    break;

                case ServerMessageType.PongWindowOpened:
                    var openedMsg = JsonUtility.FromJson<PongWindowOpenedMsg>(json);
                    ApplyView(openedMsg.view);
                    StartPongCountdown(openedMsg.seconds);
                    break;

                case ServerMessageType.PongWindowClosed:
                    StopPongCountdown();
                    ApplyView(JsonUtility.FromJson<PongWindowClosedMsg>(json).view);
                    break;

                case ServerMessageType.Ponged:
                    var ponged = JsonUtility.FromJson<PongedMsg>(json);
                    StopPongCountdown();
                    OnLaid(ponged.seat, ponged.number, ponged.laid, "뽕!");
                    ApplyView(ponged.view);
                    break;

                case ServerMessageType.NaturalPonged:
                    var natural = JsonUtility.FromJson<NaturalPongedMsg>(json);
                    if (natural.seat == MySeat && _naturalLaidLocally)
                    {
                        // 선언 순간 이미 내려놓기 연출을 했으므로 서버 확정 구성으로 치환만(콜아웃/효과음 중복 방지)
                        _naturalLaidLocally = false;
                        _timeline.RemoveAt(_timeline.Count - 1);
                        AddGroup(natural.laid.Select(c => c.ToCard()));
                        _timelineShown = _timeline.Count;
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
                    _audio.PlayOneShot(_sfxStop, 0.6f);
                    ShowCallout($"{Nicknames[stop.seat]}\n{(stop.bagaji ? "바가지!" : "스톱!")}");
                    break;

                case ServerMessageType.MeldDeclared:
                    var meld = JsonUtility.FromJson<MeldDeclaredMsg>(json);
                    _audio.PlayOneShot(_sfxPong, 0.8f);
                    ShowCallout($"{Nicknames[meld.seat]}\n{MeldDisplay(meld.meldType)}!"); // 로컬과 동일 문구
                    break;

                case ServerMessageType.RoundEnded:
                    var ended = JsonUtility.FromJson<RoundEndedMsg>(json);
                    _pendingLaid.Clear();
                    _endReason.text = ended.reason;
                    _roundHistory.Add(ended.scores);
                    ShowScorePopup($"{ended.roundIndex + 1}판 종료", ended.cumulativeDebts, fadeOut: true);
                    ApplyView(ended.view);
                    break;

                case ServerMessageType.SetEnded:
                    var set = JsonUtility.FromJson<SetEndedMsg>(json);
                    _pendingLaid.Clear();
                    _endReason.text = set.reason;
                    _roundHistory.Add(set.scores);
                    var winners = string.Join(", ", set.winnerSeats.Select(s => Nicknames[s]));
                    ShowScorePopup($"게임 종료 — 1등 {winners}", set.cumulativeDebts, fadeOut: false);
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
                        _timeline.RemoveAt(_timeline.Count - 1);
                        _timelineShown = Mathf.Min(_timelineShown, _timeline.Count);
                        _pendingLaid.Clear();
                        Render();
                    }

                    _prompt.text = error.message;
                    break;
            }
        }

        private void HandleClosed(string reason) => LeaveToLobby(reason);

        private void ApplyView(RoundView view)
        {
            _view = view;
            Render();
        }

        /// <summary>DTO의 enum 문자열 → 한글 족보명(코어 MeldNames 단일 출처).</summary>
        private static string MeldDisplay(string meldType) =>
            System.Enum.TryParse<MeldType>(meldType, out var type) ? MeldNames.Korean(type) : meldType;

        private void OnLaid(int seat, int number, CardDto[] laid, string suffix)
        {
            var cards = laid.Select(c => c.ToCard()).ToList();
            AddGroup(cards);
            if (seat == MySeat)
            {
                _pendingLaid.Clear();
                _pendingLaid.AddRange(cards);
            }

            _audio.PlayOneShot(_sfxPong, 0.8f);
            ShowCallout($"{Nicknames[seat]}\n{number}{suffix}");
        }

        /// <summary>재셔플: 고정 패는 남기고 단일 버림은 맨 위 1장만 유지(로컬과 동일 연출).</summary>
        private void KeepGroupsAndTopDiscard()
        {
            var kept = _timeline.Where(e => e.group).ToList();
            var lastSingle = _timeline.LastOrDefault(e => !e.group);
            if (lastSingle.cards != null)
            {
                kept.Add(lastSingle);
            }

            _timeline.Clear();
            _timeline.AddRange(kept);
            _timelineShown = _timeline.Count;
        }

        private void LeaveToLobby(string reason)
        {
            SetLog($"방 종료: {reason}");
            UiKit.GoTo<MainLobbyBootstrap>(_canvasGo, this);
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
            AddGroup(laid);
            _naturalLaidLocally = true;
            _audio.PlayOneShot(_sfxPong, 0.8f);
            ShowCallout($"{Nicknames[MySeat]}\n{number}자연뽕!");

            _naturalSelecting = true;
            _prompt.text = $"자연뽕! {number} 외 버릴 카드 클릭";
            Render();
        }

        // ── 렌더 ──

        private void Render()
        {
            if (_view == null)
            {
                return;
            }

            RenderSeats();
            RenderHand();
            RenderDiscard();
            RenderPrompt();
            RenderButtons();
        }

        private void RenderSeats()
        {
            foreach (Transform child in _seatsArea)
            {
                Destroy(child.gameObject);
            }

            var center = new Vector2(0.50f, 0.58f);
            var radius = new Vector2(0.40f, 0.30f);

            // 뽕 창/턴 전환 간격 동안은 아무도 포커싱하지 않음(로컬과 동일 연출)
            var focusSeat = _view.phase == RoundPhase.PongWindow || _view.phase == RoundPhase.TurnGap
                ? -1 : _view.currentSeat;

            foreach (var seatView in _view.seats)
            {
                // 내 좌석이 항상 아래로 오도록 회전 배치
                var displayIndex = (seatView.seat - MySeat + PlayerCount) % PlayerCount;
                var angle = (-90f + displayIndex * 360f / PlayerCount) * Mathf.Deg2Rad;
                var anchor = center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y);

                var mine = seatView.seat == MySeat;
                var highlight = focusSeat == seatView.seat;
                var panel = UiKit.CreatePanel(_seatsArea, highlight ? new Color(0.9f, 0.8f, 0.2f, 0.55f) : new Color(0, 0, 0, 0.35f));
                var rt = panel.rectTransform;
                rt.anchorMin = rt.anchorMax = anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(250f, mine ? 80f : 150f);

                var name = mine ? $"{seatView.nickname} (나)" : seatView.nickname;
                var label = UiKit.CreateText(panel.transform, $"{name}\n빚: {seatView.cumulativeDebt}", 24,
                    TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
                if (mine)
                {
                    continue;
                }

                UiKit.Anchor(label.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 1f));

                // 상대 손패 수: 뒤집힌 카드
                const float bw = 36f, bh = 50f, step = bw + 3f;
                var total = (seatView.handCount - 1) * step + bw;
                for (var j = 0; j < seatView.handCount; j++)
                {
                    var back = UiKit.CreatePanel(panel.transform, Color.white);
                    back.sprite = UiArt.CardBack;
                    back.type = Image.Type.Simple;
                    var brt = back.rectTransform;
                    brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.31f);
                    brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.sizeDelta = new Vector2(bw, bh);
                    brt.anchoredPosition = new Vector2(-total / 2f + bw / 2f + j * step, 0f);
                }
            }
        }

        private void RenderHand()
        {
            foreach (Transform child in _handRow)
            {
                Destroy(child.gameObject);
            }

            var cards = _view.myHand.Select(c => c.ToCard()).Where(c => !_pendingLaid.Contains(c));
            foreach (var card in TableArt.Sorted(cards))
            {
                var go = TableArt.CreateCardFace(_handRow, card, 130, 200, _font);
                var captured = card;
                go.AddComponent<Button>().onClick.AddListener(() => OnCardClicked(captured));
            }
        }

        private void RenderDiscard()
        {
            foreach (Transform child in _discardRow)
            {
                Destroy(child.gameObject);
            }

            const float w = 120f, h = 180f;
            var heapAnchor = new Vector2(0.5f, 0.45f);
            GameObject last = null;
            foreach (var (cards, group, pos, rot) in _timeline)
            {
                if (group)
                {
                    for (var j = 0; j < cards.Count; j++)
                    {
                        var fan = j - (cards.Count - 1) / 2f;
                        last = PlaceCard(cards[j], w, h, heapAnchor, pos + new Vector2(fan * 38f, j * 3f), rot + fan * 9f);
                    }
                }
                else
                {
                    last = PlaceCard(cards[0], w, h, heapAnchor, pos, rot);
                }
            }

            if (last != null && _timeline.Count > _timelineShown)
            {
                StartCoroutine(ScalePop(last.transform));
            }

            _timelineShown = _timeline.Count;
        }

        private GameObject PlaceCard(Card card, float w, float h, Vector2 anchor, Vector2 offset, float rot)
        {
            var face = TableArt.CreateCardFace(_discardRow, card, w, h, _font);
            var rt = (RectTransform)face.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = offset;
            rt.localRotation = Quaternion.Euler(0, 0, rot);
            return face;
        }

        private void RenderPrompt()
        {
            if (_naturalSelecting)
            {
                return; // OnNaturalPong이 설정한 안내 유지
            }

            _prompt.text = _view.phase switch
            {
                RoundPhase.WaitingStop when _view.currentSeat == MySeat => "스톱? 또는 계속",
                RoundPhase.WaitingDiscard when _view.currentSeat == MySeat => "버릴 카드를 클릭하세요.",
                RoundPhase.WaitingPongDiscard when _view.actorSeat == MySeat => $"뽕! {_view.pongNumber} 외 버릴 카드 클릭",
                RoundPhase.PongWindow when _view.canPong => $"{_view.pongNumber} 뽕 기회!",
                _ => ""
            };
        }

        private void RenderButtons()
        {
            _stopBtn.gameObject.SetActive(_view.phase == RoundPhase.WaitingStop && _view.canStop);
            _passBtn.gameObject.SetActive(
                (_view.phase == RoundPhase.WaitingStop && _view.currentSeat == MySeat)
                || (_view.phase == RoundPhase.PongWindow && _view.canPong));
            SetButtonLabel(_passBtn, _view.phase == RoundPhase.WaitingStop ? "계속" : "패스");
            _pongBtn.gameObject.SetActive(_view.phase == RoundPhase.PongWindow && _view.canPong);
            _meldBtn.gameObject.SetActive(_view.canMeld);
            _naturalBtn.gameObject.SetActive(_view.canNaturalPong && !_naturalSelecting);
            _roomBtn.gameObject.SetActive(_pendingRoom != null);
        }

        // ── UI 생성(로컬 테이블 축약판) ──

        private void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = canvasGo;
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = canvasGo.transform;

            var felt = UiKit.CreatePanel(root, Color.white);
            felt.sprite = UiArt.Backdrop;
            UiKit.Stretch(felt.rectTransform);

            var seatsGo = new GameObject("SeatsArea", typeof(RectTransform));
            seatsGo.transform.SetParent(root, false);
            _seatsArea = seatsGo.transform;
            UiKit.Stretch((RectTransform)_seatsArea);

            var discardGo = new GameObject("DiscardArea", typeof(RectTransform));
            discardGo.transform.SetParent(root, false);
            _discardRow = discardGo.transform;
            UiKit.Anchor((RectTransform)_discardRow, new Vector2(0.20f, 0.42f), new Vector2(0.80f, 0.72f));

            _endReason = UiKit.CreateText(root, "", 32, TextAnchor.MiddleCenter,
                new Vector2(0.25f, 0.325f), new Vector2(0.75f, 0.39f));
            _endReason.fontStyle = FontStyle.Bold;

            _prompt = UiKit.CreateText(root, "", 44, TextAnchor.MiddleCenter,
                new Vector2(0.20f, 0.335f), new Vector2(0.80f, 0.41f));
            _prompt.fontStyle = FontStyle.Bold;
            _prompt.color = new Color(1f, 0.92f, 0.4f);

            // 액션 버튼(손패 우측)
            var barGo = new GameObject("Buttons", typeof(RectTransform));
            barGo.transform.SetParent(root, false);
            UiKit.Anchor((RectTransform)barGo.transform, new Vector2(0.80f, 0.04f), new Vector2(0.995f, 0.21f));
            var layout = barGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            var bar = barGo.transform;

            _stopBtn = BarButton(bar, "스톱", () => WsClient.Instance.Send(new StopDeclareMsg()));
            _meldBtn = BarButton(bar, "족보 선언!", () => WsClient.Instance.Send(new MeldDeclareMsg()));
            _naturalBtn = BarButton(bar, "자연뽕", OnNaturalPong);
            _pongBtn = BarButton(bar, "뽕!", () => WsClient.Instance.Send(new PongDeclareMsg()));
            _passBtn = BarButton(bar, "패스", OnPass);
            _roomBtn = BarButton(bar, "대기실로", ReturnToRoom);

            // 내 손패
            var handGo = new GameObject("HandRow", typeof(RectTransform));
            handGo.transform.SetParent(root, false);
            UiKit.Anchor((RectTransform)handGo.transform, new Vector2(0f, 0.02f), new Vector2(1f, 0.225f));
            var handLayout = handGo.AddComponent<HorizontalLayoutGroup>();
            handLayout.spacing = 12;
            handLayout.childAlignment = TextAnchor.MiddleCenter;
            // 카드 크기는 CreateCardFace의 LayoutElement.preferred*로 결정 — childControl은 켜두고 확장만 끔(로컬 CreateRow와 동일)
            handLayout.childForceExpandWidth = false;
            handLayout.childForceExpandHeight = false;
            _handRow = handGo.transform;

            // 판 종료 점수표(로컬 테이블과 동일 — 헤더+판별+합계 표). 정중앙, 크기는 ShowScorePopup에서 설정.
            var popupBg = UiKit.CreatePanel(root, new Color(0.05f, 0.05f, 0.08f, 0.97f));
            _scorePopup = popupBg.gameObject;
            var popupRt = popupBg.rectTransform;
            popupRt.anchorMin = popupRt.anchorMax = new Vector2(0.5f, 0.60f);
            popupRt.pivot = new Vector2(0.5f, 0.5f);
            popupBg.raycastTarget = false;
            _scorePopupGroup = _scorePopup.AddComponent<CanvasGroup>();
            _scorePopupGroup.blocksRaycasts = false;
            _scorePopupGroup.interactable = false;

            _scoreTitle = UiKit.CreateText(popupBg.transform, "", 36, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            _scoreTitle.fontStyle = FontStyle.Bold;
            FitText(_scoreTitle, 20, 36);
            var titleRt = _scoreTitle.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(0f, -80f);
            titleRt.offsetMax = Vector2.zero;

            var gridGo = new GameObject("ScoreGrid", typeof(RectTransform));
            gridGo.transform.SetParent(popupBg.transform, false);
            _scoreGrid = gridGo.transform;
            var gridRt = (RectTransform)_scoreGrid;
            gridRt.anchorMin = Vector2.zero;
            gridRt.anchorMax = Vector2.one;
            gridRt.offsetMin = new Vector2(20f, 16f);
            gridRt.offsetMax = new Vector2(-20f, -80f);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(6, 6);
            grid.cellSize = new Vector2(180, 48);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = PlayerCount + 1;

            _scorePopup.SetActive(false);

            // 콜아웃
            _callout = UiKit.CreateText(root, "", 100, TextAnchor.MiddleCenter,
                new Vector2(0.25f, 0.43f), new Vector2(0.75f, 0.62f));
            _callout.fontStyle = FontStyle.Bold;
            _callout.color = new Color(1f, 0.92f, 0.35f);
            _callout.raycastTarget = false;
            TableArt.AddOutline(_callout);
            _calloutGroup = _callout.gameObject.AddComponent<CanvasGroup>();
            _calloutGroup.alpha = 0f;
            _calloutGroup.blocksRaycasts = false;

            _audio = canvasGo.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _sfxDraw = TableArt.Tone("draw", 880f, 0.06f, 24f);
            _sfxDiscard = TableArt.Tone("discard", 300f, 0.12f, 16f);
            _sfxPong = TableArt.Noise("pong", 0.16f, 42f);
            _sfxStop = TableArt.Tone("stop", 520f, 0.28f, 7f);

            foreach (var btn in new[] { _stopBtn, _pongBtn, _passBtn, _naturalBtn, _meldBtn, _roomBtn })
            {
                btn.gameObject.SetActive(false);
            }
        }

        private Button BarButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick) =>
            UiKit.CreateButton(parent, label, Vector2.zero, Vector2.one, onClick, 36);

        private void OnPass() =>
            WsClient.Instance.Send(_view != null && _view.phase == RoundPhase.WaitingStop
                ? new ContinueTurnMsg()
                : (object)new PongPassMsg());

        private void ReturnToRoom()
        {
            WsClient.Instance.Paused = true; // FriendRoom.Start가 구독 후 해제
            var friend = new GameObject("FriendRoom").AddComponent<FriendRoomBootstrap>();
            friend.ResumeInRoom(_pendingRoom);
            Destroy(_canvasGo);
            Destroy(gameObject);
        }

        // ── 연출 ──

        /// <summary>중앙 점수표(로컬 테이블과 동일): 헤더 + 판별 점수 행 + 합계 행. 판 종료는 잠시 후 페이드아웃.</summary>
        private void ShowScorePopup(string title, int[] debts, bool fadeOut)
        {
            _scoreTitle.text = title;

            var cols = PlayerCount + 1;
            var rows = _roundHistory.Count + 2; // 헤더 + 판별 + 합계
            ((RectTransform)_scorePopup.transform).sizeDelta = new Vector2(
                cols * 180f + (cols - 1) * 6f + 40f,
                rows * 48f + (rows - 1) * 6f + 80f + 32f);

            foreach (Transform child in _scoreGrid)
            {
                Destroy(child.gameObject);
            }

            AddCell("판수", true);
            for (var s = 0; s < PlayerCount; s++)
            {
                AddCell($"{Nicknames[s]}{(s == MySeat ? "*" : "")}", true, 22, fit: true);
            }

            for (var r = 0; r < _roundHistory.Count; r++)
            {
                AddCell($"{r + 1}", true);
                for (var s = 0; s < PlayerCount; s++)
                {
                    AddCell($"{_roundHistory[r][s]}", false);
                }
            }

            AddCell("계", true);
            for (var s = 0; s < PlayerCount; s++)
            {
                AddCell($"{debts[s]}", true);
            }

            _scorePopup.SetActive(true);
            _scorePopup.transform.SetAsLastSibling();
            _scorePopupGroup.alpha = 1f;

            if (_scoreFade != null)
            {
                StopCoroutine(_scoreFade);
                _scoreFade = null;
            }

            // 판 종료만 페이드(다음 판은 서버가 자동 시작). 게임 종료는 대기실 이동 전까지 유지.
            if (fadeOut)
            {
                _scoreFade = StartCoroutine(FadeScorePopup());
            }
        }

        private void HideScorePopup()
        {
            if (_scoreFade != null)
            {
                StopCoroutine(_scoreFade);
                _scoreFade = null;
            }

            _scorePopup.SetActive(false);
        }

        private void AddCell(string text, bool emphasize, int size = 30, bool fit = false)
        {
            var t = UiKit.CreateText(_scoreGrid, text, size, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            t.color = emphasize ? new Color(1f, 0.9f, 0.4f) : Color.white;
            if (emphasize)
            {
                t.fontStyle = FontStyle.Bold;
            }

            if (fit)
            {
                FitText(t, 12, size);
            }
        }

        /// <summary>긴 닉네임 대응: 영역에 맞게 글씨 자동 축소.</summary>
        private static void FitText(Text t, int min, int max)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = min;
            t.resizeTextMaxSize = max;
        }

        private IEnumerator FadeScorePopup()
        {
            yield return new WaitForSeconds(5f); // 충분히 보이도록(서버 다음 판 8초 전)
            for (var t = 0f; t < 1f; t += Time.deltaTime / 1.2f)
            {
                _scorePopupGroup.alpha = 1f - t;
                yield return null;
            }

            _scorePopup.SetActive(false);
        }

        private void ShowCallout(string message)
        {
            _callout.text = message;
            if (_calloutFx != null)
            {
                StopCoroutine(_calloutFx);
            }

            _calloutFx = StartCoroutine(CalloutFx());
        }

        private IEnumerator CalloutFx()
        {
            _calloutGroup.alpha = 1f;
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.15f)
            {
                _callout.transform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, t);
                yield return null;
            }

            _callout.transform.localScale = Vector3.one;
            yield return new WaitForSeconds(0.7f);
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                _calloutGroup.alpha = 1f - t;
                yield return null;
            }

            _calloutGroup.alpha = 0f;
        }

        private IEnumerator ScalePop(Transform target)
        {
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.15f)
            {
                if (target == null)
                {
                    yield break;
                }

                target.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, t);
                yield return null;
            }

            if (target != null)
            {
                target.localScale = Vector3.one;
            }
        }

        private void StartPongCountdown(int seconds)
        {
            StopPongCountdown();
            if (_view is { canPong: true })
            {
                _pongCountdown = StartCoroutine(PongCountdown(seconds));
            }
        }

        private void StopPongCountdown()
        {
            if (_pongCountdown != null)
            {
                StopCoroutine(_pongCountdown);
                _pongCountdown = null;
                SetButtonLabel(_pongBtn, "뽕!");
            }
        }

        private IEnumerator PongCountdown(int seconds)
        {
            for (var t = seconds; t > 0; t--)
            {
                SetButtonLabel(_pongBtn, $"뽕! ({t})");
                yield return new WaitForSeconds(1f);
            }
        }

        private void AddDiscard(Card card) => _timeline.Add((new List<Card> { card }, false,
            new Vector2(TableArt.Tri(150f), TableArt.Tri(50f)), TableArt.Tri(28f)));

        private void AddGroup(IEnumerable<Card> cards) => _timeline.Add((TableArt.Sorted(cards), true,
            new Vector2(TableArt.Tri(120f), TableArt.Tri(45f)), TableArt.Tri(16f)));

        private static void SetButtonLabel(Button button, string label) =>
            button.GetComponentInChildren<Text>().text = label;

        private static void SetLog(string message) => Debug.Log($"[BBONG-NET] {message}");
    }
}
