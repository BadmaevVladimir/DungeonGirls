using UnityEngine;

public static class DamageCalculator
{
    public struct DamageResult
    {
        public float DamageToHP;
        public bool WasBlocked;
    }

    // 3.3: урон меньше текущей брони блокируется полностью (0 по HP, броня не теряет единицу).
    // Иначе защита снижает урон на своё значение, остаток идёт по HP, после чего защита теряет
    // 1 единицу — кроме случая «полного пробития» ниже.
    //
    // 3.3 [ОБНОВЛЕНО, третий плейтест] «Полное пробитие»: если урон ≥ удвоенной текущей брони,
    // броня теряет 2 единицы вместо 1 (остальной расчёт урона/остатка не меняется). Пример:
    // броня=10, урон=12 — обычное пробитие, −1; урон=22 (≥20=2×10) — полное пробитие, −2.
    // Причина: без этого часть боёв могла затягиваться навечно (высокая броня вырабатывалась
    // только по 1 за удар). Считается редким случаем на практике.
    public static DamageResult ApplyPhysicalDamage(CombatantRuntime target, float incomingDamage)
    {
        if (incomingDamage < target.PhysicalDefenseCurrent)
        {
            return new DamageResult { DamageToHP = 0f, WasBlocked = true };
        }

        float remainder = incomingDamage - target.PhysicalDefenseCurrent;
        float armorLoss = incomingDamage >= target.PhysicalDefenseCurrent * 2f ? 2f : 1f;
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

    public static DamageResult ApplyDamage(CombatantRuntime target, float incomingDamage, DamageType damageType)
    {
        return damageType == DamageType.Physical
            ? ApplyPhysicalDamage(target, incomingDamage)
            : ApplyMagicalDamage(target, incomingDamage);
    }
}
