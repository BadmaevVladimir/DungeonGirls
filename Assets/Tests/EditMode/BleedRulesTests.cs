using NUnit.Framework;

public class BleedRulesTests
{
    [Test]
    public void CalculateBleedDetonationDamage_UsesExactRemainingDuration()
    {
        Assert.AreEqual(50f, BleedRules.DetonationDamage(20f, 2.5f));
    }

    [Test]
    public void CalculateBleedDetonationDamage_LegacyInfiniteTimer_UsesOneRegularDuration()
    {
        Assert.AreEqual(60f, BleedRules.DetonationDamage(20f, float.PositiveInfinity));
    }

    [Test]
    public void CalculateCritChancePercent_UsesRegularCharacterFormula()
    {
        var attacker = new CombatantRuntime
        {
            SkillCriticalHitsLevel = 3,
            CritChanceBonusFromItems = 8f,
            CritChanceDebuffPercent = 10f,
            SkillEyeForAnEyeLevel = 2
        };

        Assert.AreEqual(33f, CombatCriticalRules.CalculateChancePercent(attacker));
    }

    [Test]
    public void CalculateCritChancePercent_UsesRageWhenChampionReplacesCritChance()
    {
        var attacker = new CombatantRuntime
        {
            MaxHP = 100f,
            CurrentHP = 50f,
            CritChanceReplacedByRage = true,
            UniqueChampionOfTheTribeLevel = 5
        };

        Assert.AreEqual(51f, CombatCriticalRules.CalculateChancePercent(attacker));
    }

    [Test]
    public void CanBleedTickCritically_OnlyAtLevelFive()
    {
        Assert.IsFalse(BleedRules.CanTickCritically(4));
        Assert.IsTrue(BleedRules.CanTickCritically(5));
    }

    [Test]
    public void LevelFiveBleed_HasInfiniteDuration()
    {
        Assert.AreEqual(BleedRules.DurationSeconds, BleedRules.DurationForLevel(4));
        Assert.IsTrue(float.IsPositiveInfinity(BleedRules.DurationForLevel(5)));
    }
}
