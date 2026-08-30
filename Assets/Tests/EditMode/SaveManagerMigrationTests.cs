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
}
