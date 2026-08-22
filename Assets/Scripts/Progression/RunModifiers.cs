// Временные модификаторы "на следующий бой/привал" от квестов и ловушек (5.4-5.5).
// Каждое поле применяется один раз соответствующей системой и сбрасывается.
public class RunModifiers
{
    public float? NextCombatDamageMultiplier;
    public float? NextCombatAttackSpeedMultiplier;
    public bool NextCombatMonsterDamageBuff10Percent;

    public float? NextChestCurrencyMultiplier;
    public bool NextChestNoCurrency;

    public float? NextCampHealMultiplier;

    public float ConsumeCombatDamageMultiplier()
    {
        float value = NextCombatDamageMultiplier ?? 1f;
        NextCombatDamageMultiplier = null;
        return value;
    }

    public float ConsumeCombatAttackSpeedMultiplier()
    {
        float value = NextCombatAttackSpeedMultiplier ?? 1f;
        NextCombatAttackSpeedMultiplier = null;
        return value;
    }

    public bool ConsumeMonsterDamageBuff()
    {
        bool value = NextCombatMonsterDamageBuff10Percent;
        NextCombatMonsterDamageBuff10Percent = false;
        return value;
    }

    public float ConsumeChestCurrencyMultiplier()
    {
        float value = NextChestCurrencyMultiplier ?? 1f;
        NextChestCurrencyMultiplier = null;
        return value;
    }

    public bool ConsumeChestNoCurrency()
    {
        bool value = NextChestNoCurrency;
        NextChestNoCurrency = false;
        return value;
    }

    public float ConsumeCampHealMultiplier()
    {
        float value = NextCampHealMultiplier ?? 1f;
        NextCampHealMultiplier = null;
        return value;
    }
}
