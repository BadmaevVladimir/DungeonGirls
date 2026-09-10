using UnityEngine;

// Чистая формула крит-шанса: обычные атаки и критические тики Кровотечения используют один источник истины.
public static class CombatCriticalRules
{
    public static float ConvertedCritDamageBonus(CombatantRuntime attacker) =>
        (attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems +
         attacker.TotalCritChanceBonusPoints) * 2f;
    public static float EyeForAnEyeBonus(int level) => level switch
    {
        1 => 3f, 2 => 6f, 3 => 9f, 4 => 12f, 5 => 15f, _ => 0f
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
            attacker.CritChanceDebuffPercent + EyeForAnEyeBonus(attacker.SkillEyeForAnEyeLevel) +
            attacker.TotalCritChanceBonusPoints;
        return BalanceClamps.ClampCritChancePercent(Mathf.Max(0f, chance));
    }
}
