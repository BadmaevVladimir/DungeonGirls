using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public enum VeteranRank
{
    C,
    B,
    A,
    S,
    SPlus
}

public enum AttestationRunMode
{
    Release,
    FullMatrix
}

public enum CombatSimulationOutcome
{
    Victory,
    Defeat,
    Draw,
    Timeout,
    EventLimit,
    Error
}

public enum VeteranTrialArchetype
{
    BrassExecutioner,
    ThousandHandedGuardian,
    AshenBastion
}

public enum AttestationCompletionStatus
{
    Completed,
    Fallback
}

public static class VeteranRankFormat
{
    public static string ToPersistentString(VeteranRank rank) => rank == VeteranRank.SPlus ? "S+" : rank.ToString();

    public static bool TryParse(string value, out VeteranRank rank)
    {
        if (string.Equals(value, "S+", StringComparison.OrdinalIgnoreCase))
        {
            rank = VeteranRank.SPlus;
            return true;
        }
        return Enum.TryParse(value, true, out rank) && Enum.IsDefined(typeof(VeteranRank), rank);
    }
}

[Serializable]
public class VeteranActiveSkillSnapshot
{
    public string skillName;
    public SkillId skillId;
    public ActiveSkillType skillType;
    public float cooldownSeconds;
    public int hitCount;
    public float damageMultiplierPerHit;
}

[Serializable]
public class VeteranBuildSnapshot
{
    [NonSerialized] CombatantRuntime transientRuntime;
    public int schemaVersion = 1;
    public string characterId;
    [TextArea] public string combatantJson;
    public bool hasCritDamageMultiplierOverride;
    public float critDamageMultiplierOverridePercent;
    public VeteranActiveSkillSnapshot activeSkill;

