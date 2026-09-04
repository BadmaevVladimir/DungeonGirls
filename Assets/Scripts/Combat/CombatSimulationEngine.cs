using System;
using System.Collections.Generic;
using UnityEngine;

// The attestation owns only the scripted trial layer. Every ordinary attack, active skill hit,
// status tick, item effect and character mechanic is executed by CombatManager itself.
public sealed class CombatSimulationEngine : ICombatSimulationEngine
{
    const float Epsilon = 0.0001f;
    const float FixedStepSeconds = 0.02f;

    sealed class State
    {
        public GameObject Host;
        public CombatManager Combat;
        public ActiveSkillData TransientSkill;
        public CombatantRuntime Player;
        public CombatantRuntime Enemy;
        public List<CombatantRuntime> Enemies;
        public CombatSimulationRequest Request;
        public float Time;
        public int Events;
        public float NextSpecial = float.PositiveInfinity;
        public float NextPhase = float.PositiveInfinity;
        public float NextEscalation = float.PositiveInfinity;
        public float EnemyDamageScale = 1f;
        public float EnemySpeedScale = 1f;
        public float BastionHealingScale = 1f;
        public bool BastionVulnerable;
        public int ExecutionerJudgments;
        public int GuardianConnectedHits;
        public float GuardianArmorReductionPercent;
        public float GuardianArmorReductionUntil;
        public float BasePlayerArmorEffectivenessPercent;
        public float BasePlayerDamagePercent;
        public float BaseEnemyDamageMin;
        public float BaseEnemyDamageMax;
        public float BaseEnemyAttackSpeed;
        public uint Trace = 2166136261u;
    }

    public CombatSimulationResult Simulate(CombatSimulationRequest request)
    {
        if (request?.Snapshot == null || request.ReferenceProfile == null || request.Tier == null || request.Trial == null)
            return Error(request?.Seed ?? 0, "invalid_request");

        State state = null;
        try
        {
            state = CreateState(request);
            if (request.ProcessedEventLimit <= 0)
                return Result(state, CombatSimulationOutcome.EventLimit);

            while (state.Combat.IsCombatActive && state.Player.IsAlive && AnyEnemyAlive(state))
            {
                if (state.Events >= request.ProcessedEventLimit)
                    return Result(state, CombatSimulationOutcome.EventLimit);
                if (state.Time >= request.VirtualTimeLimitSeconds)
                    return Result(state, CombatSimulationOutcome.Timeout);

                float remaining = request.VirtualTimeLimitSeconds - state.Time;
                float step = Mathf.Min(FixedStepSeconds, remaining);
                step = Mathf.Min(step, TimeUntil(state.NextSpecial, state.Time));
                step = Mathf.Min(step, TimeUntil(state.NextPhase, state.Time));
                step = Mathf.Min(step, TimeUntil(state.NextEscalation, state.Time));
                // Float accumulation can leave an attack timer a few ulps short exactly at the
                // virtual boundary. Live combat receives the whole frame, so give the final sliver
                // the same treatment instead of returning one attack early.
                if (step > 0f && step <= Epsilon) step = Epsilon;
                if (step > 0f)
                {
                    state.Combat.Tick(step);
                    state.Time = Mathf.Min(request.VirtualTimeLimitSeconds, state.Time + step);
                    state.Events++;
                    TickTrialContinuousEffects(state, step);
                    ExpireGuardianArmorReduction(state);
                    MixTrace(state, 1u);
                }

                ProcessDueTrialEvents(state);
            }

            var outcome = state.Player.IsAlive && !AnyEnemyAlive(state)
                ? CombatSimulationOutcome.Victory
                : !state.Player.IsAlive && AnyEnemyAlive(state)
                    ? CombatSimulationOutcome.Defeat
                    : CombatSimulationOutcome.Draw;
            return Result(state, outcome);
        }
        catch (Exception exception)
        {
            return Error(request.Seed, exception.GetType().Name);
        }
        finally
        {
            Cleanup(state);
        }
    }

