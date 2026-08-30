using NUnit.Framework;

public class SuccessChanceCalculatorTests
{
    [Test]
    public void CalculateSuccessChancePercent_EqualLevels_Returns50()
    {
        Assert.AreEqual(50f, SuccessChanceCalculator.CalculateSuccessChancePercent(5, 5));
    }

    [Test]
    public void CalculateSuccessChancePercent_ClampsToMax95()
    {
        Assert.AreEqual(95f, SuccessChanceCalculator.CalculateSuccessChancePercent(20, 1));
    }

    [Test]
    public void CalculateSuccessChancePercent_ClampsToMin5()
    {
        Assert.AreEqual(5f, SuccessChanceCalculator.CalculateSuccessChancePercent(1, 20));
    }

    [Test]
    public void GetLuckBonusPercent_ScalesLinearlyByTen()
    {
        Assert.AreEqual(30f, SuccessChanceCalculator.GetLuckBonusPercent(3));
    }
}
