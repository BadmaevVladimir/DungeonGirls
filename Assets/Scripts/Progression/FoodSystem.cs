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

// FoodRecipeData объявлен ПЕРВЫМ классом в файле намеренно (после enum выше): Unity's MonoImporter
// связывает .cs-ассет с первым объявленным в нём классом, а AssetDatabase.CreateAsset ищет
// MonoScript именно по этой связи. Когда FoodEffectConfig стоял первым (не-ScriptableObject класс),
// AssetDatabase.CreateAsset<FoodRecipeData> в batchmode-генераторе (ProgressionContentAssetGenerator)
// стабильно писал m_Script: {fileID: 0} ("No script asset for FoodRecipeData") — не держите здесь
// других классов выше FoodRecipeData.
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
    // Плейсхолдер-стиль (3.8): null допустим — UI показывает цветной свотч вместо иконки,
    // пока нет пиксель-арта блюда.
    public Sprite icon;
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

// Данные (кто/что) живут в Assets/Resources/Progression/FoodRecipes/*.asset (сгенерированы
// ProgressionContentAssetGenerator.Generate — см. комментарий в файле генератора про причину
// batchmode-генерации вместо ручного YAML). Этот класс — только registry поверх них, без единого
// рецепта в самом коде.
public static class FoodRecipeCatalog
{
    const string ResourcesPath = "Progression/FoodRecipes";
    static FoodRecipeData[] recipes;
    public static IReadOnlyList<FoodRecipeData> All => recipes ??= Resources.LoadAll<FoodRecipeData>(ResourcesPath);
}