    static State CreateState(CombatSimulationRequest request)
    {
        var player = request.Snapshot.CreateFreshRuntime();
        CursedItemRules.ApplyEquippedCurses(player);
        var enemies = CreateEnemies(request);
        var enemy = enemies[0];
        var host = new GameObject("VeteranAttestationCombat") { hideFlags = HideFlags.HideAndDontSave };
        var combat = host.AddComponent<CombatManager>();
        combat.SetHeadlessSimulationMode(true);
        combat.SetRandomSource(new DeterministicCombatRandom(request.Seed));

        var state = new State
        {
            Host = host,
            Combat = combat,
            Player = player,
            Enemy = enemy,
            Enemies = enemies,
            Request = request,
            BasePlayerArmorEffectivenessPercent = player.FoodArmorEffectivenessPercent,
            BasePlayerDamagePercent = player.FoodDamagePercent,
            BaseEnemyDamageMin = enemy.Weapons[0].DamageMin,
            BaseEnemyDamageMax = enemy.Weapons[0].DamageMax,
            BaseEnemyAttackSpeed = enemy.Weapons[0].AttackSpeed
        };

        combat.AttackConnected += (attacker, target) => OnAttackConnected(state, attacker, target);
        combat.HitResolved += (target, _, _, _) => OnHitResolved(state, target);
        ConfigureActiveSkill(state, request.Snapshot.activeSkill);
        combat.StartCombat(player, enemies);
        ActivateInitialSkill(state, request.Snapshot.activeSkill);

        if (request.Trial.archetype == VeteranTrialArchetype.BrassExecutioner)
            state.NextSpecial = ScaledSpecialInterval(state);
        else if (request.Trial.archetype == VeteranTrialArchetype.ThousandHandedGuardian)
            state.NextEscalation = ScaledEscalationInterval(state);
        else if (request.Trial.archetype == VeteranTrialArchetype.AshenBastion)
        {
            RefillBastionBarrier(state);
            state.NextPhase = request.Trial.barrierDurationSeconds;
            state.NextEscalation = ScaledEscalationInterval(state);
        }

        return state;
    }

    static void ConfigureActiveSkill(State state, VeteranActiveSkillSnapshot snapshot)
    {
        if (snapshot == null) return;
        state.TransientSkill = ScriptableObject.CreateInstance<ActiveSkillData>();
        state.TransientSkill.hideFlags = HideFlags.HideAndDontSave;
        state.TransientSkill.skillName = snapshot.skillName;
        state.TransientSkill.skillId = snapshot.skillId;
        state.TransientSkill.skillType = snapshot.skillType;
        state.TransientSkill.cooldownSeconds = snapshot.cooldownSeconds;
        state.Combat.ConfigureActiveSkills(new[]
        {
            new ActiveSkillConfigEntry(state.TransientSkill, snapshot.hitCount,
                snapshot.damageMultiplierPerHit, autoMode: true)
        });
    }

    static void ActivateInitialSkill(State state, VeteranActiveSkillSnapshot snapshot)
    {
        if (snapshot == null || state.Combat.ActiveSkills.Count == 0) return;
        // Every toggle stays enabled for attestation; cooldown skills fire whenever ready.
        state.Combat.TryActivateSkill(0);
    }

    static List<CombatantRuntime> CreateEnemies(CombatSimulationRequest request)
    {
        float hp = request.ReferenceProfile.maxHp * request.Tier.healthMultiplier * request.Trial.hpMultiplier;
        float defense = request.ReferenceProfile.physicalDefense * request.Tier.healthMultiplier * request.Trial.defenseMultiplier;
        float damage = request.ReferenceProfile.hitDamage * request.Tier.pressureMultiplier * request.Trial.damageMultiplier;
        float speed = request.ReferenceProfile.attacksPerSecond * request.Trial.attackSpeedMultiplier;
        int count = request.Trial.archetype == VeteranTrialArchetype.ThousandHandedGuardian ? 3 : 1;
        var enemies = new List<CombatantRuntime>(count);
        for (int i = 0; i < count; i++) enemies.Add(new CombatantRuntime
        {
            DisplayName = count == 1 ? request.Trial.trialId : $"{request.Trial.trialId}_{i + 1}",
            MaxHP = hp / count,
            CurrentHP = hp / count,
            PhysicalDefenseMax = defense / count,
            PhysicalDefenseCurrent = defense / count,
            Weapons = new List<WeaponAttackState>
            {
                new WeaponAttackState
                {
                    DamageMin = damage,
                    DamageMax = damage,
                    DamageType = DamageType.Physical,
                    AttackSpeed = Mathf.Max(0.01f, speed / count)
                }
            }
        });
        return enemies;
    }

