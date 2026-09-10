using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum FloorDirectorState
{
    Normal,
    Variety,
    Relief,
    Challenge,
    Cooldown
}

[Serializable]
public sealed class CombatTelemetrySnapshot
{
    public float DurationSeconds;
    public float PlayerStartHP;
    public float PlayerEndHP;
    public float PlayerMaxHP;
    public float PlayerDamageTaken;
    public int EnemyCount;
    public bool WasBoss;
    public bool PlayerSurvived;
}

[Serializable]
public sealed class FloorDirectorSnapshot
{
    public int FloorNumber;
    public int MapSeed;
    public string CharacterId;
    public float StartHP;
    public float EndHPBeforeRecovery;
    public float MaxHP;
    public int RationsAtEntry;
    public int RationsBeforeRecovery;
    public int CombatCount;
    public int LowHealthCombatCount;
    public float TotalCombatSeconds;
    public float PlayerDamageTaken;
    public bool HasStrongNegativeEffect;
    public List<RoomType> VisitedRoomTypes = new List<RoomType>();
    public List<string> VisitedContentKeys = new List<string>();
}

[Serializable]
public sealed class FloorDirectorPlan
{
    public const string CurrentConfigVersion = "relief-v1";

    public int TargetFloorNumber;
    public int Seed;
    public string ConfigVersion = CurrentConfigVersion;
    public FloorDirectorState State;
    public float StressScore;
    public float DominanceScore;
    public int VarietyDebtFloors;
    public int RoomRepeatRunLength;
    public int EncounterRepeatRunLength;
    public List<RoomType> DiscouragedRoomTypes = new List<RoomType>();
    public List<string> DiscouragedEncounterSignatures = new List<string>();
    public int ReliefPathIndex = -1;
    public bool ShadowMode = true;
    public string Reason;

    public string ToDiagnosticString() =>
        $"[FloorDirector][{(ShadowMode ? "Shadow" : "Active")}] targetFloor={TargetFloorNumber} state={State} " +
        $"stress={StressScore:F2} dominance={DominanceScore:F2} varietyDebt={VarietyDebtFloors} " +
        $"roomRepeat={RoomRepeatRunLength} encounterRepeat={EncounterRepeatRunLength} " +
        $"reliefPath={ReliefPathIndex} " +
        $"seed={Seed} config={ConfigVersion} reason=\"{Reason}\"";
}

public sealed class FloorDirectorResult
{
    public FloorDirectorSnapshot Snapshot;
    public FloorDirectorState PlannedState;
    public FloorDirectorPlan NextPlan;
}

public sealed class FloorDirectorSnapshotBuilder
{
    readonly FloorDirectorSnapshot snapshot;

    public FloorDirectorSnapshotBuilder(int floorNumber, int mapSeed, string characterId,
        float startHP, float maxHP, int rationsAtEntry)
    {
        snapshot = new FloorDirectorSnapshot
        {
            FloorNumber = floorNumber,
            MapSeed = mapSeed,
            CharacterId = characterId ?? string.Empty,
            StartHP = Mathf.Max(0f, startHP),
            MaxHP = Mathf.Max(1f, maxHP),
            RationsAtEntry = Mathf.Max(0, rationsAtEntry)
        };
    }

    public void RecordCombat(CombatTelemetrySnapshot combat)
    {
        if (combat == null) return;

        snapshot.CombatCount++;
        snapshot.TotalCombatSeconds += Mathf.Max(0f, combat.DurationSeconds);
        snapshot.PlayerDamageTaken += Mathf.Max(0f, combat.PlayerDamageTaken);
        float maxHP = Mathf.Max(1f, combat.PlayerMaxHP);
        if (combat.PlayerEndHP / maxHP <= FloorDirectorEvaluator.LowHealthRatio)
            snapshot.LowHealthCombatCount++;
    }

    public void RecordVisitedRoom(RoomType roomType, string contentKey)
    {
        snapshot.VisitedRoomTypes.Add(roomType);
        snapshot.VisitedContentKeys.Add(contentKey ?? string.Empty);
    }

    public FloorDirectorSnapshot Complete(float endHPBeforeRecovery, float maxHP,
        int rationsBeforeRecovery, bool hasStrongNegativeEffect)
    {
        snapshot.EndHPBeforeRecovery = Mathf.Max(0f, endHPBeforeRecovery);
        snapshot.MaxHP = Mathf.Max(1f, maxHP);
        snapshot.RationsBeforeRecovery = Mathf.Max(0, rationsBeforeRecovery);
        snapshot.HasStrongNegativeEffect = hasStrongNegativeEffect;
        return snapshot;
    }
}

