using System;
using System.Linq;
using BbongCore.Cards;
using BbongCore.Game;
using NUnit.Framework;

namespace BbongCore.Tests.Game;

[TestFixture]
public class PongTests
{
    private static Card C(int n, CardColor color) => new(n, color);
    private static Hand HandOf(params Card[] cards) => new(cards);

    /// <summary>seat0가 7을 버린 직후 상태(턴은 seat1로 넘어감). seat1은 7 두 장 보유.</summary>
    private static RoundState AfterSeat0DiscardedSeven()
    {
        var players = new[]
        {
            new Player(0, HandOf(C(2, CardColor.Red), C(5, CardColor.Red), C(9, CardColor.Red), C(11, CardColor.Red))),
            new Player(1, HandOf(C(7, CardColor.Green), C(7, CardColor.Yellow), C(1, CardColor.Red), C(3, CardColor.Red), C(4, CardColor.Red))),
            new Player(2, HandOf(C(6, CardColor.Red), C(8, CardColor.Red), C(10, CardColor.Red), C(12, CardColor.Red), C(2, CardColor.Blue)))
        };
        var discard = new[] { C(7, CardColor.Red) }; // seat0가 버린 7
        return new RoundState(players, Array.Empty<Card>(), discard, currentSeat: 1, new SeededRandom(1));
    }

    [Test]
    public void CanPong_true_when_holding_two_of_discarded_number()
    {
        var round = AfterSeat0DiscardedSeven();

        Assert.That(round.CanPong(1), Is.True);
    }

    [Test]
    public void CanPong_false_for_the_player_who_just_discarded()
    {
        var round = AfterSeat0DiscardedSeven();

        // seat0이 버린 당사자 → 뽕 불가 (rules.md §4-1)
        Assert.That(round.CanPong(0), Is.False);
    }

    [Test]
    public void CanPong_false_when_holding_only_one_match()
    {
        var round = AfterSeat0DiscardedSeven();

        // seat2는 7이 없음
        Assert.That(round.CanPong(2), Is.False);
    }

    [Test]
    public void Pong_reduces_hand_from_five_to_two_and_records_pong()
    {
        var round = AfterSeat0DiscardedSeven();

        var after = round.Pong(1, cardToDiscardAfter: C(1, CardColor.Red));

        Assert.That(after.Players[1].Hand.Count, Is.EqualTo(2)); // 5 - 2(뽕) - 1(버림)
        Assert.That(after.Players[1].PongCount, Is.EqualTo(1));
    }

    [Test]
    public void Pong_removes_ponged_card_and_puts_extra_discard_on_top()
    {
        var round = AfterSeat0DiscardedSeven();

        var after = round.Pong(1, cardToDiscardAfter: C(1, CardColor.Red));

        // 뽕한 7R은 나간 패로 사라지고, 추가 버림(1R)이 새 맨 위
        Assert.That(after.DiscardPile.Count, Is.EqualTo(1));
        Assert.That(after.DiscardPile[0], Is.EqualTo(C(1, CardColor.Red)));
    }

    [Test]
    public void Pong_resumes_turn_at_pongers_next_seat()
    {
        var round = AfterSeat0DiscardedSeven();

        var after = round.Pong(1, cardToDiscardAfter: C(1, CardColor.Red));

        Assert.That(after.CurrentSeat, Is.EqualTo(2)); // 뽕 선언자(1)의 다음
    }

    [Test]
    public void Second_pong_empties_hand_for_two_pong_finish()
    {
        // seat1이 손패 2장(둘 다 7)인 상태에서 7을 뽕 → 손 0장(rules.md §4-3)
        var players = new[]
        {
            new Player(0, HandOf(C(2, CardColor.Red), C(5, CardColor.Red))),
            new Player(1, HandOf(C(7, CardColor.Green), C(7, CardColor.Yellow)), PongCount: 1)
        };
        var discard = new[] { C(7, CardColor.Red) };
        var round = new RoundState(players, Array.Empty<Card>(), discard, currentSeat: 1, new SeededRandom(1));

        var after = round.Pong(1, cardToDiscardAfter: null);

        Assert.That(after.Players[1].Hand.Count, Is.EqualTo(0));
        Assert.That(after.Players[1].PongCount, Is.EqualTo(2));
    }