    static void OnAttackConnected(State state, CombatantRuntime attacker, CombatantRuntime target)
    {
        if (state.Request.Trial.archetype != VeteranTrialArchetype.ThousandHandedGuardian ||
            !state.Enemies.Contains(attacker) || target != state.Player) return;

        state.GuardianConnectedHits++;
        int threshold = Mathf.Max(1, state.Request.Trial.hitCountForArmorReduction);
        if (state.GuardianConnectedHits % threshold != 0) return;
        state.GuardianArmorReductionPercent = Mathf.Min(state.Request.Trial.armorReductionCapPercent,
            state.GuardianArmorReductionPercent + state.Request.Trial.armorReductionPercent);
        state.GuardianArmorReductionUntil = state.Time + state.Request.Trial.armorReductionDurationSeconds;
        ApplyGuardianArmorReduction(state);
    }

    static void OnHitResolved(State state, CombatantRuntime target)
    {
        if (state.Request.Trial.archetype == VeteranTrialArchetype.AshenBastion && target == state.Enemy &&
            !state.BastionVulnerable && state.Enemy.ShieldPoolMax > 0f && state.Enemy.ShieldPoolCurrent <= Epsilon)
            EnterBastionVulnerability(state);
    }

    static void TickTrialContinuousEffects(State state, float deltaTime)
    {
        if (state.Request.Trial.archetype != VeteranTrialArchetype.AshenBastion || !state.Enemy.IsAlive) return;
        float heal = state.Enemy.MaxHP * state.Request.Trial.regenerationMaxHpPercentPerSecond / 100f *
            state.BastionHealingScale * deltaTime;
        state.Enemy.Heal(heal);
    }

    static void ProcessDueTrialEvents(State state)
    {
        int guard = 0;
        while (guard++ < 8 && state.Combat.IsCombatActive)
        {
            bool processed = false;
            if (state.Time + Epsilon >= state.NextSpecial)
            {
                ResolveExecutionerSpecial(state);
                processed = true;
            }
            if (state.Time + Epsilon >= state.NextPhase)
            {
                ResolveBastionPhase(state);
                processed = true;
            }
            if (state.Time + Epsilon >= state.NextEscalation)
            {
                ResolveEscalation(state);
                processed = true;
            }
            if (!processed) break;
        }
    }

    static void ResolveExecutionerSpecial(State state)
    {
        state.ExecutionerJudgments++;
        float growth = Mathf.Pow(1f + state.Request.Trial.specialDamageGrowth,
            Mathf.Max(0, state.ExecutionerJudgments - 1));
        for (int i = 0; i < 3 && state.Player.IsAlive && state.Enemy.IsAlive; i++)
            state.Combat.ResolveScriptedAttack(state.Enemy, state.Enemy.Weapons[0], growth);

        if (state.ExecutionerJudgments >= state.Request.Trial.specialCountBeforeBerserk)
        {
            state.EnemyDamageScale = Mathf.Max(state.EnemyDamageScale, state.Request.Trial.damageCapMultiplier);
            state.EnemySpeedScale = Mathf.Max(state.EnemySpeedScale, state.Request.Trial.damageCapMultiplier);
            ApplyEnemyScaling(state);
        }
        state.NextSpecial = state.Time + ScaledSpecialInterval(state);
        state.Events++;
        MixTrace(state, 2u);
    }

    static void ResolveBastionPhase(State state)
    {
        if (state.BastionVulnerable)
        {
            state.BastionVulnerable = false;
            state.Player.FoodDamagePercent = state.BasePlayerDamagePercent;
        }
        RefillBastionBarrier(state);
        state.NextPhase = state.Time + Mathf.Max(FixedStepSeconds, state.Request.Trial.barrierDurationSeconds);
        state.Events++;
        MixTrace(state, 3u);
    }

    static void EnterBastionVulnerability(State state)
    {
        state.BastionVulnerable = true;
        state.Player.FoodDamagePercent = state.BasePlayerDamagePercent +
            state.Request.Trial.vulnerabilityDamageBonusPercent;
        state.NextPhase = state.Time + Mathf.Max(FixedStepSeconds, state.Request.Trial.vulnerabilityDurationSeconds);
    }

    static void RefillBastionBarrier(State state)
    {
        state.Enemy.ShieldPoolMax = state.Enemy.MaxHP * state.Request.Trial.barrierMaxHpPercent / 100f;
        state.Enemy.ShieldPoolCurrent = state.Enemy.ShieldPoolMax;
        state.Enemy.ShieldPoolExpireTimer = float.PositiveInfinity;
    }

