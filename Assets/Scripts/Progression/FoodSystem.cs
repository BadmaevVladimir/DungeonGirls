using System;
using System.Collections.Generic;
using UnityEngine;

public enum FoodEffectType
{
    MaxHp,
    ReceivedHealing,
    BarrierAfterRest,
    PhysicalDamage,
    ArmorEffectiveness,
    HealAfterRoom,
    CritChancePoints,
    AttackSpeed,
    NegativeStatusDuration,
    BonusIngredientAfterRoom,
    AllDamageAndMaxHp,
    LowHealthHeal,
    BossDamage,
    BlockFirstNegativeStatus,
    RoyalCombination
}

[Serializable]
public class FoodEffectConfig
{
    public FoodEffectType effectType;
    public float primaryValue;
    public float secondaryValue;
    public float tertiaryValue;
    public float procChance;
    public float thresholdPercent;
}

[CreateAssetMenu(fileName = "FoodRecipe", menuName = "DungeonGirls/Tavern Food Recipe")]
public class FoodRecipeData : ScriptableObject
{
    public string recipeId;
    public string displayName;
    public string resultFoodId;
    [Min(1)] public int requiredTavernLevel = 1;
    public List<ResourceAmount> ingredientCosts = new List<ResourceAmount>();
    public FoodEffectConfig effect = new FoodEffectConfig();
    [Min(1)] public int durationRooms = 3;
    [TextArea] public string description;
}

// Backward-compatible alias for the initial backend API.
public class DishRecipeData : FoodRecipeData
{
    public string dishId { get => resultFoodId; set => resultFoodId = value; }
    public List<ResourceAmount> ingredients => ingredientCosts;
    public int portionsCreated = 1;
    public string effectId;
}

public sealed class ActiveFoodBuff
{
    public string FoodId { get; private set; }
    public int RemainingRooms { get; private set; }
    public FoodEffectConfig Effect { get; private set; }
    public bool NegativeStatusBlockConsumed { get; private set; }
    public bool ProcUsedThisRoom { get; private set; }
    public int TotalProcs { get; private set; }
    public bool IsActive => !string.IsNullOrWhiteSpace(FoodId) && RemainingRooms > 0;

    CombatantRuntime boundRuntime;
    float appliedMaxHp;

    public void Activate(FoodRecipeData recipe, CombatantRuntime runtime)
    {
        Clear();
        if (recipe == null || runtime == null) return;
        FoodId = recipe.resultFoodId;
        RemainingRooms = Mathf.Max(1, recipe.durationRooms);
        Effect = recipe.effect ?? new FoodEffectConfig();
        NegativeStatusBlockConsumed = false;
        ProcUsedThisRoom = false;
        TotalProcs = 0;
        Bind(runtime);
    }

    public void Bind(CombatantRuntime runtime)
    {
        if (!IsActive || runtime == null) return;
        Unbind();
        boundRuntime = runtime;
        runtime.ActiveFoodBuff = this;
        ApplyModifiers(runtime);
    }

    public void BeginRoom() => ProcUsedThisRoom = false;

    public bool TryBlockNegativeStatus()
    {
        if (!IsActive || Effect.effectType != FoodEffectType.BlockFirstNegativeStatus || NegativeStatusBlockConsumed)
            return false;
        NegativeStatusBlockConsumed = true;
        return true;
    }

    public bool TryLowHealthHeal(CombatantRuntime runtime)
    {
        if (!IsActive || Effect.effectType != FoodEffectType.LowHealthHeal || ProcUsedThisRoom || TotalProcs >= 3 ||
            runtime == null || !runtime.IsAlive || runtime.MaxHP <= 0f ||
            runtime.CurrentHP >= runtime.MaxHP * Effect.thresholdPercent / 100f)
            return false;
        ProcUsedThisRoom = true;
        TotalProcs++;
        runtime.Heal(runtime.MaxHP * Effect.primaryValue / 100f);
        return true;
    }

    public bool CompleteRoom(CombatantRuntime runtime, IRewardRandom random)
    {
        if (!IsActive) return false;
        if (Effect.effectType == FoodEffectType.HealAfterRoom && TotalProcs < 3)
        {
            runtime?.Heal(runtime.MaxHP * Effect.primaryValue / 100f);
            TotalProcs++;
        }
        bool bonusIngredient = Effect.effectType == FoodEffectType.BonusIngredientAfterRoom &&
            random != null && random.Value() < Effect.procChance;
        RemainingRooms--;
        if (RemainingRooms <= 0) Clear();
        return bonusIngredient;
    }