    [Test]
    public void Pong_allows_discarding_third_card_of_same_number()
    {
        // 9 세 장 보유 중 9 뽕 → 두 장 내려놓고 남은 세 번째 9를 추가 버림으로 낼 수 있다
        var players = new[]
        {
            new Player(0, HandOf(C(2, CardColor.Red), C(5, CardColor.Red))),
            new Player(1, HandOf(
                C(9, CardColor.Green), C(9, CardColor.Yellow), C(9, CardColor.Blue),
                C(1, CardColor.Red), C(3, CardColor.Red)))
        };
        var discard = new[] { C(9, CardColor.Red) }; // seat0가 버린 9
        var round = new RoundState(players, Array.Empty<Card>(), discard, currentSeat: 1, new SeededRandom(1));

        var after = round.Pong(1, cardToDiscardAfter: C(9, CardColor.Blue));

        Assert.That(after.Players[1].Hand.Cards, Is.EquivalentTo(new[] { C(1, CardColor.Red), C(3, CardColor.Red) }));
        Assert.That(after.DiscardPile[^1], Is.EqualTo(C(9, CardColor.Blue))); // 세 번째 9가 새 맨 위
    }

    [Test]
    public void Pong_with_three_card_hand_all_same_discards_third_to_empty_hand()
    {
        // 손패 3장 전부 9 → 9 뽕(2장) + 세 번째 9 버림 → 손 0장(손 털기)
        var players = new[]
        {
            new Player(0, HandOf(C(2, CardColor.Red), C(5, CardColor.Red))),
            new Player(1, HandOf(C(9, CardColor.Green), C(9, CardColor.Yellow), C(9, CardColor.Blue)), PongCount: 1)
        };
        var discard = new[] { C(9, CardColor.Red) };
        var round = new RoundState(players, Array.Empty<Card>(), discard, currentSeat: 1, new SeededRandom(1));

        var after = round.Pong(1, cardToDiscardAfter: C(9, CardColor.Blue));

        Assert.That(after.Players[1].Hand.Count, Is.EqualTo(0));
        Assert.That(after.Players[1].PongCount, Is.EqualTo(2));
    }

    // ── 자연뽕 (rules.md §4-2) ──

    [Test]
    public void CanNaturalPong_true_with_six_cards_and_a_triple()
    {
        var players = new[]
        {
            new Player(0, HandOf(
                C(5, CardColor.Red), C(5, CardColor.Blue), C(5, CardColor.Green),
                C(2, CardColor.Red), C(8, CardColor.Red), C(9, CardColor.Red)))
        };
        var round = new RoundState(players, Array.Empty<Card>(), Array.Empty<Card>(), 0, new SeededRandom(1));

        Assert.That(round.CanNaturalPong(), Is.True);
    }

    [Test]
    public void NaturalPong_sets_aside_triple_and_discards_to_two_cards()
    {
        var players = new[]
        {
            new Player(0, HandOf(
                C(5, CardColor.Red), C(5, CardColor.Blue), C(5, CardColor.Green),
                C(2, CardColor.Red), C(8, CardColor.Red), C(9, CardColor.Red))),
            new Player(1, HandOf(C(1, CardColor.Red), C(3, CardColor.Red)))
        };
        var round = new RoundState(players, Array.Empty<Card>(), Array.Empty<Card>(), 0, new SeededRandom(1));

        var after = round.NaturalPong(number: 5, cardToDiscardAfter: C(2, CardColor.Red));

        Assert.That(after.Players[0].Hand.Count, Is.EqualTo(2)); // 6 - 3(자연뽕) - 1(버림)
        Assert.That(after.Players[0].PongCount, Is.EqualTo(1));
        Assert.That(after.CurrentSeat, Is.EqualTo(1)); // 다음 좌석으로
    }

    [Test]
    public void NaturalPong_allows_discarding_fourth_card_of_same_number()
    {
        // 같은 숫자 4장 보유 → 3장 내려놓고 남은 네 번째를 추가 버림으로 낼 수 있다
        var players = new[]
        {
            new Player(0, HandOf(
                C(9, CardColor.Red), C(9, CardColor.Blue), C(9, CardColor.Green), C(9, CardColor.Yellow),
                C(2, CardColor.Red), C(8, CardColor.Red))),
            new Player(1, HandOf(C(1, CardColor.Red), C(3, CardColor.Red)))
        };
        var round = new RoundState(players, Array.Empty<Card>(), Array.Empty<Card>(), 0, new SeededRandom(1));

        var after = round.NaturalPong(number: 9, cardToDiscardAfter: C(9, CardColor.Yellow));

        Assert.That(after.Players[0].Hand.Cards, Is.EquivalentTo(new[] { C(2, CardColor.Red), C(8, CardColor.Red) }));
        Assert.That(after.DiscardPile[^1], Is.EqualTo(C(9, CardColor.Yellow)));
    }

