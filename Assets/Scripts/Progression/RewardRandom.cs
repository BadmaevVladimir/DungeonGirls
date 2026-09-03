using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

// Вынесено из RoomRewardConfig.cs (см. комментарий там) — здесь только plain-типы, ни одного
// ScriptableObject/MonoBehaviour, так что файл не подвержен путанице MonoScript-идентичности.
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
    [UnityEngine.Min(0f)] public float weight;
    [UnityEngine.Min(1)] public int minAmount = 1;
    [UnityEngine.Min(1)] public int maxAmount = 1;
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
        Currency = UnityEngine.Mathf.Max(0, currency);
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
