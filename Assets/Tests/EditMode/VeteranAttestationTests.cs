using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class VeteranAttestationTests
{
    sealed class FakeEngine : ICombatSimulationEngine
    {
        readonly Func<CombatSimulationRequest, CombatSimulationOutcome> resolve;
        public readonly List<CombatSimulationRequest> Requests = new List<CombatSimulationRequest>();
        public FakeEngine(Func<CombatSimulationRequest, CombatSimulationOutcome> resolve) => this.resolve = resolve;
        public CombatSimulationResult Simulate(CombatSimulationRequest request)
        {
            Requests.Add(request);
            return new CombatSimulationResult { Outcome = resolve(request), Seed = request.Seed };
        }
    }

    static VeteranBuildSnapshot Snapshot(float damage = 20f, float attackSpeed = 1f)
    {
        var runtime = new CombatantRuntime
        {
            DisplayName = "Test",
            IsPlayer = true,
            MaxHP = 300f,
            CurrentHP = 1f,
            PhysicalDefenseMax = 30f,
            PhysicalDefenseCurrent = 2f,
            Weapons = new List<WeaponAttackState>
            {
                new WeaponAttackState { DamageMin = damage, DamageMax = damage, AttackSpeed = attackSpeed, DamageType = DamageType.Physical }
            }
        };
        return VeteranBuildSnapshot.Capture("test", runtime, null, 1);
    }

    static VeteranAttestationConfig Config()
    {
        var config = ScriptableObject.CreateInstance<VeteranAttestationConfig>();
        config.ratingVersion = "test-v1";
        config.referenceProfile = new VeteranReferenceThreatProfile
        {
            maxHp = 100f, physicalDefense = 0f, hitDamage = 5f, attacksPerSecond = 1f
        };
        config.virtualTimeLimitSeconds = 120f;
        config.processedEventLimit = 100000;
        config.realHardTimeoutMilliseconds = 2000;
        config.trials = new List<VeteranTrialConfig>
        {
            Trial("t1", VeteranTrialArchetype.BrassExecutioner),
            Trial("t2", VeteranTrialArchetype.ThousandHandedGuardian),
            Trial("t3", VeteranTrialArchetype.AshenBastion)
        };
        VeteranRank[] ranks = { VeteranRank.B, VeteranRank.A, VeteranRank.S, VeteranRank.SPlus };
        for (int tierIndex = 0; tierIndex < ranks.Length; tierIndex++)
        {
            var tier = new VeteranTierConfig
            {
                tierId = VeteranRankFormat.ToPersistentString(ranks[tierIndex]),
                rank = ranks[tierIndex],
                requiredPassingTrials = 1,
                healthMultiplier = 1f + tierIndex,
                pressureMultiplier = 1f + tierIndex,
                escalationMultiplier = 1f + tierIndex
            };
            for (int trialIndex = 0; trialIndex < 3; trialIndex++)
                tier.trialSeeds.Add(new VeteranTrialSeedSet
                {
                    trialId = $"t{trialIndex + 1}",
                    seeds = new[] { tierIndex * 100 + trialIndex * 10 + 1, tierIndex * 100 + trialIndex * 10 + 2, tierIndex * 100 + trialIndex * 10 + 3 }
                });
            config.tiers.Add(tier);
        }
        return config;
    }

    static VeteranTrialConfig Trial(string id, VeteranTrialArchetype archetype) => new VeteranTrialConfig
    {
        trialId = id,
        archetype = archetype,
        hpMultiplier = 1f,
        defenseMultiplier = 1f,
        damageMultiplier = 1f,
        attackSpeedMultiplier = 1f,
        specialIntervalSeconds = 6f,
        specialDamageGrowth = 0.25f,
        specialCountBeforeBerserk = 4,
        hitCountForArmorReduction = 10,
        armorReductionPercent = 3f,
        armorReductionCapPercent = 30f,
        armorReductionDurationSeconds = 6f,
        barrierMaxHpPercent = 25f,
        barrierDurationSeconds = 5f,
        vulnerabilityDurationSeconds = 3f,
        vulnerabilityDamageBonusPercent = 25f,
        regenerationMaxHpPercentPerSecond = 0.5f,
        escalationIntervalSeconds = 20f,
        regenerationCapMultiplier = 3f,
        damageCapMultiplier = 3f
    };

    [Test]
    public void FullFailureAtB_ReturnsC()
    {
        var engine = new FakeEngine(_ => CombatSimulationOutcome.Defeat);
        var result = new VeteranAttestationService(engine).Evaluate(Snapshot(), Config(), AttestationRunMode.Release);
        Assert.AreEqual(VeteranRank.C, result.FinalRank);
        Assert.AreEqual(6, result.SimulationCount);
    }

    [Test]
    public void PassBFailA_ReturnsB()
    {
        var engine = new FakeEngine(request => request.Tier.rank == VeteranRank.B
            ? CombatSimulationOutcome.Victory : CombatSimulationOutcome.Defeat);
        var result = new VeteranAttestationService(engine).Evaluate(Snapshot(), Config(), AttestationRunMode.Release);
        Assert.AreEqual(VeteranRank.B, result.FinalRank);
    }

    [Test]
    public void PassingEveryTier_ReturnsSPlusAndStopsEachTrialAtTwoWins()
    {
        var engine = new FakeEngine(_ => CombatSimulationOutcome.Victory);
        var result = new VeteranAttestationService(engine).Evaluate(Snapshot(), Config(), AttestationRunMode.Release);
        Assert.AreEqual(VeteranRank.SPlus, result.FinalRank);
        Assert.AreEqual(8, result.SimulationCount);
    }

    [Test]
    public void SplitFirstTwoRuns_ExecutesThirdRun()
    {
        var engine = new FakeEngine(request => request.Seed % 10 == 2
            ? CombatSimulationOutcome.Defeat : CombatSimulationOutcome.Victory);
        var result = new VeteranAttestationService(engine).Evaluate(Snapshot(), Config(), AttestationRunMode.Release);
        Assert.AreEqual(12, result.SimulationCount);
    }

    [Test]
    public void OneSuccessfulTrialAdvancesTier()
    {
        var engine = new FakeEngine(request => request.Trial.trialId == "t3"
            ? CombatSimulationOutcome.Victory : CombatSimulationOutcome.Defeat);
        var result = new VeteranAttestationService(engine).Evaluate(Snapshot(), Config(), AttestationRunMode.Release);
        Assert.AreEqual(VeteranRank.SPlus, result.FinalRank);
        Assert.AreEqual("t3", result.QualifyingTrialId);
    }

    [Test]
    public void TierCanRequireMultipleDifferentTrials()
    {
        var config = Config();
        foreach (var tier in config.tiers) tier.requiredPassingTrials = 2;
        var engine = new FakeEngine(request => request.Trial.trialId == "t3"
            ? CombatSimulationOutcome.Victory : CombatSimulationOutcome.Defeat);

        var result = new VeteranAttestationService(engine).Evaluate(Snapshot(), config, AttestationRunMode.Release);

        Assert.AreEqual(VeteranRank.C, result.FinalRank);
    }

    [Test]
    public void FullMatrix_ExecutesAllThirtySixRuns()
    {
        var engine = new FakeEngine(_ => CombatSimulationOutcome.Defeat);
        var result = new VeteranAttestationService(engine).Evaluate(Snapshot(), Config(), AttestationRunMode.FullMatrix);
        Assert.AreEqual(36, result.SimulationCount);
        Assert.AreEqual(VeteranRank.C, result.FinalRank);
    }

    [Test]
    public void InvalidConfig_FallsBackToC()
    {
        var config = Config();
        config.ratingVersion = string.Empty;
        var result = new VeteranAttestationService(new FakeEngine(_ => CombatSimulationOutcome.Victory))
            .Evaluate(Snapshot(), config, AttestationRunMode.Release);
        Assert.AreEqual(VeteranRank.C, result.FinalRank);
        Assert.AreEqual(AttestationCompletionStatus.Fallback, result.CompletionStatus);
    }

    [Test]
    public void RealSimulation_IsDeterministicAndDoesNotMutateSnapshotOrUnityRandom()
    {
        var snapshot = Snapshot(40f, 2f);
        string before = JsonUtility.ToJson(snapshot);
        var config = Config();
        var request = new CombatSimulationRequest
        {
            Snapshot = snapshot,
            ReferenceProfile = config.referenceProfile,
            Tier = config.tiers[0],
            Trial = config.trials[1],
            Seed = 777,
            VirtualTimeLimitSeconds = 120f,
            ProcessedEventLimit = 100000
        };

        UnityEngine.Random.InitState(42);
        _ = UnityEngine.Random.value;
        var first = new CombatSimulationEngine().Simulate(request);
        float actualNext = UnityEngine.Random.value;
        UnityEngine.Random.InitState(42);
        _ = UnityEngine.Random.value;
        float expectedNext = UnityEngine.Random.value;
        var second = new CombatSimulationEngine().Simulate(request);

        Assert.AreEqual(first.Outcome, second.Outcome);
        Assert.AreEqual(first.TraceHash, second.TraceHash);
        Assert.AreEqual(expectedNext, actualNext);
        Assert.AreEqual(before, JsonUtility.ToJson(snapshot));
    }

    [Test]
    public void FreshRuntime_RestoresResourcesAndDoesNotShareWeaponState()
    {
        var snapshot = Snapshot();
        var first = snapshot.CreateFreshRuntime();
        var second = snapshot.CreateFreshRuntime();
        first.CurrentHP = 1f;
        first.Weapons[0].AttackTimer = 99f;
        Assert.AreEqual(second.MaxHP, second.CurrentHP);
        Assert.AreEqual(second.PhysicalDefenseMax, second.PhysicalDefenseCurrent);
        Assert.AreEqual(0f, second.Weapons[0].AttackTimer);
    }

    [Test]
    public void TogglePolicy_EnablesBerserkForAttestation()
    {
        var player = new CombatantRuntime { UniqueBerserkLevel = 2 };
        CombatAutoSkillPolicy.EnableAlwaysOnToggle(player, new VeteranActiveSkillSnapshot
        {
            skillId = SkillId.Berserk,
            skillType = ActiveSkillType.Toggle
        });
        Assert.IsTrue(player.IsBerserkActive);
    }

    [Test]
    public void VeteranCreation_IsAllowedOnlyForVictory()
    {
        Assert.IsTrue(RunFlowController.ShouldCreateVeteran(true));
        Assert.IsFalse(RunFlowController.ShouldCreateVeteran(false));
    }

    [Test]
    public void SimulationLimits_ReturnTimeoutAndEventLimit()
    {
        var config = Config();
        var request = new CombatSimulationRequest
        {
            Snapshot = Snapshot(), ReferenceProfile = config.referenceProfile, Tier = config.tiers[0],
            Trial = config.trials[0], Seed = 5, VirtualTimeLimitSeconds = 0.1f, ProcessedEventLimit = 100
        };
        Assert.AreEqual(CombatSimulationOutcome.Timeout, new CombatSimulationEngine().Simulate(request).Outcome);
        request.VirtualTimeLimitSeconds = 120f;
        request.ProcessedEventLimit = 0;
        Assert.AreEqual(CombatSimulationOutcome.EventLimit, new CombatSimulationEngine().Simulate(request).Outcome);
    }

    [Test]
    public void HeadlessAndLive_SimplePhysicalAttackHaveMatchingResult()
    {
        var liveObject = new GameObject("live-combat");
        try
        {
            var live = liveObject.AddComponent<CombatManager>();
            live.SetRandomSource(new DeterministicCombatRandom(123));
            var player = Snapshot(20f, 1f).CreateFreshRuntime();
            var enemy = new CombatantRuntime
            {
                MaxHP = 100f, CurrentHP = 100f, PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f,
                Weapons = new List<WeaponAttackState>
                {
                    new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f, DamageType = DamageType.Physical }
                }
            };
            live.StartCombat(player, new List<CombatantRuntime> { enemy });
            live.Tick(1f);

            var config = Config();
            config.referenceProfile.maxHp = 100f;
            config.referenceProfile.physicalDefense = 10f;
            config.referenceProfile.hitDamage = 1f;
            config.referenceProfile.attacksPerSecond = 0.01f;
            var simulation = new CombatSimulationEngine().Simulate(new CombatSimulationRequest
            {
                Snapshot = Snapshot(20f, 1f),
                ReferenceProfile = config.referenceProfile,
                Tier = new VeteranTierConfig { healthMultiplier = 1f, pressureMultiplier = 1f, escalationMultiplier = 1f },
                Trial = Trial("parity", VeteranTrialArchetype.BrassExecutioner),
                Seed = 123,
                VirtualTimeLimitSeconds = 1f,
                ProcessedEventLimit = 100
            });

            Assert.AreEqual(enemy.CurrentHP, simulation.EnemyRemainingHp, 0.001f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(liveObject);
        }
    }

    [Test]
    public void HeadlessAndLive_ComplexWeaponAndStatusesHaveMatchingResult()
    {
        var liveObject = new GameObject("live-combat-complex");
        try
        {
            var runtime = new CombatantRuntime
            {
                DisplayName = "Complex",
                IsPlayer = true,
                MaxHP = 350f,
                CurrentHP = 350f,
                PhysicalDefenseMax = 25f,
                PhysicalDefenseCurrent = 25f,
                SkillCriticalHitsLevel = 5,
                SkillBleedLevel = 5,
                SkillFreezeLevel = 5,
                SkillPoisonedBladeLevel = 5,
                SkillEyeForAnEyeLevel = 1,
                ItemEmbraceOfNightLevel = 5,
                Weapons = new List<WeaponAttackState>
                {
                    new WeaponAttackState
                    {
                        DamageMin = 30f, DamageMax = 30f, AttackSpeed = 2f, DamageType = DamageType.Physical,
                        PrototypeEffect = WeaponPrototypeEffectId.LightningSpear, PrototypePrimaryValue = 50f,
                        PrototypeMaxStacks = 2, VampirismLevel = 5, ArmorBreakLevel = 5, ExecutionLevel = 5
                    }
                }
            };
            var snapshot = VeteranBuildSnapshot.CaptureTransient("complex", runtime);
            var livePlayer = snapshot.CreateFreshRuntime();
            var liveEnemy = new CombatantRuntime
            {
                MaxHP = 600f, CurrentHP = 600f, PhysicalDefenseMax = 20f, PhysicalDefenseCurrent = 20f,
                Weapons = new List<WeaponAttackState>
                {
                    new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f, DamageType = DamageType.Physical }
                }
            };
            var live = liveObject.AddComponent<CombatManager>();
            live.SetHeadlessSimulationMode(true);
            live.SetRandomSource(new DeterministicCombatRandom(987));
            live.StartCombat(livePlayer, new List<CombatantRuntime> { liveEnemy });
            for (int i = 0; i < 150; i++) live.Tick(0.02f);

            var trial = Trial("complex-parity", VeteranTrialArchetype.BrassExecutioner);
            trial.hpMultiplier = 1f;
            trial.defenseMultiplier = 1f;
            trial.damageMultiplier = 1f;
            trial.attackSpeedMultiplier = 1f;
            trial.escalationIntervalSeconds = 20f;
            trial.specialIntervalSeconds = 20f;
            var simulation = new CombatSimulationEngine().Simulate(new CombatSimulationRequest
            {
                Snapshot = snapshot,
                ReferenceProfile = new VeteranReferenceThreatProfile
                {
                    maxHp = 600f, physicalDefense = 20f, hitDamage = 1f, attacksPerSecond = 0.01f
                },
                Tier = new VeteranTierConfig
                {
                    healthMultiplier = 1f, pressureMultiplier = 1f, escalationMultiplier = 1f
                },
                Trial = trial,
                Seed = 987,
                VirtualTimeLimitSeconds = 3f,
                ProcessedEventLimit = 10000
            });

            Assert.AreEqual(Mathf.Max(0f, liveEnemy.CurrentHP), simulation.EnemyRemainingHp, 0.001f);
            Assert.AreEqual(Mathf.Max(0f, livePlayer.CurrentHP), simulation.PlayerRemainingHp, 0.001f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(liveObject);
        }
    }

    [Test]
    public void GuardianMultiTargetScenarioMeasuresPiercingDamage()
    {
        var plainRuntime = Snapshot(30f, 2f).CreateFreshRuntime();
        var piercingRuntime = Snapshot(30f, 2f).CreateFreshRuntime();
        piercingRuntime.Weapons[0].PiercingLevel = 5;
        var trial = Trial("guardian-aoe", VeteranTrialArchetype.ThousandHandedGuardian);
        trial.damageMultiplier = 0.01f;
        trial.escalationIntervalSeconds = 20f;
        var tier = new VeteranTierConfig
        {
            healthMultiplier = 1f, pressureMultiplier = 1f, escalationMultiplier = 1f
        };

        CombatSimulationResult Run(CombatantRuntime runtime) => new CombatSimulationEngine().Simulate(
            new CombatSimulationRequest
            {
                Snapshot = VeteranBuildSnapshot.CaptureTransient("aoe", runtime),
                ReferenceProfile = new VeteranReferenceThreatProfile
                {
                    maxHp = 600f, physicalDefense = 0f, hitDamage = 1f, attacksPerSecond = 0.01f
                },
                Tier = tier,
                Trial = trial,
                Seed = 1234,
                VirtualTimeLimitSeconds = 2f,
                ProcessedEventLimit = 10000
            });

        var plain = Run(plainRuntime);
        var piercing = Run(piercingRuntime);

        Assert.Less(piercing.EnemyRemainingHp, plain.EnemyRemainingHp);
    }
}
