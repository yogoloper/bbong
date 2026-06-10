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
}
