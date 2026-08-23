using UnityEngine;

public static class DamageCalculator
{
    public struct DamageResult
    {
        public float DamageToHP;
        public bool WasBlocked;
    }

    // 3.3 [ОТКАТИЛО после плейтеста]: правило минимального гарантированного урона (1 единица)
    // убрано — урон меньше защиты снова блокируется полностью (0 по HP, броня не теряет
    // единицу). Гейт против пробития брони Скелета стартовым оружием решён снижением его
    // защиты (2.4), а не смягчением этого правила. Иначе защита снижает урон на своё значение,
    // остаток идёт по HP, после чего защита теряет 1 единицу.
    public static DamageResult ApplyPhysicalDamage(CombatantRuntime target, float incomingDamage)
    {
        if (incomingDamage < target.PhysicalDefenseCurrent)
        {
            return new DamageResult { DamageToHP = 0f, WasBlocked = true };
        }

        float remainder = incomingDamage - target.PhysicalDefenseCurrent;
        target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - 1f);
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

    public static DamageResult ApplyDamage(CombatantRuntime target, float incomingDamage, DamageType damageType)
    {
        return damageType == DamageType.Physical
            ? ApplyPhysicalDamage(target, incomingDamage)
            : ApplyMagicalDamage(target, incomingDamage);
    }
}
