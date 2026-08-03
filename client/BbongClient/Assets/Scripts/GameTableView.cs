using System;
using System.Collections;
using System.Collections.Generic;
using BbongCore.Cards;
using BbongCore.Online;
using BbongCore.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 게임 테이블 공용 뷰(단일 틀): 캔버스/좌석/손패/버림 타임라인/멘트/점수판/콜아웃/효과음을 전부 소유.
    /// 연습·친구방·일반게임 모든 모드는 이 뷰 하나를 RoundView로 렌더하고, 입력은 이벤트로 받는다.
    /// 모드별 별도 테이블 UI 금지 — 문구/연출 수정은 여기 한 곳에서.
    /// </summary>
    public sealed class GameTableView : MonoBehaviour
    {
        public int MySeat { get; set; }

        public int PlayerCount { get; set; }

        public string[] Nicknames { get; set; }

        public event Action<Card> CardClicked;

        public event Action StopClicked;

        public event Action MeldClicked;

        public event Action NaturalPongClicked;

        public event Action PongClicked;

        public event Action PassClicked;

        public GameObject CanvasGo => _canvasGo;

        private readonly List<(List<Card> cards, bool group, Vector2 pos, float rot)> _timeline = new();
        private int _timelineShown;

        private Font _font;
        private GameObject _canvasGo;
        private Transform _seatsArea;
        private Transform _discardRow;
        private Transform _handRow;
        private Transform _buttonBar;
        private Text _prompt;
        private Text _endReason;
        private GameObject _scorePopup;
        private Text _scoreTitle;
        private Transform _scoreGrid;
        private CanvasGroup _scorePopupGroup;
        private Coroutine _scoreFade;
        private Button _stopBtn, _pongBtn, _passBtn, _naturalBtn, _meldBtn;
        private Text _callout;
        private CanvasGroup _calloutGroup;
        private Coroutine _calloutFx;
        private Coroutine _pongCountdown;

        private AudioSource _audio;
        private AudioClip _sfxDraw, _sfxDiscard, _sfxPong, _sfxStop, _sfxShuffle;
        private Image _flash;
        private List<Card> _meldSet; // 족보 완성 시 6장(버림 비우고 표시)

        // ── UI 생성 ──

        public void Build()
        {
            _font = UiKit.Font;
            UiKit.EnsureEventSystem();

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

            // 액션 버튼(손패 우측) — 공통 5개. 모드 전용 버튼은 AddBarButton으로 추가.
            var barGo = new GameObject("Buttons", typeof(RectTransform));
            barGo.transform.SetParent(root, false);
            UiKit.Anchor((RectTransform)barGo.transform, new Vector2(0.80f, 0.04f), new Vector2(0.995f, 0.21f));
            var layout = barGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            _buttonBar = barGo.transform;

            _stopBtn = AddBarButton("스톱", () => StopClicked?.Invoke());
            _meldBtn = AddBarButton("족보 선언!", () => MeldClicked?.Invoke());
            _naturalBtn = AddBarButton("자연뽕", () => NaturalPongClicked?.Invoke());
            _pongBtn = AddBarButton("뽕!", () => PongClicked?.Invoke());
            _passBtn = AddBarButton("패스", () => PassClicked?.Invoke());

            // 내 손패
            var handGo = new GameObject("HandRow", typeof(RectTransform));
            handGo.transform.SetParent(root, false);
            UiKit.Anchor((RectTransform)handGo.transform, new Vector2(0f, 0.02f), new Vector2(1f, 0.225f));
            var handLayout = handGo.AddComponent<HorizontalLayoutGroup>();
            handLayout.spacing = 12;
            handLayout.childAlignment = TextAnchor.MiddleCenter;
            // 카드 크기는 CreateCardFace의 LayoutElement.preferred*로 결정 — childControl은 켜두고 확장만 끔
            handLayout.childForceExpandWidth = false;
            handLayout.childForceExpandHeight = false;
            _handRow = handGo.transform;

            // 판 종료 점수표(헤더+판별+합계 표). 정중앙, 크기는 ShowScorePopup에서 설정.
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

            // 전체 화면 플래시(연출용, 클릭 막지 않음)
            _flash = UiKit.CreatePanel(root, new Color(1, 1, 1, 0));
            UiKit.Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;
            _flash.transform.SetAsLastSibling();

            _audio = canvasGo.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _sfxDraw = TableArt.Tone("draw", 880f, 0.06f, 24f);
            _sfxDiscard = TableArt.Tone("discard", 300f, 0.12f, 16f);
            _sfxPong = TableArt.Noise("pong", 0.16f, 42f);
            _sfxStop = TableArt.Tone("stop", 520f, 0.28f, 7f);
            _sfxShuffle = TableArt.Noise("shuffle", 0.35f, 10f);
        }

        /// <summary>버튼 바에 모드 전용 버튼 추가(대기실로/다음 판/로비로 등). 생성 직후 숨김.</summary>
        public Button AddBarButton(string label, UnityEngine.Events.UnityAction onClick)
        {
            var btn = UiKit.CreateButton(_buttonBar, label, Vector2.zero, Vector2.one, onClick, 36);
            btn.gameObject.SetActive(false);
            return btn;
        }

        public static void SetButtonLabel(Button button, string label) =>
            button.GetComponentInChildren<Text>().text = label;

        // ── 렌더 ──

        /// <summary>
        /// 테이블 전체 렌더. hiddenHandCards = 서버 확정 전 손에서 숨길 카드(뽕/자연뽕 내려놓기).
        /// naturalSelecting = 자연뽕 추가 버림 선택 중(안내 문구 유지 + 자연뽕 버튼 숨김).
        /// </summary>
        public void Render(RoundView view, ICollection<Card> hiddenHandCards = null, bool naturalSelecting = false)
        {
            if (view == null)
            {
                return;
            }

            RenderSeats(view);
            RenderHand(view, hiddenHandCards);
            RenderDiscard();
            RenderPrompt(view, naturalSelecting);
            RenderButtons(view, naturalSelecting);
        }

        public void SetPrompt(string text) => _prompt.text = text;

        public void SetEndReason(string text) => _endReason.text = text;

        private void RenderSeats(RoundView view)
        {
            foreach (Transform child in _seatsArea)
            {
                Destroy(child.gameObject);
            }

            var center = new Vector2(0.50f, 0.58f);
            var radius = new Vector2(0.40f, 0.30f);

            // 뽕 창/턴 전환 간격 동안은 아무도 포커싱하지 않음. 뽕 추가 버림은 선언자를 포커싱.
            var focusSeat = view.phase == RoundPhase.PongWindow || view.phase == RoundPhase.TurnGap ? -1
                : view.phase == RoundPhase.WaitingPongDiscard ? view.actorSeat
                : view.currentSeat;

            foreach (var seatView in view.seats)
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
                FitText(label, 16, 24);
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

        private void RenderHand(RoundView view, ICollection<Card> hidden)
        {
            foreach (Transform child in _handRow)
            {
                Destroy(child.gameObject);
            }

            var cards = new List<Card>();
            foreach (var dto in view.myHand)
            {
                var card = dto.ToCard();
                if (hidden == null || !hidden.Contains(card))
                {
                    cards.Add(card);
                }
            }

            foreach (var card in TableArt.Sorted(cards))
            {
                var go = TableArt.CreateCardFace(_handRow, card, 130, 200, _font);
                var captured = card;
                go.AddComponent<Button>().onClick.AddListener(() => CardClicked?.Invoke(captured));
            }
        }

        private void RenderDiscard()
        {
            foreach (Transform child in _discardRow)
            {
                Destroy(child.gameObject);
            }

            // 족보 완성: 버림 타임라인 대신 족보 6장만 영역 정중앙에 펼쳐 표시
            if (_meldSet != null)
            {
                for (var i = 0; i < _meldSet.Count; i++)
                {
                    var offset = (i - (_meldSet.Count - 1) / 2f) * 136f;
                    PlaceCard(_meldSet[i], 128, 192, new Vector2(0.5f, 0.5f), new Vector2(offset, 0f), 0f);
                }

                return;
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

            if (last != null)
            {
                HighlightTop(last);

                if (_timeline.Count > _timelineShown)
                {
                    StartCoroutine(ScalePop(last.transform));
                }
            }

            _timelineShown = _timeline.Count;
        }

        /// <summary>맨 위(마지막 버림) 카드 강조: 카드 바로 아래에 노란 헤일로를 깔아 테두리처럼 보이게.</summary>
        private void HighlightTop(GameObject top)
        {
            var topRt = (RectTransform)top.transform;
            var halo = new GameObject("TopHalo", typeof(RectTransform), typeof(Image));
            halo.transform.SetParent(_discardRow, false);

            var img = halo.GetComponent<Image>();
            img.sprite = TableArt.Halo;
            img.type = Image.Type.Sliced;
            img.color = new Color(1f, 0.92f, 0.3f, 0.95f);
            img.raycastTarget = false;

            var rt = (RectTransform)halo.transform;
            rt.anchorMin = rt.anchorMax = topRt.anchorMin;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = topRt.sizeDelta + new Vector2(14f, 14f);
            rt.anchoredPosition = topRt.anchoredPosition;
            rt.localRotation = topRt.localRotation;

            halo.transform.SetSiblingIndex(top.transform.GetSiblingIndex()); // 카드 바로 아래로
        }

        /// <summary>족보 완성 6장 표시(다음 판 시작 ClearTimeline까지 유지).</summary>
        public void ShowMeldSet(IEnumerable<Card> cards) => _meldSet = TableArt.Sorted(cards);

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

        private void RenderPrompt(RoundView view, bool naturalSelecting)
        {
            if (naturalSelecting)
            {
                return; // 자연뽕 안내(SetPrompt) 유지
            }

            _prompt.text = view.phase switch
            {
                RoundPhase.WaitingStop when view.currentSeat == MySeat => "스톱? 또는 계속",
                RoundPhase.WaitingDiscard when view.currentSeat == MySeat && view.canMeld =>
                    $"족보 완성! [{MeldKorean(view.meldType)} {view.meldScore}점] — 선언 또는 버리고 계속",
                RoundPhase.WaitingDiscard when view.currentSeat == MySeat && view.canNaturalPong =>
                    "버릴 카드를 클릭하세요 (또는 자연뽕)",
                RoundPhase.WaitingDiscard when view.currentSeat == MySeat => "버릴 카드를 클릭하세요.",
                RoundPhase.WaitingPongDiscard when view.actorSeat == MySeat => $"뽕! {view.pongNumber} 외 버릴 카드 클릭",
                RoundPhase.PongWindow when view.canPong => $"{view.pongNumber} 뽕 기회!",
                _ => ""
            };
        }

        /// <summary>DTO/뷰의 enum 문자열 → 한글 족보명(코어 MeldNames 단일 출처).</summary>
        public static string MeldKorean(string meldType) =>
            Enum.TryParse<MeldType>(meldType, out var type) ? MeldNames.Korean(type) : meldType;

        private void RenderButtons(RoundView view, bool naturalSelecting)
        {
            _stopBtn.gameObject.SetActive(view.phase == RoundPhase.WaitingStop && view.canStop);
            _passBtn.gameObject.SetActive(
                (view.phase == RoundPhase.WaitingStop && view.currentSeat == MySeat)
                || (view.phase == RoundPhase.PongWindow && view.canPong));
            SetButtonLabel(_passBtn, view.phase == RoundPhase.WaitingStop ? "계속" : "패스");
            _pongBtn.gameObject.SetActive(view.phase == RoundPhase.PongWindow && view.canPong);
            _meldBtn.gameObject.SetActive(view.canMeld);
            _naturalBtn.gameObject.SetActive(view.canNaturalPong && !naturalSelecting);
        }

        // ── 버림 타임라인 ──

        public void AddDiscard(Card card) => _timeline.Add((new List<Card> { card }, false,
            new Vector2(TableArt.Tri(150f), TableArt.Tri(50f)), TableArt.Tri(28f)));

        public void AddGroup(IEnumerable<Card> cards) => _timeline.Add((TableArt.Sorted(cards), true,
            new Vector2(TableArt.Tri(120f), TableArt.Tri(45f)), TableArt.Tri(16f)));

        /// <summary>마지막 그룹을 확정 카드 구성으로 치환(자리·회전 유지, 팝 연출 없음).</summary>
        public void ReplaceLastGroup(IEnumerable<Card> cards)
        {
            var (_, _, pos, rot) = _timeline[^1];
            _timeline[^1] = (TableArt.Sorted(cards), true, pos, rot);
            _timelineShown = _timeline.Count;
        }

        /// <summary>마지막 타임라인 항목 제거(낙관적 연출 원복).</summary>
        public void RemoveLastTimelineEntry()
        {
            _timeline.RemoveAt(_timeline.Count - 1);
            _timelineShown = Mathf.Min(_timelineShown, _timeline.Count);
        }

        /// <summary>재셔플: 고정 패는 남기고 단일 버림은 맨 위 1장만 유지.</summary>
        public void KeepGroupsAndTopDiscard()
        {
            var kept = new List<(List<Card> cards, bool group, Vector2 pos, float rot)>();
            (List<Card> cards, bool group, Vector2 pos, float rot) lastSingle = default;
            foreach (var e in _timeline)
            {
                if (e.group)
                {
                    kept.Add(e);
                }
                else
                {
                    lastSingle = e;
                }
            }

            if (lastSingle.cards != null)
            {
                kept.Add(lastSingle);
            }

            _timeline.Clear();
            _timeline.AddRange(kept);
            _timelineShown = _timeline.Count;
        }

        public void ClearTimeline()
        {
            _timeline.Clear();
            _timelineShown = 0;
            _meldSet = null;
        }

        // ── 효과음/연출 ──

        public void PlayDrawSfx() => _audio.PlayOneShot(_sfxDraw, 0.5f);

        public void PlayDiscardSfx() => _audio.PlayOneShot(_sfxDiscard, 0.5f);

        public void PlayPongSfx() => _audio.PlayOneShot(_sfxPong, 0.8f);

        public void PlayStopSfx() => _audio.PlayOneShot(_sfxStop, 0.6f);

        /// <summary>뽕/자연뽕/족보 공통 연출: 효과음 + 화면 플래시 + 콜아웃.</summary>
        public void PongFx(string callout)
        {
            PlayPongSfx();
            Flash(new Color(1f, 0.95f, 0.4f, 0.5f));
            ShowCallout(callout);
        }

        /// <summary>재셔플 연출: 콜아웃 + 플래시 + 셔플 효과음.</summary>
        public void ShuffleFx()
        {
            ShowCallout("더미 셔플!");
            Flash(new Color(1f, 1f, 1f, 0.3f));
            _audio.PlayOneShot(_sfxShuffle, 0.7f);
        }

        public void Flash(Color color)
        {
            _flash.color = color;
            StartCoroutine(FadeFlash());
        }

        private IEnumerator FadeFlash()
        {
            var start = _flash.color;
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
            {
                _flash.color = new Color(start.r, start.g, start.b, Mathf.Lerp(start.a, 0f, t));
                yield return null;
            }

            _flash.color = new Color(start.r, start.g, start.b, 0f);
        }

        // ── 콜아웃/점수판 ──

        public void ShowCallout(string message)
        {
            _callout.text = message;
            if (_calloutFx != null)
            {
                StopCoroutine(_calloutFx);
            }

            _calloutFx = StartCoroutine(CalloutFx());
        }

        /// <summary>중앙 점수표: 헤더 + 판별 점수 행 + 합계 행. fadeOut이면 잠시 후 사라지고 onFadedOut 호출.</summary>
        public void ShowScorePopup(string title, IReadOnlyList<int> debts, IReadOnlyList<int[]> roundHistory, bool fadeOut,
            Action onFadedOut = null)
        {
            _scoreTitle.text = title;

            var cols = PlayerCount + 1;
            var rows = roundHistory.Count + 2; // 헤더 + 판별 + 합계
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

            for (var r = 0; r < roundHistory.Count; r++)
            {
                AddCell($"{r + 1}", true);
                for (var s = 0; s < PlayerCount; s++)
                {
                    AddCell($"{roundHistory[r][s]}", false);
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

            if (fadeOut)
            {
                _scoreFade = StartCoroutine(FadeScorePopup(onFadedOut));
            }
        }

        public void HideScorePopup()
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

        private IEnumerator FadeScorePopup(Action onFadedOut)
        {
            yield return new WaitForSeconds(5f); // 충분히 보이도록(다음 판 시작 전)
            for (var t = 0f; t < 1f; t += Time.deltaTime / 1.2f)
            {
                _scorePopupGroup.alpha = 1f - t;
                yield return null;
            }

            _scorePopup.SetActive(false);
            onFadedOut?.Invoke();
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

        // ── 뽕 카운트다운 ──

        public void StartPongCountdown(int seconds)
        {
            StopPongCountdown();
            _pongCountdown = StartCoroutine(PongCountdown(seconds));
        }

        public void StopPongCountdown()
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
    }
}
