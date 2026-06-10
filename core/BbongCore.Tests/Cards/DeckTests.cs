using System.Linq;
using BbongCore.Cards;
using NUnit.Framework;

namespace BbongCore.Tests.Cards;

[TestFixture]
public class DeckTests
{
    [Test]
    public void CreateStandard_has_48_cards()
    {
        var deck = Deck.CreateStandard();

        Assert.That(deck.Cards.Count, Is.EqualTo(48));
    }

    [Test]
    public void CreateStandard_covers_every_number_color_combination_exactly_once()
    {
        var deck = Deck.CreateStandard();

        var distinct = deck.Cards
            .Select(c => (c.Number, c.Color))
            .Distinct()
            .Count();

        Assert.That(distinct, Is.EqualTo(48), "1~12 × 4색 조합이 중복 없이 모두 존재해야 합니다.");
    }

    [Test]
    public void CreateStandard_numbers_range_from_1_to_12()
    {
        var deck = Deck.CreateStandard();

        Assert.That(deck.Cards.Select(c => c.Number), Is.All.InRange(1, 12));
    }

    // ── 셔플: IRandom 주입(서버 보안 시드/테스트 재현) (rules.md §2) ──

    [Test]
    public void Shuffle_preserves_all_48_cards()
    {
        var shuffled = Deck.CreateStandard().Shuffle(new SeededRandom(123));

        Assert.That(shuffled.Cards.Count, Is.EqualTo(48));
        Assert.That(shuffled.Cards, Is.EquivalentTo(Deck.CreateStandard().Cards));
    }

    [Test]
    public void Shuffle_with_same_seed_is_reproducible()
    {
        var a = Deck.CreateStandard().Shuffle(new SeededRandom(123));
        var b = Deck.CreateStandard().Shuffle(new SeededRandom(123));

        Assert.That(a.Cards, Is.EqualTo(b.Cards)); // 순서까지 동일
    }

    [Test]
    public void Shuffle_does_not_mutate_original_deck()
    {
        var deck = Deck.CreateStandard();
        var originalOrder = deck.Cards.ToList();

        deck.Shuffle(new SeededRandom(7));

        Assert.That(deck.Cards, Is.EqualTo(originalOrder));
    }
}
