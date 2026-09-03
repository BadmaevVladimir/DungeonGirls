using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public enum RewardRoomContext
{
    Combat,
    Boss,
    Trap,
    Special
}

public interface IRewardRandom
{
    float Value();
    int Range(int minInclusive, int maxExclusive);
}

public sealed class UnityRewardRandom : IRewardRandom
{
    public float Value() => UnityEngine.Random.value;
    public int Range(int minInclusive, int maxExclusive) => UnityEngine.Random.Range(minInclusive, maxExclusive);
}

public sealed class SeededRewardRandom : IRewardRandom
{
    readonly System.Random random;
    public SeededRewardRandom(int seed) => random = new System.Random(seed);
    public float Value() => (float)random.NextDouble();
    public int Range(int minInclusive, int maxExclusive) => random.Next(minInclusive, maxExclusive);
}

[Serializable]
public class IngredientDropRule
{
    public string resourceId;
    [Min(0f)] public float weight;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
}

[CreateAssetMenu(fileName = "RoomRewardConfig", menuName = "DungeonGirls/Room Reward Config")]
public class RoomRewardConfig : ScriptableObject
{
    [Range(0f, 1f)] public float combatChestDropChance = 0.50f;
    [Range(0f, 1f)] public float combatIngredientDropChance = 0.30f;
    [Range(0f, 1f)] public float successfulTrapIngredientDropChance = 0.45f;
    [Range(0f, 1f)] public float supportedSpecialIngredientDropChance = 0.60f;
    [Range(0f, 1f)] public float normalCombatForgeMaterialChance = 0.05f;
    [Range(0f, 1f)] public float successfulTrapForgeMaterialChance = 0.08f;
    public List<IngredientDropRule> combatIngredientDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.RawMeat, weight=65 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterEggs, weight=20 },
        new IngredientDropRule { resourceId=PersistentResourceIds.HealingHerbs, weight=10 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RootVegetables, weight=5 }
    };
    public List<IngredientDropRule> trapIngredientDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.HealingHerbs, weight=45 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RootVegetables, weight=30 },
        new IngredientDropRule { resourceId=PersistentResourceIds.CaveMushrooms, weight=20 },
        new IngredientDropRule { resourceId=PersistentResourceIds.Grain, weight=5 }
    };
    public List<IngredientDropRule> specialIngredientDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.Grain, weight=30 },
        new IngredientDropRule { resourceId=PersistentResourceIds.CaveMushrooms, weight=22 },
        new IngredientDropRule { resourceId=PersistentResourceIds.HealingHerbs, weight=20 },
        new IngredientDropRule { resourceId=PersistentResourceIds.Dairy, weight=15 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RootVegetables, weight=10 },
        new IngredientDropRule { resourceId=PersistentResourceIds.EtherealSpice, weight=3 }
    };
    public List<IngredientDropRule> bossIngredientDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterEggs, weight=30 },
        new IngredientDropRule { resourceId=PersistentResourceIds.Dairy, weight=20 },
        new IngredientDropRule { resourceId=PersistentResourceIds.CaveMushrooms, weight=15 },
        new IngredientDropRule { resourceId=PersistentResourceIds.HealingHerbs, weight=10 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RawMeat, weight=10 },
        new IngredientDropRule { resourceId=PersistentResourceIds.Grain, weight=5 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RootVegetables, weight=5 },
        new IngredientDropRule { resourceId=PersistentResourceIds.EtherealSpice, weight=5 }
    };
    public List<IngredientDropRule> combatForgeMaterialDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.TemperedSteel, weight=60 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MagicCrystal, weight=25 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterCore, weight=15 }
    };
    public List<IngredientDropRule> trapForgeMaterialDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.TemperedSteel, weight=45 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MagicCrystal, weight=35 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterCore, weight=20 }
    };
    public List<IngredientDropRule> bossForgeMaterialDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.TemperedSteel, weight=35 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MagicCrystal, weight=30 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterCore, weight=25 },
        new IngredientDropRule { resourceId=PersistentResourceIds.AncientShard, weight=10 }
    };
    public List<IngredientDropRule> abandonedForgeMaterialDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.TemperedSteel, weight=45 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MagicCrystal, weight=35 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterCore, weight=15 },
        new IngredientDropRule { resourceId=PersistentResourceIds.AncientShard, weight=5 }
    };
}

// Неизменяемый снимок: UI и последующие состояния видят тот же roll и тот же chest payload.
public sealed class RoomRewardResult
{
    readonly ReadOnlyCollection<ResourceAmount> ingredients;
    readonly ReadOnlyCollection<ResourceAmount> forgeMaterials;

    public int Currency { get; }
    public IReadOnlyList<ResourceAmount> Ingredients => ingredients;
    public IReadOnlyList<ResourceAmount> ForgeMaterials => forgeMaterials;
    public bool HasChest { get; }
    public RewardRoomContext RoomContext { get; }
    public ChestReward Chest { get; }

    public RoomRewardResult(int currency, IEnumerable<ResourceAmount> ingredients, bool hasChest,
        RewardRoomContext roomContext, ChestReward chest, IEnumerable<ResourceAmount> forgeMaterials = null)
    {
        Currency = Mathf.Max(0, currency);
        this.ingredients = new List<ResourceAmount>(ingredients ?? Array.Empty<ResourceAmount>()).AsReadOnly();
        this.forgeMaterials = new List<ResourceAmount>(forgeMaterials ?? Array.Empty<ResourceAmount>()).AsReadOnly();
        HasChest = hasChest;
        RoomContext = roomContext;
        Chest = hasChest ? chest : null;
    }
}

// Однократное применение отделено и от расчёта, и от UI-подтверждения.
public sealed class RoomRewardGrant
{
    public RoomRewardResult Result { get; }
    public bool IsApplied { get; private set; }

    public RoomRewardGrant(RoomRewardResult result) => Result = result ?? throw new ArgumentNullException(nameof(result));

    public bool TryApply(Action<int> addCurrency, Action<ResourceAmount> addResource)
    {
        if (IsApplied) return false;
        // Устанавливается до callbacks, чтобы re-entrant callback тоже не мог выдать награду снова.
        IsApplied = true;
        addCurrency?.Invoke(Result.Currency);
        if (addResource != null)
        {
            for (int i = 0; i < Result.Ingredients.Count; i++) addResource(Result.Ingredients[i]);
            for (int i = 0; i < Result.ForgeMaterials.Count; i++) addResource(Result.ForgeMaterials[i]);
        }
        return true;
    }
}