public static class FloorDirectorEvaluator
{
    public const float LowHealthRatio = 0.35f;
    public const float HighDamageRatio = 0.80f;
    public const float DominantEndHealthRatio = 0.75f;
    public const float DominantDamageRatio = 0.20f;
    public const float DominantDurationRatio = 0.65f;
    public const int MinimumCombatsForDominance = 3;
    public const int FloorsForChallenge = 2;
    public const int FloorsForVariety = 2;

    public static FloorDirectorPlan Evaluate(FloorDirectorSnapshot current,
        IReadOnlyList<FloorDirectorResult> history, FloorDirectorState currentFloorPlannedState,
        int targetFloorNumber, int seed)
    {
        if (current == null) throw new ArgumentNullException(nameof(current));

        float stress = CalculateStress(current);
        float dominance = CalculateDominance(current);
        int dominantFloors = CountConsecutive(history, current, IsDominant);
        int varietyDebt = CountConsecutive(history, current, HasNoMeaningfulVariety);
        bool highStress = IsHighStress(current);

        var plan = new FloorDirectorPlan
        {
            TargetFloorNumber = targetFloorNumber,
            Seed = seed,
            StressScore = stress,
            DominanceScore = dominance,
            VarietyDebtFloors = varietyDebt
        };
        PopulateRepeatGuards(plan, history, current);

        if (highStress)
        {
            plan.State = FloorDirectorState.Relief;
            plan.ReliefPathIndex = FloorDirectorMapPolicy.SelectReliefPath(seed);
            plan.Reason = current.EndHPBeforeRecovery / Mathf.Max(1f, current.MaxHP) <= LowHealthRatio
                ? "health_before_recovery_below_threshold"
                : "floor_damage_above_threshold";
        }
        else if (currentFloorPlannedState == FloorDirectorState.Challenge)
        {
            plan.State = FloorDirectorState.Cooldown;
            plan.Reason = "challenge_was_planned_for_completed_floor";
        }
        else if (!current.HasStrongNegativeEffect && dominantFloors >= FloorsForChallenge)
        {
            plan.State = FloorDirectorState.Challenge;
            plan.Reason = "two_consecutive_dominant_floors";
        }
        else if (varietyDebt >= FloorsForVariety)
        {
            plan.State = FloorDirectorState.Variety;
            plan.Reason = "two_floors_without_visited_trap_or_special";
        }
        else
        {
            plan.State = FloorDirectorState.Normal;
            plan.Reason = "no_stable_signal";
        }

        // Активны Variety, Relief и мягкая защита от повторов.
        // Challenge и Cooldown продолжают быть теневыми и не применяют свои эффекты.
        bool hasRepeatGuard = plan.DiscouragedRoomTypes.Count > 0 ||
            plan.DiscouragedEncounterSignatures.Count > 0;
        plan.ShadowMode = plan.State != FloorDirectorState.Variety &&
            plan.State != FloorDirectorState.Relief && !hasRepeatGuard;
        if (plan.State == FloorDirectorState.Normal && hasRepeatGuard)
            plan.Reason = "room_or_encounter_repeat_guard";

        return plan;
    }

    public static bool IsHighStress(FloorDirectorSnapshot value)
    {
        if (value == null) return false;
        float maxHP = Mathf.Max(1f, value.MaxHP);
        return value.EndHPBeforeRecovery / maxHP <= LowHealthRatio ||
            value.PlayerDamageTaken / maxHP >= HighDamageRatio;
    }

    public static bool IsDominant(FloorDirectorSnapshot value)
    {
        if (value == null || value.CombatCount < MinimumCombatsForDominance || value.HasStrongNegativeEffect)
            return false;

        float maxHP = Mathf.Max(1f, value.MaxHP);
        float averageDuration = value.TotalCombatSeconds / Mathf.Max(1, value.CombatCount);
        return value.EndHPBeforeRecovery / maxHP >= DominantEndHealthRatio &&
            value.PlayerDamageTaken / maxHP <= DominantDamageRatio &&
            value.RationsBeforeRecovery >= value.RationsAtEntry &&
            averageDuration <= ExpectedCombatSeconds(value.FloorNumber) * DominantDurationRatio;
    }

