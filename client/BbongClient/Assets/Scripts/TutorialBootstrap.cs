using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Game;
using BbongCore.Online;
using BbongCore.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 튜토리얼: 미리 짜인 4인 판(RoundState.Restore)으로 기본 턴 → 뽕 → 자연뽕 → 족보 → 스톱/박을
    /// 차례로 체험한다. 덱이 수제 고정이라 모든 유저가 동일한 내용을 학습한다.
    /// 렌더는 공용 GameTableView, 진행은 안내 패널 + 실제 조작(카드/버튼 클릭)으로만.
    /// </summary>
    public sealed class TutorialBootstrap : MonoBehaviour
    {
        private static Card C(int n, CardColor c) => new(n, c);

        private GameTableView _table;
        private RoundState _round;
        private string[] _names;

        private Text _guideText;
        private GameObject _nextBtn;
        private Text _nextLabel;
        private GameObject _prevBtn;

        // '이전' = 결정적 리플레이. 튜토리얼은 전부 스크립트라, 처음부터 목표 직전 단계까지
        // 연출·대기 없이 즉시 재실행하면 판 상태(손패·더미·좌석)가 그대로 복원된다.
        // 사용자가 골랐던 카드는 기록해 두고 같은 카드로 재현한다.
        private int _bitIndex;                       // 현재 표시 중인 설명 비트 번호(1부터)
        private bool _ff;                            // 빨리감기(리플레이) 중인가
        private int _ffTarget;                       // 리플레이가 멈출 비트
        private int _ffChoiceCursor;                 // 리플레이가 소비한 카드 선택 수
        private readonly List<Card> _cardChoices = new(); // 사용자가 실제로 골랐던 카드들
        private readonly List<bool> _bitHasNext = new();  // 비트별 '다음' 유무 — 되감기 목표 계산용
        private Coroutine _runner;                   // 진행 코루틴 — 되감기 때 이것만 끊는다
        private bool _rewinding;                     // 블링크 전환 중 중복 되감기 방지

        // 뷰 입력 → 레슨 코루틴이 소비하는 플래그
        private bool _nextPressed;
        private Card? _clickedCard;
        private bool _pongPressed;
        private bool _naturalPressed;
        private bool _meldPressed;
        private bool _stopPressed;
        private readonly List<Card> _laidNow = new(); // 이번 레슨에서 내려놓아 손에서 숨길 카드

        private void Start()
        {
            var me = string.IsNullOrEmpty(Session.Nickname) ? "나" : Session.Nickname;
            _names = new[] { me, "너구리 사범", "수달 조교", "펭귄 문하생" };

            _table = gameObject.AddComponent<GameTableView>();
            // 좌석 배치는 실전과 완전히 동일하게 둔다. 맞은편(위) 좌석은 안내 밴드가 통째로
            // 덮는데, 25개 레슨 어디서도 그 좌석을 언급하지 않는 배경 인물이라 가려도 된다.
            _table.MySeat = 0;
            _table.PlayerCount = 4;
            _table.Nicknames = _names;
            _table.ShowTurnCountdown = false; // 튜토리얼은 시간 제한 없음
            _table.Build();

            _table.CardClicked += card => _clickedCard = card;
            _table.PongClicked += () => _pongPressed = true;
            _table.NaturalPongClicked += () => _naturalPressed = true;
            _table.MeldClicked += () => _meldPressed = true;
            _table.StopClicked += () => _stopPressed = true;
            _table.ExitConfirmText = "튜토리얼을 그만할까요?\n언제든 다시 볼 수 있어요.";
            _table.ExitConfirmed += () => UiKit.GoTo<MainLobbyBootstrap>(_table.CanvasGo, this);

            BuildGuidePanel();
            _runner = StartCoroutine(RunTutorial());
        }

        // ── 안내 패널 ──

        private void BuildGuidePanel()
        {
            // 살짝 반투명 — 박스가 맞은편 좌석·더미 위에 얹히므로 뒤가 은은히 비쳐야 덜 답답하다
            var face = new Color(UiTheme.PanelBg.r, UiTheme.PanelBg.g, UiTheme.PanelBg.b, 0.82f);
            var panel = UiKit.CreatePanel(_table.CanvasGo.transform, face);
            if (UiArt.Panel9 != null)
            {
                panel.sprite = UiArt.Panel9;
                panel.type = Image.Type.Sliced;
                panel.color = face;
            }

            // 교습 박스: 한 뼘 더 내려 맞은편 좌석의 닉네임·빚 줄이 위로 드러나게 한다.
            // (뒷면 카드는 반투명 배경 너머로 비친다.) 내부는 한 줄 구성 —
            // [텍스트 영역(좌) | 버튼 영역(우)] 둘 다 박스 가운데줄에 정렬한다.
            // 높이는 고정 — 단계마다 변하면 아래 판이 들썩여서 빈 여백보다 나쁘다.
            UiKit.Anchor(panel.rectTransform, new Vector2(0.165f, 0.66f), new Vector2(0.935f, 0.85f));
            var shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(8f, -8f);

            // 폰트는 42 고정. bestFit은 내용이 많은 단계일수록 글자를 줄이는 — 정확히
            // 거꾸로 가는 — 장치라 쓰지 않는다. 대신 문구를 2줄 상한으로 맞춘다(Guide 가드).
            // 텍스트 영역: 버튼 영역(0.748~) 직전까지 — 현행 문구가 개행 없이 들어가는 폭.
            _guideText = UiKit.CreateText(panel.transform, "", 42, TextAnchor.MiddleLeft,
                new Vector2(0.022f, 0.08f), new Vector2(0.738f, 0.92f));
            _guideText.color = UiTheme.InkOn;
            _guideText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _guideText.lineSpacing = 1.15f;

            // 버튼 영역: [이전][다음] 가로 배치, 박스 가운데줄에 정렬. 다음이 최우측,
            // 이전은 그 왼쪽 고정 슬롯 — 다음이 숨는 행동 비트에서도 같은 자리다.
            // 첫 설명에서는 이전이 숨는다.
            _prevBtn = UiKit.CreateButton(panel.transform, "이전",
                new Vector2(0.748f, 0.20f), new Vector2(0.862f, 0.80f), OnPrevPressed, 28).gameObject;
            var next = UiKit.CreateButton(panel.transform, "다음",
                new Vector2(0.872f, 0.20f), new Vector2(0.986f, 0.80f), () => _nextPressed = true, 28);
            _nextLabel = next.GetComponentInChildren<Text>();
            _nextLabel.fontStyle = FontStyle.Bold;
            _nextBtn = next.gameObject;
        }

        private void Guide(string message, bool showNext, string nextLabel = "다음")
        {
            _bitIndex++;
            if (_ff)
            {
                if (_bitIndex < _ffTarget)
                {
                    return; // 아직 빨리감기 중 — 표시 생략
                }

                // 목표 비트 도착: 리플레이 종료, 여기부터 실플레이 재개.
                // 목표 이후에 기록된 카드 선택은 무효가 되므로 잘라낸다.
                _ff = false;
                _cardChoices.RemoveRange(_ffChoiceCursor, _cardChoices.Count - _ffChoiceCursor);
            }

            WarnIfOverflow(message);
            if (_bitHasNext.Count < _bitIndex)
            {
                _bitHasNext.Add(showNext); // 비트 종류는 스크립트 고정 — 리플레이 때는 이미 기록돼 있다
            }

            _guideText.text = message;
            _nextLabel.text = nextLabel;
            _nextBtn.SetActive(showNext);
            // 행동 대기 비트에서도 '이전' 허용 — 직전 설명으로 돌아가 다시 읽을 수 있어야 한다.
            // 목표는 RewindTarget이 잡는다: 직전이 설명이면 그 설명, 묶음 중간이면 묶음 시작.
            _prevBtn.SetActive(RewindTarget() >= 1);
        }

        /// <summary>
        /// 되감기 목표 비트. 직전이 설명이면 그 비트, 직전이 플레이어블 구간이면
        /// 그 묶음(연속된 행동 비트들)의 첫 비트 — 행동을 처음부터 다시 하게 한다.
        /// </summary>
        private int RewindTarget()
        {
            var k = _bitIndex - 1;
            if (k < 1)
            {
                return 0;
            }

            if (_bitHasNext[k - 1])
            {
                return k;
            }

            while (k > 1 && !_bitHasNext[k - 2])
            {
                k--; // 행동 묶음의 첫 비트까지 거슬러 올라간다
            }

            return k;
        }

        /// <summary>
        /// '이전': 진행 코루틴을 끊고 처음부터 목표 비트까지 즉시 리플레이한다.
        /// 판 상태(손패·더미·좌석·버튼)까지 그 시점 그대로 복원되고, 거기서 실플레이가 이어진다.
        /// 상태 스왑이 한 프레임에 일어나 제자리에서 카드가 휙 바뀌어 보이므로,
        /// 화면 전체를 짧게 어둡혔다 밝히는 블링크로 감싸 "시간을 되돌렸다"로 읽히게 한다.
        /// </summary>
        private void OnPrevPressed()
        {
            var target = RewindTarget();
            if (_ff || _rewinding || target < 1)
            {
                return;
            }

            StartCoroutine(RewindWithBlink(target));
        }

        private Image _blinkScrim;

        /// <summary>
        /// 되감기 가림막. 리플레이는 레슨 경계마다 프레임을 넘기므로(중첩 코루틴) 몇 프레임에
        /// 걸쳐 중간 상태가 지나간다 — 반투명이면 바닥패가 생겼다 없어지는 게 비쳐 보인다.
        /// 완전 불투명 + 최상위 별도 캔버스로 올려 리플레이가 끝날 때까지 전부 가린다.
        /// </summary>
        private void EnsureBlinkScrim()
        {
            if (_blinkScrim != null)
            {
                return;
            }

            var go = new GameObject("RewindScrim", typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 700; // 설정 오버레이(500)보다도 위 — 무엇이 새로 생겨도 덮는다
            _blinkScrim = go.GetComponent<Image>();
            _blinkScrim.color = new Color(0f, 0f, 0f, 0f);
            _blinkScrim.raycastTarget = false;
        }

        private void OnDestroy()
        {
            if (_blinkScrim != null)
            {
                Destroy(_blinkScrim.gameObject); // 별도 캔버스라 화면 전환 정리에 안 딸려 간다
            }
        }

        private IEnumerator RewindWithBlink(int target)
        {
            _rewinding = true;
            EnsureBlinkScrim();
            _blinkScrim.raycastTarget = true; // 전환 중 오탭 방지

            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.10f)
            {
                _blinkScrim.color = new Color(0f, 0f, 0f, t);
                yield return null;
            }

            _blinkScrim.color = Color.black;

            if (_runner != null)
            {
                StopCoroutine(_runner); // 진행 코루틴만 — 이 블링크 코루틴은 계속 살아야 한다
            }

            // 아직 날아가던 카드·콜아웃은 여기서 전부 끊는다 — 블랙아웃이 끝난 뒤 뒤늦게
            // 착지해 되감은 판에 유령 카드를 남기는 것을 막는다.
            _table.CancelTransientFx();

            _ffTarget = target;
            _ff = true;
            _bitIndex = 0;
            _ffChoiceCursor = 0;
            _nextPressed = false;
            _pongPressed = false;
            _naturalPressed = false;
            _meldPressed = false;
            _stopPressed = false;
            _clickedCard = null;
            _laidNow.Clear();
            _runner = StartCoroutine(RunTutorial());

            // 리플레이가 목표 비트에 닿을 때까지 완전 불투명 유지 — 중간 상태는 한 장도 안 보인다
            yield return new WaitUntil(() => !_ff);

            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.18f)
            {
                _blinkScrim.color = new Color(0f, 0f, 0f, 1f - t);
                yield return null;
            }

            _blinkScrim.color = new Color(0f, 0f, 0f, 0f);
            _blinkScrim.raycastTarget = false;
            _rewinding = false;
        }

        /// <summary>
        /// 밴드는 42px 고정 2줄 상한 — 길면 문구를 쪼개 '다음'으로 잇는다. 넘치면 조용히
        /// 삐져나가므로 렌더 줄 수를 어림해 개발 중에 바로 잡는다.
        /// 한 줄 예산은 16:9 기준 전각 31자(ASCII는 0.5자).
        /// </summary>
        private static void WarnIfOverflow(string message)
        {
            var lines = 0;
            foreach (var logical in message.Split('\n'))
            {
                var width = 0f;
                foreach (var ch in logical)
                {
                    width += ch < 128 ? 0.5f : 1f;
                }

                lines += Mathf.Max(1, Mathf.CeilToInt(width / 31f));
            }

            if (lines > 2)
            {
                Debug.LogWarning($"[Tutorial] 안내 {lines}줄 — 2줄 초과: {message[..Mathf.Min(20, message.Length)]}…");
            }
        }

        private IEnumerator WaitNext()
        {
            if (_ff)
            {
                yield break; // 리플레이: 사용자가 이미 지나온 '다음'은 즉시 통과
            }

            _nextPressed = false;
            yield return new WaitUntil(() => _nextPressed);
            // 누르는 즉시 감춘다 — 연출(카드 비행·1초 호흡)이 끝나고 뽕/패스로 바뀔 때까지
            // 다음이 남아 있으면 전환이 굼떠 보인다. 다음 단계의 Guide가 필요하면 다시 켠다.
            _nextBtn.SetActive(false);
        }

        private IEnumerator WaitCard(System.Func<Card, bool> valid)
        {
            if (_ff)
            {
                _clickedCard = ReplayCard(valid); // 리플레이: 그때 골랐던 카드를 그대로
                yield break;
            }

            _clickedCard = null;
            while (true)
            {
                yield return new WaitUntil(() => _clickedCard.HasValue);
                if (valid(_clickedCard.Value))
                {
                    _cardChoices.Add(_clickedCard.Value); // 되감기 재현용 기록
                    yield break;
                }

                _clickedCard = null; // 대상 아님 — 계속 대기
            }
        }

        /// <summary>리플레이 중 카드 선택 재현. 기록이 모자라면(비정상) 손에서 첫 유효 카드.</summary>
        private Card ReplayCard(System.Func<Card, bool> valid)
        {
            if (_ffChoiceCursor < _cardChoices.Count)
            {
                return _cardChoices[_ffChoiceCursor++];
            }

            return _round.Players[0].Hand.Cards.First(valid);
        }

        /// <summary>버튼 입력 대기 — 리플레이 중에는 즉시 통과.</summary>
        private IEnumerator WaitSignal(System.Func<bool> signaled, System.Action reset)
        {
            if (_ff)
            {
                yield break;
            }

            yield return new WaitUntil(signaled);
            reset();
        }

        /// <summary>리플레이 중에는 건너뛰는 연출 호흡.</summary>
        private IEnumerator Beat(float seconds)
        {
            if (!_ff)
            {
                yield return new WaitForSeconds(seconds);
            }
        }

        // ── 연출 래퍼: 리플레이 중엔 비행 없이 상태만 쌓는다 ──

        private void FxDraw(int seat)
        {
            if (!_ff)
            {
                _table.DrawFx(seat);
            }
        }

        private void FxDiscard(int seat, Card card)
        {
            if (_ff)
            {
                _table.AddDiscard(card); // 더미 내용만 유지 — 다음 Show()가 그린다
            }
            else
            {
                _table.DiscardFx(seat, card);
            }
        }

        private void FxGroup(int seat, IEnumerable<Card> cards)
        {
            if (_ff)
            {
                _table.AddGroup(cards);
            }
            else
            {
                _table.GroupFx(seat, cards.ToList());
            }
        }

        private void FxPong(string text)
        {
            if (!_ff)
            {
                _table.PongFx(text);
            }
        }

        private void FxCallout(string text, Color color)
        {
            if (!_ff)
            {
                _table.ShowCallout(text, color);
            }
        }

        private void FxStopSfx()
        {
            if (!_ff)
            {
                _table.PlayStopSfx();
            }
        }

        private void FxMeldSet(IEnumerable<Card> cards, int laidSeat)
        {
            if (_ff)
            {
                _table.ShowMeldSetInstant(cards, laidSeat); // 리플레이: 비행 없이 즉시 — 유령 착지 방지
            }
            else
            {
                _table.ShowMeldSet(cards, laidSeat);
            }
        }

        // ── 판 리깅/렌더 ──

        private void Setup(Player[] players, Card[] draw, Card[] discard, int currentSeat)
        {
            _round = RoundState.Restore(players, draw, discard, currentSeat, new SeededRandom(1));
            _laidNow.Clear();
            _table.ClearTimeline();
            _table.SetEndReason("");
            foreach (var card in discard)
            {
                _table.AddDiscard(card);
            }
        }

        /// <summary>튜토리얼이 원하는 버튼/포커스만 켠 RoundView 합성 — 단계별 입력 게이팅.</summary>
        private void Show(string phase, int currentSeat, bool canPong = false, int pongNumber = 0,
            bool canStop = false, bool canMeld = false, string meldType = "", int meldScore = 0,
            bool canNatural = false, int naturalNumber = 0)
        {
            var view = new RoundView
            {
                mySeat = 0,
                currentSeat = currentSeat,
                phase = phase,
                actorSeat = currentSeat,
                drawPileCount = 20, // 연출용 고정(리깅 덱은 필요한 카드만 들어 있음)
                pongNumber = pongNumber,
                canStop = canStop,
                canMeld = canMeld,
                meldType = meldType,
                meldScore = meldScore,
                canNaturalPong = canNatural,
                naturalPongNumber = naturalNumber,
                canPong = canPong,
                myHand = CardDto.FromAll(_round.Players[0].Hand.Cards),
                seats = _round.Players.Select(p => new SeatView
                {
                    seat = p.Seat,
                    nickname = _names[p.Seat],
                    handCount = p.Hand.Count,
                    pairExposed = p.Hand.Count == 2 && p.Hand.Cards[0].Number == p.Hand.Cards[1].Number, // 쌍 공개(§7)
                    cumulativeDebt = 0
                }).ToArray()
            };
            _table.Render(view, _laidNow);

            // 중앙 프롬프트("~를 클릭하세요")는 안내 밴드와 같은 지시를 두 번 하는 중복이라
            // 억제한다. Render 직후 덮어써야 하므로 반드시 이 자리에서.
            _table.SetPrompt("");
        }

        // ── 레슨 진행 ──

        private IEnumerator RunTutorial()
        {
            yield return LessonBasics();
            yield return LessonPong();
            yield return LessonNaturalPong();
            yield return LessonHandClearBak();
            yield return LessonNaturalBagaji();
            yield return LessonMeld();
            yield return LessonStop();
            yield return LessonBagaji();

            Guide("수고하셨어요! 이제 [연습]에서 봇 상대로 감을 잡아보세요.\n" +
                  "실전에서는 행동마다 5초 제한이 있으니 서두르셔야 합니다!", true, "로비로");
            yield return WaitNext();
            UiKit.GoTo<MainLobbyBootstrap>(_table.CanvasGo, this);
        }

        private IEnumerator LessonBasics()
        {
            Setup(new[]
                {
                    new Player(0, new Hand(new[] { C(3, CardColor.Red), C(6, CardColor.Blue), C(9, CardColor.Green), C(11, CardColor.Yellow), C(12, CardColor.Red) })),
                    new Player(1, new Hand(new[] { C(1, CardColor.Red), C(4, CardColor.Blue), C(7, CardColor.Green), C(10, CardColor.Yellow), C(8, CardColor.Yellow) })),
                    new Player(2, new Hand(new[] { C(2, CardColor.Red), C(5, CardColor.Blue), C(8, CardColor.Green), C(11, CardColor.Blue), C(9, CardColor.Blue) })),
                    new Player(3, new Hand(new[] { C(1, CardColor.Blue), C(4, CardColor.Green), C(7, CardColor.Yellow), C(10, CardColor.Red), C(12, CardColor.Blue) }))
                },
                draw: new[] { C(7, CardColor.Blue) }, discard: new Card[0], currentSeat: 0);
            Show(RoundPhase.TurnGap, 0);

            Guide("나이롱뽕에 오신 걸 환영합니다!\n" +
                  "같은 숫자 카드를 모아 \"뽕!\"을 외치는 게임이에요.", true);
            yield return WaitNext();

            Guide("라운드가 끝나면 남은 손패 합이 '빚'이 되고,\n" +
                  "5라운드 누적 빚이 가장 적은 사람이 우승합니다.", true);
            yield return WaitNext();

            Guide("전원 5장으로 시작합니다.\n" +
                  "내 차례엔 한 장 뽑고 한 장 버려 5장을 유지해요.", true);
            yield return WaitNext();

            _round = _round.Draw();
            FxDraw(0);
            Show(RoundPhase.WaitingDiscard, 0);
            Guide("방금 7을 뽑았습니다.\n필요 없어 보이는 카드를 하나 골라 버려보세요!", false);
            yield return WaitCard(_ => true);

            var tossed = _clickedCard!.Value;
            _round = _round.Discard(tossed);
            FxDiscard(0, tossed);
            Show(RoundPhase.TurnGap, 1);

            Guide("좋아요! 버린 카드는 더미에 쌓이고 차례가 넘어갑니다.\n" +
                  "뽑고 버리고, 이게 게임의 기본 흐름입니다.", true);
            yield return WaitNext();
        }

        private IEnumerator LessonPong()
        {
            Setup(new[]
                {
                    new Player(0, new Hand(new[] { C(8, CardColor.Red), C(8, CardColor.Blue), C(2, CardColor.Green), C(5, CardColor.Yellow), C(11, CardColor.Red) })),
                    new Player(1, new Hand(new[] { C(8, CardColor.Green), C(1, CardColor.Red), C(4, CardColor.Blue), C(6, CardColor.Yellow), C(9, CardColor.Blue) })),
                    new Player(2, new Hand(new[] { C(2, CardColor.Blue), C(3, CardColor.Green), C(7, CardColor.Red), C(10, CardColor.Blue), C(12, CardColor.Green) })),
                    new Player(3, new Hand(new[] { C(1, CardColor.Green), C(3, CardColor.Yellow), C(6, CardColor.Red), C(9, CardColor.Yellow), C(5, CardColor.Green) }))
                },
                draw: new[] { C(4, CardColor.Red) }, discard: new Card[0], currentSeat: 1);
            Show(RoundPhase.WaitingDiscard, 1);

            Guide("이번엔 이 게임의 핵심, '뽕'입니다.\n" +
                  "내 손에 8이 두 장 보이시죠? 너구리 사범 차례를 지켜보세요.", true);
            yield return WaitNext();

            // 사범 턴도 실전처럼 — 한 장 뽑아 6장이 됐다가 한 장 버려 5장
            _round = _round.Draw();
            FxDraw(1);
            Show(RoundPhase.WaitingDiscard, 1);
            yield return Beat(0.9f);

            var botToss = C(8, CardColor.Green);
            _round = _round.Discard(botToss);
            FxDiscard(1, botToss);
            Show(RoundPhase.TurnGap, 1); // 버리는 즉시 사범 손패 수 갱신
            yield return Beat(1f); // 카드가 날아가 놓이는 걸 본 뒤에 뽕 타임
            Show(RoundPhase.PongWindow, 1, canPong: true, pongNumber: 8);

            Guide("사범이 8을 버렸습니다! 같은 숫자 두 장이면 뽕 찬스예요.\n" +
                  "지금입니다, [뽕] 버튼을 누르세요!", false);
            yield return WaitSignal(() => _pongPressed, () => _pongPressed = false);

            var laid = _round.Players[0].Hand.Cards.Where(c => c.Number == 8).ToList();
            _laidNow.AddRange(laid);
            FxGroup(0, laid);
            FxPong($"{_names[0]}\n8뽕!");
            Show(RoundPhase.WaitingPongDiscard, 0);

            Guide("버린 8 위에 내 8 두 장을 얹었습니다. 손을 떠난 카드는 빚 제외!\n" +
                  "이제 남은 카드 중 하나를 골라 마저 버리세요.", false);
            yield return WaitCard(c => c.Number != 8);

            var toss = _clickedCard!.Value;
            _round = _round.Pong(0, toss);
            _laidNow.Clear();
            FxDiscard(0, toss);
            Show(RoundPhase.TurnGap, 1);

            // 붉은 뒷면(쌍 경고) 설명은 여기서 예고하지 않는다 — 다음 레슨이 자연뽕이라
            // "다음 레슨에서 보시죠"가 어긋난다. 손 털기 레슨 도입부가 직접 설명한다.
            Guide("뽕 완성! 손패가 5장에서 2장으로 확 줄었죠.\n" +
                  "손패가 적을수록 라운드가 끝날 때 남는 빚도 적습니다.", true);
            yield return WaitNext();
        }

        private IEnumerator LessonNaturalPong()
        {
            Setup(new[]
                {
                    new Player(0, new Hand(new[] { C(5, CardColor.Red), C(5, CardColor.Blue), C(2, CardColor.Green), C(7, CardColor.Yellow), C(12, CardColor.Red) })),
                    new Player(1, new Hand(new[] { C(1, CardColor.Yellow), C(3, CardColor.Blue), C(6, CardColor.Green), C(9, CardColor.Red), C(11, CardColor.Blue) })),
                    new Player(2, new Hand(new[] { C(2, CardColor.Yellow), C(4, CardColor.Green), C(8, CardColor.Blue), C(10, CardColor.Green), C(10, CardColor.Yellow) })),
                    new Player(3, new Hand(new[] { C(3, CardColor.Red), C(6, CardColor.Blue), C(9, CardColor.Green), C(12, CardColor.Yellow), C(8, CardColor.Yellow) }))
                },
                draw: new[] { C(5, CardColor.Green) }, discard: new Card[0], currentSeat: 0);
            Show(RoundPhase.TurnGap, 0);

            Guide("'자연뽕'도 있습니다. 내 손에 같은 숫자가 3장 모이면\n" +
                  "남이 버리기를 기다릴 것 없이 내 턴에 바로 내려놓는 거예요.", true);
            yield return WaitNext();

            _round = _round.Draw();
            FxDraw(0);
            Show(RoundPhase.WaitingDiscard, 0, canNatural: true, naturalNumber: 5);
            Guide("방금 5를 뽑아서 손에 5가 세 장이 됐습니다!\n[자연뽕] 버튼을 누르세요.", false);
            yield return WaitSignal(() => _naturalPressed, () => _naturalPressed = false);

            var laid = _round.Players[0].Hand.Cards.Where(c => c.Number == 5).Take(3).ToList();
            _laidNow.AddRange(laid);
            FxGroup(0, laid);
            FxPong($"{_names[0]}\n5자연뽕!");
            Show(RoundPhase.WaitingDiscard, 0);
            Guide("내 손의 5 세 장을 그대로 내려놓았습니다.\n뽕과 마찬가지로 1장을 마저 버립니다. 카드를 클릭하세요.", false);
            yield return WaitCard(c => c.Number != 5);

            var toss = _clickedCard!.Value;
            _round = _round.NaturalPong(5, toss);
            _laidNow.Clear();
            FxDiscard(0, toss);
            Show(RoundPhase.TurnGap, 1);

            Guide("자연뽕 완성! 내려놓은 세 장은 빚에서 빠집니다.\n" +
                  "그런데 뽕을 두 번 해 손패가 다 사라지면? 다음 레슨에서 보시죠.", true);
            yield return WaitNext();
        }

        private IEnumerator LessonHandClearBak()
        {
            Setup(new[]
                {
                    new Player(0, new Hand(new[] { C(9, CardColor.Yellow), C(2, CardColor.Red), C(4, CardColor.Blue), C(6, CardColor.Green), C(11, CardColor.Blue) })),
                    new Player(1, new Hand(new[] { C(9, CardColor.Red), C(9, CardColor.Blue) }), PongCount: 1),
                    new Player(2, new Hand(new[] { C(1, CardColor.Green), C(3, CardColor.Blue), C(5, CardColor.Yellow), C(7, CardColor.Red), C(10, CardColor.Blue) })),
                    new Player(3, new Hand(new[] { C(2, CardColor.Blue), C(4, CardColor.Yellow), C(6, CardColor.Red), C(8, CardColor.Green), C(12, CardColor.Green) }))
                },
                draw: new[] { C(3, CardColor.Green) }, discard: new Card[0], currentSeat: 0);
            Show(RoundPhase.TurnGap, 0);

            Guide("뽕을 두 번 해 손을 털면 '손 털기' — 즉시 라운드가 끝나요.\n" +
                  "지금 너구리 사범은 뽕을 한 번 해서 손이 딱 2장입니다.", true);
            yield return WaitNext();

            Guide("사범 카드 뒷면이 붉죠?\n" +
                  "남은 2장이 같은 숫자라는 경고입니다.", true);
            yield return WaitNext();

            // 실전과 같게 내 턴은 뽑기부터 — 6장 상태에서 버리게 한다
            _round = _round.Draw();
            FxDraw(0);
            Show(RoundPhase.WaitingDiscard, 0);
            Guide("붉은 뒷면인 상대에게 같은 숫자를 버려주면 어떻게 될까요?\n직접 해봅시다. 손에 있는 9를 버려보세요.", false);
            yield return WaitCard(c => c.Number == 9);

            var tossed = _clickedCard!.Value;
            _round = _round.Discard(tossed);
            FxDiscard(0, tossed);
            Show(RoundPhase.TurnGap, 1); // 버린 카드가 손에서 빠진 상태로 재렌더 — 안 하면 1초간 유령 카드가 남는다
            yield return Beat(1f); // 내 카드가 놓인 걸 보고 나서 사범이 뽕

            var laid = _round.Players[1].Hand.Cards.ToList(); // 사범 손의 9 두 장(버린 9는 더미에 그대로)
            _round = _round.Pong(1, null); // 사범의 두 번째 뽕 = 손 털기
            FxGroup(1, laid);
            FxPong($"{_names[1]}\n9뽕!");
            Show(RoundPhase.RoundOver, 1);
            FxCallout($"{_names[1]}\n손 털기!", new Color(1f, 0.4f, 0.35f));

            Guide("사범이 내 9를 받아 두 번째 뽕, 손을 다 털었습니다.\n" +
                  "이게 '일반뽕 바가지'예요.", true);
            yield return WaitNext();

            Guide("버린 나는 손합에 30을 더해 물고, 사범은 0점이에요.\n" +
                  "나머지는 손합만큼 빚지고, 다음 선은 내가 잡습니다.", true);
            yield return WaitNext();
        }

        /// <summary>자연뽕 바가지를 설명이 아니라 직접 겪게 한다 — 경고 없이 당하는 게 핵심이라 체험이 맞다.</summary>
        private IEnumerator LessonNaturalBagaji()
        {
            Setup(new[]
                {
                    new Player(0, new Hand(new[] { C(2, CardColor.Red), C(6, CardColor.Blue), C(9, CardColor.Green), C(11, CardColor.Yellow), C(12, CardColor.Red) })),
                    new Player(1, new Hand(new[] { C(2, CardColor.Blue), C(2, CardColor.Yellow), C(5, CardColor.Red), C(5, CardColor.Blue), C(5, CardColor.Green) })),
                    new Player(2, new Hand(new[] { C(1, CardColor.Green), C(3, CardColor.Blue), C(7, CardColor.Red), C(10, CardColor.Blue), C(12, CardColor.Green) })),
                    new Player(3, new Hand(new[] { C(3, CardColor.Yellow), C(4, CardColor.Red), C(6, CardColor.Green), C(8, CardColor.Blue), C(10, CardColor.Yellow) }))
                },
                draw: new[] { C(1, CardColor.Red) }, discard: new Card[0], currentSeat: 0);

            // 실전과 같게 내 턴은 뽑기부터 — 6장 상태에서 버리게 한다
            _round = _round.Draw();
            FxDraw(0);
            Show(RoundPhase.WaitingDiscard, 0);

            Guide("붉은 뒷면은 보고 피할 수 있지만, 경고 없는 바가지도 있습니다.\n" +
                  "직접 겪어보죠. 내 차례입니다 — 손에 있는 2를 버려보세요.", false);
            yield return WaitCard(c => c.Number == 2);

            var tossed = _clickedCard!.Value;
            _round = _round.Discard(tossed);
            FxDiscard(0, tossed);
            Show(RoundPhase.TurnGap, 1); // 버린 카드가 손에서 빠진 상태로 재렌더
            yield return Beat(1f); // 내 카드가 놓인 걸 보고 나서 사범이 뽕

            // 사범: 내 2를 받아 뽕 → 남은 5,5,5를 그 자리에서 자연뽕 → 손 털기.
            // 엔진에 뽕+자연뽕 연쇄를 태우는 대신 연출만 하고, 내려놓을 때마다 사범 손패 수가
            // 맞는 상태로 뷰를 재구성한다. Setup()은 타임라인(내려놓은 묶음)까지 지우므로 안 쓴다.
            var pongLaid = _round.Players[1].Hand.Cards.Where(c => c.Number == 2).ToList();
            FxGroup(1, pongLaid);
            FxPong($"{_names[1]}\n2뽕!");
            _round = RoundState.Restore(new[]
                {
                    new Player(0, new Hand(_round.Players[0].Hand.Cards.ToArray())),
                    new Player(1, new Hand(new[] { C(5, CardColor.Red), C(5, CardColor.Blue), C(5, CardColor.Green) }), PongCount: 1),
                    new Player(2, new Hand(_round.Players[2].Hand.Cards.ToArray())),
                    new Player(3, new Hand(_round.Players[3].Hand.Cards.ToArray()))
                },
                new[] { C(1, CardColor.Red) }, new Card[0], 1, new SeededRandom(1));
            Show(RoundPhase.WaitingPongDiscard, 1); // 내려놓은 2장이 사범 손에서도 빠진 상태
            yield return Beat(1.2f);

            var naturalLaid = _round.Players[1].Hand.Cards.ToList(); // 남은 5,5,5
            FxGroup(1, naturalLaid);
            FxPong($"{_names[1]}\n5자연뽕!");
            _round = RoundState.Restore(new[]
                {
                    new Player(0, new Hand(_round.Players[0].Hand.Cards.ToArray())),
                    new Player(1, new Hand(System.Array.Empty<Card>()), PongCount: 2),
                    new Player(2, new Hand(_round.Players[2].Hand.Cards.ToArray())),
                    new Player(3, new Hand(_round.Players[3].Hand.Cards.ToArray()))
                },
                new[] { C(1, CardColor.Red) }, new Card[0], 1, new SeededRandom(1));
            Show(RoundPhase.RoundOver, 1); // 손 털기 — 사범 손 0장
            yield return Beat(0.8f);

            FxCallout($"{_names[1]}\n자연뽕 바가지!", new Color(1f, 0.4f, 0.35f));

            Guide("경고 없이 당했습니다! 이게 '자연뽕 바가지'입니다.\n" +
                  "내 2로 뽕을 하고, 남은 5,5,5를 바로 자연뽕해 손을 털었죠.", true);
            yield return WaitNext();

            Guide("피할 수 없는 만큼 벌도 무겁습니다 — 버린 나는 50점!\n" +
                  "노리는 사람이 있으니 뽕은 신중하게.", true);
            yield return WaitNext();
        }

        private IEnumerator LessonMeld()
        {
            Setup(new[]
                {
                    new Player(0, new Hand(new[] { C(1, CardColor.Red), C(2, CardColor.Blue), C(3, CardColor.Green), C(4, CardColor.Yellow), C(5, CardColor.Red) })),
                    new Player(1, new Hand(new[] { C(7, CardColor.Red), C(9, CardColor.Blue), C(11, CardColor.Green), C(10, CardColor.Yellow), C(2, CardColor.Red) })),
                    new Player(2, new Hand(new[] { C(6, CardColor.Yellow), C(8, CardColor.Red), C(10, CardColor.Blue), C(12, CardColor.Green), C(1, CardColor.Yellow) })),
                    new Player(3, new Hand(new[] { C(4, CardColor.Red), C(7, CardColor.Blue), C(9, CardColor.Green), C(11, CardColor.Yellow), C(5, CardColor.Green) }))
                },
                draw: new[] { C(6, CardColor.Blue) }, discard: new Card[0], currentSeat: 0);
            Show(RoundPhase.TurnGap, 0);

            Guide("'족보'는 한 방 역전기 — 뽑은 직후 6장이 특별한 조합이면\n" +
                  "선언과 동시에 라운드가 끝나고 누적 빚을 크게 덜어냅니다.", true);
            yield return WaitNext();

            _round = _round.Draw();
            FxDraw(0);
            var meld = HandEvaluator.Evaluate(_round.Players[0].Hand);
            Show(RoundPhase.WaitingDiscard, 0, canMeld: true, meldType: meld.Type.ToString(), meldScore: meld.Score);
            Guide("지금 손패가 1-2-3-4-5-6, 연속 6장이면 '스트레이트'입니다!\n[스트레이트] 버튼을 누르세요.", false);
            yield return WaitSignal(() => _meldPressed, () => _meldPressed = false);

            FxMeldSet(_round.Players[0].Hand.Cards, 0);
            FxPong($"{_names[0]}\n{MeldNames.Korean(meld.Type)}!");
            Show(RoundPhase.RoundOver, 0);

            Guide("족보 완성! 스트레이트는 여섯 장의 합만큼 빚을 탕감합니다.\n" +
                  "1+2+3+4+5+6이니 21점을 덜었죠.", true);
            yield return WaitNext();

            Guide("족보는 다섯: 스트레이트(연속 6장) · 또이또이(2+2+2/3+3, 빚 0)\n" +
                  "총통(같은 4장) · 10이하 · 66이상 — 셋 다 100점 탕감!", true);
            yield return WaitNext();
        }

        private IEnumerator LessonStop()
        {
            Setup(new[]
                {
                    new Player(0, new Hand(new[] { C(1, CardColor.Red), C(2, CardColor.Blue) }), PongCount: 1),
                    new Player(1, new Hand(new[] { C(9, CardColor.Red), C(9, CardColor.Blue) }), PongCount: 1),
                    new Player(2, new Hand(new[] { C(2, CardColor.Yellow), C(4, CardColor.Green), C(8, CardColor.Blue), C(10, CardColor.Green), C(10, CardColor.Yellow) })),
                    new Player(3, new Hand(new[] { C(3, CardColor.Red), C(6, CardColor.Blue), C(9, CardColor.Green), C(12, CardColor.Yellow), C(8, CardColor.Yellow) }))
                },
                draw: new[] { C(7, CardColor.Green) }, discard: new Card[0], currentSeat: 0);
            Show(RoundPhase.TurnGap, 0);

            Guide("'스톱': 뽕한 사람이 2명 이상이고 내 남은 2장 합이 10 이하면\n" +
                  "내 턴에 외칠 수 있어요. 뽕한 사람 중 손합이 가장 낮아야 성공!", true);
            yield return WaitNext();

            Show(RoundPhase.WaitingStop, 0, canStop: true);
            Guide("나와 사범 둘 다 뽕! 내 손합 3, 사범 18로 내가 훨씬 낮습니다.\n" +
                  "[스톱] 버튼으로 라운드를 끝내세요!", false);
            yield return WaitSignal(() => _stopPressed, () => _stopPressed = false);

            FxMeldSet(_round.Players[0].Hand.Cards, 0);
            FxStopSfx();
            FxCallout($"{_names[0]}\n스톱!", new Color(0.55f, 0.85f, 1f));
            Show(RoundPhase.RoundOver, 0);

            Guide("스톱 성공! 10−손합만큼 청산 — 내 합이 3이니 7점을 덜었죠.\n" +
                  "나머지는 손합만큼. 그런데 함정이 하나… 다음 레슨에서!", true);
            yield return WaitNext();
        }

        private IEnumerator LessonBagaji()
        {
            Setup(new[]
                {
                    new Player(0, new Hand(new[] { C(1, CardColor.Red), C(1, CardColor.Blue) }), PongCount: 1),
                    new Player(1, new Hand(new[] { C(3, CardColor.Red), C(5, CardColor.Blue) }), PongCount: 1),
                    new Player(2, new Hand(new[] { C(2, CardColor.Yellow), C(4, CardColor.Green), C(8, CardColor.Blue), C(10, CardColor.Green), C(10, CardColor.Yellow) })),
                    new Player(3, new Hand(new[] { C(3, CardColor.Green), C(6, CardColor.Blue), C(9, CardColor.Green), C(12, CardColor.Yellow), C(5, CardColor.Green) }))
                },
                draw: new[] { C(7, CardColor.Yellow) }, discard: new Card[0], currentSeat: 1);
            Show(RoundPhase.TurnGap, 1);

            Guide("스톱의 함정, '스톱 바가지'! 사범이 손합 8로 스톱을 외치려는데\n" +
                  "내 손합은 2 — 사범보다 낮습니다. 어떻게 될까요?", true);
            yield return WaitNext();

            FxStopSfx();
            FxCallout($"{_names[1]}\n스톱 바가지!", new Color(1f, 0.4f, 0.35f));
            FxMeldSet(_round.Players[0].Hand.Cards, 0);
            Show(RoundPhase.RoundOver, 1);

            Guide("스톱 바가지! 손합이 선언자보다 적거나 같은\n" +
                  "뽕 플레이어가 있으면 스톱은 실패합니다.", true);
            yield return WaitNext();

            Guide("실패한 사범은 손합 8에 30을 더해 38점을 물고,\n" +
                  "바가지를 먹인 나는 0점, 나머지는 손합만큼 빚을 집니다.", true);
            yield return WaitNext();

            Guide("다음 선은 당한 사범이 잡습니다.\n" +
                  "뽕을 했다면 손합을 낮게 — 공격에도 수비에도 좋아요!", true);
            yield return WaitNext();
        }
    }
}
