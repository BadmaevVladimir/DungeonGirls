using System.Collections.Generic;
using NUnit.Framework;

public class SaveManagerMigrationTests
{
    [Test]
    public void MigrateIfNeeded_NullCollections_BecomeEmptyLists()
    {
        var data = new SaveData
        {
            veteranDeck = null,
            gachaOwnedCharacters = null,
            characterRunCounts = null,
            seenVNScenes = null,
            relationshipPoints = null,
            seenTutorialHints = null
        };

        SaveManager.MigrateIfNeeded(data);

        Assert.IsNotNull(data.veteranDeck);
        Assert.IsNotNull(data.gachaOwnedCharacters);
        Assert.IsNotNull(data.characterRunCounts);
        Assert.IsNotNull(data.seenVNScenes);
        Assert.IsNotNull(data.relationshipPoints);
        Assert.IsNotNull(data.seenTutorialHints);
    }

    [Test]
    public void MigrateIfNeeded_LegacyRogueKey_MergedIntoVioletId()
    {
        var data = new SaveData
        {
            gachaOwnedCharacters = new List<KeyCountEntry>
            {
                new KeyCountEntry { key = "rogue", count = 2 },
                new KeyCountEntry { key = "violet", count = 1 }
            }
        };

        SaveManager.MigrateIfNeeded(data);

        var violetEntries = data.gachaOwnedCharacters.FindAll(e => e.key == "violet");
        Assert.AreEqual(1, violetEntries.Count);
        Assert.AreEqual(3, violetEntries[0].count);
    }

    [Test]
    public void MigrateIfNeeded_AlwaysGrantsAtLeastOneJenniferCopy()
    {
        var data = new SaveData { gachaOwnedCharacters = new List<KeyCountEntry>() };

        SaveManager.MigrateIfNeeded(data);

        var jennifer = data.gachaOwnedCharacters.Find(e => e.key == "jennifer");
        Assert.IsNotNull(jennifer);
        Assert.GreaterOrEqual(jennifer.count, 1);
    }

    [Test]
    public void MigrateIfNeeded_SetsCurrentSaveVersion()
    {
        var data = new SaveData();

        SaveManager.MigrateIfNeeded(data);

        Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
    }

    [Test]
    public void NewSaveData_RoundTripsPersistentProgression()
    {
        var data = new SaveData();
        data.resources.Add(new KeyCountEntry { key = PersistentResourceIds.Grain, count = 4 });
        data.unlockedTavernRecipes.Add("recipe_stew");
        data.unlockedForgeBlueprints.Add("blueprint_sword");
        data.researchedItemPrototypes.Add("prototype_sword");
        data.preparedDishes.Add(new KeyCountEntry { key = "dish_stew", count = 2 });
        var loaded = UnityEngine.JsonUtility.FromJson<SaveData>(UnityEngine.JsonUtility.ToJson(data));
        Assert.AreEqual(4, loaded.resources[0].count);
        Assert.AreEqual("recipe_stew", loaded.unlockedTavernRecipes[0]);
        Assert.AreEqual("prototype_sword", loaded.researchedItemPrototypes[0]);
        Assert.AreEqual(2, loaded.preparedDishes[0].count);
    }

    [Test]
    public void Migration_NormalizesNullDuplicatesAndNegativeCountsWithoutLosingLegacyFields()
    {
        const string oldJson = "{\"saveVersion\":6,\"metaCurrency\":123,\"forgeLevel\":4," +
            "\"resources\":[{\"key\":\"grain\",\"count\":-3},{\"key\":\"grain\",\"count\":5}]," +
            "\"preparedDishes\":null,\"unlockedTavernRecipes\":[\"stew\",\"stew\",\"\"]}";
        var data = UnityEngine.JsonUtility.FromJson<SaveData>(oldJson);
        SaveManager.MigrateIfNeeded(data);
        Assert.AreEqual(123, data.metaCurrency);
        Assert.AreEqual(4, data.forgeLevel);
        Assert.AreEqual(1, data.resources.Count);
        Assert.AreEqual(5, data.resources[0].count);
        Assert.IsNotNull(data.preparedDishes);
        CollectionAssert.AreEqual(new[] { "stew" }, data.unlockedTavernRecipes);
    }
}
