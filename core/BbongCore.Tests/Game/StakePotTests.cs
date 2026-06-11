using BbongCore.Game;
using NUnit.Framework;

namespace BbongCore.Tests.Game;

[TestFixture]
public class StakePotTests
{
    [Test]
    public void Single_winner_takes_the_whole_pot()
    {
        // 판돈 1000 × 3명 = 3000, 단독 1등(seat1)이 전부 (rules.md §9-2)
        var payouts = StakePot.Distribute(stakePerPlayer: 1000, playerCount: 3, winnerSeats: new[] { 1 });

        Assert.That(payouts, Is.EqualTo(new[] { 0, 3000, 0 }));
    }

    [Test]
    public void Co_winners_split_the_pot_equally()
    {
        // 공동 1등 2명 → 총 판돈 반씩 (rules.md §9-3)
        var payouts = StakePot.Distribute(stakePerPlayer: 1000, playerCount: 4, winnerSeats: new[] { 0, 2 });

        Assert.That(payouts, Is.EqualTo(new[] { 2000, 0, 2000, 0 }));
    }

    [Test]
    public void Remainder_is_truncated_when_pot_not_divisible()
    {
        // 100 × 3 = 300, 공동 1등 2명 → 150씩 (나머지 0)
        // 100 × 3 = 300, 3명 공동? → 300/3 = 100. 나머지 절사 케이스: 500×3=1500, 2명 → 750
        var payouts = StakePot.Distribute(stakePerPlayer: 500, playerCount: 3, winnerSeats: new[] { 0, 1 });

        // 1500 / 2 = 750씩
        Assert.That(payouts, Is.EqualTo(new[] { 750, 750, 0 }));
    }

    [Test]
    public void Truncates_indivisible_share()
    {
        // 100 × 5 = 500, 공동 1등 3명 → 166씩 (500/3=166.67 → 절사 166)
        var payouts = StakePot.Distribute(stakePerPlayer: 100, playerCount: 5, winnerSeats: new[] { 0, 1, 2 });

        Assert.That(payouts, Is.EqualTo(new[] { 166, 166, 166, 0, 0 }));
    }
}
