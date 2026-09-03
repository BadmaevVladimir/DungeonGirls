using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class TavernForgeBackendTests
{
    readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (var value in created) Object.DestroyImmediate(value);
    }

    DishRecipeData Recipe(int cost = 2)
    {
        var recipe = ScriptableObject.CreateInstance<DishRecipeData>();
        created.Add(recipe);
        recipe.recipeId = "recipe_stew";
        recipe.dishId = "dish_stew";
        recipe.ingredients.Add(new ResourceAmount(PersistentResourceIds.RawMeat, cost));
        recipe.ingredients.Add(new ResourceAmount(PersistentResourceIds.Grain, 1));
        return recipe;
    }

    ForgeBlueprintData Blueprint()
    {
        var blueprint = ScriptableObject.CreateInstance<ForgeBlueprintData>();
        created.Add(blueprint);
        blueprint.itemPrototype = ScriptableObject.CreateInstance<ItemData>();
        created.Add(blueprint.itemPrototype);
        blueprint.blueprintId = "blueprint_sword";
        blueprint.prototypeId = "prototype_sword";
        blueprint.itemPrototype.prototypeId = blueprint.prototypeId;
        blueprint.materialCost.Add(new ResourceAmount(PersistentResourceIds.TemperedSteel, 2));
        return blueprint;
    }

    [Test]
    public void ResourceInventory_UsesAtomicMultiCostAndRaisesChangeNotification()
    {
        var entries = new List<KeyCountEntry>
        {
            new KeyCountEntry { key = PersistentResourceIds.RawMeat, count = 3 },
            new KeyCountEntry { key = PersistentResourceIds.Grain, count = 0 }
        };
        var inventory = new ResourceInventory(entries);
        int changes = 0;
        inventory.Changed += () => changes++;
        var cost = new[]
        {
            new ResourceAmount(PersistentResourceIds.RawMeat, 2),
            new ResourceAmount(PersistentResourceIds.Grain, 1)
        };
        Assert.IsFalse(inventory.TrySpend(cost));
        Assert.AreEqual(3, inventory.GetAmount(PersistentResourceIds.RawMeat));
        Assert.AreEqual(0, changes);
        Assert.IsTrue(inventory.Add(PersistentResourceIds.Grain, 1));
        Assert.IsTrue(inventory.TrySpend(cost));
        Assert.AreEqual(1, inventory.GetAmount(PersistentResourceIds.RawMeat));
        Assert.AreEqual(0, inventory.GetAmount(PersistentResourceIds.Grain));
        Assert.AreEqual(2, changes);
    }

    [Test]
    public void ClosedRecipe_CannotCook()
    {
        var data = new SaveData();
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.RawMeat, count = 9 });
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.Grain, count = 9 });
        Assert.IsFalse(new TavernService(data, access: new CatalogUnlockPolicy(false)).TryCook(Recipe()));
    }

    [Test]
    public void MissingIngredient_DoesNotPartiallySpend()
    {
        var data = new SaveData();
        data.unlockedTavernRecipes.Add("recipe_stew");
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.RawMeat, count = 5 });
        Assert.IsFalse(new TavernService(data, access: new CatalogUnlockPolicy(false)).TryCook(Recipe()));
        Assert.AreEqual(5, data.resources[0].count);
    }

    [Test]
    public void SuccessfulCooking_AtomicallySpendsAndAddsPortions()
    {
        var data = new SaveData();
        data.unlockedTavernRecipes.Add("recipe_stew");
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.RawMeat, count = 5 });
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.Grain, count = 1 });
        int saves = 0;
        Assert.IsTrue(new TavernService(data, () => saves++, new CatalogUnlockPolicy(false)).TryCook(Recipe()));
        Assert.AreEqual(3, data.resources[0].count);
        Assert.AreEqual(0, data.resources[1].count);
        Assert.AreEqual(1, data.preparedDishes[0].count);
        Assert.AreEqual(1, saves);
    }

    [Test]
    public void TavernLevel_BlocksHigherRecipeWithoutSpending()
    {
        var data = new SaveData();
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.RawMeat, count = 5 });
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.Grain, count = 2 });
        var recipe = Recipe();
        recipe.requiredTavernLevel = 2;
        Assert.IsFalse(new TavernService(data).TryCook(recipe, 1));
        Assert.AreEqual(5, data.resources[0].count);
    }

    [Test]
    public void ApprovedCatalogsContainExactlyFifteenRecipesAndSixBlueprints()
    {
        Assert.AreEqual(15, FoodRecipeCatalog.All.Count);
        Assert.AreEqual(6, ForgeBlueprintCatalog.All.Count);
        foreach (var recipe in FoodRecipeCatalog.All) Assert.AreEqual(3, recipe.durationRooms);
    }

    [Test]
    public void ForgeCraftDoesNotGrantItemAndCanUseStableItemIdWithoutAsset()
    {
        var data = new SaveData();
        var blueprint = Blueprint();
        blueprint.itemPrototype = null;
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.TemperedSteel, count = 2 });
        Assert.IsTrue(new ForgeService(data).TryResearch(blueprint));
        CollectionAssert.Contains(data.researchedItemPrototypes, blueprint.prototypeId);
    }

    [Test]
    public void ClosedBlueprintCannotResearch_AndRepeatDoesNotSpend()
    {
        var data = new SaveData();
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.TemperedSteel, count = 5 });
        var blueprint = Blueprint();
        var policy = new CatalogUnlockPolicy(false);
        var forge = new ForgeService(data, access: policy);
        Assert.IsFalse(forge.TryResearch(blueprint));
        data.unlockedForgeBlueprints.Add(blueprint.blueprintId);
        Assert.IsTrue(forge.TryResearch(blueprint));
        Assert.AreEqual(3, data.resources[0].count);
        Assert.IsTrue(forge.TryResearch(blueprint));
        Assert.AreEqual(3, data.resources[0].count);
    }

    [Test]
    public void TestOverride_DoesNotMutateSavedUnlockState()
    {
        var data = new SaveData();
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.RawMeat, count = 5 });
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.Grain, count = 5 });
        var policy = new CatalogUnlockPolicy(true);
        var service = new TavernService(data, access: policy);
        Assert.IsTrue(service.CanCook(Recipe()));
        Assert.IsEmpty(data.unlockedTavernRecipes);
        policy.UnlockAllForTesting = false;
        Assert.IsFalse(service.CanCook(Recipe()));
    }

    [Test]
    public void ResearchedPrototypeJoinsLootPoolWithoutRemovingLegacyItems()
    {
        var host = new GameObject("reward-manager");
        created.Add(host);
        var manager = host.AddComponent<RewardManager>();
        var legacy = ScriptableObject.CreateInstance<ItemData>();
        var prototype = ScriptableObject.CreateInstance<ItemData>();
        var catalog = ScriptableObject.CreateInstance<ItemCatalogData>();
        created.Add(legacy); created.Add(prototype); created.Add(catalog);
        prototype.prototypeId = "prototype_sword";
        catalog.items = new[] { legacy, prototype };
        manager.SetItemCatalog(catalog);
        manager.SetPrototypeProgression(new List<string>(), false);
        CollectionAssert.AreEqual(new[] { legacy }, manager.GetCompatibleLootItems(null));
        manager.SetPrototypeProgression(new List<string> { "prototype_sword" }, false);
        CollectionAssert.AreEquivalent(new[] { legacy, prototype }, manager.GetCompatibleLootItems(null));
    }

    [Test]
    public void PrototypeAssetsReuseEpicArchetypeStatsAndApprovedClassRestrictions()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalogData>(
            "Assets/ScriptableObjects/Items/ItemCatalog.asset");
        Assert.IsNotNull(catalog);
        ItemData Find(string id) => System.Array.Find(catalog.items, item => item != null && item.prototypeId == id);

        var scimitar = Find("prototype_resonance_scimitar");
        var spellEater = Find("prototype_spell_eater");
        var dayNight = Find("prototype_day_and_night");
        var lastArgument = Find("prototype_last_argument_prototype");
        Assert.IsNotNull(scimitar); Assert.IsNotNull(spellEater);
        Assert.IsNotNull(dayNight); Assert.IsNotNull(lastArgument);
        Assert.AreEqual(ItemTier.Epic, scimitar.tier);
        Assert.IsTrue(ItemCatalogData.IsAllowedForClass(scimitar, CharacterClass.Warrior));
        Assert.IsTrue(ItemCatalogData.IsAllowedForClass(scimitar, CharacterClass.Rogue));
        Assert.IsTrue(ItemCatalogData.IsAllowedForClass(scimitar, CharacterClass.Barbarian));
        Assert.IsTrue(ItemCatalogData.IsAllowedForClass(dayNight, CharacterClass.Rogue));
        Assert.IsFalse(ItemCatalogData.IsAllowedForClass(dayNight, CharacterClass.Warrior));
        Assert.IsTrue(ItemCatalogData.IsAllowedForClass(lastArgument, CharacterClass.Barbarian));
        Assert.IsFalse(ItemCatalogData.IsAllowedForClass(lastArgument, CharacterClass.Rogue));
        Assert.AreEqual(DamageType.Physical, spellEater.damageType);

        var epicSword = AssetDatabase.LoadAssetAtPath<ItemData>(
            "Assets/ScriptableObjects/Items/Weapons/Sword/Item_Sword_Epic_BloodSword.asset");
        var epicAxe = AssetDatabase.LoadAssetAtPath<ItemData>(
            "Assets/ScriptableObjects/Items/Weapons/Axe/Item_Axe_Epic_Rubilo.asset");
        Assert.AreEqual(epicSword.baseDamage, scimitar.baseDamage);
        Assert.AreEqual(epicSword.attackSpeed, scimitar.attackSpeed);
        Assert.AreEqual(epicAxe.baseDamage, spellEater.baseDamage);
        Assert.AreEqual(epicAxe.attackSpeed, spellEater.attackSpeed);
    }

    [Test]
    public void RealPrototypeCatalogItemsAreGatedUntilCrafted()
    {
        var host = new GameObject("prototype-gating"); created.Add(host);
        var manager = host.AddComponent<RewardManager>();
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalogData>(
            "Assets/ScriptableObjects/Items/ItemCatalog.asset");
        manager.SetItemCatalog(catalog);
        manager.SetPrototypeProgression(new List<string>(), false);
        Assert.IsFalse(manager.GetCompatibleLootItems(CharacterClass.Rogue)
            .Exists(item => item.prototypeId == "prototype_day_and_night"));
        manager.SetPrototypeProgression(new List<string> { "prototype_day_and_night" }, false);
        Assert.IsTrue(manager.GetCompatibleLootItems(CharacterClass.Rogue)
            .Exists(item => item.prototypeId == "prototype_day_and_night"));
    }
}
