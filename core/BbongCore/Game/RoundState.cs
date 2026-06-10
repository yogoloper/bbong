using System;
using System.Collections.Generic;
using System.Linq;
using BbongCore.Cards;

namespace BbongCore.Game;

/// <summary>한 판의 상태(불변). 더미·버림·턴·플레이어들을 가집니다(rules.md §2~4).</summary>
public sealed class RoundState
{
    private readonly List<Player> _players;
    private readonly List<Card> _drawPile;
    private readonly List<Card> _discardPile;
    private readonly IRandom _random;

    internal RoundState(
        IEnumerable<Player> players,
        IEnumerable<Card> drawPile,
        IEnumerable<Card> discardPile,
        int currentSeat,
        IRandom random)
    {
        _players = players.ToList();
        _drawPile = drawPile.ToList();
        _discardPile = discardPile.ToList();
        CurrentSeat = currentSeat;
        _random = random;
    }

    public IReadOnlyList<Player> Players => _players;

    public IReadOnlyList<Card> DrawPile => _drawPile;

    public IReadOnlyList<Card> DiscardPile => _discardPile;

    public int CurrentSeat { get; }

    public Player CurrentPlayer => _players[CurrentSeat];

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

        return new RoundState(players, cards.Skip(cursor), Enumerable.Empty<Card>(), dealerSeat, random);
    }

    /// <summary>현재 플레이어가 바닥 더미 맨 위 1장을 손에 넣습니다(rules.md §3). 턴 유지.</summary>
    public RoundState Draw()
    {
        var (drawPile, discardPile) = _drawPile.Count == 0 ? Reshuffle() : (_drawPile, _discardPile);

        var top = drawPile[0];
        var newPlayers = ReplaceCurrent(CurrentPlayer.WithHand(CurrentPlayer.Hand.Draw(top)));

        return new RoundState(newPlayers, drawPile.Skip(1), discardPile, CurrentSeat, _random);
    }

    /// <summary>현재 플레이어가 카드 1장을 버림 더미에 올리고 다음 좌석으로 넘깁니다(rules.md §3).</summary>
    public RoundState Discard(Card card)
    {
        var newPlayers = ReplaceCurrent(CurrentPlayer.WithHand(CurrentPlayer.Hand.Discard(card)));
        var newDiscard = _discardPile.Append(card); // 맨 위 = 마지막 원소

        return new RoundState(newPlayers, _drawPile, newDiscard, NextSeat(CurrentSeat), _random);
    }

    // ── 뽕 (rules.md §4) ──

    /// <summary>seat 플레이어가 버림 더미 맨 위 카드를 뽕할 수 있는지(같은 숫자 2장 보유 + 버린 당사자 아님).</summary>
    public bool CanPong(int seat)
    {
        if (_discardPile.Count == 0 || seat == LastDiscarderSeat)
        {
            return false;
        }

        var number = TopDiscard.Number;
        return _players[seat].Hand.Cards.Count(c => c.Number == number) >= 2;
    }

    /// <summary>
    /// 뽕 처리: 버림 더미 맨 위 카드 + 손의 같은 숫자 2장 = 나간 패(제거). 손패 5→3.
    /// 이어서 1장 더 버림(손패 →2). 단 둘째 뽕으로 손이 비면(0장) 더 버리지 않고 판 종료.
    /// 턴은 뽕 선언자의 다음 좌석부터(rules.md §4-1, §4-3).
    /// </summary>
    public RoundState Pong(int seat, Card? cardToDiscardAfter)
    {
        var pongedNumber = TopDiscard.Number;
        var player = _players[seat];

        var keep = RemoveCount(player.Hand.Cards, pongedNumber, 2);
        var afterRemove = player.WithHand(new Hand(keep)).RecordPong();

        var discardPile = DropTop(_discardPile); // 뽕한 카드는 나간 패로 사라짐

        if (afterRemove.Hand.Count == 0)
        {
            // 둘째 뽕: 손 소진 → 추가 버림 없음, 판 종료(§4-3)
            return new RoundState(ReplaceAt(seat, afterRemove), _drawPile, discardPile, NextSeat(seat), _random);
        }

        if (cardToDiscardAfter is not { } extra)
        {
            throw new InvalidOperationException("뽕 이후 버릴 카드를 지정해야 합니다.");
        }

        var afterDiscard = afterRemove.WithHand(afterRemove.Hand.Discard(extra));
        return new RoundState(ReplaceAt(seat, afterDiscard), _drawPile, discardPile.Append(extra), NextSeat(seat), _random);
    }

    /// <summary>자기 턴, 6장 상태에서 같은 숫자 3장을 들고 있으면 자연뽕 가능(rules.md §4-2).</summary>
    public bool CanNaturalPong() =>
        CurrentPlayer.Hand.Count == 6 &&
        CurrentPlayer.Hand.Cards.GroupBy(c => c.Number).Any(g => g.Count() >= 3);

    /// <summary>자연뽕: 같은 숫자 3장을 나간 패로 내려놓고(6→3), 1장 더 버린 뒤(→2) 다음 좌석으로.</summary>
    public RoundState NaturalPong(int number, Card cardToDiscardAfter)
    {
        var player = CurrentPlayer;
        var keep = RemoveCount(player.Hand.Cards, number, 3);
        var afterRemove = player.WithHand(new Hand(keep)).RecordPong();
        var afterDiscard = afterRemove.WithHand(afterRemove.Hand.Discard(cardToDiscardAfter));

        return new RoundState(
            ReplaceAt(CurrentSeat, afterDiscard),
            _drawPile,
            _discardPile.Append(cardToDiscardAfter),
            NextSeat(CurrentSeat),
            _random);
    }

    // ── 내부 헬퍼 ──

    private Card TopDiscard => _discardPile[_discardPile.Count - 1];

    private int LastDiscarderSeat => (CurrentSeat - 1 + _players.Count) % _players.Count;

    private int NextSeat(int seat) => (seat + 1) % _players.Count;

    private IEnumerable<Player> ReplaceCurrent(Player updated) => ReplaceAt(CurrentSeat, updated);

    private IEnumerable<Player> ReplaceAt(int seat, Player updated) =>
        _players.Select(p => p.Seat == seat ? updated : p);

    /// <summary>같은 숫자 카드를 count장 제거한 나머지 손패를 반환합니다.</summary>
    private static List<Card> RemoveCount(IReadOnlyList<Card> cards, int number, int count)
    {
        var result = new List<Card>(cards);
        for (var i = 0; i < count; i++)
        {
            result.Remove(result.First(c => c.Number == number));
        }

        return result;
    }

    private static List<Card> DropTop(IReadOnlyList<Card> discard) =>
        discard.Take(discard.Count - 1).ToList();

    /// <summary>바닥 더미 소진 시: 버림 더미 맨 위 1장만 남기고 나머지를 셔플(rules.md §3).</summary>
    private (List<Card> draw, List<Card> discard) Reshuffle()
    {
        if (_discardPile.Count <= 1)
        {
            throw new InvalidOperationException("재셔플할 카드가 부족합니다.");
        }

        var top = TopDiscard;
        var rest = _discardPile.Take(_discardPile.Count - 1).ToList();
        return (Shuffler.Shuffle(rest, _random), new List<Card> { top });
    }
}
