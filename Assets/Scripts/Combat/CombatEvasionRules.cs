// Чистая формула шанса уклонения цели: используется и при разрешении атаки (CombatManager),
// и для отображения текущих статов персонажа (Pause), один источник истины — как CombatCriticalRules.
public static class CombatEvasionRules
{
    public static float CalculateChancePercent(CombatantRuntime target)
    {
        if (target == null)
        {
            return 0f;
        }

        float itemEvasionPercent = BalanceClamps.ClampItemEvasionPercent(
            ItemEffectBalance.ElusivenessEvasionPercent(target.ItemElusivenessLevel) + target.ItemEvasionBonusPercent);
        float chance = target.SkillEvasionLevel * 5f + itemEvasionPercent + target.MonsterEvasionPercent;

        float slipAwayBonus = target.SkillSlipAwayLevel switch { 1 => 2f, 2 => 4f, 3 => 6f, 4 => 8f, 5 => 10f, _ => 0f };
        chance += slipAwayBonus;

        if (target.IsStealthed && target.UniqueShadowLevel > 0)
        {
            chance += target.UniqueShadowLevel switch { 1 => 10f, 2 => 15f, 3 => 20f, 4 => 25f, 5 => 30f, _ => 0f };
        }

        return BalanceClamps.ClampEvasionChancePercent(chance);
    }
}
