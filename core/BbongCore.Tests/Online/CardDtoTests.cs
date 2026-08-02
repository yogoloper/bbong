using BbongCore.Cards;
using BbongCore.Online;
using NUnit.Framework;

namespace BbongCore.Tests.Online;

[TestFixture]
public class CardDtoTests
{
    [Test]
    public void Round_trips_card_through_dto()
    {
        var card = new Card(9, CardColor.Blue);

        var dto = CardDto.From(card);
        var back = dto.ToCard();

        Assert.That(dto.number, Is.EqualTo(9));
        Assert.That(dto.color, Is.EqualTo((int)CardColor.Blue));
        Assert.That(back, Is.EqualTo(card));
    }

    [Test]
    public void Converts_card_list_to_dto_array()
    {
        var cards = new[] { new Card(1, CardColor.Red), new Card(12, CardColor.Yellow) };

        var dtos = CardDto.FromAll(cards);

        Assert.That(dtos, Has.Length.EqualTo(2));
        Assert.That(dtos[1].number, Is.EqualTo(12));
        Assert.That(dtos[1].ToCard(), Is.EqualTo(cards[1]));
    }
}