    public void Clear()
    {
        Unbind();
        FoodId = null;
        RemainingRooms = 0;
        Effect = null;
        NegativeStatusBlockConsumed = false;
        ProcUsedThisRoom = false;
        TotalProcs = 0;
    }

    void ApplyModifiers(CombatantRuntime runtime)
    {
        float maxHpPercent = Effect.effectType switch
        {
            FoodEffectType.MaxHp => Effect.primaryValue,
            FoodEffectType.AllDamageAndMaxHp => Effect.secondaryValue,
            _ => 0f
        };
        if (maxHpPercent > 0f)
        {
            appliedMaxHp = runtime.MaxHP * maxHpPercent / 100f;
            runtime.MaxHP += appliedMaxHp;
            runtime.CurrentHP += appliedMaxHp;
        }
        runtime.FoodReceivedHealingPercent = Effect.effectType switch
        {
            FoodEffectType.ReceivedHealing => Effect.primaryValue,
            FoodEffectType.RoyalCombination => Effect.tertiaryValue,
            _ => 0f
        };
        runtime.FoodDamagePercent = Effect.effectType switch
        {
            FoodEffectType.AllDamageAndMaxHp => Effect.primaryValue,
            FoodEffectType.RoyalCombination => Effect.primaryValue,
            _ => 0f
        };
        runtime.FoodPhysicalDamagePercent = Effect.effectType == FoodEffectType.PhysicalDamage ? Effect.primaryValue : 0f;
        runtime.FoodBossDamagePercent = Effect.effectType == FoodEffectType.BossDamage ? Effect.primaryValue : 0f;
        runtime.FoodArmorEffectivenessPercent = Effect.effectType switch
        {
            FoodEffectType.ArmorEffectiveness => Effect.primaryValue,
            FoodEffectType.RoyalCombination => Effect.secondaryValue,
            _ => 0f
        };
        runtime.FoodAttackSpeedPercent = Effect.effectType == FoodEffectType.AttackSpeed ? Effect.primaryValue : 0f;
        runtime.FoodCritChancePoints = Effect.effectType == FoodEffectType.CritChancePoints ? Effect.primaryValue : 0f;
        runtime.FoodNegativeStatusDurationReductionPercent =
            Effect.effectType == FoodEffectType.NegativeStatusDuration ? Effect.primaryValue : 0f;
        if (Effect.effectType == FoodEffectType.BarrierAfterRest)
        {
            runtime.ShieldPoolMax = runtime.MaxHP * Effect.primaryValue / 100f;
            runtime.ShieldPoolCurrent = runtime.ShieldPoolMax;
            runtime.ShieldPoolExpireTimer = float.PositiveInfinity;
            runtime.FoodBarrierActive = true;
        }
    }

    void Unbind()
    {
        if (boundRuntime == null) return;
        if (appliedMaxHp > 0f)
        {
            boundRuntime.MaxHP = Mathf.Max(1f, boundRuntime.MaxHP - appliedMaxHp);
            boundRuntime.CurrentHP = Mathf.Min(boundRuntime.CurrentHP, boundRuntime.MaxHP);
        }
        if (boundRuntime.FoodBarrierActive)
        {
            boundRuntime.ShieldPoolCurrent = 0f;
            boundRuntime.ShieldPoolMax = 0f;
            boundRuntime.FoodBarrierActive = false;
        }
        boundRuntime.FoodReceivedHealingPercent = 0f;
        boundRuntime.FoodDamagePercent = 0f;
        boundRuntime.FoodPhysicalDamagePercent = 0f;
        boundRuntime.FoodBossDamagePercent = 0f;
        boundRuntime.FoodArmorEffectivenessPercent = 0f;
        boundRuntime.FoodAttackSpeedPercent = 0f;
        boundRuntime.FoodCritChancePoints = 0f;
        boundRuntime.FoodNegativeStatusDurationReductionPercent = 0f;
        boundRuntime.ActiveFoodBuff = null;
        boundRuntime = null;
        appliedMaxHp = 0f;
    }
}

public static class FoodRecipeCatalog
{
    static FoodRecipeData[] recipes;
    public static IReadOnlyList<FoodRecipeData> All => recipes ??= CreateAll();

