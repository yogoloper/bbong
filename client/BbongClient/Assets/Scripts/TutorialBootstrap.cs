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
            StartCoroutine(RunTutorial());
        }

        // ── 안내 패널 ──

        private void BuildGuidePanel()
        {
            // 판 색은 PanelBg — 불투명으로만 올린다(뒤 텍스트 비침 방지)
            var opaque = new Color(UiTheme.PanelBg.r, UiTheme.PanelBg.g, UiTheme.PanelBg.b, 1f);
            var panel = UiKit.CreatePanel(_table.CanvasGo.transform, opaque);
            if (UiArt.Panel9 != null)
            {
                panel.sprite = UiArt.Panel9;
                panel.type = Image.Type.Sliced;
                panel.color = opaque;
            }

            // 교습 밴드: 맞은편 좌석을 통째로 덮는 층. 화면 꼭대기에 붙이는 것보다 한 뼘
            // 아래라 시선이 편하고, 하한은 버림 더미 산개 상단(≈0.69)이 정한다.
            // 높이 고정 — 단계마다 높이가 변하면 아래 판이 들썩여서 빈 여백보다 나쁘다.
            UiKit.Anchor(panel.rectTransform, new Vector2(0.165f, 0.775f), new Vector2(0.935f, 0.965f));
            var shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(8f, -8f);

            // 폰트는 36 고정. bestFit은 내용이 많은 단계일수록 글자를 줄이는 — 정확히
            // 거꾸로 가는 — 장치라 쓰지 않는다. 대신 문구를 3줄 상한으로 맞춘다(Guide 가드).
            // 세로 0.13~0.87: 3줄(42×1.15×3 ≈ 145유닛)이 차도 위아래로 27유닛씩 숨 쉴 여백
            _guideText = UiKit.CreateText(panel.transform, "", 42, TextAnchor.MiddleLeft,
                new Vector2(0.022f, 0.13f), new Vector2(0.978f, 0.87f));
            _guideText.color = UiTheme.InkOn;
            _guideText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _guideText.lineSpacing = 1.15f;

            // '다음'은 실전 액션 버튼(뽕/패스)과 같은 자리·같은 너비의 우측 하단 바에 둔다 —
            // 튜토리얼 내내 "행동은 오른쪽 아래"라는 실전 감각을 그대로 익히게 한다.
            // 다음이 보일 때 액션 버튼은 항상 숨어 있어(단계가 상호배타) 자리 다툼이 없다.
            var next = _table.AddBarButton("다음", () => _nextPressed = true);
            _nextLabel = next.GetComponentInChildren<Text>();
            _nextBtn = next.gameObject;
        }

        private void Guide(string message, bool showNext, string nextLabel = "다음")
        {
            WarnIfOverflow(message);
            _guideText.text = message;
            _nextLabel.text = nextLabel;
            _nextBtn.SetActive(showNext);
        }

        /// <summary>
        /// 밴드는 42px 고정 2줄 상한 — 길면 문구를 쪼개 '다음'으로 잇는다. 넘치면 조용히
        /// 삐져나가므로 렌더 줄 수를 어림해 개발 중에 바로 잡는다.
        /// 한 줄 예산은 16:9 기준 전각 33자(ASCII는 0.5자).
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

                lines += Mathf.Max(1, Mathf.CeilToInt(width / 33f));
            }

            if (lines > 2)
            {
                Debug.LogWarning($"[Tutorial] 안내 {lines}줄 — 2줄 초과: {message[..Mathf.Min(20, message.Length)]}…");
            }
        }

        private IEnumerator WaitNext()
        {
            _nextPressed = false;
            yield return new WaitUntil(() => _nextPressed);
            // 누르는 즉시 감춘다 — 연출(카드 비행·1초 호흡)이 끝나고 뽕/패스로 바뀔 때까지
            // 다음이 남아 있으면 전환이 굼떠 보인다. 다음 단계의 Guide가 필요하면 다시 켠다.
            _nextBtn.SetActive(false);
        }

        private IEnumerator WaitCard(System.Func<Card, bool> valid)
        {
            _clickedCard = null;
            while (true)
            {
                yield return new WaitUntil(() => _clickedCard.HasValue);
                if (valid(_clickedCard.Value))
                {
                    yield break;
                }

                _clickedCard = null; // 대상 아님 — 계속 대기
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
            _table.DrawFx(0);
            Show(RoundPhase.WaitingDiscard, 0);
            Guide("방금 7을 뽑았습니다.\n필요 없어 보이는 카드를 하나 골라 버려보세요!", false);
            yield return WaitCard(_ => true);

            var tossed = _clickedCard!.Value;
            _round = _round.Discard(tossed);
            _table.DiscardFx(0, tossed);
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
            _table.DrawFx(1);
            Show(RoundPhase.WaitingDiscard, 1);
            yield return new WaitForSeconds(0.9f);

            var botToss = C(8, CardColor.Green);
            _round = _round.Discard(botToss);
            _table.DiscardFx(1, botToss);
            Show(RoundPhase.TurnGap, 1); // 버리는 즉시 사범 손패 수 갱신
            yield return new WaitForSeconds(1f); // 카드가 날아가 놓이는 걸 본 뒤에 뽕 타임
            Show(RoundPhase.PongWindow, 1, canPong: true, pongNumber: 8);

            Guide("사범이 8을 버렸습니다! 같은 숫자 두 장이면 뽕을 부를 수 있어요.\n" +
                  "지금입니다, [뽕] 버튼을 누르세요!", false);
            yield return new WaitUntil(() => _pongPressed);
            _pongPressed = false;

            var laid = _round.Players[0].Hand.Cards.Where(c => c.Number == 8).ToList();
            _laidNow.AddRange(laid);
            _table.GroupFx(0, laid);
            _table.PongFx($"{_names[0]}\n8뽕!");
            Show(RoundPhase.WaitingPongDiscard, 0);

            Guide("버린 8 위에 내 8 두 장을 얹었습니다. 손을 떠난 카드는 빚 제외!\n" +
                  "이제 남은 카드 중 하나를 골라 마저 버리세요.", false);
            yield return WaitCard(c => c.Number != 8);

            var toss = _clickedCard!.Value;
            _round = _round.Pong(0, toss);
            _laidNow.Clear();
            _table.DiscardFx(0, toss);
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
            _table.DrawFx(0);
            Show(RoundPhase.WaitingDiscard, 0, canNatural: true, naturalNumber: 5);
            Guide("방금 5를 뽑아서 손에 5가 세 장이 됐습니다!\n[자연뽕] 버튼을 누르세요.", false);
            yield return new WaitUntil(() => _naturalPressed);
            _naturalPressed = false;

            var laid = _round.Players[0].Hand.Cards.Where(c => c.Number == 5).Take(3).ToList();
            _laidNow.AddRange(laid);
            _table.GroupFx(0, laid);
            _table.PongFx($"{_names[0]}\n5자연뽕!");
            Show(RoundPhase.WaitingDiscard, 0);
            Guide("내 손의 5 세 장을 그대로 내려놓았습니다.\n뽕과 마찬가지로 1장을 마저 버립니다. 카드를 클릭하세요.", false);
            yield return WaitCard(c => c.Number != 5);

            var toss = _clickedCard!.Value;
            _round = _round.NaturalPong(5, toss);
            _laidNow.Clear();
            _table.DiscardFx(0, toss);
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
            _table.DrawFx(0);
            Show(RoundPhase.WaitingDiscard, 0);
            Guide("붉은 뒷면인 상대에게 같은 숫자를 버려주면 어떻게 될까요?\n직접 해봅시다. 손에 있는 9를 버려보세요.", false);
            yield return WaitCard(c => c.Number == 9);

            var tossed = _clickedCard!.Value;
            _round = _round.Discard(tossed);
            _table.DiscardFx(0, tossed);
            Show(RoundPhase.TurnGap, 1); // 버린 카드가 손에서 빠진 상태로 재렌더 — 안 하면 1초간 유령 카드가 남는다
            yield return new WaitForSeconds(1f); // 내 카드가 놓인 걸 보고 나서 사범이 뽕

            var laid = _round.Players[1].Hand.Cards.ToList(); // 사범 손의 9 두 장(버린 9는 더미에 그대로)
            _round = _round.Pong(1, null); // 사범의 두 번째 뽕 = 손 털기
            _table.GroupFx(1, laid);
            _table.PongFx($"{_names[1]}\n9뽕!");
            Show(RoundPhase.RoundOver, 1);
            _table.ShowCallout($"{_names[1]}\n손 털기!", new Color(1f, 0.4f, 0.35f));

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
            _table.DrawFx(0);
            Show(RoundPhase.WaitingDiscard, 0);

            Guide("붉은 뒷면은 보고 피할 수 있지만, 경고 없는 바가지도 있습니다.\n" +
                  "직접 겪어보죠. 내 차례입니다 — 손에 있는 2를 버려보세요.", false);
            yield return WaitCard(c => c.Number == 2);

            var tossed = _clickedCard!.Value;
            _round = _round.Discard(tossed);
            _table.DiscardFx(0, tossed);
            Show(RoundPhase.TurnGap, 1); // 버린 카드가 손에서 빠진 상태로 재렌더
            yield return new WaitForSeconds(1f); // 내 카드가 놓인 걸 보고 나서 사범이 뽕

            // 사범: 내 2를 받아 뽕 → 남은 5,5,5를 그 자리에서 자연뽕 → 손 털기.
            // 엔진에 뽕+자연뽕 연쇄를 태우는 대신 연출만 하고, 내려놓을 때마다 사범 손패 수가
            // 맞는 상태로 뷰를 재구성한다. Setup()은 타임라인(내려놓은 묶음)까지 지우므로 안 쓴다.
            var pongLaid = _round.Players[1].Hand.Cards.Where(c => c.Number == 2).ToList();
            _table.GroupFx(1, pongLaid);
            _table.PongFx($"{_names[1]}\n2뽕!");
            _round = RoundState.Restore(new[]
                {
                    new Player(0, new Hand(_round.Players[0].Hand.Cards.ToArray())),
                    new Player(1, new Hand(new[] { C(5, CardColor.Red), C(5, CardColor.Blue), C(5, CardColor.Green) }), PongCount: 1),
                    new Player(2, new Hand(_round.Players[2].Hand.Cards.ToArray())),
                    new Player(3, new Hand(_round.Players[3].Hand.Cards.ToArray()))
                },
                new[] { C(1, CardColor.Red) }, new Card[0], 1, new SeededRandom(1));
            Show(RoundPhase.WaitingPongDiscard, 1); // 내려놓은 2장이 사범 손에서도 빠진 상태
            yield return new WaitForSeconds(1.2f);

            var naturalLaid = _round.Players[1].Hand.Cards.ToList(); // 남은 5,5,5
            _table.GroupFx(1, naturalLaid);
            _table.PongFx($"{_names[1]}\n5자연뽕!");
            _round = RoundState.Restore(new[]
                {
                    new Player(0, new Hand(_round.Players[0].Hand.Cards.ToArray())),
                    new Player(1, new Hand(System.Array.Empty<Card>()), PongCount: 2),
                    new Player(2, new Hand(_round.Players[2].Hand.Cards.ToArray())),
                    new Player(3, new Hand(_round.Players[3].Hand.Cards.ToArray()))
                },
                new[] { C(1, CardColor.Red) }, new Card[0], 1, new SeededRandom(1));
            Show(RoundPhase.RoundOver, 1); // 손 털기 — 사범 손 0장
            yield return new WaitForSeconds(0.8f);

            _table.ShowCallout($"{_names[1]}\n자연뽕 바가지!", new Color(1f, 0.4f, 0.35f));

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
            _table.DrawFx(0);
            var meld = HandEvaluator.Evaluate(_round.Players[0].Hand);
            Show(RoundPhase.WaitingDiscard, 0, canMeld: true, meldType: meld.Type.ToString(), meldScore: meld.Score);
            Guide("지금 손패가 1-2-3-4-5-6, 연속 6장이면 '스트레이트'입니다!\n[스트레이트] 버튼을 누르세요.", false);
            yield return new WaitUntil(() => _meldPressed);
            _meldPressed = false;

            _table.ShowMeldSet(_round.Players[0].Hand.Cards, 0);
            _table.PongFx($"{_names[0]}\n{MeldNames.Korean(meld.Type)}!");
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
            yield return new WaitUntil(() => _stopPressed);
            _stopPressed = false;

            _table.ShowMeldSet(_round.Players[0].Hand.Cards, 0);
            _table.PlayStopSfx();
            _table.ShowCallout($"{_names[0]}\n스톱!", new Color(0.55f, 0.85f, 1f));
            Show(RoundPhase.RoundOver, 0);

            Guide("스톱 성공! 10−손합만큼 청산 — 내 합이 3이니 7점을 덜었죠.\n" +
                  "나머지는 손합만큼 빚집니다. 그런데 함정이 하나… 다음 레슨에서!", true);
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

            _table.PlayStopSfx();
            _table.ShowCallout($"{_names[1]}\n스톱 바가지!", new Color(1f, 0.4f, 0.35f));
            _table.ShowMeldSet(_round.Players[0].Hand.Cards, 0);
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
