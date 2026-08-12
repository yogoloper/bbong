using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Config;
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

        /// <summary>나가기 확정(확인 모달에서 "나가기" 선택) — 실제 퇴장 처리는 드라이버 몫.</summary>
        public event Action ExitConfirmed;

        public GameObject CanvasGo => _canvasGo;

        /// <summary>턴 카운트다운 "(N)" 표시 여부 — 타이머 없는 화면(튜토리얼)은 끈다.</summary>
        public bool ShowTurnCountdown { get; set; } = true;

        /// <summary>나가기 확인 모달 문구(모드별로 교체 가능).</summary>
        public string ExitConfirmText { get; set; } = "게임에서 나가시겠습니까?\n진행 중인 라운드는 포기됩니다.";

        private readonly List<(List<Card> cards, bool group, Vector2 pos, float rot)> _timeline = new();
        private int _timelineShown;

        private Font _font;
        private GameObject _canvasGo;
        private static readonly Vector2 SeatCenter = new(0.50f, 0.58f); // 좌석 타원 중심/반경(좌석·비행 연출 공용)
        private Vector2 _seatRadius = new(0.40f, 0.30f);

        /// <summary>좌석 타원 반경 조정(튜토리얼: 설명 패널과 맞은편 좌석 겹침 방지).</summary>
        public void SetSeatRadius(Vector2 radius) => _seatRadius = radius;
        private static readonly Vector2 DeckAnchor = new(0.40f, 0.555f); // 드로우 덱 — 중앙에서 살짝 왼쪽(버림 더미와 한 세트)
        private static readonly Vector2 HeapScreenAnchor = new(0.578f, 0.555f); // 버림 더미 중심(화면 좌표 — 비행 연출용)
        private const float GroupSpread = 68f; // 그룹(뽕/공개 패) 부채꼴 카드 간격

        /// <summary>
        /// 액션·모달 버튼 높이(캔버스 단위). Expand 스케일러에서 캔버스 높이는 항상 1080 이상이라
        /// 이 값이 곧 화면 최소 터치 높이(≈46px @ 높이 375px 모바일 웹 가로화면)를 보장한다.
        /// 앵커 비율로 잡으면 부모 rect가 작을 때(모달 안 등) 터치 높이가 무너진다.
        /// </summary>
        private const float TapButtonHeight = UiKit.MinTapHeight * 1080f;

        private Transform _seatsArea;
        private Transform _discardRow;
        private GameObject _deckGroup;
        private Text _deckCount;
        private Transform _handRow;
        private Transform _buttonBar;
        private Text _prompt;
        private Text _endReason;
        private GameObject _scorePopup;

        /// <summary>점수판이 화면에 떠 있는지 — 종료 버튼(다음 라운드 등)은 이때부터 노출.</summary>
        public bool ScorePopupVisible => _scorePopup != null && _scorePopup.activeSelf;

        /// <summary>지연 노출된 점수판이 실제로 표시된 순간(드라이버가 버튼 갱신에 사용).</summary>
        public event Action ScorePopupShown;
        private Text _scoreTitle;
        private Text _scoreSubtitle; // 판 종료 사유(타이틀 아래)
        private Transform _scoreGrid;
        private CanvasGroup _scorePopupGroup;
        private Coroutine _scoreFade;
        private Button _stopBtn, _pongBtn, _passBtn, _naturalBtn, _meldBtn;
        private Text _callout;
        private CanvasGroup _calloutGroup;
        private Coroutine _calloutFx;
        private Coroutine _pongCountdown;
        private Coroutine _turnCountdownFx;
        private string _turnCountdownKey; // 같은 대기 상태에서 리렌더돼도 카운트다운이 리셋되지 않게 하는 키
        private string _promptBase = "";  // 카운트다운 접미사 "(N)" 를 제외한 안내 문구 원문
        private int _countdownRemaining;  // 0 = 카운트다운 미표시

        private AudioSource _audio;
        private int _flightsActive;      // 비행 중 카드 수 — 점수판은 전부 착지한 뒤에 노출
        private RoundView _lastView;     // 착지 후 좌석 재렌더용
        private Coroutine _pairRefresh;
        // 좌석별 쌍 공개(붉은 뒷면) 표시 상태 — 비행이 모두 착지한 순간에만 갱신(중간 플리커 방지)
        private readonly Dictionary<int, bool> _seatDanger = new();
        private Coroutine _scoreDelay;
        private AudioClip _sfxDraw, _sfxDiscard, _sfxPong, _sfxStop, _sfxShuffle;
        private Image _flash;
        private RectTransform _shakeRoot;
        private Coroutine _shakeFx;
        private Transform _leaderboardRows; // 좌상단 상시 누적 리더보드
        private GameObject _exitModal;      // 나가기 확인 모달
        private Text _exitModalText;
        private int _meldLaidSeat = -1; // 판 종료 공개 패의 주인 좌석 — 그 좌석의 손패(내 손/상대 뒷면)를 숨김

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

            // 배경은 안전 영역 밖(화면 끝까지), 그 위에 안전 영역 루트를 얹는다
            var felt = UiKit.CreatePanel(canvasGo.transform, Color.white);
            felt.sprite = UiArt.Backdrop; // 로비와 동일한 네이비 배경(전 화면 톤 통일)
            UiKit.Stretch(felt.rectTransform);

            var safeRoot = SafeArea.Wrap(canvasGo.transform);

            // 전체 UI를 담는 셰이크 루트 — 뽕/셔플 연출 때 화면 전체를 흔든다
            var shakeGo = new GameObject("ShakeRoot", typeof(RectTransform));
            shakeGo.transform.SetParent(safeRoot, false);
            _shakeRoot = (RectTransform)shakeGo.transform;
            UiKit.Stretch(_shakeRoot);
            var root = shakeGo.transform;

            var seatsGo = new GameObject("SeatsArea", typeof(RectTransform));
            seatsGo.transform.SetParent(root, false);
            _seatsArea = seatsGo.transform;
            UiKit.Stretch((RectTransform)_seatsArea);

            var discardGo = new GameObject("DiscardArea", typeof(RectTransform));
            discardGo.transform.SetParent(root, false);
            _discardRow = discardGo.transform;
            UiKit.Anchor((RectTransform)_discardRow, new Vector2(0.20f, 0.42f), new Vector2(0.80f, 0.72f));

            // 엎어진 드로우 덱(버림 더미 왼쪽) — 겹친 카드백 2장 + 남은 장수
            _deckGroup = new GameObject("DeckStack", typeof(RectTransform));
            _deckGroup.transform.SetParent(root, false);
            var deckRt = (RectTransform)_deckGroup.transform;
            deckRt.anchorMin = deckRt.anchorMax = DeckAnchor;
            deckRt.pivot = new Vector2(0.5f, 0.5f);
            deckRt.sizeDelta = new Vector2(110f, 165f);
            // 버림 카드(특히 6장 공개 패)가 덱을 스치더라도 항상 덱 위에 보이도록 덱을 버림 영역 뒤로
            _deckGroup.transform.SetSiblingIndex(_discardRow.GetSiblingIndex());
            for (var i = 1; i >= 0; i--)
            {
                var back = UiKit.CreatePanel(_deckGroup.transform, Color.white);
                back.sprite = UiArt.CardBack;
                back.type = Image.Type.Simple;
                back.raycastTarget = false;
                var backRt = back.rectTransform;
                backRt.anchorMin = Vector2.zero;
                backRt.anchorMax = Vector2.one;
                backRt.offsetMin = new Vector2(i * 5f, -i * 5f);
                backRt.offsetMax = new Vector2(i * 5f, -i * 5f);
                if (i == 1)
                {
                    back.color = new Color(0.75f, 0.75f, 0.8f); // 아래 장은 어둡게 — 쌓임 표현
                }
            }

            var deckShadow = _deckGroup.AddComponent<Shadow>();
            deckShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            deckShadow.effectDistance = new Vector2(4f, -4f);

            var deckLabel = UiKit.CreateText(_deckGroup.transform, "남은 카드", 18, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            deckLabel.color = new Color(1f, 1f, 1f, 0.6f);
            var deckLabelRt = deckLabel.rectTransform;
            deckLabelRt.anchorMin = new Vector2(0f, 0f);
            deckLabelRt.anchorMax = new Vector2(1f, 0f);
            deckLabelRt.pivot = new Vector2(0.5f, 1f);
            deckLabelRt.offsetMin = new Vector2(0f, -66f);
            deckLabelRt.offsetMax = new Vector2(0f, -44f);

            _deckCount = UiKit.CreateText(_deckGroup.transform, "", 26, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            _deckCount.fontStyle = FontStyle.Bold;
            _deckCount.color = new Color(0.95f, 0.95f, 1f);
            TableArt.AddOutline(_deckCount);
            var countRt = _deckCount.rectTransform;
            countRt.anchorMin = new Vector2(0f, 0f);
            countRt.anchorMax = new Vector2(1f, 0f);
            countRt.pivot = new Vector2(0.5f, 1f);
            countRt.offsetMin = new Vector2(0f, -40f);
            countRt.offsetMax = new Vector2(0f, -6f);

            // 좌상단 상시 리더보드(누적 빚 순위) — 어떤 인원수에서도 좌석 타원과 안 겹치는 코너
            var lbPanel = UiKit.CreatePanel(root, new Color(0f, 0f, 0f, 0.38f));
            if (UiArt.Panel9 != null)
            {
                lbPanel.sprite = UiArt.Panel9;
                lbPanel.type = Image.Type.Sliced;
                lbPanel.color = new Color(0.10f, 0.13f, 0.22f, 0.85f);
            }

            lbPanel.raycastTarget = false;
            var lbRt = lbPanel.rectTransform;
            lbRt.anchorMin = lbRt.anchorMax = new Vector2(0f, 1f); // 좌상단 고정
            lbRt.pivot = new Vector2(0f, 1f);
            lbRt.anchoredPosition = new Vector2(10f, -10f);
            lbRt.sizeDelta = new Vector2(372f, 0f); // 높이는 인원수에 맞춰 자동. 닉네임 12자가 한 줄에 들어가는 폭
            var lbLayout = lbPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            lbLayout.padding = new RectOffset(8, 8, 6, 6);
            lbLayout.spacing = 2;
            lbLayout.childControlWidth = true;
            lbLayout.childControlHeight = true;
            lbLayout.childForceExpandHeight = false;
            lbPanel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _leaderboardRows = lbPanel.transform;

            _endReason = UiKit.CreateText(root, "", 32, TextAnchor.MiddleCenter,
                new Vector2(0.25f, 0.325f), new Vector2(0.75f, 0.39f));
            _endReason.fontStyle = FontStyle.Bold;
            _endReason.color = new Color(1f, 0.96f, 0.88f);
            TableArt.AddOutline(_endReason);

            _prompt = UiKit.CreateText(root, "", 44, TextAnchor.MiddleCenter,
                new Vector2(0.20f, 0.335f), new Vector2(0.80f, 0.41f));
            _prompt.fontStyle = FontStyle.Bold;
            _prompt.color = new Color(0.98f, 0.94f, 0.80f); // 웜화이트 — 다크 밴드 위 눈 편한 대비
            TableArt.AddOutline(_prompt);

            // 액션 버튼(손패 우측) — 공통 5개. 모드 전용 버튼은 AddBarButton으로 추가.
            // 상단 0.35는 6인 우측 좌석 패널 하단(≈0.356)과 안 겹치는 상한. 버튼은 아래에 붙여
            // 쌓으므로(LowerCenter) 개수가 적을 땐 위쪽 여백이 그대로 남는다.
            var barGo = new GameObject("Buttons", typeof(RectTransform));
            barGo.transform.SetParent(root, false);
            UiKit.Anchor((RectTransform)barGo.transform, new Vector2(0.80f, 0.025f), new Vector2(0.995f, 0.35f));
            var layout = barGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14;
            layout.childAlignment = TextAnchor.LowerCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false; // 개수에 따라 늘어나면 2개일 때만 커진다 — 높이는 고정
            _buttonBar = barGo.transform;

            // 선언 액션(뽕/자연뽕/족보)은 같은 레드오렌지 — 시그니처 색으로 통일
            var declareColor = new Color(0.95f, 0.45f, 0.15f);
            _stopBtn = AddBarButton("스톱", () => StopClicked?.Invoke(), new Color(0.90f, 0.30f, 0.25f));
            _meldBtn = AddBarButton("족보", () => MeldClicked?.Invoke(), declareColor);
            _naturalBtn = AddBarButton("자연뽕", () => NaturalPongClicked?.Invoke(), declareColor);
            _pongBtn = AddBarButton("뽕", () => PongClicked?.Invoke(), declareColor);
            _passBtn = AddBarButton("패스", () => PassClicked?.Invoke(), new Color(0.36f, 0.46f, 0.66f));

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
            // 발라트로풍: 진한 패널 + 골드 타이틀 밴드 + 줄무늬 행 + 점수 색 구분(빨강=빚 증가, 하늘=감소).
            var popupBg = UiKit.CreatePanel(root, new Color(0.07f, 0.09f, 0.16f, 0.80f));
            if (UiArt.Panel9 != null)
            {
                popupBg.sprite = UiArt.Panel9;
                popupBg.type = Image.Type.Sliced;
                popupBg.color = new Color(0.13f, 0.16f, 0.26f, 0.82f); // 반투명 — 뒤 족보 카드/테이블이 비치게
            }

            _scorePopup = popupBg.gameObject;
            var popupRt = popupBg.rectTransform;
            popupRt.anchorMin = popupRt.anchorMax = new Vector2(0.5f, 0.60f);
            popupRt.pivot = new Vector2(0.5f, 0.5f);
            popupBg.raycastTarget = false;
            var popupShadow = _scorePopup.AddComponent<Shadow>();
            popupShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            popupShadow.effectDistance = new Vector2(8f, -8f);
            _scorePopupGroup = _scorePopup.AddComponent<CanvasGroup>();
            _scorePopupGroup.blocksRaycasts = false;
            _scorePopupGroup.interactable = false;

            // 골드 타이틀(패널 상단) — 단순 텍스트 + 아웃라인
            _scoreTitle = UiKit.CreateText(popupBg.transform, "", 40, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            _scoreTitle.fontStyle = FontStyle.Bold;
            _scoreTitle.color = GoldText;
            TableArt.AddOutline(_scoreTitle);
            FitText(_scoreTitle, 24, 40);
            var titleRt = _scoreTitle.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(0f, -70f);
            titleRt.offsetMax = new Vector2(0f, -8f);

            // 종료 사유(타이틀 아래) — "누구누구 스톱" 등
            _scoreSubtitle = UiKit.CreateText(popupBg.transform, "", 26, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            _scoreSubtitle.color = new Color(0.92f, 0.90f, 0.84f);
            TableArt.AddOutline(_scoreSubtitle);
            FitText(_scoreSubtitle, 16, 26);
            var subtitleRt = _scoreSubtitle.rectTransform;
            subtitleRt.anchorMin = new Vector2(0f, 1f);
            subtitleRt.anchorMax = new Vector2(1f, 1f);
            subtitleRt.pivot = new Vector2(0.5f, 1f);
            subtitleRt.offsetMin = new Vector2(0f, -108f);
            subtitleRt.offsetMax = new Vector2(0f, -72f);

            var gridGo = new GameObject("ScoreGrid", typeof(RectTransform));
            gridGo.transform.SetParent(popupBg.transform, false);
            _scoreGrid = gridGo.transform;
            var gridRt = (RectTransform)_scoreGrid;
            gridRt.anchorMin = Vector2.zero;
            gridRt.anchorMax = Vector2.one;
            gridRt.offsetMin = new Vector2(20f, 18f);
            gridRt.offsetMax = new Vector2(-20f, -116f);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(4, 4);
            grid.cellSize = new Vector2(210, 58);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = PlayerCount + 1;

            _scorePopup.SetActive(false);

            // 콜아웃
            _callout = UiKit.CreateText(root, "", 100, TextAnchor.MiddleCenter,
                new Vector2(0.25f, 0.43f), new Vector2(0.75f, 0.62f));
            _callout.fontStyle = FontStyle.Bold;
            _callout.color = UiKit.Accent;
            _callout.raycastTarget = false;
            TableArt.AddOutline(_callout);
            _calloutGroup = _callout.gameObject.AddComponent<CanvasGroup>();
            _calloutGroup.alpha = 0f;
            _calloutGroup.blocksRaycasts = false;

            // 비네트 — 가장자리를 은은히 어둡게(테이블 집중감)
            var vignette = UiKit.CreatePanel(root, Color.white);
            vignette.sprite = UiArt.Vignette;
            vignette.raycastTarget = false;
            UiKit.Stretch(vignette.rectTransform);

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
            _sfxShuffle = TableArt.Riffle("shuffle", 0.55f);

            BuildExitUi(root);
        }

        /// <summary>나가기 버튼(우상단 코너 — 손패·액션 버튼과 대각선 반대라 오클릭 최소) + 확인 모달.</summary>
        private void BuildExitUi(Transform root)
        {
            // 나가기: 터치 가능 크기(≈48dp) + 중성 회색 — 붉은색은 확인 모달의 파괴적 동작에만
            var exitBtn = UiKit.CreateButton(root, "나가기",
                new Vector2(0.90f, 0.868f), new Vector2(0.99f, 0.99f), ShowExitConfirm, 26); // 상하 여백 명시(터치 하한 확장이 상단에 붙는 것 방지)
            exitBtn.GetComponent<Image>().color = new Color(0.32f, 0.36f, 0.44f, 0.92f);
            var exitLabel = exitBtn.GetComponentInChildren<Text>();
            exitLabel.color = Color.white;
            exitLabel.fontStyle = FontStyle.Bold;
            TableArt.AddOutline(exitLabel);

            // 확인 모달 — 캔버스 직속(셰이크 무관), 표시할 때 최상위로 올림
            var dim = UiKit.CreatePanel(_canvasGo.transform, new Color(0f, 0f, 0f, 0.65f));
            dim.raycastTarget = true; // 뒤 클릭 차단
            UiKit.Stretch(dim.rectTransform);
            _exitModal = dim.gameObject;

            var box = UiKit.CreatePanel(dim.transform, new Color(0.10f, 0.13f, 0.22f, 0.98f));
            if (UiArt.Panel9 != null)
            {
                box.sprite = UiArt.Panel9;
                box.type = Image.Type.Sliced;
            }

            // 세로 375px 모바일 웹에서 버튼 두 개가 하단에 44px 이상으로 들어가는 최소 높이
            UiKit.Anchor(box.rectTransform, new Vector2(0.30f, 0.30f), new Vector2(0.70f, 0.70f));
            var boxShadow = box.gameObject.AddComponent<Shadow>();
            boxShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            boxShadow.effectDistance = new Vector2(8f, -8f);

            _exitModalText = UiKit.CreateText(box.transform, "", 30, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.92f));
            _exitModalText.fontStyle = FontStyle.Bold;
            _exitModalText.color = new Color(0.96f, 0.95f, 0.90f);
            TableArt.AddOutline(_exitModalText);
            FitText(_exitModalText, 18, 30);

            var stay = UiKit.CreateButton(box.transform, "계속하기",
                new Vector2(0.08f, 0.10f), new Vector2(0.46f, 0.34f), () => _exitModal.SetActive(false), 28);
            stay.GetComponent<Image>().color = new Color(0.36f, 0.46f, 0.66f);
            StyleModalButton(stay);

            var leave = UiKit.CreateButton(box.transform, "나가기",
                new Vector2(0.54f, 0.10f), new Vector2(0.92f, 0.34f), () =>
                {
                    _exitModal.SetActive(false);
                    ExitConfirmed?.Invoke();
                }, 28);
            leave.GetComponent<Image>().color = new Color(0.90f, 0.30f, 0.25f);
            StyleModalButton(leave);

            _exitModal.SetActive(false);

            // 게임 중 기기 뒤로가기 = 나가기 확인(모달이 떠 있으면 닫기). 판이 통째로 날아가는 걸 막는다.
            UiKit.BackAction = () =>
            {
                if (_exitModal != null && _exitModal.activeSelf)
                {
                    _exitModal.SetActive(false);
                    return;
                }

                ShowExitConfirm();
            };
        }

        /// <summary>모달 버튼: 글자 스타일 + 하단 고정 높이 배치(가로 위치는 호출부 앵커 유지).</summary>
        private static void StyleModalButton(Button button)
        {
            var text = button.GetComponentInChildren<Text>();
            text.color = Color.white;
            text.fontStyle = FontStyle.Bold;
            TableArt.AddOutline(text);

            var rt = button.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(rt.anchorMin.x, 0f);
            rt.anchorMax = new Vector2(rt.anchorMax.x, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(0f, 26f);
            rt.offsetMax = new Vector2(0f, 26f + TapButtonHeight);
        }

        private void ShowExitConfirm()
        {
            _exitModalText.text = ExitConfirmText;
            _exitModal.transform.SetAsLastSibling(); // 안내 패널 등 무엇보다 위
            _exitModal.SetActive(true);
        }

        /// <summary>버튼 바에 버튼 추가 — 컬러 버튼(흰 글씨+아웃라인+그림자). tint 미지정 시 블루그레이(이동/진행 계열).</summary>
        public Button AddBarButton(string label, UnityEngine.Events.UnityAction onClick, Color? tint = null)
        {
            var btn = UiKit.CreateButton(_buttonBar, label, Vector2.zero, Vector2.one, onClick, 36);
            var size = btn.gameObject.AddComponent<LayoutElement>();
            size.preferredHeight = TapButtonHeight;
            size.flexibleHeight = 0f;
            var buttonShadow = btn.gameObject.AddComponent<Shadow>();
            buttonShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            buttonShadow.effectDistance = new Vector2(4f, -4f);
            btn.GetComponent<Image>().color = tint ?? new Color(0.36f, 0.46f, 0.66f);
            var text = btn.GetComponentInChildren<Text>();
            text.color = Color.white;
            text.fontStyle = FontStyle.Bold;
            TableArt.AddOutline(text);

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
            _lastView = view; // 착지 후 좌석 재렌더(쌍 공개 전환)용

            if (view == null)
            {
                return;
            }

            RenderSeats(view);
            RenderHand(view, hiddenHandCards);
            RenderDiscard();
            RenderPrompt(view, naturalSelecting);
            RenderButtons(view, naturalSelecting);
            RenderLeaderboard(view);
            RenderDeck(view);
            UpdateTurnCountdown(view, naturalSelecting);
        }

        private void RenderDeck(RoundView view)
        {
            _deckGroup.SetActive(view.drawPileCount > 0);
            _deckCount.text = view.drawPileCount.ToString();
        }

        /// <summary>좌상단 상시 리더보드: 누적 빚 오름차순(공동 등수), 1위 골드·내 줄 ★.</summary>
        private void RenderLeaderboard(RoundView view)
        {
            foreach (Transform child in _leaderboardRows)
            {
                Destroy(child.gameObject);
            }

            var rank = 0;
            var seen = 0;
            var prevDebt = int.MinValue;
            foreach (var seat in view.seats.OrderBy(s => s.cumulativeDebt))
            {
                seen++;
                if (seat.cumulativeDebt != prevDebt)
                {
                    rank = seen;
                    prevDebt = seat.cumulativeDebt;
                }

                var mine = seat.seat == MySeat;
                var color = mine ? MineText : new Color(0.80f, 0.82f, 0.87f);
                var row = new GameObject("LbRow", typeof(RectTransform)).AddComponent<HorizontalLayoutGroup>();
                row.transform.SetParent(_leaderboardRows, false);
                row.spacing = 4;
                row.childControlWidth = true;
                row.childControlHeight = true;
                row.childForceExpandWidth = false;
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

                // 순위·점수는 우측, 닉네임은 좌측 정렬 — 세로 정렬선이 생겨 표로 읽힌다
                LeaderboardCell(row.transform, $"{rank}위", 48f, color, mine, TextAnchor.MiddleRight);
                LeaderboardCell(row.transform, seat.nickname, 240f, color, mine, TextAnchor.MiddleLeft, fit: true); // "형용사 동물 봇"(최대 12자)이 한 줄에 들어가는 폭
                LeaderboardCell(row.transform, seat.cumulativeDebt.ToString(), 56f, color, mine, TextAnchor.MiddleRight);
            }
        }

        /// <summary>리더보드 셀: 고정 폭(선 없는 표).</summary>
        private void LeaderboardCell(Transform row, string text, float width, Color color, bool bold,
            TextAnchor align, bool fit = false)
        {
            var cell = UiKit.CreateText(row, text, 24, align, Vector2.zero, Vector2.one);
            cell.color = color;
            if (bold)
            {
                cell.fontStyle = FontStyle.Bold;
            }

            TableArt.AddOutline(cell);
            var le = cell.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
            if (fit)
            {
                FitText(cell, 13, 24);
                // 자동 축소는 줄바꿈을 동반한다. 세로로 넘치면 아랫줄 닉네임과 겹치므로 한 줄로 잘라낸다.
                cell.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        /// <summary>
        /// 내 행동 대기 상태면 5초 카운트다운 표시(턴 타이머와 동기). 상태 키가 바뀔 때만 재시작 —
        /// 로컬/서버 타이머는 각자 돌고, 이 표시는 뷰가 렌더 상태로부터 자체 관리한다.
        /// </summary>
        private void UpdateTurnCountdown(RoundView view, bool naturalSelecting)
        {
            var actionable = ShowTurnCountdown &&
                ((view.phase == RoundPhase.WaitingStop && view.currentSeat == MySeat)
                || (view.phase == RoundPhase.WaitingDiscard && view.currentSeat == MySeat)
                || (view.phase == RoundPhase.WaitingPongDiscard && view.actorSeat == MySeat));
            var key = actionable ? $"{view.phase}:{view.currentSeat}:{view.actorSeat}:{naturalSelecting}" : null;
            if (key == _turnCountdownKey)
            {
                return;
            }

            _turnCountdownKey = key;
            if (_turnCountdownFx != null)
            {
                StopCoroutine(_turnCountdownFx);
                _turnCountdownFx = null;
            }

            if (key == null)
            {
                _countdownRemaining = 0;
                ApplyPrompt();
                return;
            }

            _turnCountdownFx = StartCoroutine(TurnCountdownFx(GameConfig.TurnTimerSeconds));
        }

        private IEnumerator TurnCountdownFx(int seconds)
        {
            for (var t = seconds; t > 0; t--)
            {
                _countdownRemaining = t;
                ApplyPrompt(); // 안내 문구 뒤에 "(N)" — 예: 버릴 카드를 클릭하세요. (5)
                yield return new WaitForSeconds(1f);
            }

            _countdownRemaining = 0;
            ApplyPrompt();
        }

        /// <summary>안내 문구 설정. 턴 카운트다운 중이면 "(남은 초)"가 뒤에 붙는다.</summary>
        public void SetPrompt(string text)
        {
            _promptBase = text;
            ApplyPrompt();
        }

        private void ApplyPrompt() =>
            _prompt.text = _countdownRemaining > 0 && _promptBase.Length > 0
                ? $"{_promptBase} ({_countdownRemaining})"
                : _promptBase;

        public void SetEndReason(string text) => _endReason.text = text;

        /// <summary>비행 종료 후 좌석만 다시 그려 쌍 공개(붉은 뒷면)를 착지 시점에 반영.</summary>
        private void SchedulePairRefresh()
        {
            if (_pairRefresh != null)
            {
                return;
            }

            _pairRefresh = StartCoroutine(PairRefreshAfterFlights());
        }

        private IEnumerator PairRefreshAfterFlights()
        {
            yield return new WaitWhile(() => _flightsActive > 0);
            _pairRefresh = null;
            if (_lastView != null)
            {
                RenderSeats(_lastView);
            }
        }

        private void RenderSeats(RoundView view)
        {
            foreach (Transform child in _seatsArea)
            {
                Destroy(child.gameObject);
            }

            // 뽕 창/턴 전환 간격 동안은 아무도 포커싱하지 않음. 뽕 추가 버림은 선언자를 포커싱.
            var focusSeat = view.phase == RoundPhase.PongWindow || view.phase == RoundPhase.TurnGap ? -1
                : view.phase == RoundPhase.WaitingPongDiscard ? view.actorSeat
                : view.currentSeat;

            foreach (var seatView in view.seats)
            {
                var anchor = SeatAnchor(seatView.seat);

                var mine = seatView.seat == MySeat;
                var highlight = focusSeat == seatView.seat;
                if (mine)
                {
                    // 내 좌석 상시 골드 테두리 — 턴 강조(채움)와 구분되는 정체성 표식
                    var frame = UiKit.CreatePanel(_seatsArea, new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.45f));
                    frame.sprite = UiArt.Button;
                    frame.type = Image.Type.Sliced;
                    frame.raycastTarget = false;
                    var frameRt = frame.rectTransform;
                    frameRt.anchorMin = frameRt.anchorMax = anchor;
                    frameRt.pivot = new Vector2(0.5f, 0.5f);
                    frameRt.sizeDelta = new Vector2(268f, 92f);

                    // 좌석 패널이 반투명이라 골드가 전체에 비쳐 보임 — 불투명 속판으로 가려 테두리만 남긴다
                    var inner = UiKit.CreatePanel(_seatsArea, new Color(0.07f, 0.11f, 0.22f, 1f));
                    if (UiArt.Panel9 != null)
                    {
                        inner.sprite = UiArt.Panel9;
                        inner.type = Image.Type.Sliced;
                    }

                    inner.raycastTarget = false;
                    var innerRt = inner.rectTransform;
                    innerRt.anchorMin = innerRt.anchorMax = anchor;
                    innerRt.pivot = new Vector2(0.5f, 0.5f);
                    innerRt.sizeDelta = new Vector2(260f, 84f);
                }

                // 턴 강조는 Accent 단일 출처(내 좌석 골드 링과 같은 골드), 평시는 리더보드와 동일 톤
                var panel = UiKit.CreatePanel(_seatsArea, highlight
                    ? new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.5f)
                    : new Color(0.10f, 0.13f, 0.22f, 0.85f));
                if (UiArt.Panel9 != null)
                {
                    panel.sprite = UiArt.Panel9;
                    panel.type = Image.Type.Sliced; // 리더보드·모달과 같은 9-slice 라운드 — 유일한 직각 패널 제거
                }
                var rt = panel.rectTransform;
                rt.anchorMin = rt.anchorMax = anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(260f, mine ? 84f : 160f);

                var label = UiKit.CreateText(panel.transform, $"{seatView.nickname}\n빚: {seatView.cumulativeDebt}", 28,
                    TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
                FitText(label, 18, 28);
                label.verticalOverflow = VerticalWrapMode.Truncate; // 뒷면 카드 영역 침범 금지
                if (mine)
                {
                    continue;
                }

                UiKit.Anchor(label.rectTransform, new Vector2(0f, 0.58f), new Vector2(1f, 1f));

                if (seatView.seat == _meldLaidSeat)
                {
                    continue; // 공개 패 주인 — 손패는 전부 테이블에 내려놓은 상태(뒷면 안 그림)
                }

                // 상대 손패 수: 뒤집힌 카드 — 크기·간격 고정, 가운데 정렬.
                // 시작점을 정수로 반올림해 홀수 장일 때 생기는 0.5px 오프셋(테두리가 매번 다르게
                // 리샘플링돼 크기가 변해 보이는 원인)을 제거한다. 최다 6장: 231 ≤ 패널 안폭 236.
                const float bw = 36f, bh = 54f, step = bw + 3f;
                var rowStart = Mathf.Round(-((seatView.handCount - 1) * step + bw) / 2f + bw / 2f);
                // 쌍 공개(§7): 붉은 뒷면. 평가는 "모든 비행 착지 + 표시 손패가 정확히 2장"일 때만 —
                // 그 사이(뽕 토스 대기·드로우/버림 비행 중)엔 직전 색을 유지해 플리커를 막는다.
                if (_flightsActive == 0)
                {
                    _seatDanger[seatView.seat] = seatView.pairExposed && seatView.handCount == 2;
                }
                else
                {
                    SchedulePairRefresh(); // 착지 후 좌석만 재평가
                }

                var showDanger = _seatDanger.TryGetValue(seatView.seat, out var danger) && danger;

                for (var j = 0; j < seatView.handCount; j++)
                {
                    var back = UiKit.CreatePanel(panel.transform, Color.white);
                    back.sprite = showDanger ? UiArt.CardBackSmallDanger : UiArt.CardBackSmall; // 축소 앨리어싱 방지 소형 스프라이트
                    back.type = Image.Type.Simple;
                    var brt = back.rectTransform;
                    brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.28f);
                    brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.sizeDelta = new Vector2(bw, bh);
                    brt.anchoredPosition = new Vector2(rowStart + j * step, 0f);
                }
            }
        }

        /// <summary>좌석 앵커(내 좌석이 항상 아래로 오는 회전 배치) — 좌석 패널·비행 연출 공용.</summary>
        private Vector2 SeatAnchor(int seat)
        {
            var displayIndex = (seat - MySeat + PlayerCount) % PlayerCount;
            var angle = (-90f + displayIndex * 360f / PlayerCount) * Mathf.Deg2Rad;
            var ry = _seatRadius.y;
            if (PlayerCount == 6 && (displayIndex == 2 || displayIndex == 4))
            {
                ry = 0.25f; // 6인 상단 대각 좌석만 중앙 쪽으로 — 좌상단 리더보드(6행)와 간섭 방지
            }

            return SeatCenter + new Vector2(Mathf.Cos(angle) * _seatRadius.x, Mathf.Sin(angle) * ry);
        }

        private void RenderHand(RoundView view, ICollection<Card> hidden)
        {
            foreach (Transform child in _handRow)
            {
                Destroy(child.gameObject);
            }

            if (_meldLaidSeat == MySeat)
            {
                return; // 내 공개 패 — 전부 테이블에 내려놓음(ShowMeldSet이 펼침)
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
                go.AddComponent<CardMotion>(); // 둥실거림 + 호버 확대
            }
        }

        private void RenderDiscard()
        {
            foreach (Transform child in _discardRow)
            {
                Destroy(child.gameObject);
            }

            const float w = 120f, h = 180f;
            var heapAnchor = new Vector2(0.63f, 0.45f); // 버림 더미 — 중앙에서 살짝 오른쪽(덱과 한 세트)
            GameObject last = null;
            var older = new List<GameObject>();
            var top = new List<GameObject>();
            for (var t = 0; t < _timeline.Count; t++)
            {
                var (cards, group, pos, rot) = _timeline[t];
                var isTop = t == _timeline.Count - 1;
                if (group)
                {
                    for (var j = 0; j < cards.Count; j++)
                    {
                        var fan = j - (cards.Count - 1) / 2f;
                        var go = PlaceCard(cards[j], w, h, heapAnchor, pos + new Vector2(fan * GroupSpread, -Mathf.Abs(fan) * 8f), rot - fan * 10f);
                        if (isTop)
                        {
                            last = go;
                            top.Add(go);
                        }
                        else
                        {
                            older.Add(go);
                        }
                    }
                }
                else
                {
                    var go = PlaceCard(cards[0], w, h, heapAnchor, pos, rot);
                    if (isTop)
                    {
                        last = go;
                        top.Add(go);
                    }
                    else
                    {
                        older.Add(go);
                    }
                }
            }

            // 최신 항목만 원색 — 그 아래 깔린 카드(낱장·뽕 묶음 모두)는 어둡게+축소해 위계를 만든다
            foreach (var under in older)
            {
                under.transform.localScale = Vector3.one * 0.92f;
                var dim = UiKit.CreatePanel(under.transform, new Color(0.03f, 0.05f, 0.12f, 0.45f));
                dim.raycastTarget = false;
                UiKit.Stretch(dim.rectTransform);
            }

            if (last != null)
            {
                // 묶음(뽕/자연뽕/족보)은 한 단위 — 헤일로를 묶음 전체 아래에 깔아
                // 카드 사이 이음새 없이 바깥 실루엣만 골드 테두리로 읽히게 한다
                var below = top[0].transform.GetSiblingIndex();
                foreach (var go in top)
                {
                    HighlightTop(go, below);
                }

                if (_timeline.Count > _timelineShown)
                {
                    StartCoroutine(ScalePop(last.transform));
                }
            }

            _timelineShown = _timeline.Count;
        }

        /// <summary>맨 위(마지막 버림) 카드 강조: 카드 바로 아래에 노란 헤일로를 깔아 테두리처럼 보이게.</summary>
        private void HighlightTop(GameObject top, int? siblingIndex = null)
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

            halo.transform.SetSiblingIndex(siblingIndex ?? top.transform.GetSiblingIndex()); // 카드(또는 묶음 전체) 바로 아래로
        }

        /// <summary>
        /// 판 종료 공개 패(족보/스톱 손패)를 좌석에서 날려 버림 더미 위에 부채꼴로 올린다
        /// (뽕 그룹과 동일 연출, 다음 판 ClearTimeline까지 유지).
        /// laidSeat 좌석의 손패는 즉시 숨겨져 "내려놓는" 연출이 된다(내 손패든 상대 좌석 뒷면이든).
        /// </summary>
        public void ShowMeldSet(IEnumerable<Card> cards, int laidSeat = -1)
        {
            _meldLaidSeat = laidSeat;
            if (laidSeat < 0)
            {
                AddGroup(cards); // 좌석 미지정 — 비행 없이 즉시 쌓기
                RenderDiscard();
                return;
            }

            GroupFx(laidSeat, cards);
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

        private void RenderPrompt(RoundView view, bool naturalSelecting)
        {
            if (naturalSelecting)
            {
                return; // 자연뽕 안내(SetPrompt) 유지
            }

            SetPrompt(view.phase switch
            {
                RoundPhase.WaitingStop when view.currentSeat == MySeat => "스톱할까요, 계속할까요?",
                RoundPhase.WaitingDiscard when view.currentSeat == MySeat && view.canMeld =>
                    $"{MeldKorean(view.meldType)} {view.meldScore}점! 선언하거나 그냥 버려도 됩니다",
                RoundPhase.WaitingDiscard when view.currentSeat == MySeat && view.canNaturalPong =>
                    "버릴 카드를 클릭하세요 (또는 자연뽕)",
                RoundPhase.WaitingDiscard when view.currentSeat == MySeat => "버릴 카드를 클릭하세요",
                RoundPhase.WaitingPongDiscard when view.actorSeat == MySeat => "버릴 카드를 클릭하세요",
                RoundPhase.PongWindow when view.canPong => $"{view.pongNumber} 뽕 기회!",
                _ => ""
            });
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
            if (view.canMeld)
            {
                SetButtonLabel(_meldBtn, MeldKorean(view.meldType)); // 예: "또이또이"
            }
            _naturalBtn.gameObject.SetActive(view.canNaturalPong && !naturalSelecting);
        }

        // ── 버림 타임라인 ──

        public void AddDiscard(Card card) => _timeline.Add((new List<Card> { card }, false,
            new Vector2(TableArt.Tri(150f), TableArt.Tri(50f)), TableArt.Tri(28f)));

        public void AddGroup(IEnumerable<Card> cards) => _timeline.Add((TableArt.Sorted(cards), true,
            // 그룹은 오른쪽으로만 흩뿌림 — 6장 부채꼴(±170px)이 왼쪽 덱을 침범하지 않는 하한 확보
            new Vector2(60f + Mathf.Abs(TableArt.Tri(90f)), TableArt.Tri(45f)), TableArt.Tri(16f)));

        /// <summary>마지막 그룹을 확정 카드 구성으로 치환(자리·회전 유지, 팝 연출 없음).</summary>
        public void ReplaceLastGroup(IEnumerable<Card> cards)
        {
            if (_timeline.Count == 0 || !_timeline[^1].group)
            {
                AddGroup(cards); // 낙관 그룹이 아직 비행 중(미착지) — 확정 구성으로 바로 쌓기
                return;
            }

            var (_, _, pos, rot) = _timeline[^1];
            _timeline[^1] = (TableArt.Sorted(cards), true, pos, rot);
            _timelineShown = _timeline.Count;
        }

        /// <summary>마지막 타임라인 항목 제거(낙관적 연출 원복). 비행 중이라 아직 없으면 무시.</summary>
        public void RemoveLastTimelineEntry()
        {
            if (_timeline.Count == 0)
            {
                return;
            }

            _timeline.RemoveAt(_timeline.Count - 1);
            _timelineShown = Mathf.Min(_timelineShown, _timeline.Count);
        }

        /// <summary>재셔플: 고정 패는 남기고 단일 버림은 맨 위 1장만 유지.</summary>
        public void ClearTimeline()
        {
            _timeline.Clear();
            _timelineShown = 0;
            _meldLaidSeat = -1;
        }

        // ── 효과음/연출 ──

        public void PlayDrawSfx() => _audio.PlayOneShot(_sfxDraw, 0.5f);

        /// <summary>드로우 연출: 덱에서 카드 한 장(뒷면)이 해당 좌석으로 날아감 + 효과음. delay로 셔플 연출 뒤로 미룰 수 있다.</summary>
        public void DrawFx(int seat, float delay = 0f)
        {
            StartCoroutine(FlyCardFromDeck(seat, delay));
        }

        /// <summary>
        /// 버림 연출: 좌석(내 좌석은 손패)에서 카드가 앞면으로 날아가 더미의 최종 위치·기울기
        /// 그대로 착지한다(목표를 미리 추첨해 착지 점프 없음). 타임라인 추가도 착지 시점에 view가 수행.
        /// </summary>
        public void DiscardFx(int seat, Card card)
        {
            PlayDiscardSfx();
            var pos = new Vector2(TableArt.Tri(150f), TableArt.Tri(50f)); // 최종 흩어짐을 미리 추첨
            var rot = TableArt.Tri(28f);
            StartCoroutine(FlyFace(seat, card, HeapToScreen(pos), rot, 120f, 180f, 0f, () =>
            {
                _timeline.Add((new List<Card> { card }, false, pos, rot));
                _timelineShown = _timeline.Count; // 비행으로 이미 보여줬으니 착지 팝 없음
                RenderDiscard();
            }));
        }

        /// <summary>뽕/자연뽕 고정 패 연출: 좌석에서 카드들이 부채꼴 최종 위치로 각각 날아가 무더기로 쌓인다.</summary>
        public void GroupFx(int seat, IEnumerable<Card> cards)
        {
            var sorted = TableArt.Sorted(cards);
            var pos = new Vector2(TableArt.Tri(120f), TableArt.Tri(45f));
            var rot = TableArt.Tri(16f);
            var landed = 0;
            for (var j = 0; j < sorted.Count; j++)
            {
                var fan = j - (sorted.Count - 1) / 2f;
                var target = pos + new Vector2(fan * GroupSpread, -Mathf.Abs(fan) * 8f);
                // 시차 없이 동시 출발 — 한 묶음이 부채꼴로 벌어지며 함께 날아간다
                StartCoroutine(FlyFace(seat, sorted[j], HeapToScreen(target), rot - fan * 10f, 120f, 180f, 0f, () =>
                {
                    if (++landed == sorted.Count)
                    {
                        _timeline.Add((sorted, true, pos, rot));
                        _timelineShown = _timeline.Count; // 착지 팝 없음
                        RenderDiscard();
                    }
                }));
            }
        }

        /// <summary>버림 더미 중심 기준 픽셀 오프셋 → 화면 정규 좌표(비행 목표 계산).</summary>
        private Vector2 HeapToScreen(Vector2 offsetPx)
        {
            var size = _shakeRoot.rect.size;
            return HeapScreenAnchor + new Vector2(offsetPx.x / size.x, offsetPx.y / size.y);
        }

        /// <summary>카드 앞면 한 장을 좌석에서 목표 지점·기울기로 비행시키고 착지 콜백 실행.</summary>
        private IEnumerator FlyFace(int seat, Card card, Vector2 targetAnchor, float targetRot,
            float w, float h, float delay, System.Action onLand)
        {
            _flightsActive++;
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            var fly = TableArt.CreateCardFace(_shakeRoot, card, w, h, _font);
            var rt = (RectTransform)fly.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);

            var from = seat == MySeat ? new Vector2(0.5f, 0.15f) : SeatAnchor(seat);
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
            {
                if (fly == null)
                {
                    _flightsActive--;
                    yield break;
                }

                var eased = 1f - (1f - t) * (1f - t); // ease-out
                rt.anchorMin = rt.anchorMax = Vector2.Lerp(from, targetAnchor, eased);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, targetRot, eased));
                yield return null;
            }

            _flightsActive--;
            Destroy(fly);
            onLand?.Invoke();
        }

        /// <summary>재셔플 수렴 연출: 버림 더미 주변의 카드들이 뒷면으로 덮여 덱으로 모여든다.</summary>
        private IEnumerator ShuffleGatherFx()
        {
            var backs = new List<RectTransform>();
            for (var i = 0; i < 5; i++)
            {
                var back = UiKit.CreatePanel(_shakeRoot, Color.white);
                back.sprite = UiArt.CardBack;
                back.raycastTarget = false;
                var rt = back.rectTransform;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(100f, 150f);
                rt.anchorMin = rt.anchorMax = HeapScreenAnchor
                    + new Vector2((i - 2) * 0.02f, (i % 2 == 0 ? 1 : -1) * 0.015f); // 더미 주변에 흩어진 상태
                rt.localRotation = Quaternion.Euler(0f, 0f, (i - 2) * 9f);
                backs.Add(rt);
            }

            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
            {
                var eased = t * t; // ease-in — 빨려들 듯
                for (var i = 0; i < backs.Count; i++)
                {
                    if (backs[i] == null)
                    {
                        continue;
                    }

                    var delay = Mathf.Clamp01((t - i * 0.06f) / 0.7f); // 한 장씩 시차
                    var p = Mathf.SmoothStep(0f, 1f, delay);
                    var start = HeapScreenAnchor + new Vector2((i - 2) * 0.02f, (i % 2 == 0 ? 1 : -1) * 0.015f);
                    backs[i].anchorMin = backs[i].anchorMax = Vector2.Lerp(start, DeckAnchor, p);
                    backs[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp((i - 2) * 9f, 0f, p));
                }

                yield return null;
            }

            foreach (var back in backs)
            {
                if (back != null)
                {
                    Destroy(back.gameObject);
                }
            }
        }

        private IEnumerator FlyCardFromDeck(int seat, float delay = 0f)
        {
            _flightsActive++;
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            PlayDrawSfx();
            var go = new GameObject("FlyCard", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_shakeRoot, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiArt.CardBack;
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 150f);

            var from = DeckAnchor;
            var to = seat == MySeat ? new Vector2(0.5f, 0.15f) : SeatAnchor(seat); // 내 좌석은 손패로
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.28f)
            {
                if (go == null)
                {
                    _flightsActive--;
                    yield break;
                }

                var eased = 1f - (1f - t) * (1f - t); // ease-out
                rt.anchorMin = rt.anchorMax = Vector2.Lerp(from, to, eased);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, eased);
                yield return null;
            }

            _flightsActive--;
            Destroy(go);
        }

        public void PlayDiscardSfx() => _audio.PlayOneShot(_sfxDiscard, 0.5f);

        public void PlayPongSfx() => _audio.PlayOneShot(_sfxPong, 0.8f);

        public void PlayStopSfx() => _audio.PlayOneShot(_sfxStop, 0.6f);

        /// <summary>뽕/자연뽕/족보 공통 연출: 효과음 + 화면 플래시 + 콜아웃 + 셰이크.</summary>
        public void PongFx(string callout)
        {
            PlayPongSfx();
            Flash(new Color(1f, 0.95f, 0.4f, 0.5f));
            ShowCallout(callout);
            Shake(12f, 0.28f);
        }

        /// <summary>재셔플 연출: 버림 카드들이 뒷면으로 덱에 모여드는 수렴 + 콜아웃/플래시/효과음/셰이크.</summary>
        public void ShuffleFx()
        {
            ShowCallout("더미 셔플!", new Color(0.75f, 0.9f, 1f));
            Flash(new Color(1f, 1f, 1f, 0.3f));
            _audio.PlayOneShot(_sfxShuffle, 0.7f);
            Shake(7f, 0.22f);
            StartCoroutine(ShuffleGatherFx());
        }

        /// <summary>화면 전체 흔들림(감쇠). 뽕/족보 같은 임팩트 순간용.</summary>
        public void Shake(float amplitude, float duration)
        {
            if (_shakeFx != null)
            {
                StopCoroutine(_shakeFx);
            }

            _shakeFx = StartCoroutine(ShakeFx(amplitude, duration));
        }

        private IEnumerator ShakeFx(float amplitude, float duration)
        {
            for (var t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                var damp = (1f - t) * amplitude;
                _shakeRoot.anchoredPosition = new Vector2(
                    (Mathf.PerlinNoise(Time.time * 30f, 0.5f) - 0.5f) * 2f * damp,
                    (Mathf.PerlinNoise(0.5f, Time.time * 30f) - 0.5f) * 2f * damp);
                yield return null;
            }

            _shakeRoot.anchoredPosition = Vector2.zero;
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

        /// <summary>중앙 콜아웃. color 미지정 시 골드 — 이벤트 성격별 색(스톱=하늘, 바가지=빨강 등) 지정 가능.</summary>
        public void ShowCallout(string message, Color? color = null)
        {
            _callout.text = message;
            _callout.color = color ?? UiKit.Accent;
            if (_calloutFx != null)
            {
                StopCoroutine(_calloutFx);
            }

            _calloutFx = StartCoroutine(CalloutFx());
        }

        private static readonly Color GoldText = UiKit.Accent; // 소프트 골드 — 전 화면 공용 색과 단일 출처
        private static readonly Color MineText = new(0.55f, 0.88f, 1f);   // 내 닉네임 표시(★ 대신 색으로 구분)

        /// <summary>판별 점수 색: 빚 증가(+)=빨강(나쁨), 감소(-)=하늘(좋음), 0=회색.</summary>
        private static Color ScoreColor(int value) => value > 0 ? new Color(1f, 0.5f, 0.45f)
            : value < 0 ? new Color(0.5f, 0.8f, 1f)
            : new Color(0.72f, 0.72f, 0.78f);

        /// <summary>중앙 점수표: 마지막 카드가 착지하고 1초 뒤에 노출(연출을 가리지 않게).</summary>
        public void ShowScorePopup(string title, IReadOnlyList<int> debts, IReadOnlyList<int[]> roundHistory, bool fadeOut,
            Action onFadedOut = null)
        {
            if (_scoreDelay != null)
            {
                StopCoroutine(_scoreDelay);
            }

            _scoreDelay = StartCoroutine(ShowScorePopupAfterFlights(title, debts, roundHistory, fadeOut, onFadedOut));
        }

        private IEnumerator ShowScorePopupAfterFlights(string title, IReadOnlyList<int> debts,
            IReadOnlyList<int[]> roundHistory, bool fadeOut, Action onFadedOut)
        {
            yield return new WaitWhile(() => _flightsActive > 0);
            yield return new WaitForSeconds(1f);
            _scoreDelay = null;
            ShowScorePopupNow(title, debts, roundHistory, fadeOut, onFadedOut);
        }

        /// <summary>등수 헤더 + 판별 점수 행(줄무늬) + 합계 행. fadeOut이면 잠시 후 사라지고 onFadedOut 호출.</summary>
        private void ShowScorePopupNow(string title, IReadOnlyList<int> debts, IReadOnlyList<int[]> roundHistory, bool fadeOut,
            Action onFadedOut)
        {
            _scoreTitle.text = title;
            _scoreSubtitle.text = _endReason.text; // 종료 사유를 점수판 안에도 표시

            var cols = PlayerCount + 1;
            var rows = roundHistory.Count + 3; // 등수 + 닉네임 + 판별 + 합계
            ((RectTransform)_scorePopup.transform).sizeDelta = new Vector2(
                cols * 210f + (cols - 1) * 4f + 40f,
                rows * 58f + (rows - 1) * 4f + 116f + 34f);

            foreach (Transform child in _scoreGrid)
            {
                Destroy(child.gameObject);
            }

            // 현재 등수: 누적 빚이 낮을수록 상위(동점은 공동 등수)
            var ranks = new int[PlayerCount];
            for (var s = 0; s < PlayerCount; s++)
            {
                ranks[s] = 1;
                for (var o = 0; o < PlayerCount; o++)
                {
                    if (debts[o] < debts[s])
                    {
                        ranks[s]++;
                    }
                }
            }

            // 열 순서는 순위 오름차순 — 1위가 왼쪽(동점은 좌석 순)
            var order = Enumerable.Range(0, PlayerCount).OrderBy(s => debts[s]).ThenBy(s => s).ToArray();

            // 등수 행(닉네임 위 별도 셀)
            AddCell("", Color.white, bold: false);
            foreach (var s in order)
            {
                AddCell($"{ranks[s]}위", new Color(0.75f, 0.77f, 0.83f), bold: true, size: 30);
            }

            // 닉네임 행 — 내 닉네임은 하늘색으로만 구분
            AddCell("라운드", GoldText, bold: true, size: 30);
            foreach (var s in order)
            {
                AddCell(Nicknames[s], s == MySeat ? MineText : new Color(0.96f, 0.94f, 0.86f), bold: true, size: 26, fit: true);
            }

            for (var r = 0; r < roundHistory.Count; r++)
            {
                AddCell($"{r + 1}", new Color(0.75f, 0.77f, 0.83f), bold: true, size: 30);
                foreach (var s in order)
                {
                    var value = roundHistory[r][s];
                    AddCell(value.ToString("+0;-0;0"), ScoreColor(value), bold: false);
                }
            }

            AddCell("계", GoldText, bold: true);
            foreach (var s in order)
            {
                AddCell($"{debts[s]}", Color.white, bold: true);
            }

            _scorePopup.SetActive(true);
            ScorePopupShown?.Invoke();
            _scorePopup.transform.SetAsLastSibling();
            _scorePopupGroup.alpha = 1f;
            _endReason.gameObject.SetActive(false); // 사유는 팝업 안에 표시 — 뒤에 비치는 중복 텍스트 숨김

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
            if (_scoreDelay != null)
            {
                StopCoroutine(_scoreDelay);
                _scoreDelay = null;
            }

            if (_scoreFade != null)
            {
                StopCoroutine(_scoreFade);
                _scoreFade = null;
            }

            _scorePopup.SetActive(false);
            _endReason.gameObject.SetActive(true);
        }

        /// <summary>점수표 셀: 아웃라인 텍스트(배경 없음 — 색·크기로만 구분해 가독성 유지).</summary>
        private void AddCell(string text, Color color, bool bold, int size = 36, bool fit = false)
        {
            var t = UiKit.CreateText(_scoreGrid, text, size, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            t.color = color;
            if (bold)
            {
                t.fontStyle = FontStyle.Bold;
            }

            TableArt.AddOutline(t);
            if (fit)
            {
                FitText(t, 15, size);
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
            _endReason.gameObject.SetActive(true); // 팝업이 사라지면 테이블 위 사유 다시 표시
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
                SetButtonLabel(_pongBtn, "뽕");
            }
        }

        private IEnumerator PongCountdown(int seconds)
        {
            for (var t = seconds; t > 0; t--)
            {
                SetButtonLabel(_pongBtn, $"뽕 ({t})");
                yield return new WaitForSeconds(1f);
            }
        }
    }
}
