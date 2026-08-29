using UnityEngine;

public static class DamageCalculator
{
    public struct DamageResult
    {
        public float DamageToHP;
        public bool WasBlocked;
        // true, если урон был полностью заблокирован, но всё равно износил броню.
        // Магический щит эту логику не использует — у ApplyMagicalDamage всегда false.
        public bool ArmorWornOnBlock;
    }

    // 3.3 [ОБНОВЛЕНО после анализа брони]: любой положительный физический удар гарантированно
    // изнашивает броню. Износ = max(1, floor(урон / 20)); при полном пробитии сохраняется минимум
    // 2 единицы. Благодаря этому слабые враги больше не могут бесконечно бить броню без последствий,
    // а сильные удары заметно быстрее расходуют слишком большой запас.
    // 3.11 Часть 2 (НОВОЕ): armorIgnorePercent — только для Клинка (Зазубренный/Моменто Мори,
    // см. WeaponAttackState.ArmorIgnorePercent) — снижает ЭФФЕКТИВНУЮ броню для целей проверки
    // блок/пробитие/износ, но НЕ влияет на то, сколько единиц брони теряется при пробитии (те
    // правила остаются про АБСОЛЮТНУЮ броню, не эффективную).
    public static DamageResult ApplyPhysicalDamage(CombatantRuntime target, float incomingDamage, float armorIgnorePercent = 0f)
    {
        incomingDamage = Mathf.Max(0f, incomingDamage);
        float effectiveDefense = target.PhysicalDefenseCurrent * (1f - Mathf.Clamp01(armorIgnorePercent / 100f));
        float armorLoss = incomingDamage > 0f ? Mathf.Max(1f, Mathf.Floor(incomingDamage / 20f)) : 0f;

        if (incomingDamage < effectiveDefense)
        {
            bool armorWorn = armorLoss > 0f && target.PhysicalDefenseCurrent > 0f;
            target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - armorLoss);

            return new DamageResult { DamageToHP = 0f, WasBlocked = true, ArmorWornOnBlock = armorWorn };
        }

        float remainder = incomingDamage - effectiveDefense;
        if (effectiveDefense > 0f && incomingDamage >= effectiveDefense * 2f)
        {
            armorLoss = Mathf.Max(2f, armorLoss);
        }
        target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - armorLoss);
        target.CurrentHP -= remainder;

        return new DamageResult { DamageToHP = remainder, WasBlocked = false };
    }

    // 3.3: магический урон поглощается магическим щитом, пока тот не закончится.
    public static DamageResult ApplyMagicalDamage(CombatantRuntime target, float incomingDamage)
    {
        if (target.MagicShieldCurrent >= incomingDamage)
        {
            target.MagicShieldCurrent -= incomingDamage;
            return new DamageResult { DamageToHP = 0f, WasBlocked = true };
        }

        float remainder = incomingDamage - target.MagicShieldCurrent;
        target.MagicShieldCurrent = 0f;
        target.CurrentHP -= remainder;

        return new DamageResult { DamageToHP = remainder, WasBlocked = false };
    }

    // 3.2 [ОБНОВЛЕНО после плейтеста]: любое ранее фиксированное значение урона снаряжения теперь
    // диапазон вокруг базового значения — [ПОЛ(база×0.8); ОКРУГЛВВЕРХ(база×1.2)]. Общее правило,
    // применяется в CombatantFactory при сборке WeaponAttackState из ItemData.EffectiveDamage.
    // Урон монстров НЕ идёт через эту формулу — MonsterData уже хранит явные diapazon-поля
    // damageMin/damageMax (не единое "базовое" значение), их не трогаем.
    public static void ComputeDamageRange(float baseDamage, out float min, out float max)
    {
        min = Mathf.Floor(baseDamage * 0.8f);
        max = Mathf.Ceil(baseDamage * 1.2f);
    }

    // 3.11 Часть 2 (НОВОЕ): "% сопротивления урону" — общий множитель, первый шаг в цепочке расчёта,
    // ДО брони/щита. Суммируется по всем источникам одного типа урона, клампится на 100% (0 урона
    // дальше по цепочке, а не отрицательный урон).
    public static DamageResult ApplyDamage(CombatantRuntime target, float incomingDamage, DamageType damageType, float armorIgnorePercent = 0f)
    {
        float resistancePercent = damageType == DamageType.Physical ? target.PhysicalResistancePercent : target.MagicalResistancePercent;
        float damageAfterResistance = incomingDamage * (1f - Mathf.Clamp01(resistancePercent / 100f));

        return damageType == DamageType.Physical
            ? ApplyPhysicalDamage(target, damageAfterResistance, armorIgnorePercent)
            : ApplyMagicalDamage(target, damageAfterResistance);
    }
}
