using UnityEngine;

// Чистая формула крит-шанса: обычные атаки и критические тики Кровотечения используют один источник истины.
public static class CombatCriticalRules
{
    public static float EyeForAnEyeBonus(int level) => level switch
    {
        1 => 2f, 2 => 5f, 3 => 7.5f, 4 => 10f, 5 => 12.5f, _ => 0f
    };

    public static float CalculateChancePercent(CombatantRuntime attacker)
    {
        if (attacker == null)
        {
            return 0f;
        }

        if (attacker.CritChanceReplacedByRage)
        {
            return Mathf.Clamp(attacker.Rage * RageRules.SkillMultiplier(attacker.UniqueChampionOfTheTribeLevel), 0f, 100f);
        }

        float chance = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems -
            attacker.CritChanceDebuffPercent + EyeForAnEyeBonus(attacker.SkillEyeForAnEyeLevel);
        return BalanceClamps.ClampCritChancePercent(Mathf.Max(0f, chance));
    }
}
