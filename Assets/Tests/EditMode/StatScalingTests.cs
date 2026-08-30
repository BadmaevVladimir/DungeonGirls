using NUnit.Framework;

public class StatScalingTests
{
    [Test]
    public void ItemEffectRank_Level1To3_ReturnsRank1()
    {
        Assert.AreEqual(1, StatScaling.ItemEffectRank(1));
        Assert.AreEqual(1, StatScaling.ItemEffectRank(3));
    }

    [Test]
    public void ItemEffectRank_HighLevel_ClampsToRank5()
    {
        Assert.AreEqual(5, StatScaling.ItemEffectRank(999));
    }

    [Test]
    public void ApplyLevelBonus_ZeroBaseStat_StaysZero()
    {
        Assert.AreEqual(0f, StatScaling.ApplyLevelBonus(0f, 10));
    }

    [Test]
    public void ApplyLevelBonus_Level1_ReturnsBaseStatUnchanged()
    {
        Assert.AreEqual(100f, StatScaling.ApplyLevelBonus(100f, 1));
    }

    [Test]
    public void ApplyLevelBonus_MinimumIncrementIsOne()
    {
        // baseStat=5 -> round(5*0.1)=0, but increment is clamped to max(1, ...) = 1 per level
        Assert.AreEqual(7f, StatScaling.ApplyLevelBonus(5f, 3));
    }

    [Test]
    public void ArmorBreakExtraWearChancePercent_ClampsTo100()
    {
        Assert.AreEqual(100f, ItemEffectBalance.ArmorBreakExtraWearChancePercent(5));
    }
}