    public static float CalculateStress(FloorDirectorSnapshot value)
    {
        float maxHP = Mathf.Max(1f, value.MaxHP);
        float healthPressure = 1f - Mathf.Clamp01(value.EndHPBeforeRecovery / maxHP);
        float damagePressure = Mathf.Clamp01(value.PlayerDamageTaken / (maxHP * HighDamageRatio));
        float lowHealthPressure = value.CombatCount > 0
            ? Mathf.Clamp01((float)value.LowHealthCombatCount / value.CombatCount)
            : 0f;
        float rationPressure = value.RationsBeforeRecovery < value.RationsAtEntry ? 0.35f : 0f;
        return Mathf.Clamp01(Mathf.Max(healthPressure, damagePressure, lowHealthPressure) + rationPressure);
    }

    public static float CalculateDominance(FloorDirectorSnapshot value)
    {
        if (value == null || value.CombatCount == 0) return 0f;
        float maxHP = Mathf.Max(1f, value.MaxHP);
        float hp = Mathf.Clamp01(value.EndHPBeforeRecovery / maxHP);
        float lowDamage = 1f - Mathf.Clamp01(value.PlayerDamageTaken / maxHP);
        float averageDuration = value.TotalCombatSeconds / value.CombatCount;
        float speed = 1f - Mathf.Clamp01(averageDuration / ExpectedCombatSeconds(value.FloorNumber));
        float resource = value.RationsBeforeRecovery >= value.RationsAtEntry ? 1f : 0f;
        return Mathf.Clamp01((hp + lowDamage + speed + resource) / 4f);
    }

    public static float ExpectedCombatSeconds(int floorNumber) => 18f + Mathf.Max(1, floorNumber) * 2f;

    static bool HasNoMeaningfulVariety(FloorDirectorSnapshot value) =>
        value != null && !value.VisitedRoomTypes.Any(type => type == RoomType.Trap || type == RoomType.Special);

    static void PopulateRepeatGuards(FloorDirectorPlan plan,
        IReadOnlyList<FloorDirectorResult> history, FloorDirectorSnapshot current)
    {
        var snapshots = new List<FloorDirectorSnapshot>();
        if (history != null)
        {
            foreach (var result in history)
                if (result?.Snapshot != null) snapshots.Add(result.Snapshot);
        }
        snapshots.Add(current);

        var roomSequence = snapshots
            .SelectMany(snapshot => snapshot.VisitedRoomTypes ?? new List<RoomType>())
            .Where(type => type != RoomType.Boss)
            .ToList();
        plan.RoomRepeatRunLength = TrailingRunLength(roomSequence);
        if (plan.RoomRepeatRunLength >= 3 && roomSequence.Count > 0)
            plan.DiscouragedRoomTypes.Add(roomSequence[roomSequence.Count - 1]);

        var encounterSequence = snapshots
            .SelectMany(snapshot => snapshot.VisitedContentKeys ?? new List<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key) && key.StartsWith("combat:", StringComparison.Ordinal))
            .Select(FloorDirectorEncounterPolicy.NormalizeSignature)
            .ToList();
        plan.EncounterRepeatRunLength = TrailingRunLength(encounterSequence);
        if (plan.EncounterRepeatRunLength >= 2 && encounterSequence.Count > 0)
            plan.DiscouragedEncounterSignatures.Add(encounterSequence[encounterSequence.Count - 1]);
    }

    static int TrailingRunLength<T>(IReadOnlyList<T> values)
    {
        if (values == null || values.Count == 0) return 0;
        int count = 1;
        var comparer = EqualityComparer<T>.Default;
        for (int i = values.Count - 2; i >= 0; i--)
        {
            if (!comparer.Equals(values[i], values[values.Count - 1])) break;
            count++;
        }
        return count;
    }

    static int CountConsecutive(IReadOnlyList<FloorDirectorResult> history,
        FloorDirectorSnapshot current, Func<FloorDirectorSnapshot, bool> predicate)
    {
        int count = predicate(current) ? 1 : 0;
        if (count == 0 || history == null) return count;

        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (!predicate(history[i].Snapshot)) break;
            count++;
        }
        return count;
    }
}

public sealed class FloorDirectorMapAdjustment
{
    public bool Applied;
    public bool StartRoomAdjusted;
    public bool RoomSequenceAdjusted;
    public string OfferedNodeId;
    public string ReplacedNodeId;
    public RoomType AvoidedRoomType;
    public int ReliefPathIndex = -1;
    public int ReliefTrapsMoved;
    public string Reason;
}

