using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;

namespace BbongCore.Game;

/// <summary>한 판의 상태(불변). 더미·버림·턴·플레이어들을 가집니다(rules.md §2~3).</summary>
public sealed class RoundState
{
    private readonly List<Player> _players;
    private readonly List<Card> _drawPile;
    private readonly List<Card> _discardPile;

    private RoundState(
        IEnumerable<Player> players,
        IEnumerable<Card> drawPile,
        IEnumerable<Card> discardPile,
        int currentSeat)
    {
        _players = players.ToList();
        _drawPile = drawPile.ToList();
        _discardPile = discardPile.ToList();
        CurrentSeat = currentSeat;
    }

    public IReadOnlyList<Player> Players => _players;

    public IReadOnlyList<Card> DrawPile => _drawPile;

    public IReadOnlyList<Card> DiscardPile => _discardPile;

    public int CurrentSeat { get; }

    /// <summary>
    /// 판 시작: 48장 셔플 → 전원 5장 → 남은 카드는 바닥 더미.
    /// 버림 더미는 빈 상태로 시작, 선(dealerSeat)부터 진행(rules.md §2).
    /// </summary>
    public static RoundState Deal(Deck deck, int playerCount, IRandom random, int dealerSeat = 0)
    {
        const int handSize = 5;

        var cards = deck.Shuffle(random).Cards;

        var players = new List<Player>(playerCount);
        var cursor = 0;
        for (var seat = 0; seat < playerCount; seat++)
        {
            var hand = new Hand(cards.Skip(cursor).Take(handSize));
            players.Add(new Player(seat, hand));
            cursor += handSize;
        }

        var drawPile = cards.Skip(cursor);

        return new RoundState(players, drawPile, discardPile: Enumerable.Empty<Card>(), dealerSeat);
    }

    public Player CurrentPlayer => _players[CurrentSeat];

    /// <summary>현재 플레이어가 바닥 더미 맨 위 1장을 손에 넣습니다(rules.md §3). 턴 유지.</summary>
    public RoundState Draw()
    {
        var top = _drawPile[0];
        var newPlayers = ReplaceCurrent(CurrentPlayer.WithHand(CurrentPlayer.Hand.Draw(top)));

        return new RoundState(newPlayers, _drawPile.Skip(1), _discardPile, CurrentSeat);
    }

    /// <summary>현재 플레이어가 카드 1장을 버림 더미에 올리고 다음 좌석으로 넘깁니다(rules.md §3).</summary>
    public RoundState Discard(Card card)
    {
        var newPlayers = ReplaceCurrent(CurrentPlayer.WithHand(CurrentPlayer.Hand.Discard(card)));
        var newDiscard = _discardPile.Append(card); // 맨 위 = 마지막 원소
        var nextSeat = (CurrentSeat + 1) % _players.Count;

        return new RoundState(newPlayers, _drawPile, newDiscard, nextSeat);
    }

    private IEnumerable<Player> ReplaceCurrent(Player updated) =>
        _players.Select(p => p.Seat == CurrentSeat ? updated : p);
}
