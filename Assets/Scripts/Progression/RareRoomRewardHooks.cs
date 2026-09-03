using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RareRoomConfig", menuName = "DungeonGirls/Rare Room Config")]
public class RareRoomConfig : ScriptableObject
{
    [Range(0f, 1f)] public float mushroomCaveChance = 0.10f;
    [Min(1)] public int mushroomCaveMinimumFloor = 2;
    [Min(1)] public int mushroomCavePerFloorLimit = 1;
    [Range(0f, 1f)] public float harpyNestChance = 0.10f;
    [Min(1)] public int harpyNestMinimumFloor = 2;
    [Min(1)] public int harpyNestPerFloorLimit = 1;
    [Range(0f, 1f)] public float abandonedForgeChance = 0.08f;
    [Min(1)] public int abandonedForgeMinimumFloor = 3;
    [Min(1)] public int abandonedForgePerFloorLimit = 1;
    [Min(1)] public int mushroomSafeAmount = 2;
    [Min(1)] public int mushroomRiskAmount = 4;
    [Range(0f, 1f)] public float mushroomPoisonChance = 0.25f;
    [Min(1)] public int mushroomPoisonDurationRooms = 3;
    [Range(0f, 100f)] public float mushroomPoisonHealingPenaltyPercent = 10f;
    [Min(1)] public int harpySuccessMinEggs = 2;
    [Min(1)] public int harpySuccessMaxEggs = 3;
    [Min(1)] public int harpyFailureVictoryEggs = 1;
    [Range(0f, 1f)] public float abandonedForgeTwoMaterialsChance = 0.25f;
}

public enum RareRoomContentId { None, MushroomCave, HarpyNest, AbandonedForge }

public sealed class RareRoomFloorState
{
    readonly Dictionary<RareRoomContentId, int> counts = new Dictionary<RareRoomContentId, int>();
    public int Count(RareRoomContentId id) => counts.TryGetValue(id, out int value) ? value : 0;
    public void Reserve(RareRoomContentId id) => counts[id] = Count(id) + 1;
}

public static class RareRoomContentResolver
{
    public static RareRoomContentId Resolve(RoomType roomType, int floor, RareRoomConfig config,
        RareRoomFloorState state, IRewardRandom random)
    {
        if (config == null || state == null || random == null) return RareRoomContentId.None;
        float roll = random.Value();
        if (roomType == RoomType.Trap)
            return TryPick(RareRoomContentId.HarpyNest, floor, config.harpyNestMinimumFloor,
                config.harpyNestPerFloorLimit, config.harpyNestChance, roll, state);
        if (roomType != RoomType.Special) return RareRoomContentId.None;
        if (IsEligible(RareRoomContentId.MushroomCave, floor, config.mushroomCaveMinimumFloor,
            config.mushroomCavePerFloorLimit, state) && roll < config.mushroomCaveChance)
        {
            state.Reserve(RareRoomContentId.MushroomCave);
            return RareRoomContentId.MushroomCave;
        }
        float forgeStart = IsEligible(RareRoomContentId.MushroomCave, floor, config.mushroomCaveMinimumFloor,
            config.mushroomCavePerFloorLimit, state) ? config.mushroomCaveChance : 0f;
        if (IsEligible(RareRoomContentId.AbandonedForge, floor, config.abandonedForgeMinimumFloor,
            config.abandonedForgePerFloorLimit, state) && roll >= forgeStart &&
            roll < forgeStart + config.abandonedForgeChance)
        {
            state.Reserve(RareRoomContentId.AbandonedForge);
            return RareRoomContentId.AbandonedForge;
        }
        return RareRoomContentId.None;
    }

    static RareRoomContentId TryPick(RareRoomContentId id, int floor, int minFloor, int limit,
        float chance, float roll, RareRoomFloorState state)
    {
        if (!IsEligible(id, floor, minFloor, limit, state) || roll >= chance) return RareRoomContentId.None;
        state.Reserve(id);
        return id;
    }

    static bool IsEligible(RareRoomContentId id, int floor, int minFloor, int limit,
        RareRoomFloorState state) => floor >= minFloor && state.Count(id) < Mathf.Max(0, limit);
}

// Backend outcomes. UI consumes these without changing the top-level room taxonomy.
public static class RareRoomRewardHooks
{
    public struct MushroomCaveOutcome
    {
        public ResourceAmount Mushrooms;
        public bool ApplyNegativeConsequence;
    }

    public static MushroomCaveOutcome ResolveMushroomCave(bool risky, RareRoomConfig config, IRewardRandom random)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        random ??= new UnityRewardRandom();
        return new MushroomCaveOutcome
        {
            Mushrooms = new ResourceAmount(PersistentResourceIds.CaveMushrooms,
                risky ? config.mushroomRiskAmount : config.mushroomSafeAmount),
            ApplyNegativeConsequence = risky && random.Value() < config.mushroomPoisonChance
        };
    }

    public static ResourceAmount ResolveHarpyNestSuccess(RareRoomConfig config, IRewardRandom random)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        random ??= new UnityRewardRandom();
        return new ResourceAmount(PersistentResourceIds.MonsterEggs,
            random.Range(config.harpySuccessMinEggs, config.harpySuccessMaxEggs + 1));
    }

    // Call only after the failure combat has been won. Monster_Harpy already exists in project data.
    public static ResourceAmount ResolveHarpyNestFailureCombatVictory(RareRoomConfig config) =>
        new ResourceAmount(PersistentResourceIds.MonsterEggs, config.harpyFailureVictoryEggs);

    public static int RollAbandonedForgeMaterialCount(RareRoomConfig config, IRewardRandom random)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        random ??= new UnityRewardRandom();
        return random.Value() < config.abandonedForgeTwoMaterialsChance ? 2 : 1;
    }
}
