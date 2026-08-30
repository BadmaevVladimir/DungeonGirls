using NUnit.Framework;

public class GachaCopyBonusCalculatorTests
{
    [Test]
    public void CalculateBonus_FirstCopyOnly_NoBonus()
    {
        var bonus = GachaCopyBonusCalculator.CalculateBonus(1);

        Assert.AreEqual(0, bonus.GearLevelBonus);
        Assert.AreEqual(0, bonus.PassiveLevelBonus);
        Assert.AreEqual(0, bonus.ActiveLevelBonus);
    }

    [Test]
    public void CalculateBonus_FiveCopies_FourExtraCyclesThroughGearPassiveGearActive()
    {
        // extraCopies = 4 -> steps 0,1,2,3 -> Gear, Passive, Gear, Active
        var bonus = GachaCopyBonusCalculator.CalculateBonus(5);

        Assert.AreEqual(2, bonus.GearLevelBonus);
        Assert.AreEqual(1, bonus.PassiveLevelBonus);
        Assert.AreEqual(1, bonus.ActiveLevelBonus);
    }

    [Test]
    public void CalculateBonus_ManyCopies_PassiveBonusClampsToFour()
    {
        var bonus = GachaCopyBonusCalculator.CalculateBonus(1 + 4 * 10); // 10 full cycles of extra copies

        Assert.AreEqual(4, bonus.PassiveLevelBonus);
        Assert.AreEqual(2, bonus.ActiveLevelBonus);
    }
}