    static void ResolveEscalation(State state)
    {
        float step = 0.1f * state.Request.Tier.escalationMultiplier;
        state.EnemyDamageScale = Mathf.Min(state.Request.Trial.damageCapMultiplier,
            state.EnemyDamageScale * (1f + step));
        state.EnemySpeedScale = Mathf.Min(state.Request.Trial.damageCapMultiplier,
            state.EnemySpeedScale * (1f + step));
        state.BastionHealingScale = Mathf.Min(state.Request.Trial.regenerationCapMultiplier,
            state.BastionHealingScale * (1f + step));
        ApplyEnemyScaling(state);
        state.NextEscalation = state.Time + ScaledEscalationInterval(state);
        state.Events++;
        MixTrace(state, 4u);
    }

    static void ApplyEnemyScaling(State state)
    {
        foreach (var enemy in state.Enemies)
        {
            var weapon = enemy.Weapons[0];
            weapon.DamageMin = state.BaseEnemyDamageMin * state.EnemyDamageScale;
            weapon.DamageMax = state.BaseEnemyDamageMax * state.EnemyDamageScale;
            weapon.AttackSpeed = Mathf.Max(0.01f, state.BaseEnemyAttackSpeed * state.EnemySpeedScale);
        }
    }

    static void ApplyGuardianArmorReduction(State state) =>
        state.Player.FoodArmorEffectivenessPercent = state.BasePlayerArmorEffectivenessPercent -
            state.GuardianArmorReductionPercent;

    static void ExpireGuardianArmorReduction(State state)
    {
        if (state.GuardianArmorReductionPercent <= 0f || state.Time + Epsilon < state.GuardianArmorReductionUntil)
            return;
        state.GuardianArmorReductionPercent = 0f;
        state.Player.FoodArmorEffectivenessPercent = state.BasePlayerArmorEffectivenessPercent;
    }

    static float ScaledSpecialInterval(State state) => Mathf.Max(FixedStepSeconds,
        state.Request.Trial.specialIntervalSeconds / Mathf.Max(0.01f, state.Request.Tier.escalationMultiplier));

    static float ScaledEscalationInterval(State state) => Mathf.Max(FixedStepSeconds,
        state.Request.Trial.escalationIntervalSeconds / Mathf.Max(0.01f, state.Request.Tier.escalationMultiplier));

    static float TimeUntil(float scheduledTime, float now) => float.IsPositiveInfinity(scheduledTime)
        ? float.PositiveInfinity
        : Mathf.Max(0f, scheduledTime - now);

    static void MixTrace(State state, uint eventCode)
    {
        void Mix(uint value) { state.Trace ^= value; state.Trace *= 16777619u; }
        Mix(eventCode);
        Mix((uint)Mathf.RoundToInt(state.Time * 1000f));
        Mix((uint)Mathf.Max(0, Mathf.RoundToInt(state.Player.CurrentHP * 100f)));
        Mix((uint)Mathf.Max(0, Mathf.RoundToInt(TotalEnemyHp(state) * 100f)));
    }

    static CombatSimulationResult Result(State state, CombatSimulationOutcome outcome) => new CombatSimulationResult
    {
        Outcome = outcome,
        VirtualDuration = state?.Time ?? 0f,
        ProcessedEventCount = state?.Events ?? 0,
        PlayerRemainingHp = state == null ? 0f : Mathf.Max(0f, state.Player.CurrentHP),
        EnemyRemainingHp = state == null ? 0f : Mathf.Max(0f, TotalEnemyHp(state)),
        Seed = state?.Request.Seed ?? 0,
        TraceHash = state == null ? string.Empty : state.Trace.ToString("X8")
    };

    static CombatSimulationResult Error(int seed, string code) => new CombatSimulationResult
    {
        Outcome = CombatSimulationOutcome.Error,
        Seed = seed,
        ErrorCode = code
    };

    static void Cleanup(State state)
    {
        if (state == null) return;
        if (state.Combat != null && state.Combat.IsCombatActive) state.Combat.AbortCombat();
        DestroyTransient(state.TransientSkill);
        DestroyTransient(state.Host);
    }

    static bool AnyEnemyAlive(State state)
    {
        if (state?.Enemies == null) return false;
        foreach (var enemy in state.Enemies) if (enemy != null && enemy.IsAlive) return true;
        return false;
    }

    static float TotalEnemyHp(State state)
    {
        float total = 0f;
        if (state?.Enemies == null) return total;
        foreach (var enemy in state.Enemies) if (enemy != null) total += Mathf.Max(0f, enemy.CurrentHP);
        return total;
    }

    static void DestroyTransient(UnityEngine.Object value)
    {
        if (value == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(value);
        else UnityEngine.Object.Destroy(value);
#else
        UnityEngine.Object.Destroy(value);
#endif
    }
}
