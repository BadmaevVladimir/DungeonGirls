using NUnit.Framework;

public class StatScalingTests
{
    [Test]
    public void ItemEffectRank_IsIndependentFromItemLevel()
    {
        var item = UnityEngine.ScriptableObject.CreateInstance<ItemData>();
        item.itemLevel = 15;
        item.itemRank = 2;
        Assert.AreEqual(2, item.EffectRank);
        Assert.AreEqual(2f, StatScaling.ScaleItemEffect(1f, item.EffectRank));
        UnityEngine.Object.DestroyImmediate(item);
    }

    [Test]
    public void LegacyZeroItemRank_FallsBackToRankOne()
    {
        var item = UnityEngine.ScriptableObject.CreateInstance<ItemData>();
        item.itemLevel = 15;
        item.itemRank = 0;
        Assert.AreEqual(1, item.EffectRank);
        UnityEngine.Object.DestroyImmediate(item);
    }

    [TestCase(1, .99f, 1)]
    [TestCase(3, .349f, 1)]
    [TestCase(3, .35f, 2)]
    [TestCase(5, .149f, 1)]
    [TestCase(5, .15f, 2)]
    [TestCase(5, .45f, 3)]
    [TestCase(7, .099f, 1)]
    [TestCase(7, .10f, 2)]
    [TestCase(7, .25f, 3)]
    [TestCase(7, .55f, 4)]
    [TestCase(9, .049f, 1)]
    [TestCase(9, .05f, 2)]
    [TestCase(9, .15f, 3)]
    [TestCase(9, .30f, 4)]
    [TestCase(9, .55f, 5)]
    public void ItemRankRoll_UsesTemporaryFloorWeights(int floor, float roll, int expected)
    {
        Assert.AreEqual(expected, RewardManager.RollItemRank(floor, roll));
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