    [Test]
    public void CanNaturalPong_true_with_three_card_hand_all_same()
    {
        // 뽕 후 손패 2장 → 드로우 3장이 전부 같은 숫자(7) → 자연뽕 가능(6장 아님)
        var players = new[]
        {
            new Player(0, HandOf(C(7, CardColor.Red), C(7, CardColor.Blue), C(7, CardColor.Green)), PongCount: 1)
        };
        var round = new RoundState(players, Array.Empty<Card>(), Array.Empty<Card>(), 0, new SeededRandom(1));

        Assert.That(round.CanNaturalPong(), Is.True);
    }

    [Test]
    public void NaturalPong_with_three_card_hand_empties_hand_no_discard()
    {
        var players = new[]
        {
            new Player(0, HandOf(C(7, CardColor.Red), C(7, CardColor.Blue), C(7, CardColor.Green)), PongCount: 1),
            new Player(1, HandOf(C(1, CardColor.Red), C(3, CardColor.Red)))
        };
        var round = new RoundState(players, Array.Empty<Card>(), Array.Empty<Card>(), 0, new SeededRandom(1));

        var after = round.NaturalPong(number: 7, cardToDiscardAfter: null);

        Assert.That(after.Players[0].Hand.Count, Is.EqualTo(0)); // 손 소진
        Assert.That(after.CurrentSeat, Is.EqualTo(1));
    }
    // ── 뽕 + 자연뽕 손 소진 (뽕 바가지, rules.md §4) ──

    /// <summary>seat0가 2를 버린 직후. seat1 손패 = 2,2,5,5,5 — 뽕 후 남은 3장이 같은 숫자.</summary>
    private static RoundState PongClearScenario()
    {
        var players = new[]
        {
            new Player(0, HandOf(C(9, CardColor.Red), C(10, CardColor.Red), C(11, CardColor.Red), C(12, CardColor.Red))),
            new Player(1, HandOf(C(2, CardColor.Green), C(2, CardColor.Yellow), C(5, CardColor.Red), C(5, CardColor.Green), C(5, CardColor.Blue))),
            new Player(2, HandOf(C(6, CardColor.Red), C(8, CardColor.Red), C(10, CardColor.Blue), C(12, CardColor.Blue), C(3, CardColor.Blue)))
        };
        var discard = new[] { C(2, CardColor.Red) }; // seat0가 버린 2
        return new RoundState(players, Array.Empty<Card>(), discard, currentSeat: 1, new SeededRandom(1));
    }

    [Test]
    public void CanPongThenNaturalPong_true_when_rest_is_three_of_a_kind()
    {
        Assert.That(PongClearScenario().CanPongThenNaturalPong(1), Is.True);
    }

    [Test]
    public void CanPongThenNaturalPong_false_when_rest_is_mixed()
    {
        var players = new[]
        {
            new Player(0, HandOf(C(9, CardColor.Red), C(10, CardColor.Red), C(11, CardColor.Red), C(12, CardColor.Red))),
            new Player(1, HandOf(C(2, CardColor.Green), C(2, CardColor.Yellow), C(5, CardColor.Red), C(5, CardColor.Green), C(7, CardColor.Blue))),
        };
        var round = new RoundState(players, Array.Empty<Card>(), new[] { C(2, CardColor.Red) }, currentSeat: 1, new SeededRandom(1));

        Assert.That(round.CanPong(1), Is.True);
        Assert.That(round.CanPongThenNaturalPong(1), Is.False); // 5,5,7 — 자연뽕 불가
    }

    [Test]
    public void PongThenNaturalPong_clears_hand_and_records_two_pongs()
    {
        var after = PongClearScenario().PongThenNaturalPong(1);

        Assert.That(after.Players[1].Hand.Count, Is.EqualTo(0)); // 2뽕 + 5,5,5 자연뽕 → 손 소진
        Assert.That(after.Players[1].PongCount, Is.EqualTo(2));  // 뽕과 자연뽕 각각 기록
        Assert.That(after.DiscardPile, Is.Empty);                // 뽕한 2는 나간 패, 5들도 나간 패
    }
}
