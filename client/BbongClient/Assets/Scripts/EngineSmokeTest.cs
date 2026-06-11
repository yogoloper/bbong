using BbongCore.Cards;
using BbongCore.Game;
using UnityEngine;

namespace Bbong.Client
{
    /// <summary>
    /// 코어 엔진(BbongCore.dll) 연동 확인용 스모크 테스트.
    /// 빈 GameObject에 붙이고 Play하면 Console에 덱/딜링 결과가 찍힙니다.
    /// </summary>
    public sealed class EngineSmokeTest : MonoBehaviour
    {
        private void Start()
        {
            var deck = Deck.CreateStandard();
            Debug.Log($"[BBONG] 코어 연동 OK — 덱 {deck.Cards.Count}장 생성 (기대 48)");

            var round = RoundState.Deal(deck, playerCount: 4, new SeededRandom(42));
            for (var seat = 0; seat < round.Players.Count; seat++)
            {
                var hand = round.Players[seat].Hand;
                Debug.Log($"[BBONG] P{seat} 손패 {hand.Count}장, 합 {hand.Sum()}");
            }

            Debug.Log($"[BBONG] 바닥 더미 {round.DrawPile.Count}장");
        }
    }
}