    public static VeteranBuildSnapshot Capture(string characterId, CombatantRuntime runtime,
        ActiveSkillData activeSkillData, int activeSkillLevel)
    {
        if (runtime == null) throw new ArgumentNullException(nameof(runtime));
        var serializableRuntime = CombatRuntimeClone.Clone(runtime);
        serializableRuntime.ShieldPoolExpireTimer = 0f;
        foreach (var effect in serializableRuntime.ActiveDebuffs)
            if (float.IsInfinity(effect.RemainingTime)) effect.RemainingTime = float.MaxValue;
        var snapshot = new VeteranBuildSnapshot
        {
            characterId = characterId,
            combatantJson = JsonUtility.ToJson(serializableRuntime),
            hasCritDamageMultiplierOverride = runtime.CritDamageMultiplierOverridePercent.HasValue,
            critDamageMultiplierOverridePercent = runtime.CritDamageMultiplierOverridePercent ?? 0f
        };
        snapshot.transientRuntime = CombatRuntimeClone.Clone(serializableRuntime);

        if (activeSkillData != null)
        {
            snapshot.activeSkill = new VeteranActiveSkillSnapshot
            {
                skillName = activeSkillData.skillName,
                skillId = activeSkillData.skillId,
                skillType = activeSkillData.skillType,
                cooldownSeconds = activeSkillData.cooldownSeconds,
                hitCount = activeSkillData.skillType == ActiveSkillType.Toggle ? 0 : CombatManager.ResolveActiveSkillHitCount(
                    activeSkillData.skillId == SkillId.SmokeBomb ? CharacterClass.Rogue : CharacterClass.Warrior),
                damageMultiplierPerHit = activeSkillLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f }
            };
        }
        return snapshot;
    }

    public static VeteranBuildSnapshot CaptureTransient(string characterId, CombatantRuntime runtime,
        VeteranActiveSkillSnapshot activeSkillSnapshot = null)
    {
        if (runtime == null) throw new ArgumentNullException(nameof(runtime));
        return new VeteranBuildSnapshot
        {
            characterId = characterId,
            transientRuntime = CombatRuntimeClone.Clone(runtime),
            hasCritDamageMultiplierOverride = runtime.CritDamageMultiplierOverridePercent.HasValue,
            critDamageMultiplierOverridePercent = runtime.CritDamageMultiplierOverridePercent ?? 0f,
            activeSkill = activeSkillSnapshot
        };
    }

    public CombatantRuntime CreateFreshRuntime()
    {
        var runtime = transientRuntime != null
            ? CombatRuntimeClone.Clone(transientRuntime)
            : string.IsNullOrWhiteSpace(combatantJson) ? null : JsonUtility.FromJson<CombatantRuntime>(combatantJson);
        if (runtime == null) throw new InvalidOperationException("Veteran snapshot has no combatant payload.");

        runtime.IsPlayer = true;
        runtime.CurrentHP = runtime.MaxHP;
        runtime.PhysicalDefenseCurrent = runtime.PhysicalDefenseMax;
        runtime.MagicShieldCurrent = runtime.MagicShieldMax;
        runtime.ShieldPoolCurrent = 0f;
        runtime.ShieldPoolMax = 0f;
        runtime.ShieldPoolExpireTimer = float.PositiveInfinity;
        runtime.AttackLocked = false;
        runtime.Target = null;
        runtime.BossEncounter = null;
        runtime.BleedSource = null;
        runtime.ActiveFoodBuff = null;
        runtime.FoodReceivedHealingPercent = 0f;
        runtime.RunReceivedHealingPercent = 0f;
        runtime.FoodDamagePercent = 0f;
        runtime.FoodPhysicalDamagePercent = 0f;
        runtime.FoodBossDamagePercent = 0f;
        runtime.FoodArmorEffectivenessPercent = 0f;
        runtime.FoodAttackSpeedPercent = 0f;
        runtime.FoodCritChancePoints = 0f;
        runtime.FoodNegativeStatusDurationReductionPercent = 0f;
        runtime.FoodBarrierActive = false;
        runtime.CritDamageMultiplierOverridePercent = hasCritDamageMultiplierOverride
            ? critDamageMultiplierOverridePercent
            : (float?)null;
        runtime.ActiveDebuffs ??= new List<ActiveDebuff>();
        runtime.ActiveDebuffs.RemoveAll(effect => effect == null || !effect.IsEquipmentCurse);
        runtime.IsStealthed = false;
        runtime.StealthTimer = 0f;
        runtime.SmokeBombGuaranteedCritsRemaining = 0;
        runtime.IsBerserkActive = runtime.UniqueBerserkLevel > 0;
        runtime.BerserkTickAccumulator = 0f;
        runtime.HitsTakenSinceLastRegen = 0;
        runtime.CombatRegenCooldownRemaining = 0f;
        runtime.CursedParanoiaStacks = 0;
        runtime.CursedRecklessStacks = 0;
        runtime.CursedRecklessDecayTimer = 0f;
        runtime.Weapons ??= new List<WeaponAttackState>();
        foreach (var weapon in runtime.Weapons)
        {
            weapon.AttackTimer = 0f;
            weapon.CursedStacks = 0;
            weapon.PrototypeCounter = 0;
            weapon.PrototypeAccumulatedDamage = 0f;
            weapon.SecondsSinceLastAttack = 0f;
        }
        return runtime;
    }
}

static class CombatRuntimeClone
{
    public static CombatantRuntime Clone(CombatantRuntime source)
    {
        if (source == null) return null;
        var clone = new CombatantRuntime();
        foreach (var field in typeof(CombatantRuntime).GetFields())
        {
            if (field.IsNotSerialized || field.IsInitOnly) continue;
            if (field.Name == nameof(CombatantRuntime.Weapons) || field.Name == nameof(CombatantRuntime.ActiveDebuffs)) continue;
            field.SetValue(clone, field.GetValue(source));
        }
        clone.Weapons = new List<WeaponAttackState>();
        if (source.Weapons != null)
            foreach (var weapon in source.Weapons) clone.Weapons.Add(CloneWeapon(weapon));
        clone.ActiveDebuffs = new List<ActiveDebuff>();
        if (source.ActiveDebuffs != null)
            foreach (var effect in source.ActiveDebuffs)
                clone.ActiveDebuffs.Add(new ActiveDebuff
                {
                    Id = effect.Id,
                    RemainingTime = effect.RemainingTime,
                    AttackSpeedMultiplier = effect.AttackSpeedMultiplier,
                    IsBuff = effect.IsBuff,
                    IsEquipmentCurse = effect.IsEquipmentCurse,
                    CursedEffect = effect.CursedEffect
                });
        return clone;
    }

