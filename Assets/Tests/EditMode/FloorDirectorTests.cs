using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class FloorDirectorTests
{
    static FloorDirectorSnapshot Snapshot(int floor, float endHP = 100f, float damage = 0f,
        float averageCombatSeconds = 5f, bool withVariety = true)
    {
        var value = new FloorDirectorSnapshot
        {
            FloorNumber = floor,
            MapSeed = 1000 + floor,
            CharacterId = "jennifer",
            StartHP = 100f,
            EndHPBeforeRecovery = endHP,
            MaxHP = 100f,
            RationsAtEntry = 3,
            RationsBeforeRecovery = 3,
            CombatCount = 3,
            TotalCombatSeconds = averageCombatSeconds * 3f,
            PlayerDamageTaken = damage
        };
        value.VisitedRoomTypes.Add(RoomType.Combat);
        if (withVariety) value.VisitedRoomTypes.Add(RoomType.Trap);
        return value;
    }

    static FloorDirectorResult Result(FloorDirectorSnapshot snapshot,
        FloorDirectorState applied = FloorDirectorState.Normal) => new FloorDirectorResult
    {
        Snapshot = snapshot,
        PlannedState = applied
    };

    [Test]
    public void Evaluate_HighStressWinsOverConsecutiveDominance()
    {
        var history = new List<FloorDirectorResult>
        {
            Result(Snapshot(1)),
            Result(Snapshot(2))
        };
        var current = Snapshot(3, endHP: 30f, damage: 90f);

        var plan = FloorDirectorEvaluator.Evaluate(current, history,
            FloorDirectorState.Normal, targetFloorNumber: 4, seed: 77);

        Assert.AreEqual(FloorDirectorState.Relief, plan.State);
        Assert.AreEqual("health_before_recovery_below_threshold", plan.Reason);
        Assert.IsFalse(plan.ShadowMode);
        Assert.That(plan.ReliefPathIndex, Is.InRange(0, FloorMapGenerator.PathCount - 1));
    }

    [Test]
    public void Evaluate_TwoConsecutiveDominantFloors_ProposesChallenge()
    {
        var history = new List<FloorDirectorResult> { Result(Snapshot(1)) };

        var plan = FloorDirectorEvaluator.Evaluate(Snapshot(2), history,
            FloorDirectorState.Normal, targetFloorNumber: 3, seed: 77);

        Assert.AreEqual(FloorDirectorState.Challenge, plan.State);
    }

    [Test]
    public void Evaluate_ChallengePlannedForCompletedFloor_ForcesCooldown()
    {
        var plan = FloorDirectorEvaluator.Evaluate(Snapshot(3), new List<FloorDirectorResult>(),
            FloorDirectorState.Challenge, targetFloorNumber: 4, seed: 77);

        Assert.AreEqual(FloorDirectorState.Cooldown, plan.State);
    }

    [Test]
    public void Evaluate_TwoFloorsWithoutVisitedEvent_ProposesVariety()
    {
        var previous = Snapshot(1, averageCombatSeconds: 30f, withVariety: false);
        var current = Snapshot(2, averageCombatSeconds: 30f, withVariety: false);

        var plan = FloorDirectorEvaluator.Evaluate(current,
            new List<FloorDirectorResult> { Result(previous) }, FloorDirectorState.Normal, 3, 77);

        Assert.AreEqual(FloorDirectorState.Variety, plan.State);
        Assert.AreEqual(2, plan.VarietyDebtFloors);
        Assert.IsFalse(plan.ShadowMode);
    }

    [Test]
    public void Evaluate_SameSnapshotAndSeed_IsDeterministic()
    {
        var current = Snapshot(1, averageCombatSeconds: 30f);

        var first = FloorDirectorEvaluator.Evaluate(current, new List<FloorDirectorResult>(),
            FloorDirectorState.Normal, 2, 1234);
        var second = FloorDirectorEvaluator.Evaluate(current, new List<FloorDirectorResult>(),
            FloorDirectorState.Normal, 2, 1234);

        Assert.AreEqual(JsonUtility.ToJson(first), JsonUtility.ToJson(second));
    }

    [Test]
    public void Session_RestartedFloor_ReplacesAttemptInsteadOfDuplicatingIt()
    {
        var session = new FloorDirectorSession();
        session.BeginRun();
        session.BeginFloor(1, 10, "jennifer", 100f, 100f, 3);
        session.RecordVisitedRoom(RoomType.Combat, "combat:first-attempt");

        session.BeginFloor(1, 10, "jennifer", 100f, 100f, 3);
        session.RecordVisitedRoom(RoomType.Trap, "trap:second-attempt");
        session.CompleteFloor(90f, 100f, 3);

        Assert.AreEqual(1, session.History.Count);
        CollectionAssert.AreEqual(new[] { RoomType.Trap }, session.History[0].Snapshot.VisitedRoomTypes);
    }

    [Test]
    public void SnapshotBuilder_AggregatesCombatMetrics()
    {
        var builder = new FloorDirectorSnapshotBuilder(2, 22, "violet", 80f, 100f, 4);
        builder.RecordCombat(new CombatTelemetrySnapshot
        {
            DurationSeconds = 12f,
            PlayerMaxHP = 100f,
            PlayerEndHP = 30f,
            PlayerDamageTaken = 45f,
            EnemyCount = 2,
            PlayerSurvived = true
        });

        var snapshot = builder.Complete(30f, 100f, 3, false);

        Assert.AreEqual(1, snapshot.CombatCount);
        Assert.AreEqual(1, snapshot.LowHealthCombatCount);
        Assert.AreEqual(12f, snapshot.TotalCombatSeconds);
        Assert.AreEqual(45f, snapshot.PlayerDamageTaken);
        Assert.AreEqual(3, snapshot.RationsBeforeRecovery);
    }

    [TestCase("jennifer", 1101)]
    [TestCase("violet", 2202)]
    [TestCase("sasha", 3303)]
    public void Session_FixedHeroScenario_ProducesActiveVarietyPlan(string characterId, int seed)
    {
        var session = new FloorDirectorSession();
        session.BeginRun();

        session.BeginFloor(1, seed, characterId, 100f, 100f, 3);
        session.RecordVisitedRoom(RoomType.Combat, "combat:one");
        session.CompleteFloor(100f, 100f, 3);

        session.BeginFloor(2, seed + 1, characterId, 100f, 100f, 3);
        session.RecordVisitedRoom(RoomType.Combat, "combat:two");
        FloorDirectorPlan plan = session.CompleteFloor(100f, 100f, 3);

        Assert.AreEqual(characterId, session.History[1].Snapshot.CharacterId);
        Assert.AreEqual(FloorDirectorState.Variety, plan.State);
        Assert.IsFalse(plan.ShadowMode);
    }

    [Test]
    public void VarietyPolicy_GuaranteesOptionalInterestingRoomWithoutBreakingMapLimits()
    {
        for (int seed = 1; seed <= 100; seed++)
        {
            FloorMap map = FloorMapGenerator.Generate(3, seed);
            var plan = new FloorDirectorPlan
            {
                TargetFloorNumber = 3,
                Seed = seed * 17,
                State = FloorDirectorState.Variety,
                ShadowMode = false
            };

            FloorDirectorMapAdjustment adjustment = FloorDirectorMapPolicy.Apply(map, plan);
            var firstFork = map.Nodes.Where(node => node.Depth == 1).ToList();
            FloorMapNode start = map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Start);

            Assert.IsFalse(start.RoomType == RoomType.Trap || start.RoomType == RoomType.Special,
                $"Seed {seed} forces an interesting start room during Variety.");
            Assert.IsTrue(firstFork.Any(node => node.RoomType == RoomType.Trap || node.RoomType == RoomType.Special),
                $"Seed {seed} has no interesting option on the first fork.");
            Assert.IsTrue(firstFork.Any(node => node.RoomType != RoomType.Trap && node.RoomType != RoomType.Special),
                $"Seed {seed} forces the player into an interesting room.");
            Assert.IsFalse(string.IsNullOrEmpty(adjustment.OfferedNodeId));
            Assert.IsEmpty(FloorMapGenerator.Validate(map), $"Seed {seed} produced an invalid map.");
        }
    }

    [Test]
    public void VarietyPolicy_MovesInterestingStartToOptionalForkWithoutChangingCounts()
    {
        FloorMap map = null;
        for (int seed = 1; seed <= 10000; seed++)
        {
            FloorMap candidate = FloorMapGenerator.Generate(3, seed);
            FloorMapNode candidateStart = candidate.Nodes.Single(node => node.Kind == FloorMapNodeKind.Start);
            if (candidateStart.RoomType == RoomType.Trap || candidateStart.RoomType == RoomType.Special)
            {
                map = candidate;
                break;
            }
        }
        Assert.NotNull(map, "The fixed seed range should contain an interesting start room.");
        var countsBefore = map.Nodes.GroupBy(node => node.RoomType)
            .ToDictionary(group => group.Key, group => group.Count());
        var plan = new FloorDirectorPlan
        {
            TargetFloorNumber = 3,
            Seed = 445566,
            State = FloorDirectorState.Variety,
            ShadowMode = false
        };

        FloorDirectorMapAdjustment adjustment = FloorDirectorMapPolicy.Apply(map, plan);
        FloorMapNode start = map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Start);
        var firstFork = map.Nodes.Where(node => node.Depth == 1).ToList();
        var countsAfter = map.Nodes.GroupBy(node => node.RoomType)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.IsTrue(adjustment.StartRoomAdjusted);
        Assert.IsFalse(start.RoomType == RoomType.Trap || start.RoomType == RoomType.Special);
        Assert.IsTrue(firstFork.Any(node => node.RoomType == RoomType.Trap || node.RoomType == RoomType.Special));
        Assert.IsTrue(firstFork.Any(node => node.RoomType != RoomType.Trap && node.RoomType != RoomType.Special));
        foreach (var pair in countsBefore)
            Assert.AreEqual(pair.Value, countsAfter[pair.Key], $"Room count changed for {pair.Key}.");
        Assert.IsEmpty(FloorMapGenerator.Validate(map));
    }

    [Test]
    public void VarietyPolicy_SameMapAndPlanSeed_IsDeterministic()
    {
        var plan = new FloorDirectorPlan
        {
            TargetFloorNumber = 4,
            Seed = 99881,
            State = FloorDirectorState.Variety,
            ShadowMode = false
        };
        FloorMap first = FloorMapGenerator.Generate(4, 7788);
        FloorMap second = FloorMapGenerator.Generate(4, 7788);

        FloorDirectorMapPolicy.Apply(first, plan);
        FloorDirectorMapPolicy.Apply(second, plan);

        Assert.AreEqual(FloorMapGenerator.Dump(first), FloorMapGenerator.Dump(second));
    }

    [Test]
    public void VarietyPolicy_ShadowState_DoesNotChangeMap()
    {
        FloorMap map = FloorMapGenerator.Generate(3, 7788);
        string before = FloorMapGenerator.Dump(map);
        var plan = new FloorDirectorPlan
        {
            TargetFloorNumber = 3,
            Seed = 99881,
            State = FloorDirectorState.Relief,
            ShadowMode = true
        };

        FloorDirectorMapAdjustment adjustment = FloorDirectorMapPolicy.Apply(map, plan);

        Assert.IsFalse(adjustment.Applied);
        Assert.AreEqual(before, FloorMapGenerator.Dump(map));
    }

    [Test]
    public void ReliefPolicy_CreatesOneVisibleTrapFreeRouteAndSuppressesOnlyItsCombatModifiers()
    {
        for (int seed = 1; seed <= 100; seed++)
        {
            FloorMap map = FloorMapGenerator.Generate(4, seed);
            var countsBefore = map.Nodes.GroupBy(node => node.RoomType)
                .ToDictionary(group => group.Key, group => group.Count());
            var plan = new FloorDirectorPlan
            {
                TargetFloorNumber = 4,
                Seed = seed * 31,
                State = FloorDirectorState.Relief,
                ReliefPathIndex = FloorDirectorMapPolicy.SelectReliefPath(seed * 31),
                ShadowMode = false
            };

            FloorDirectorMapAdjustment adjustment = FloorDirectorMapPolicy.Apply(map, plan);
            var reliefNodes = map.Nodes.Where(node => node.IsReliefRoute).OrderBy(node => node.Depth).ToList();

            Assert.IsTrue(adjustment.Applied);
            Assert.AreEqual(plan.ReliefPathIndex, adjustment.ReliefPathIndex);
            Assert.AreEqual(FloorMapGenerator.BranchDepthCount, reliefNodes.Count);
            Assert.IsTrue(reliefNodes.All(node => node.PathIndex == plan.ReliefPathIndex));
            Assert.IsFalse(reliefNodes.Any(node => node.RoomType == RoomType.Trap));
            Assert.IsTrue(reliefNodes.Where(node => node.RoomType == RoomType.Combat)
                .All(node => node.SuppressMonsterModifiers));
            Assert.IsFalse(map.Nodes.Where(node => !node.IsReliefRoute)
                .Any(node => node.SuppressMonsterModifiers));
            Assert.IsFalse(map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Boss).SuppressMonsterModifiers);
            Assert.AreNotEqual(RoomType.Trap,
                map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Start).RoomType);
            foreach (var pair in countsBefore)
                Assert.AreEqual(pair.Value, map.Nodes.Count(node => node.RoomType == pair.Key),
                    $"Seed {seed}: room count changed for {pair.Key}.");
            Assert.IsEmpty(FloorMapGenerator.Validate(map), $"Seed {seed} produced an invalid map.");
        }
    }

    [Test]
    public void ReliefPolicy_IsDeterministicAndBossNeverUsesReliefSuppression()
    {
        var planA = new FloorDirectorPlan
        {
            TargetFloorNumber = 5,
            Seed = 887766,
            State = FloorDirectorState.Relief,
            ShadowMode = false
        };
        var planB = JsonUtility.FromJson<FloorDirectorPlan>(JsonUtility.ToJson(planA));
        FloorMap first = FloorMapGenerator.Generate(5, 12345);
        FloorMap second = FloorMapGenerator.Generate(5, 12345);

        FloorDirectorMapPolicy.Apply(first, planA);
        FloorDirectorMapPolicy.Apply(second, planB);

        CollectionAssert.AreEqual(
            first.Nodes.Select(node => $"{node.Id}:{node.RoomType}:{node.IsReliefRoute}:{node.SuppressMonsterModifiers}").ToList(),
            second.Nodes.Select(node => $"{node.Id}:{node.RoomType}:{node.IsReliefRoute}:{node.SuppressMonsterModifiers}").ToList());
        FloorMapNode combat = first.Nodes.First(node => node.SuppressMonsterModifiers);
        Assert.IsTrue(FloorDirectorEncounterPolicy.ShouldSuppressMonsterModifiers(combat, isBoss: false));
        Assert.IsFalse(FloorDirectorEncounterPolicy.ShouldSuppressMonsterModifiers(combat, isBoss: true));
    }

    [Test]
    public void VarietyDebt_ResetsOnlyAfterPlayerActuallyVisitsInterestingRoom()
    {
        var session = new FloorDirectorSession();
        session.BeginRun();

        session.BeginFloor(1, 101, "jennifer", 100f, 100f, 3);
        session.RecordVisitedRoom(RoomType.Combat, "combat:one");
        session.CompleteFloor(100f, 100f, 3);
        session.BeginFloor(2, 102, "jennifer", 100f, 100f, 3);
        session.RecordVisitedRoom(RoomType.Combat, "combat:two");
        FloorDirectorPlan offeredPlan = session.CompleteFloor(100f, 100f, 3);
        Assert.AreEqual(FloorDirectorState.Variety, offeredPlan.State);

        FloorMap offeredMap = FloorMapGenerator.Generate(3, 103);
        FloorDirectorMapAdjustment offer = FloorDirectorMapPolicy.Apply(offeredMap, offeredPlan);
        Assert.IsFalse(string.IsNullOrEmpty(offer.OfferedNodeId));

        session.BeginFloor(3, 103, "jennifer", 100f, 100f, 3);
        session.RecordVisitedRoom(RoomType.Combat, "combat:declined-offer");
        FloorDirectorPlan stillOwed = session.CompleteFloor(100f, 100f, 3);
        Assert.AreEqual(FloorDirectorState.Variety, stillOwed.State);
        Assert.AreEqual(3, stillOwed.VarietyDebtFloors);

        session.BeginFloor(4, 104, "jennifer", 100f, 100f, 3);
        session.RecordVisitedRoom(RoomType.Special, "special:accepted-offer");
        FloorDirectorPlan debtCleared = session.CompleteFloor(100f, 100f, 3);
        Assert.AreEqual(FloorDirectorState.Normal, debtCleared.State);
        Assert.AreEqual(0, debtCleared.VarietyDebtFloors);
    }

    [Test]
    public void Evaluate_ThreeRepeatedRoomTypes_ActivatesSoftRoomGuard()
    {
        var current = Snapshot(2, endHP: 30f, averageCombatSeconds: 30f);
        current.VisitedRoomTypes.Clear();
        current.VisitedRoomTypes.AddRange(new[] { RoomType.Combat, RoomType.Combat, RoomType.Combat, RoomType.Boss });
        current.VisitedContentKeys.Clear();
        current.VisitedContentKeys.AddRange(new[] { "combat:A", "combat:B", "combat:C", "boss:Boss" });

        FloorDirectorPlan plan = FloorDirectorEvaluator.Evaluate(current,
            new List<FloorDirectorResult>(), FloorDirectorState.Normal, 3, 991);

        Assert.AreEqual(FloorDirectorState.Relief, plan.State, "The safety-state priority must remain unchanged.");
        Assert.IsFalse(plan.ShadowMode, "Relief and the repeat guard must both be active.");
        Assert.AreEqual(3, plan.RoomRepeatRunLength);
        CollectionAssert.AreEqual(new[] { RoomType.Combat }, plan.DiscouragedRoomTypes);
    }

    [Test]
    public void Evaluate_TwoEquivalentEncounterCompositions_ActivatesEncounterGuard()
    {
        var current = Snapshot(2, averageCombatSeconds: 30f);
        current.VisitedContentKeys.Clear();
        current.VisitedContentKeys.Add("combat:Harpy|Slime");
        current.VisitedContentKeys.Add("combat:Slime|Harpy");

        FloorDirectorPlan plan = FloorDirectorEvaluator.Evaluate(current,
            new List<FloorDirectorResult>(), FloorDirectorState.Normal, 3, 992);

        Assert.AreEqual(2, plan.EncounterRepeatRunLength);
        CollectionAssert.AreEqual(new[] { "combat:Harpy|Slime" }, plan.DiscouragedEncounterSignatures);
        Assert.IsTrue(FloorDirectorEncounterPolicy.IsDiscouraged(plan, new[] { "Slime", "Harpy" }));
        Assert.IsFalse(FloorDirectorEncounterPolicy.IsDiscouraged(plan, new[] { "Goblin" }));
    }

    [Test]
    public void RoomRepeatGuard_GivesFirstForkAlternativeAndPreservesRoomCounts()
    {
        FloorMap map = null;
        for (int seed = 1; seed <= 10000; seed++)
        {
            FloorMap candidate = FloorMapGenerator.Generate(3, seed);
            if (candidate.Nodes.Where(node => node.Depth == 1).All(node => node.RoomType == RoomType.Combat))
            {
                map = candidate;
                break;
            }
        }
        Assert.NotNull(map, "The fixed seed range should contain an all-Combat first fork.");
        var countsBefore = map.Nodes.GroupBy(node => node.RoomType)
            .ToDictionary(group => group.Key, group => group.Count());
        var plan = new FloorDirectorPlan
        {
            TargetFloorNumber = 3,
            Seed = 77123,
            State = FloorDirectorState.Normal,
            ShadowMode = false,
            DiscouragedRoomTypes = new List<RoomType> { RoomType.Combat }
        };

        FloorDirectorMapAdjustment adjustment = FloorDirectorMapPolicy.Apply(map, plan);
        var firstFork = map.Nodes.Where(node => node.Depth == 1).ToList();

        Assert.IsTrue(adjustment.RoomSequenceAdjusted);
        Assert.IsTrue(firstFork.Any(node => node.RoomType != RoomType.Combat));
        var countsAfter = map.Nodes.GroupBy(node => node.RoomType)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var pair in countsBefore)
            Assert.AreEqual(pair.Value, countsAfter[pair.Key], $"Room count changed for {pair.Key}.");
        Assert.IsEmpty(FloorMapGenerator.Validate(map));
    }

    [Test]
    public void EncounterPolicy_NormalizesMonsterOrder()
    {
        Assert.AreEqual("combat:Harpy|Slime|Slime",
            FloorDirectorEncounterPolicy.BuildSignature(new[] { "Slime", "Harpy", "Slime" }));
        Assert.AreEqual("combat:Harpy|Slime",
            FloorDirectorEncounterPolicy.NormalizeSignature("combat:Slime|Harpy"));
    }
}