public static class FloorDirectorMapPolicy
{
    public static FloorDirectorMapAdjustment Apply(FloorMap map, FloorDirectorPlan plan)
    {
        var result = new FloorDirectorMapAdjustment();
        if (map == null)
        {
            result.Reason = "map_is_null";
            return result;
        }
        if (plan == null || plan.TargetFloorNumber != map.FloorNumber)
        {
            result.Reason = "no_matching_plan";
            return result;
        }
        if (plan.ShadowMode)
        {
            result.Reason = "state_is_shadow_only";
            return result;
        }

        var firstFork = map.Nodes
            .Where(node => node != null && node.Kind == FloorMapNodeKind.Normal && node.Depth == 1)
            .OrderBy(node => node.PathIndex)
            .ToList();
        if (plan.State == FloorDirectorState.Relief)
            ApplyReliefRoute(map, plan, result);
        if (plan.State == FloorDirectorState.Variety)
            RemoveForcedInterestingStart(map, plan, firstFork, result);
        ApplyRoomRepeatGuard(map, plan, firstFork, result);
        if (plan.State == FloorDirectorState.Relief)
            RefreshReliefRouteFlags(map, plan.ReliefPathIndex);

        if (plan.State != FloorDirectorState.Variety)
        {
            if (string.IsNullOrEmpty(result.Reason))
                result.Reason = plan.DiscouragedEncounterSignatures != null &&
                    plan.DiscouragedEncounterSignatures.Count > 0
                    ? "encounter_repeat_guard_deferred_to_content_resolution"
                    : "no_map_adjustment_needed";
            ValidateIfChanged(map, result);
            return result;
        }

        var existingOffer = firstFork.FirstOrDefault(IsInteresting);
        if (existingOffer != null)
        {
            result.OfferedNodeId = existingOffer.Id;
            AddReason(result, "interesting_room_already_on_first_fork");
            ValidateIfChanged(map, result);
            return result;
        }

        var candidates = firstFork.Where(node => node.RoomType == RoomType.Combat).ToList();
        if (candidates.Count == 0)
        {
            int merchantCount = map.Nodes.Count(node => node != null && node.RoomType == RoomType.Merchant);
            if (merchantCount > FloorMapGenerator.MinShopsPerFloor)
                candidates.AddRange(firstFork.Where(node => node.RoomType == RoomType.Merchant));
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException("Variety director could not create an optional first-fork room without breaking map limits.");

        var random = new System.Random(plan.Seed);
        FloorMapNode offered = candidates[random.Next(candidates.Count)];
        FloorMapNode existingSpecial = map.Nodes.FirstOrDefault(node =>
            node != null && node.RoomType == RoomType.Special && node != offered);
        if (existingSpecial != null)
        {
            FloorMapGenerator.ResetNodeContent(existingSpecial, RoomType.Combat);
            result.ReplacedNodeId = existingSpecial.Id;
        }

        FloorMapGenerator.ResetNodeContent(offered, RoomType.Special);
        result.Applied = true;
        result.OfferedNodeId = offered.Id;
        AddReason(result, existingSpecial != null
            ? "special_room_moved_to_optional_first_fork"
            : "special_room_created_on_optional_first_fork");

        ValidateIfChanged(map, result);
        return result;
    }

    public static int SelectReliefPath(int seed) => (int)((uint)seed % FloorMapGenerator.PathCount);

    static void ApplyReliefRoute(FloorMap map, FloorDirectorPlan plan, FloorDirectorMapAdjustment result)
    {
        int reliefPath = plan.ReliefPathIndex >= 0 && plan.ReliefPathIndex < FloorMapGenerator.PathCount
            ? plan.ReliefPathIndex
            : SelectReliefPath(plan.Seed);
        plan.ReliefPathIndex = reliefPath;
        result.ReliefPathIndex = reliefPath;

        var random = new System.Random(unchecked(plan.Seed ^ 0x41c64e6d));
        var trapsToMove = map.Nodes
            .Where(node => node != null && node.RoomType == RoomType.Trap &&
                (node.Kind == FloorMapNodeKind.Start ||
                 (node.Kind == FloorMapNodeKind.Normal && node.PathIndex == reliefPath)))
            .OrderBy(node => node.Depth)
            .ToList();
        foreach (FloorMapNode trap in trapsToMove)
        {
            var donors = map.Nodes
                .Where(node => node != null && node.Kind == FloorMapNodeKind.Normal &&
                    node.PathIndex != reliefPath && node.RoomType == RoomType.Combat)
                .OrderBy(node => node.PathIndex)
                .ThenBy(node => node.Depth)
                .ToList();
            if (donors.Count == 0)
                throw new InvalidOperationException("Relief director could not move a trap away from the safe route.");

            FloorMapNode donor = donors[random.Next(donors.Count)];
            FloorMapGenerator.ResetNodeContent(trap, RoomType.Combat);
            FloorMapGenerator.ResetNodeContent(donor, RoomType.Trap);
            result.Applied = true;
            result.ReliefTrapsMoved++;
        }

        RefreshReliefRouteFlags(map, reliefPath);
        result.Applied = true;
        result.OfferedNodeId = FloorMapGenerator.NodeId(reliefPath, 1);
        AddReason(result, result.ReliefTrapsMoved > 0
            ? "relief_route_marked_and_traps_moved"
            : "relief_route_marked");
    }

    static void RefreshReliefRouteFlags(FloorMap map, int reliefPath)
    {
        foreach (FloorMapNode node in map.Nodes.Where(node => node != null))
        {
            node.IsReliefRoute = node.Kind == FloorMapNodeKind.Normal && node.PathIndex == reliefPath;
            node.SuppressMonsterModifiers = node.IsReliefRoute && node.RoomType == RoomType.Combat;
        }
    }

    static void RemoveForcedInterestingStart(FloorMap map, FloorDirectorPlan plan,
        List<FloorMapNode> firstFork, FloorDirectorMapAdjustment result)
    {
        FloorMapNode start = map.Nodes.FirstOrDefault(node => node != null && node.Kind == FloorMapNodeKind.Start);
        if (start == null || !IsInteresting(start)) return;

        var candidates = firstFork.Where(node => node.RoomType == RoomType.Combat).ToList();
        if (candidates.Count == 0)
        {
            int branchMerchantCount = map.Nodes.Count(node => node != null &&
                node.Kind == FloorMapNodeKind.Normal && node.RoomType == RoomType.Merchant);
            if (branchMerchantCount > FloorMapGenerator.MinShopsPerFloor)
                candidates.AddRange(firstFork.Where(node => node.RoomType == RoomType.Merchant));
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException("Variety director could not move the forced interesting start to a legal optional branch.");

        var random = new System.Random(unchecked(plan.Seed ^ 0x63d83595));
        FloorMapNode optionalTarget = candidates[random.Next(candidates.Count)];
        RoomType startType = start.RoomType;
        RoomType ordinaryType = optionalTarget.RoomType;
        FloorMapGenerator.ResetNodeContent(start, ordinaryType);
        FloorMapGenerator.ResetNodeContent(optionalTarget, startType);
        result.Applied = true;
        result.StartRoomAdjusted = true;
        result.OfferedNodeId = optionalTarget.Id;
        result.ReplacedNodeId = start.Id;
        AddReason(result, "forced_interesting_start_moved_to_optional_first_fork");
    }

    static void ApplyRoomRepeatGuard(FloorMap map, FloorDirectorPlan plan,
        List<FloorMapNode> firstFork, FloorDirectorMapAdjustment result)
    {
        if (plan.DiscouragedRoomTypes == null || plan.DiscouragedRoomTypes.Count == 0 ||
            firstFork.Any(node => !plan.DiscouragedRoomTypes.Contains(node.RoomType))) return;

        var random = new System.Random(unchecked(plan.Seed ^ 0x2c9277b5));
        FloorMapNode target = firstFork[random.Next(firstFork.Count)];
        var donors = map.Nodes
            .Where(node => node != null && node.Kind == FloorMapNodeKind.Normal &&
                node.PathIndex == target.PathIndex && node.Depth > target.Depth &&
                !plan.DiscouragedRoomTypes.Contains(node.RoomType))
            .OrderBy(node => node.Depth)
            .ToList();
        if (donors.Count == 0)
        {
            AddReason(result, "room_repeat_guard_no_legal_swap");
            return;
        }

        FloorMapNode donor = donors[random.Next(donors.Count)];
        RoomType avoided = target.RoomType;
        RoomType replacement = donor.RoomType;
        FloorMapGenerator.ResetNodeContent(target, replacement);
        FloorMapGenerator.ResetNodeContent(donor, avoided);
        result.Applied = true;
        result.RoomSequenceAdjusted = true;
        result.AvoidedRoomType = avoided;
        AddReason(result, "first_fork_alternative_to_repeated_room_type");
    }

    static void ValidateIfChanged(FloorMap map, FloorDirectorMapAdjustment result)
    {
        if (!result.Applied) return;
        var errors = FloorMapGenerator.Validate(map);
        if (errors.Count > 0)
            throw new InvalidOperationException($"Floor director produced an invalid map: {string.Join("; ", errors)}");
    }

    static void AddReason(FloorDirectorMapAdjustment result, string reason)
    {
        result.Reason = string.IsNullOrEmpty(result.Reason) ? reason : $"{result.Reason};{reason}";
    }

    static bool IsInteresting(FloorMapNode node) =>
        node.RoomType == RoomType.Trap || node.RoomType == RoomType.Special;
}

public static class FloorDirectorEncounterPolicy
{
    public const int MaxResolveAttempts = 4;

    public static bool IsDiscouraged(FloorDirectorPlan plan, IReadOnlyList<string> monsterIds)
    {
        if (plan == null || plan.ShadowMode || plan.DiscouragedEncounterSignatures == null)
            return false;
        return plan.DiscouragedEncounterSignatures.Contains(BuildSignature(monsterIds));
    }

    public static bool ShouldSuppressMonsterModifiers(FloorMapNode node, bool isBoss) =>
        !isBoss && node != null && node.Kind == FloorMapNodeKind.Normal &&
        node.RoomType == RoomType.Combat && node.SuppressMonsterModifiers;

    public static string BuildSignature(IReadOnlyList<string> monsterIds)
    {
        if (monsterIds == null || monsterIds.Count == 0) return "combat:";
        return $"combat:{string.Join("|", monsterIds.Where(id => !string.IsNullOrWhiteSpace(id)).OrderBy(id => id, StringComparer.Ordinal))}";
    }

    public static string NormalizeSignature(string contentKey)
    {
        if (string.IsNullOrWhiteSpace(contentKey) || !contentKey.StartsWith("combat:", StringComparison.Ordinal))
            return contentKey ?? string.Empty;
        string[] monsterIds = contentKey.Substring("combat:".Length)
            .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        return BuildSignature(monsterIds);
    }
}

public sealed class FloorDirectorSession
{
    readonly List<FloorDirectorResult> history = new List<FloorDirectorResult>();
    FloorDirectorSnapshotBuilder currentBuilder;
    FloorDirectorState currentPlannedState;

    public IReadOnlyList<FloorDirectorResult> History => history;
    public FloorDirectorPlan PendingPlan { get; private set; }

    public void BeginRun()
    {
        history.Clear();
        currentBuilder = null;
        currentPlannedState = FloorDirectorState.Normal;
        PendingPlan = null;
    }

    public void BeginFloor(int floorNumber, int mapSeed, string characterId,
        float startHP, float maxHP, int rationsAtEntry)
    {
        currentPlannedState = PendingPlan != null && PendingPlan.TargetFloorNumber == floorNumber
            ? PendingPlan.State
            : FloorDirectorState.Normal;
        currentBuilder = new FloorDirectorSnapshotBuilder(floorNumber, mapSeed, characterId,
            startHP, maxHP, rationsAtEntry);
    }

    public void RecordCombat(CombatTelemetrySnapshot combat) => currentBuilder?.RecordCombat(combat);

    public void RecordVisitedRoom(RoomType roomType, string contentKey) =>
        currentBuilder?.RecordVisitedRoom(roomType, contentKey);

    public FloorDirectorPlan CompleteFloor(float endHPBeforeRecovery, float maxHP,
        int rationsBeforeRecovery, bool hasStrongNegativeEffect = false)
    {
        if (currentBuilder == null) throw new InvalidOperationException("Floor director observation has not started.");

        FloorDirectorSnapshot snapshot = currentBuilder.Complete(endHPBeforeRecovery, maxHP,
            rationsBeforeRecovery, hasStrongNegativeEffect);
        int targetFloor = snapshot.FloorNumber + 1;
        int planSeed = unchecked(snapshot.MapSeed * 397 ^ targetFloor * 7919 ^ 0x5f3759df);
        PendingPlan = FloorDirectorEvaluator.Evaluate(snapshot, history, currentPlannedState, targetFloor, planSeed);
        history.Add(new FloorDirectorResult
        {
            Snapshot = snapshot,
            PlannedState = currentPlannedState,
            NextPlan = PendingPlan
        });
        currentBuilder = null;
        return PendingPlan;
    }
}
