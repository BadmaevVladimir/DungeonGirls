using System.Collections.Generic;

// 4.7: чистая функция для визуального фидбэка боя — превращает состояние CombatantRuntime в
// список подписей баф/дебафф для отображения под спрайтом бойца. Не имеет побочных эффектов,
// удобна для юнит-теста с вручную собранным CombatantRuntime.
public static class CombatantStatusEffects
{
    static readonly Dictionary<string, string> ActiveDebuffNames = new Dictionary<string, string>
    {
        { "warlock_slow", "Проклятие замедления" },
    };

    public static List<(string label, bool isBuff)> GetActiveEffects(CombatantRuntime combatant)
    {
        var effects = new List<(string label, bool isBuff)>();
        if (combatant == null)
        {
            return effects;
        }

        if (combatant.IsFrozen)
        {
            effects.Add(("Заморожен", false));
        }
        else if (combatant.FreezeStacks > 0)
        {
            effects.Add(($"Заморозка ×{combatant.FreezeStacks}", false));
        }

        if (combatant.FreezeImmune)
        {
            effects.Add(("Иммунитет к заморозке", true));
        }

        if (combatant.PoisonStacks > 0)
        {
            effects.Add(($"Яд ×{combatant.PoisonStacks}", false));
        }

        if (combatant.HasBleed)
        {
            effects.Add(("Кровотечение", false));
        }

        if (combatant.CritChanceDebuffPercent > 0f)
        {
            effects.Add(("Оглушающий крик", false));
        }

        foreach (var debuff in combatant.ActiveDebuffs)
        {
            string label = ActiveDebuffNames.TryGetValue(debuff.Id, out var name) ? name : debuff.Id;
            effects.Add((label, false));
        }

        return effects;
    }
}