    static WeaponAttackState CloneWeapon(WeaponAttackState source)
    {
        if (source == null) return null;
        var clone = new WeaponAttackState();
        foreach (var field in typeof(WeaponAttackState).GetFields()) field.SetValue(clone, field.GetValue(source));
        return clone;
    }
}

[Serializable]
public class VeteranReferenceThreatProfile
{
    public float maxHp;
    public float physicalDefense;
    public float hitDamage;
    public float attacksPerSecond;
}

[Serializable]
public class VeteranTrialConfig
{
    public string trialId;
    public VeteranTrialArchetype archetype;
    public float hpMultiplier;
    public float defenseMultiplier;
    public float damageMultiplier;
    public float attackSpeedMultiplier;
    public float specialIntervalSeconds;
    public float specialDamageGrowth;
    public int specialCountBeforeBerserk;
    public int hitCountForArmorReduction;
    public float armorReductionPercent;
    public float armorReductionCapPercent;
    public float armorReductionDurationSeconds;
    public float barrierMaxHpPercent;
    public float barrierDurationSeconds;
    public float vulnerabilityDurationSeconds;
    public float vulnerabilityDamageBonusPercent;
    public float regenerationMaxHpPercentPerSecond;
    public float escalationIntervalSeconds;
    public float regenerationCapMultiplier;
    public float damageCapMultiplier;
}

[Serializable]
public class VeteranTrialSeedSet
{
    public string trialId;
    public int[] seeds = Array.Empty<int>();
}

[Serializable]
public class VeteranTierConfig
{
    public string tierId;
    public VeteranRank rank;
    [Range(1, 3)] public int requiredPassingTrials = 1;
    public float healthMultiplier;
    public float pressureMultiplier;
    public float escalationMultiplier;
    public List<VeteranTrialSeedSet> trialSeeds = new List<VeteranTrialSeedSet>();
}

public sealed class CombatSimulationRequest
{
    public VeteranBuildSnapshot Snapshot;
    public VeteranReferenceThreatProfile ReferenceProfile;
    public VeteranTierConfig Tier;
    public VeteranTrialConfig Trial;
    public int Seed;
    public float VirtualTimeLimitSeconds;
    public int ProcessedEventLimit;
}

public sealed class CombatSimulationResult
{
    public CombatSimulationOutcome Outcome;
    public float VirtualDuration;
    public int ProcessedEventCount;
    public float PlayerRemainingHp;
    public float EnemyRemainingHp;
    public int Seed;
    public string TraceHash;
    public string ErrorCode;
}

public sealed class VeteranAttestationRunResult
{
    public string TierId;
    public string TrialId;
    public int Seed;
    public CombatSimulationResult Simulation;
}

public sealed class VeteranAttestationResult
{
    public VeteranRank FinalRank = VeteranRank.C;
    public string RatingVersion;
    public string QualifyingTrialId;
    public int SimulationCount;
    public long CalculationMilliseconds;
    public AttestationCompletionStatus CompletionStatus;
    public string ErrorCode;
    public List<VeteranAttestationRunResult> Runs = new List<VeteranAttestationRunResult>();
}

public interface ICombatSimulationEngine
{
    CombatSimulationResult Simulate(CombatSimulationRequest request);
}

public interface IVeteranAttestationService
{
    VeteranAttestationResult Evaluate(VeteranBuildSnapshot snapshot, VeteranAttestationConfig config,
        AttestationRunMode mode);
}
