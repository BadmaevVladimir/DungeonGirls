using UnityEngine;

public static class DamageCalculator
{
    public struct DamageResult
    {
        public float DamageToHP;
        public bool WasBlocked;
    }

    // 3.3: урон меньше защиты — почти полностью блокируется, но минимум 1 единица урона
    // всё равно проходит по HP (броня при этом НЕ теряет единицу); иначе защита снижает
    // урон на своё значение, остаток идёт по HP, после чего защита теряет 1 единицу.
    public static DamageResult ApplyPhysicalDamage(CombatantRuntime target, float incomingDamage)
    {
        if (incomingDamage < target.PhysicalDefenseCurrent)
        {
            target.CurrentHP -= 1f;
            return new DamageResult { DamageToHP = 1f, WasBlocked = true };
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
