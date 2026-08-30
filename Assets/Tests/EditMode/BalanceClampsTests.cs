using NUnit.Framework;

public class BalanceClampsTests
{
    [Test]
    public void ClampCritChancePercent_AboveMax_ClampsTo75()
    {
        Assert.AreEqual(75f, BalanceClamps.ClampCritChancePercent(120f));
    }

    [Test]
    public void ThornsReflectPercent_Level5_ClampsToMax50()
    {
        Assert.AreEqual(50f, BalanceClamps.ThornsReflectPercent(5));
    }

    [Test]
    public void ThornsReflectPercent_Level2_Returns20()
    {
        Assert.AreEqual(20f, BalanceClamps.ThornsReflectPercent(2));
    }

    [Test]
    public void CombatRegenHitsRequired_Level1_Returns6()
    {
        Assert.AreEqual(6, BalanceClamps.CombatRegenHitsRequired(1));
    }

    [Test]
    public void CombatRegenHitsRequired_Level5_Returns2()
    {
        Assert.AreEqual(2, BalanceClamps.CombatRegenHitsRequired(5));
    }
}
