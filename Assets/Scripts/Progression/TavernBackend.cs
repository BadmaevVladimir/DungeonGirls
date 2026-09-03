using System;
using System.Collections.Generic;
using UnityEngine;

public interface IDishEffect
{
    string EffectId { get; }
    void Apply(CampManager campManager, CharacterManager characterManager);
}

// UI-facing state for one recipe card (Tavern screen). Backend remains source of truth: the UI
// only reads this to decide which visual state/label to show and whether the cook button is
// interactable — TryCook() below is still the only thing that actually spends resources.
public enum TavernRecipeState
{
    AvailableToCook,
    LockedByTavernLevel,
    LockedRecipe,
    NotEnoughIngredients
}

public sealed class CatalogUnlockPolicy
{
    public bool UnlockAllForTesting { get; set; }

    public CatalogUnlockPolicy(bool unlockAllForTesting = true) => UnlockAllForTesting = unlockAllForTesting;

    public bool IsUnlocked(string id, IReadOnlyList<string> savedIds) =>
        UnlockAllForTesting || Contains(savedIds, id);

    static bool Contains(IReadOnlyList<string> values, string id)
    {
        if (values == null || string.IsNullOrWhiteSpace(id)) return false;
        for (int i = 0; i < values.Count; i++)
            if (string.Equals(values[i], id, StringComparison.Ordinal)) return true;
        return false;
    }
}

public sealed class TavernService
{
    readonly SaveData data;
    readonly Action persist;
    readonly CatalogUnlockPolicy access;

    public TavernService(SaveData data, Action persist = null, CatalogUnlockPolicy access = null)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.persist = persist;
        this.access = access ?? new CatalogUnlockPolicy();
    }

    public TavernRecipeState GetRecipeState(FoodRecipeData recipe, int tavernLevel)
    {
        if (recipe == null || string.IsNullOrWhiteSpace(recipe.recipeId)) return TavernRecipeState.LockedRecipe;
        if (tavernLevel < recipe.requiredTavernLevel) return TavernRecipeState.LockedByTavernLevel;
        if (!access.IsUnlocked(recipe.recipeId, data.unlockedTavernRecipes)) return TavernRecipeState.LockedRecipe;
        if (!new ResourceInventory(data.resources).CanAfford(recipe.ingredientCosts)) return TavernRecipeState.NotEnoughIngredients;
        return TavernRecipeState.AvailableToCook;
    }

    public int GetIngredientAmount(string resourceId) => new ResourceInventory(data.resources).GetAmount(resourceId);

    public bool CanCook(FoodRecipeData recipe, int tavernLevel = int.MaxValue)
    {
        if (recipe == null || string.IsNullOrWhiteSpace(recipe.recipeId) ||
            string.IsNullOrWhiteSpace(recipe.resultFoodId) || tavernLevel < recipe.requiredTavernLevel) return false;
        for (int i = 0; i < recipe.ingredientCosts.Count; i++)
            if (string.IsNullOrWhiteSpace(recipe.ingredientCosts[i].resourceId) || recipe.ingredientCosts[i].amount <= 0) return false;
        return access.IsUnlocked(recipe.recipeId, data.unlockedTavernRecipes) &&
            new ResourceInventory(data.resources).CanAfford(recipe.ingredientCosts);
    }

    public bool UnlockRecipe(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId) || data.unlockedTavernRecipes.Contains(recipeId)) return false;
        data.unlockedTavernRecipes.Add(recipeId);
        persist?.Invoke();
        return true;
    }

    // Ресурсы и порции меняются после полной валидации и сохраняются одной записью.
    public bool TryCook(FoodRecipeData recipe, int tavernLevel = int.MaxValue)
    {
        if (!CanCook(recipe, tavernLevel)) return false;
        var inventory = new ResourceInventory(data.resources);
        foreach (var cost in recipe.ingredientCosts)
            inventory.FindOrCreate(cost.resourceId).count -= cost.amount;
        var dish = FindOrCreate(data.preparedDishes, recipe.resultFoodId);
        dish.count += 1;
        persist?.Invoke();
        return true;
    }

    public bool TryConsumeDishAtCamp(FoodRecipeData recipe, CampManager campManager,
        CharacterManager characterManager, out CampManager.CampResult result, float healMultiplier = 1f)
    {
        result = default;
        if (recipe == null || campManager == null || characterManager == null) return false;
        var dish = data.preparedDishes.Find(entry => entry != null && entry.key == recipe.resultFoodId);
        if (dish == null || dish.count <= 0) return false;
        dish.count--;
        persist?.Invoke();
        result = campManager.RestWithPreparedDish(characterManager, healMultiplier);
        characterManager.ActivateFood(recipe);
        return true;
    }

    public int GetPreparedCount(string resultFoodId)
    {
        if (string.IsNullOrWhiteSpace(resultFoodId)) return 0;
        var dish = data.preparedDishes.Find(entry => entry != null && entry.key == resultFoodId);
        return dish != null ? Mathf.Max(0, dish.count) : 0;
    }

    static KeyCountEntry FindOrCreate(List<KeyCountEntry> entries, string id)
    {
        var entry = entries.Find(candidate => candidate != null && candidate.key == id);
        if (entry != null) return entry;
        entry = new KeyCountEntry { key = id };
        entries.Add(entry);
        return entry;
    }
}
