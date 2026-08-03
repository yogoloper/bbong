using System;
using System.Linq;
using BbongCore.Rules;
using NUnit.Framework;

namespace BbongCore.Tests.Rules;

[TestFixture]
public class MeldNamesTests
{
    // ── 족보 표시명 단일 출처: 연습·친구방·향후 모드 어디서든 같은 문구 (rules.md §5) ──

    [TestCase(MeldType.Chongtong, "총통")]
    [TestCase(MeldType.Ttoittoi, "또이또이")]
    [TestCase(MeldType.Straight, "스트레이트")]
    [TestCase(MeldType.TenOrUnder, "10이하")]
    [TestCase(MeldType.SixtySixOrOver, "66이상")]
    public void Korean_maps_each_meld_to_rulebook_name(MeldType type, string expected)
    {
        Assert.That(MeldNames.Korean(type), Is.EqualTo(expected));
    }

    [Test]
    public void Korean_covers_every_meld_type_without_falling_back_to_enum_name()
    {
        var melds = Enum.GetValues<MeldType>().Where(t => t != MeldType.None);

        foreach (var type in melds)
        {
            Assert.That(MeldNames.Korean(type), Is.Not.EqualTo(type.ToString()),
                $"{type}의 한글 족보명이 없습니다.");
        }
    }
}
