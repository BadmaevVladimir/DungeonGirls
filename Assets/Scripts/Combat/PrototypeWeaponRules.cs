using System;
using System.Collections.Generic;
using UnityEngine;

public static class PrototypeWeaponRules
{
    public static int CountUniquePositiveStatuses(CombatantRuntime owner) => Count(owner, true);
    public static int CountUniqueNegativeStatuses(CombatantRuntime owner) => Count(owner, false);

    static int Count(CombatantRuntime owner, bool positive)
    {
        if (owner == null) return 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var status in owner.ActiveDebuffs)
            if (status != null && status.IsBuff == positive && !string.IsNullOrWhiteSpace(status.Id)) ids.Add(status.Id);
        if (positive)
        {
            if (owner.IsStealthed) ids.Add("stealth");
            if (owner.IsBerserkActive) ids.Add("berserk");
            if (owner.ShieldPoolCurrent > 0f) ids.Add("barrier");
        }
        else
        {
            if (owner.IsFrozen || owner.FreezeStacks > 0) ids.Add("freeze");
            if (owner.HasBleed) ids.Add("bleed");
            if (owner.PoisonStacks > 0) ids.Add("poison");
            if (owner.RoguePoisonStacksOnTarget > 0) ids.Add("rogue_poison");
            if (owner.CritChanceDebuffPercent > 0f) ids.Add("crit_down");
        }
        return ids.Count;
    }

    public static float ResonanceDamageMultiplier(CombatantRuntime owner, WeaponAttackState weapon) =>
        1f + Mathf.Min(Mathf.Max(0, weapon.PrototypeMaxStacks), CountUniquePositiveStatuses(owner)) *
        Mathf.Max(0f, weapon.PrototypePrimaryValue) / 100f;

    public static float ResonanceAttackSpeedPercent(CombatantRuntime owner, WeaponAttackState weapon) =>
        Mathf.Min(Mathf.Max(0, weapon.PrototypeMaxStacks), CountUniqueNegativeStatuses(owner)) *
        Mathf.Max(0f, weapon.PrototypeSecondaryValue);

    public static float PendulumBonusPercent(float seconds, float percentPerSecond, float capPercent) =>
        Mathf.Min(Mathf.Max(0f, capPercent), Mathf.Floor(Mathf.Max(0f, seconds)) * Mathf.Max(0f, percentPerSecond));

    public static float LastArgumentDamageMultiplier(CombatantRuntime owner, WeaponAttackState weapon) =>
        1f + (owner == null || weapon == null ? 0f : owner.GetPositiveAttackSpeedBonusPercent() *
            Mathf.Max(0f, weapon.PrototypePrimaryValue) / 100f);

    public static float ActualShieldRemoved(float before, float after) => Mathf.Max(0f, before - Mathf.Max(0f, after));

    public static DamageCalculator.DamageResult Combine(DamageCalculator.DamageResult first,
        DamageCalculator.DamageResult second) => new DamageCalculator.DamageResult
    {
        DamageToHP = first.DamageToHP + second.DamageToHP,
        WasBlocked = first.WasBlocked && second.WasBlocked,
        ArmorWornOnBlock = first.ArmorWornOnBlock || second.ArmorWornOnBlock,
        ShieldPoolDamageAbsorbed = first.ShieldPoolDamageAbsorbed + second.ShieldPoolDamageAbsorbed
    };

    public static bool AdvanceLightningCounter(WeaponAttackState weapon)
    {
        int interval = Mathf.Max(1, weapon.PrototypeMaxStacks);
        weapon.PrototypeCounter++;
        if (weapon.PrototypeCounter < interval) return false;
        weapon.PrototypeCounter = 0;
        return true;
    }
}
