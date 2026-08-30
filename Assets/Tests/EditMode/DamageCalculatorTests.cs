using NUnit.Framework;

public class DamageCalculatorTests
{
    [Test]
    public void ApplyPhysicalDamage_DamageBelowDefense_IsFullyBlockedAndWearsArmor()
    {
        var target = new CombatantRuntime { PhysicalDefenseCurrent = 50f, CurrentHP = 100f };

        var result = DamageCalculator.ApplyPhysicalDamage(target, 10f);

        Assert.IsTrue(result.WasBlocked);
        Assert.AreEqual(0f, result.DamageToHP);
        Assert.AreEqual(49f, target.PhysicalDefenseCurrent); // max(1, floor(10/20)) = 1
        Assert.AreEqual(100f, target.CurrentHP);
    }

    [Test]
    public void ApplyPhysicalDamage_DamageAboveDefense_DealsRemainderToHP()
    {
        var target = new CombatantRuntime { PhysicalDefenseCurrent = 10f, CurrentHP = 100f };

        var result = DamageCalculator.ApplyPhysicalDamage(target, 30f);

        Assert.IsFalse(result.WasBlocked);
        Assert.AreEqual(20f, result.DamageToHP);
        Assert.AreEqual(80f, target.CurrentHP);
    }

    [Test]
    public void ApplyMagicalDamage_DamageExceedsShield_DealsRemainderToHP()
    {
        var target = new CombatantRuntime { MagicShieldCurrent = 15f, CurrentHP = 50f };

        var result = DamageCalculator.ApplyMagicalDamage(target, 20f);

        Assert.IsFalse(result.WasBlocked);
        Assert.AreEqual(5f, result.DamageToHP);
        Assert.AreEqual(0f, target.MagicShieldCurrent);
        Assert.AreEqual(45f, target.CurrentHP);
    }

    [Test]
    public void ApplyDamage_WithResistance_ReducesDamageBeforeDefense()
    {
        var target = new CombatantRuntime { PhysicalDefenseCurrent = 0f, CurrentHP = 100f, PhysicalResistancePercent = 50f };

        var result = DamageCalculator.ApplyDamage(target, 40f, DamageType.Physical);

        Assert.AreEqual(20f, result.DamageToHP); // 40 * (1 - 0.5) = 20
        Assert.AreEqual(80f, target.CurrentHP);
    }

    [Test]
    public void ComputeDamageRange_ReturnsFloorAndCeilOfPlusMinus20Percent()
    {
        DamageCalculator.ComputeDamageRange(10f, out float min, out float max);

        Assert.AreEqual(8f, min);
        Assert.AreEqual(12f, max);
    }
}