    static FoodRecipeData[] CreateAll() => new[]
    {
        Make("meat_stew", "Мясное рагу", 1, FoodEffectType.MaxHp, 8, (PersistentResourceIds.RawMeat,2),(PersistentResourceIds.RootVegetables,1)),
        Make("mushroom_soup", "Грибной суп", 1, FoodEffectType.ReceivedHealing, 10, (PersistentResourceIds.CaveMushrooms,2),(PersistentResourceIds.HealingHerbs,1)),
        Make("knight_porridge", "Рыцарская каша", 1, FoodEffectType.BarrierAfterRest, 10, (PersistentResourceIds.Grain,2),(PersistentResourceIds.Dairy,1)),
        Make("warden_roast", "Жаркое Стража", 2, FoodEffectType.PhysicalDamage, 6, (PersistentResourceIds.RawMeat,2),(PersistentResourceIds.CaveMushrooms,1)),
        Make("root_puree", "Корнеплодное пюре", 2, FoodEffectType.ArmorEffectiveness, 6, (PersistentResourceIds.RootVegetables,2),(PersistentResourceIds.Dairy,1)),
        Make("herbal_broth", "Травяной бульон", 2, FoodEffectType.HealAfterRoom, 3, (PersistentResourceIds.HealingHerbs,2),(PersistentResourceIds.Grain,1)),
        Make("hunters_omelette", "Омлет охотника", 3, FoodEffectType.CritChancePoints, 4, (PersistentResourceIds.MonsterEggs,2),(PersistentResourceIds.RawMeat,1)),
        Make("spicy_omelette", "Острый омлет", 3, FoodEffectType.AttackSpeed, 5, (PersistentResourceIds.MonsterEggs,2),(PersistentResourceIds.HealingHerbs,1)),
        Make("mushroom_pie", "Грибной пирог", 3, FoodEffectType.NegativeStatusDuration, 20, (PersistentResourceIds.CaveMushrooms,1),(PersistentResourceIds.Grain,1),(PersistentResourceIds.Dairy,1)),
        Make("explorer_stew", "Похлёбка исследователя", 4, FoodEffectType.BonusIngredientAfterRoom, 0, (PersistentResourceIds.RootVegetables,1),(PersistentResourceIds.CaveMushrooms,1),(PersistentResourceIds.Grain,1), procChance:.25f),
        Make("hearty_breakfast", "Сытный завтрак", 4, FoodEffectType.AllDamageAndMaxHp, 4, (PersistentResourceIds.RawMeat,1),(PersistentResourceIds.MonsterEggs,1),(PersistentResourceIds.Grain,1), secondary:5),
        Make("healing_casserole", "Целебная запеканка", 4, FoodEffectType.LowHealthHeal, 8, (PersistentResourceIds.HealingHerbs,1),(PersistentResourceIds.Dairy,1),(PersistentResourceIds.MonsterEggs,1), threshold:30),
        Make("veterans_steak", "Стейк ветерана", 5, FoodEffectType.BossDamage, 10, (PersistentResourceIds.RawMeat,2),(PersistentResourceIds.EtherealSpice,1)),
        Make("ethereal_soup", "Эфирный суп", 5, FoodEffectType.BlockFirstNegativeStatus, 0, (PersistentResourceIds.CaveMushrooms,1),(PersistentResourceIds.HealingHerbs,1),(PersistentResourceIds.EtherealSpice,1)),
        Make("royal_pie", "Королевский пирог", 5, FoodEffectType.RoyalCombination, 5, (PersistentResourceIds.Grain,1),(PersistentResourceIds.Dairy,1),(PersistentResourceIds.MonsterEggs,1),(PersistentResourceIds.EtherealSpice,1), secondary:5, tertiary:5)
    };

    static FoodRecipeData Make(string id, string name, int level, FoodEffectType type, float primary,
        (string id,int amount) a, (string id,int amount) b, (string id,int amount)? c = null,
        (string id,int amount)? d = null, float secondary = 0, float tertiary = 0,
        float procChance = 0, float threshold = 0)
    {
        var value = ScriptableObject.CreateInstance<FoodRecipeData>();
        value.recipeId = id;
        value.resultFoodId = "food_" + id;
        value.displayName = name;
        value.requiredTavernLevel = level;
        value.durationRooms = 3;
        value.ingredientCosts.Add(new ResourceAmount(a.id, a.amount));
        value.ingredientCosts.Add(new ResourceAmount(b.id, b.amount));
        if (c.HasValue) value.ingredientCosts.Add(new ResourceAmount(c.Value.id, c.Value.amount));
        if (d.HasValue) value.ingredientCosts.Add(new ResourceAmount(d.Value.id, d.Value.amount));
        value.effect = new FoodEffectConfig { effectType=type, primaryValue=primary, secondaryValue=secondary,
            tertiaryValue=tertiary, procChance=procChance, thresholdPercent=threshold };
        return value;
    }
}
