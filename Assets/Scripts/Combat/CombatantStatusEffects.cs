using System.Collections.Generic;

// 4.7: чистая функция для визуального фидбэка боя — превращает состояние CombatantRuntime в
// список подписей баф/дебафф для отображения под спрайтом бойца. Не имеет побочных эффектов,
// удобна для юнит-теста с вручную собранным CombatantRuntime.
public static class CombatantStatusEffects
{
    // Финальный ревью-фикс #3: раньше здесь были только записи для ActiveDebuff.Id, известные ДО
    // этого фикс-волны — "by_a_thread" ("На волоске", 3.11 Плут) и "intimidation" ("Запугивание",
    // 3.11 Варвар) отсутствовали, и игроки видели сырой английский Id вместо русской подписи
    // (см. фолбэк в GetActiveEffects ниже). Ключ — ActiveDebuff.Id, значение — отображаемое имя;
    // баф/дебафф-окраска берётся из ActiveDebuff.IsBuff самого объекта, а не из этой таблицы.
    static readonly Dictionary<string, string> ActiveDebuffNames = new Dictionary<string, string>
    {
        { "warlock_slow", "Проклятие замедления" },
        { "by_a_thread", "На волоске" }, // 3.11 (Плут) — бафф скорости атаки, см. ActiveDebuff.IsBuff
        { "intimidation", "Запугивание" }, // 3.11 (Варвар) — дебафф скорости атаки цели крита
        { "event_damage_down", "Урон снижен" },
        { "event_attack_speed_down", "Скорость атаки снижена" },
        { "alarm_damage_buff", "Урон усилен сигнализацией" },
        { "cursed_oathbreaker", "Расплата" },
        { "cursed_executioner", "Нетерпение палача" },
        { "cursed_berserkeraxe", "Безрассудство" },
        { "cursed_recklesscharge", "Открытая стойка" },
        { "cursed_lastargument", "Запрет восстановления брони" },
        { "cursed_betrayerandaccomplice", "Предательство скрытности" },
        { "cursed_paranoiablades", "Срыв" },
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

        // Финальный ревью-фикс #3 (3.11, Плут) — "Скрытность": бафф, независимый от ActiveDebuffs.
        if (combatant.IsStealthed)
        {
            effects.Add(("Скрытность", true));
        }

        // Финальный ревью-фикс #3 (3.11, Плут) — "Отравленный клинок": собственный яд Плута на
        // цели, отдельный от монстрового PoisonStacks выше (уже покрыт "Яд ×N" строкой выше) —
        // метка "Яд Плута" отличает его от монстрового яда, тем же форматом ×N.
        if (combatant.RoguePoisonStacksOnTarget > 0)
        {
            effects.Add(($"Яд Плута ×{combatant.RoguePoisonStacksOnTarget}", false));
        }

        // Финальный ревью-фикс #3 (3.11, Варвар) — "Берсерк": ручной тумблер-бафф (даёт физ.
        // сопротивление, тикает самоурон — см. CombatManager.Tick). Ярость (Rage) сознательно НЕ
        // включена сюда: это числовой стат (0-100%+), пересчитываемый каждый кадр от текущего HP,
        // а не переключаемый статус-эффект вроде остальных записей этого списка — ей место в
        // отдельном HUD-индикаторе (полоса/число), а не в списке баф/дебафф-меток боя.
        if (combatant.IsBerserkActive)
        {
            effects.Add(("Берсерк", true));
        }

        if (combatant.SmokeBombGuaranteedCritsRemaining > 0)
        {
            effects.Add(($"Гарантированные критические атаки ×{combatant.SmokeBombGuaranteedCritsRemaining}", true));
        }

        if (combatant.RiposteArmed)
        {
            effects.Add(("Рипост готов", true));
        }

        // Boss framework (минимальный слайс) — shield pool способности (см. CombatantRuntime.
        // ShieldPoolCurrent/Max), НЕ путать с MagicShieldCurrent/Max (те не отображаются здесь вовсе —
        // у игрока свой отдельный HUD-индикатор "Щит", см. RunFlowController.Combat.UpdateCombatUI).
        if (combatant.ShieldPoolCurrent > 0f)
        {
            effects.Add(($"Барьер {combatant.ShieldPoolCurrent:F0}/{combatant.ShieldPoolMax:F0}", true));
        }

        if (combatant.PhysicalResistancePercent > 0f)
        {
            effects.Add(($"Физ. сопротивление {combatant.PhysicalResistancePercent:F0}%", true));
        }

        if (combatant.MagicalResistancePercent > 0f)
        {
            effects.Add(($"Маг. сопротивление {combatant.MagicalResistancePercent:F0}%", true));
        }

        if (combatant.SkillStubbornnessLevel > 0 && combatant.Rage > StubbornnessThreshold(combatant.SkillStubbornnessLevel))
        {
            effects.Add(("Упёртость: защита от отрицательных эффектов", true));
        }

        foreach (var debuff in combatant.ActiveDebuffs)
        {
            string label = ActiveDebuffNames.TryGetValue(debuff.Id, out var name) ? name : debuff.Id;
            effects.Add((label, debuff.IsBuff));
        }

        return effects;
    }

    static float StubbornnessThreshold(int level) => level switch
    {
        1 => 90f,
        2 => 80f,
        3 => 70f,
        4 => 60f,
        5 => 50f,
        _ => 101f
    };
}
