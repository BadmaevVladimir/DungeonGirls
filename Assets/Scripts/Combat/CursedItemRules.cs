using UnityEngine;

// Чистые балансные правила Cursed. ItemData выбирает эффект, а менеджеры лишь применяют результат.
public static class CursedItemRules
{
    public const int MaxStacks = 5;
    public const int RecklessMaxStacks = 10;
    public const float RecklessStackDecaySeconds = 3f;
    public const int OathbreakerCurrencyPerCrit = 30;

    static readonly float[] StackPercent = { 0f, 5f, 6f, 7f, 8f, 10f };
    static readonly float[] ChargeSpeedPercent = { 0f, 15f, 20f, 25f, 30f, 35f };
    static readonly float[] LastArgumentHpPercent = { 0f, 2f, 3f, 4f, 5f, 6f };
    static readonly float[] StealthDamagePercent = { 0f, 20f, 25f, 30f, 35f, 40f };
    static readonly float[] ThornSpeedPercent = { 0f, 20f, 25f, 30f, 35f, 40f };

    static int Rank(int rank) => Mathf.Clamp(rank, 1, 5);
    public static float StackBonusPercent(int rank, int stacks) => StackPercent[Rank(rank)] * Mathf.Clamp(stacks, 0, MaxStacks);
    public static float ChargeAttackSpeedPercent(int rank) => ChargeSpeedPercent[Rank(rank)];
    public static float LastArgumentBonusDamage(float maxHp, int rank) => Mathf.Max(0f, maxHp) * LastArgumentHpPercent[Rank(rank)] / 100f;
    public static float StealthDamageBonusPercent(int rank) => StealthDamagePercent[Rank(rank)];
    public static float ThornAttackSpeedBonusPercent(int rank) => ThornSpeedPercent[Rank(rank)];
    public static float ExecutionerDamageMultiplier(float hp, float maxHp)
    {
        float ratio = maxHp > 0f ? hp / maxHp : 0f;
        if (ratio <= 0.25f) return 2f;
        if (ratio >= 0.75f) return 0.75f;
        return 1f;
    }
    public static float RecklessDefenseMultiplier(int stacks) => Mathf.Max(0.7f, 1f - Mathf.Clamp(stacks, 0, RecklessMaxStacks) * 0.03f);
    public static float ParanoiaIncomingMultiplier(int stacks) => 1f + Mathf.Clamp(stacks, 0, MaxStacks) * 0.05f;

    public static float CalculateNormalCritDamage(CombatantRuntime owner, WeaponAttackState weapon)
    {
        if (owner == null || weapon == null) return 0f;
        float damage = (weapon.DamageMin + weapon.DamageMax) * 0.5f;
        if (owner.SkillUnyieldingLevel > 0 && owner.HasActiveDebuff)
            damage *= 1f + owner.SkillUnyieldingLevel * 0.05f;
        damage *= 1f + owner.ItemDamageBonusPercent / 100f;
        float critMultiplier = owner.CritDamageMultiplierOverridePercent ?? 150f;
        if (owner.CritChanceReplacedByRage)
            critMultiplier += (owner.SkillCriticalHitsLevel * 10f + owner.CritChanceBonusFromItems) * 2f;
        return damage * critMultiplier / 100f;
    }

    public static string CurseId(CursedEffectId effect) => "cursed_" + effect.ToString().ToLowerInvariant();
    public static string CurseName(CursedEffectId effect) => effect switch
    {
        CursedEffectId.Oathbreaker => "Расплата",
        CursedEffectId.Executioner => "Нетерпение палача",
        CursedEffectId.BerserkerAxe => "Безрассудство",
        CursedEffectId.RecklessCharge => "Открытая стойка",
        CursedEffectId.LastArgument => "Запрет восстановления брони",
        CursedEffectId.BetrayerAndAccomplice => "Предательство скрытности",
        CursedEffectId.ParanoiaBlades => "Срыв",
        _ => string.Empty
    };

    public static bool HasEquipmentCurse(CursedEffectId effect) =>
        effect != CursedEffectId.None && effect != CursedEffectId.ThornAxe;

    public static bool IsCurseActive(CombatantRuntime owner, CursedEffectId effect) =>
        owner != null && owner.ActiveDebuffs.Exists(d => d.IsEquipmentCurse && d.CursedEffect == effect);

    public static bool IgnoresNewDebuffs(CombatantRuntime target) => target != null &&
        target.SkillStubbornnessLevel > 0 && target.Rage > RageRules.StubbornnessThreshold(target.SkillStubbornnessLevel);

    public static bool TryApplyEquipmentCurse(CombatantRuntime owner, CursedEffectId effect)
    {
        if (owner == null || !HasEquipmentCurse(effect) || IgnoresNewDebuffs(owner)) return false;
        if (IsCurseActive(owner, effect)) return true;
        owner.ActiveDebuffs.Add(new ActiveDebuff
        {
            Id = CurseId(effect), RemainingTime = float.PositiveInfinity, IsEquipmentCurse = true,
            CursedEffect = effect
        });
        return true;
    }

    public static void ApplyEquippedCurses(CombatantRuntime owner)
    {
        if (owner == null) return;
        foreach (var weapon in owner.Weapons) TryApplyEquipmentCurse(owner, weapon.CursedEffect);
    }
}
