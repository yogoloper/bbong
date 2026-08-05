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

            BuildGuidePanel();
            StartCoroutine(RunTutorial());
        }

        // ── 안내 패널 ──

        private void BuildGuidePanel()
        {
            var panel = UiKit.CreatePanel(_table.CanvasGo.transform, new Color(0.09f, 0.12f, 0.21f, 0.96f));
            if (UiArt.Panel9 != null)
            {
                panel.sprite = UiArt.Panel9;
                panel.type = Image.Type.Sliced;
            }

            UiKit.Anchor(panel.rectTransform, new Vector2(0.16f, 0.775f), new Vector2(0.84f, 0.99f));
            var shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(6f, -6f);

            _guideText = UiKit.CreateText(panel.transform, "", 28, TextAnchor.MiddleLeft,
                new Vector2(0.03f, 0.08f), new Vector2(0.77f, 0.92f));
            _guideText.color = new Color(0.96f, 0.95f, 0.90f);
            _guideText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _guideText.resizeTextForBestFit = true;
            _guideText.resizeTextMinSize = 16;
            _guideText.resizeTextMaxSize = 28;

            var next = UiKit.CreateButton(panel.transform, "다음",
                new Vector2(0.80f, 0.10f), new Vector2(0.975f, 0.45f), () => _nextPressed = true, 28);
            next.GetComponent<Image>().color = UiKit.Accent;
            _nextLabel = next.GetComponentInChildren<Text>();
            _nextLabel.fontStyle = FontStyle.Bold;
            _nextBtn = next.gameObject;
        }

        private void Guide(string message, bool showNext, string nextLabel = "다음")
        {
            _guideText.text = message;
            _nextLabel.text = nextLabel;
            _nextBtn.SetActive(showNext);
        }

        private IEnumerator WaitNext()
        {
            _nextPressed = false;
            yield return new WaitUntil(() => _nextPressed);
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
                    cumulativeDebt = 0
                }).ToArray()
            };
            _table.Render(view, _laidNow);
        }

        // ── 레슨 진행 ──

        private IEnumerator RunTutorial()
        {
            yield return LessonBasics();
            yield return LessonPong();
            yield return LessonNaturalPong();
            yield return LessonHandClearBak();
            yield return LessonMeld();
            yield return LessonStop();
            yield return LessonBagaji();

            Guide("일곱 가지 레슨을 모두 마쳤습니다. 축하합니다!\n" +
                  "이제 [연습]에서 봇을 상대로 실전 감각을 익혀보세요.\n" +
                  "실전에서는 매 행동에 5초 제한이 있다는 것, 잊지 마세요!", true, "로비로");
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

            Guide("「뽕」에 오신 것을 환영합니다!\n" +
                  "같은 숫자 카드를 모아 \"뽕!\"을 외치는 게임입니다.\n" +
                  "판마다 점수(빚)를 적게 쌓고, 5판 뒤 빚이 가장 적은 사람이 우승합니다.", true);
            yield return WaitNext();

            Guide("차례가 오면 자동으로 덱에서 한 장을 뽑아 손패가 6장이 되고,\n" +
                  "반드시 한 장을 버려서 5장으로 유지합니다.", true);
            yield return WaitNext();

            _round = _round.Draw();
            _table.DrawFx(0);
            Show(RoundPhase.WaitingDiscard, 0);
            Guide("방금 덱에서 7을 뽑았습니다.\n필요 없어 보이는 카드를 아무거나 클릭해서 버려보세요!", false);
            yield return WaitCard(_ => true);

            var tossed = _clickedCard!.Value;
            _round = _round.Discard(tossed);
            _table.AddDiscard(tossed);
            _table.PlayDiscardSfx();
            Show(RoundPhase.TurnGap, 1);

            Guide("잘하셨어요! 버린 카드는 가운데 더미에 쌓이고, 차례가 다음 사람에게 넘어갑니다.\n" +
                  "'뽑고 버리기' — 이것이 게임의 기본 흐름입니다.", true);
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

            Guide("이번엔 이 게임의 핵심, '뽕'!\n" +
                  "내 손에 8이 두 장 보이시죠? 너구리 사범의 차례를 지켜보세요.", true);
            yield return WaitNext();

            var botToss = C(8, CardColor.Green);
            _round = _round.Discard(botToss);
            _table.AddDiscard(botToss);
            _table.PlayDiscardSfx();
            Show(RoundPhase.PongWindow, 1, canPong: true, pongNumber: 8);

            Guide("사범이 8을 버렸습니다! 내 8 두 장과 합치면 세 장 —\n지금이 기회입니다. [뽕] 버튼을 누르세요!", false);
            yield return new WaitUntil(() => _pongPressed);
            _pongPressed = false;

            var laid = _round.Players[0].Hand.Cards.Where(c => c.Number == 8).ToList();
            _laidNow.AddRange(laid);
            _table.AddGroup(laid);
            _table.PongFx($"{_names[0]}\n8뽕!");
            Show(RoundPhase.WaitingPongDiscard, 0);

            Guide("8 세 장을 테이블에 내려놓았습니다. 내려놓은 카드는 판이 끝날 때 0점!\n" +
                  "뽕을 한 뒤에는 한 장을 더 버립니다 — 남은 카드 중 하나를 클릭하세요.", false);
            yield return WaitCard(c => c.Number != 8);

            var toss = _clickedCard!.Value;
            _round = _round.Pong(0, toss);
            _laidNow.Clear();
            _table.AddDiscard(toss);
            _table.PlayDiscardSfx();
            Show(RoundPhase.TurnGap, 1);

            Guide("뽕 완성! 손패가 5장에서 2장으로 확 줄었습니다.\n" +
                  "손패가 적을수록 판이 끝날 때 남는 빚도 적습니다.\n" +
                  "차례는 뽕한 사람의 다음 사람부터 이어집니다.", true);
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

            Guide("'자연뽕'도 있습니다. 남이 버린 카드를 가로채는 뽕과 달리,\n" +
                  "내가 뽑은 카드로 같은 숫자 세 장이 손에 모이는 경우입니다.", true);
            yield return WaitNext();

            _round = _round.Draw();
            _table.DrawFx(0);
            Show(RoundPhase.WaitingDiscard, 0, canNatural: true, naturalNumber: 5);
            Guide("방금 5를 뽑아서 손에 5가 세 장이 됐습니다!\n[자연뽕] 버튼을 누르세요.", false);
            yield return new WaitUntil(() => _naturalPressed);
            _naturalPressed = false;

            var laid = _round.Players[0].Hand.Cards.Where(c => c.Number == 5).Take(3).ToList();
            _laidNow.AddRange(laid);
            _table.AddGroup(laid);
            _table.PongFx($"{_names[0]}\n5자연뽕!");
            Show(RoundPhase.WaitingDiscard, 0);
            Guide("5 세 장을 내려놓았습니다.\n뽕과 마찬가지로 한 장을 더 버립니다 — 카드를 클릭하세요.", false);
            yield return WaitCard(c => c.Number != 5);

            var toss = _clickedCard!.Value;
            _round = _round.NaturalPong(5, toss);
            _laidNow.Clear();
            _table.AddDiscard(toss);
            _table.PlayDiscardSfx();
            Show(RoundPhase.TurnGap, 1);

            Guide("자연뽕 완성! 효과는 뽕과 같습니다 — 내려놓은 세 장은 0점.\n" +
                  "그런데 한 판에 뽕을 두 번 하면 손패가 전부 사라지겠죠?\n" +
                  "그때 무슨 일이 벌어지는지, 다음 레슨에서 확인해봅시다.", true);
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

            Guide("누군가 뽕을 두 번 해서 손패를 전부 털면 '손 털기' — 그 즉시 판이 끝납니다.\n" +
                  "지금 너구리 사범은 뽕을 한 번 한 상태라 손이 딱 2장 남았습니다.", true);
            yield return WaitNext();

            Show(RoundPhase.WaitingDiscard, 0);
            Guide("만약 사범이 기다리는 숫자를 내가 버려주면 어떻게 될까요?\n직접 확인해봅시다 — 손에 있는 9를 버려보세요.", false);
            yield return WaitCard(c => c.Number == 9);

            var tossed = _clickedCard!.Value;
            _round = _round.Discard(tossed);
            _table.AddDiscard(tossed);
            _table.PlayDiscardSfx();

            var laid = _round.Players[1].Hand.Cards.ToList(); // 사범 손의 9 두 장(버린 9는 더미에 그대로)
            _round = _round.Pong(1, null); // 사범의 두 번째 뽕 = 손 털기
            _table.AddGroup(laid);
            _table.PongFx($"{_names[1]}\n9뽕!");
            Show(RoundPhase.RoundOver, 1);
            _table.ShowCallout($"{_names[1]}\n손 털기!", new Color(1f, 0.4f, 0.35f));

            Guide("사범이 내 9를 받아 두 번째 뽕 — 손패를 모두 털고 판이 즉시 끝났습니다!\n" +
                  "게다가 마지막 카드를 건네준 나는 '박'으로 +20 벌점까지 받습니다.\n" +
                  "손패가 2장뿐인 상대가 있다면, 버리는 카드를 신중하게 고르세요.", true);
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

            Guide("'족보'는 한 방 역전기입니다.\n" +
                  "카드를 뽑은 직후 6장이 특별한 조합을 이루면,\n그 자리에서 선언해 판을 끝내고 점수를 크게 깎을 수 있습니다.", true);
            yield return WaitNext();

            _round = _round.Draw();
            _table.DrawFx(0);
            var meld = HandEvaluator.Evaluate(_round.Players[0].Hand);
            Show(RoundPhase.WaitingDiscard, 0, canMeld: true, meldType: meld.Type.ToString(), meldScore: meld.Score);
            Guide("지금 손패가 1-2-3-4-5-6, 연속 6장 = '스트레이트'!\n[스트레이트] 버튼으로 선언하세요.", false);
            yield return new WaitUntil(() => _meldPressed);
            _meldPressed = false;

            _table.ShowMeldSet(_round.Players[0].Hand.Cards, 0);
            _table.PongFx($"{_names[0]}\n{MeldNames.Korean(meld.Type)}!");
            Show(RoundPhase.RoundOver, 0);

            Guide("족보 완성! 족보는 모두 다섯 가지입니다.\n" +
                  "스트레이트(연속 6장) · 또이또이(같은 숫자 2+2+2 또는 3+3) · 총통(같은 숫자 4장)\n" +
                  "10이하(6장 합이 10 이하) · 66이상(6장 합이 66 이상) — 특히 총통·10이하·66이상은 -100점!", true);
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

            Guide("이번엔 '스톱'. 판에서 두 명 이상이 뽕을 한 상태라면,\n" +
                  "뽕을 했던 사람은 남은 손패 합이 10 이하일 때 스스로 판을 끝낼 수 있습니다.", true);
            yield return WaitNext();

            Show(RoundPhase.WaitingStop, 0, canStop: true);
            Guide("지금 나와 사범 둘 다 뽕을 했고, 내 남은 손패는 1+2=3.\n[스톱] 버튼으로 판을 끝내세요!", false);
            yield return new WaitUntil(() => _stopPressed);
            _stopPressed = false;

            _table.ShowMeldSet(_round.Players[0].Hand.Cards, 0);
            _table.PlayStopSfx();
            _table.ShowCallout($"{_names[0]}\n스톱!", new Color(0.55f, 0.85f, 1f));
            Show(RoundPhase.RoundOver, 0);

            Guide("스톱! 나는 빚 없이 판을 끝내고, 다른 사람들은 남은 손패 합만큼 빚을 얻습니다.\n" +
                  "단, 스톱에는 함정이 하나 있습니다... 다음 레슨에서!", true);
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

            Guide("스톱의 함정, '바가지'!\n" +
                  "지금 사범이 손패 합 8(3+5)로 스톱을 외치려 합니다.\n그런데 내 손패는 합 2(1+1)... 사범보다 낮습니다.", true);
            yield return WaitNext();

            _table.PlayStopSfx();
            _table.ShowCallout($"{_names[1]}\n바가지!", new Color(1f, 0.4f, 0.35f));
            _table.ShowMeldSet(_round.Players[0].Hand.Cards, 0);
            Show(RoundPhase.RoundOver, 1);

            Guide("바가지! 스톱한 사람보다 손패 합이 낮은 뽕 플레이어가 있으면 스톱은 실패합니다.\n" +
                  "사범은 +30 벌점을 물고, 가장 낮았던 내가 이 판의 승자 — 이게 '박 씌우기'입니다.\n" +
                  "뽕을 했다면 손패 합을 최대한 낮게 유지하는 것이 공격과 수비 모두에 유리합니다.", true);
            yield return WaitNext();
        }
    }
}
