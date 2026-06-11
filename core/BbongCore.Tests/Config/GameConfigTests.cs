using BbongCore.Config;
using NUnit.Framework;

namespace BbongCore.Tests.Config;

[TestFixture]
public class GameConfigTests
{
    [Test]
    public void Default_matches_rules_spec()
    {
        var config = GameConfig.Default;

        Assert.That(config.StopLimit, Is.EqualTo(10));
        Assert.That(config.SetRounds, Is.EqualTo(5));
    }

    [Test]
    public void Fixed_rule_constants_match_spec()
    {
        Assert.That(GameConfig.HandSize, Is.EqualTo(5));
        Assert.That(GameConfig.PongBakPenalty, Is.EqualTo(20));
        Assert.That(GameConfig.StopBagajiPenalty, Is.EqualTo(30));
        Assert.That(GameConfig.PongTimerSeconds, Is.EqualTo(2));
        Assert.That(GameConfig.TurnTimerSeconds, Is.EqualTo(5));
        Assert.That(GameConfig.MaxReshuffles, Is.EqualTo(2));
    }

    [Test]
    public void Stake_options_match_spec()
    {
        Assert.That(GameConfig.StakeOptions, Is.EqualTo(new[] { 100, 500, 1000, 5000, 10000 }));
    }

    [Test]
    public void Nickname_validation_allows_1_to_12_chars()
    {
        Assert.That(GameConfig.MaxNicknameLength, Is.EqualTo(12));
        Assert.That(GameConfig.IsValidNickname("왕눈이"), Is.True);
        Assert.That(GameConfig.IsValidNickname("수줍은 너구리"), Is.True); // 띄어쓰기 포함 허용
        Assert.That(GameConfig.IsValidNickname("열두글자닉네임이름입니다"), Is.True);
        Assert.That(GameConfig.IsValidNickname("열세글자가되는닉네임이름임"), Is.False);
        Assert.That(GameConfig.IsValidNickname(""), Is.False);
        Assert.That(GameConfig.IsValidNickname("   "), Is.False);
        Assert.That(GameConfig.IsValidNickname(null), Is.False);
    }

    [Test]
    public void Player_count_validation_uses_2_to_6_range()
    {
        Assert.That(GameConfig.IsValidPlayerCount(2), Is.True);
        Assert.That(GameConfig.IsValidPlayerCount(6), Is.True);
        Assert.That(GameConfig.IsValidPlayerCount(1), Is.False);
        Assert.That(GameConfig.IsValidPlayerCount(7), Is.False);
    }

    [Test]
    public void Stake_validation_accepts_only_listed_options()
    {
        Assert.That(GameConfig.IsValidStake(1000), Is.True);
        Assert.That(GameConfig.IsValidStake(10000), Is.True);
        Assert.That(GameConfig.IsValidStake(999), Is.False);
        Assert.That(GameConfig.IsValidStake(0), Is.False);
    }

    [Test]
    public void Custom_config_overrides_tunable_values()
    {
        var config = GameConfig.Default with { StopLimit = 5, SetRounds = 3 };

        Assert.That(config.StopLimit, Is.EqualTo(5));
        Assert.That(config.SetRounds, Is.EqualTo(3));
    }
}
