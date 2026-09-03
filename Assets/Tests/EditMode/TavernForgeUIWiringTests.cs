using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Доработка Codex (сентябрь 2026): доводит Таверну/Кузницу до играбельного состояния — UI-facing
// state helpers (GetRecipeState/GetBlueprintState), data-asset миграция (FoodRecipeCatalog/
// ForgeBlueprintCatalog теперь Resources.LoadAll, не хардкод) и healMultiplier passthrough для
// привала с блюдом. TavernForgeBackendTests.cs/FoodAndPrototypeMechanicsTests.cs уже покрывают
// исходный Codex-бэкенд — этот файл покрывает только то, что добавлено поверх него.
public class TavernForgeUIWiringTests
{
    readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (var value in created) Object.DestroyImmediate(value);
    }

    FoodRecipeData Recipe(int tavernLevel = 1)
    {
        var recipe = ScriptableObject.CreateInstance<FoodRecipeData>();
        created.Add(recipe);
        recipe.recipeId = "recipe_stew";
        recipe.requiredTavernLevel = tavernLevel;
        recipe.ingredientCosts.Add(new ResourceAmount(PersistentResourceIds.RawMeat, 2));
        return recipe;
    }

    ForgeBlueprintData Blueprint()
    {
        var blueprint = ScriptableObject.CreateInstance<ForgeBlueprintData>();
        created.Add(blueprint);
        blueprint.blueprintId = "blueprint_sword";
        blueprint.prototypeId = "prototype_sword";
        blueprint.materialCost.Add(new ResourceAmount(PersistentResourceIds.TemperedSteel, 2));
        return blueprint;
    }

    // ==================== Data assets (Resources.LoadAll single source of truth) ====================

    [Test]
    public void FoodRecipeCatalog_LoadsFifteenAssetsWithApprovedContent()
    {
        var recipes = FoodRecipeCatalog.All;
        Assert.AreEqual(15, recipes.Count);
        Assert.AreEqual(15, recipes.Select(r => r.recipeId).Distinct().Count(), "recipeId должны быть уникальны");
        Assert.AreEqual(15, recipes.Select(r => r.resultFoodId).Distinct().Count());
        foreach (var recipe in recipes)
        {
            Assert.AreEqual(3, recipe.durationRooms, recipe.recipeId);
            Assert.IsTrue(recipe.requiredTavernLevel >= 1 && recipe.requiredTavernLevel <= 5, recipe.recipeId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(recipe.description), $"{recipe.recipeId} должен иметь описание эффекта");
            Assert.Greater(recipe.ingredientCosts.Count, 0, recipe.recipeId);
        }

        for (int level = 1; level <= 5; level++)
            Assert.AreEqual(3, recipes.Count(r => r.requiredTavernLevel == level), $"3 блюда на уровень {level}");
    }

    [Test]
    public void ForgeBlueprintCatalog_LoadsSixAssetsEachWiredToRealPrototypeItem()
    {
        var blueprints = ForgeBlueprintCatalog.All;
        Assert.AreEqual(6, blueprints.Count);
        foreach (var blueprint in blueprints)
        {
            Assert.IsNotNull(blueprint.itemPrototype, $"{blueprint.blueprintId} должен ссылаться на реальный ForgePrototypes/ItemData ассет");
            Assert.AreEqual(blueprint.prototypeId, blueprint.itemPrototype.prototypeId, blueprint.blueprintId);
            Assert.AreEqual(ItemTier.Epic, blueprint.rarity, blueprint.blueprintId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(blueprint.description), $"{blueprint.blueprintId} должен иметь описание эффекта");
            Assert.AreEqual(4, blueprint.materialCost.Count, blueprint.blueprintId);
        }
    }

    // ==================== TavernRecipeState / ForgeBlueprintState (Tavern/Forge UI) ====================

    [Test]
    public void TavernRecipeState_ReflectsLevelUnlockAndIngredients()
    {
        var data = new SaveData();
        var recipe = Recipe(tavernLevel: 2);
        var closed = new TavernService(data, access: new CatalogUnlockPolicy(false));
        Assert.AreEqual(TavernRecipeState.LockedByTavernLevel, closed.GetRecipeState(recipe, 1));
        Assert.AreEqual(TavernRecipeState.LockedRecipe, closed.GetRecipeState(recipe, 2));

        var openNoIngredients = new TavernService(data, access: new CatalogUnlockPolicy(true));
        Assert.AreEqual(TavernRecipeState.NotEnoughIngredients, openNoIngredients.GetRecipeState(recipe, 2));

        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.RawMeat, count = 5 });
        Assert.AreEqual(TavernRecipeState.AvailableToCook, openNoIngredients.GetRecipeState(recipe, 2));
    }

    [Test]
    public void ForgeBlueprintState_ReflectsLockedMaterialsAndAlreadyCreated()
    {
        var data = new SaveData();
        var blueprint = Blueprint();
        var closed = new ForgeService(data, access: new CatalogUnlockPolicy(false));
        Assert.AreEqual(ForgeBlueprintState.BlueprintLocked, closed.GetBlueprintState(blueprint));

        var open = new ForgeService(data, access: new CatalogUnlockPolicy(true));
        Assert.AreEqual(ForgeBlueprintState.NotEnoughMaterials, open.GetBlueprintState(blueprint));

        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.TemperedSteel, count = 5 });
        Assert.AreEqual(ForgeBlueprintState.AvailableToResearch, open.GetBlueprintState(blueprint));

        Assert.IsTrue(open.TryResearch(blueprint));
        Assert.AreEqual(ForgeBlueprintState.PrototypeCreated, open.GetBlueprintState(blueprint));
    }

    // ==================== Rest/Camp integration: healMultiplier passthrough ====================

    [Test]
    public void TryConsumeDishAtCamp_ForwardsHealMultiplierToCampManager()
    {
        var data = new SaveData();
        data.preparedDishes.Add(new KeyCountEntry { key = "food_test", count = 1 });
        var recipe = ScriptableObject.CreateInstance<FoodRecipeData>();
        created.Add(recipe);
        recipe.resultFoodId = "food_test";

        var characterData = ScriptableObject.CreateInstance<CharacterData>();
        created.Add(characterData);
        characterData.baseHealth = 100;
        var characterManagerGO = new GameObject("character-manager"); created.Add(characterManagerGO);
        var characterManager = characterManagerGO.AddComponent<CharacterManager>();
        characterManager.BeginRun(characterData, null, null);
        characterManager.Combatant.CurrentHP = 10f;

        var campManagerGO = new GameObject("camp-manager"); created.Add(campManagerGO);
        var campManager = campManagerGO.AddComponent<CampManager>();

        var service = new TavernService(data);
        Assert.IsTrue(service.TryConsumeDishAtCamp(recipe, campManager, characterManager, out var result, healMultiplier: 2f));
        // База привала — 50% макс. HP (CampManager.RestoreAtCamp); ×2 healMultiplier -> полное лечение.
        Assert.AreEqual(100f, characterManager.Combatant.CurrentHP);
        Assert.AreEqual(0, service.GetPreparedCount("food_test"));
    }

    [Test]
    public void GetIngredientAmount_And_GetMaterialAmount_ReadResourceInventory()
    {
        var data = new SaveData();
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.Grain, count = 7 });
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.MagicCrystal, count = 3 });
        Assert.AreEqual(7, new TavernService(data).GetIngredientAmount(PersistentResourceIds.Grain));
        Assert.AreEqual(0, new TavernService(data).GetIngredientAmount(PersistentResourceIds.RawMeat));
        Assert.AreEqual(3, new ForgeService(data).GetMaterialAmount(PersistentResourceIds.MagicCrystal));
    }

    // ==================== Service lifecycle (HubManager/SaveManager) ====================

    [Test]
    public void SaveManager_CreatesIndependentTavernAndForgeServiceInstancesSharingSaveData()
    {
        var saveManagerGO = new GameObject("save-manager-wiring"); created.Add(saveManagerGO);
        var saveManager = saveManagerGO.AddComponent<SaveManager>();
        saveManager.LoadGame();

        var tavern = saveManager.CreateTavernService();
        var forge = saveManager.CreateForgeService();
        Assert.IsNotNull(tavern);
        Assert.IsNotNull(forge);

        // Оба сервиса читают/пишут один и тот же SaveManager.Data — критично для того, чтобы Tavern
        // и Forge screens (созданные один раз в HubManager.Start()) видели изменения друг друга без
        // повторного создания сервисов при каждом открытии экрана.
        saveManager.Data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.RawMeat, count = 4 });
        Assert.AreEqual(4, tavern.GetIngredientAmount(PersistentResourceIds.RawMeat));
    }
}
