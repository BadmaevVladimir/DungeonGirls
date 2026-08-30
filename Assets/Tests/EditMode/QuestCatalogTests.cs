using NUnit.Framework;

public class QuestCatalogTests
{
    [Test]
    public void PickForFloor_Floor1_ReturnsSphinx()
    {
        // Floor 1 is below the hunt-quest eligibility floor (2+), so hunt can never roll here.
        var quest = QuestCatalog.PickForFloor(1, huntAlreadyTriggered: false, swordAlreadySucceeded: false);
        Assert.AreEqual(QuestCatalog.Sphinx, quest);
    }

    [Test]
    public void PickForFloor_Floor2_HuntAlreadyTriggered_NeverReturnsHunt()
    {
        for (int i = 0; i < 20; i++)
        {
            var quest = QuestCatalog.PickForFloor(2, huntAlreadyTriggered: true, swordAlreadySucceeded: false);
            Assert.AreNotEqual(QuestCatalog.Hunt, quest);
        }
    }

    [Test]
    public void PickForFloor_HighFloor_SwordAlreadySucceeded_ReturnsFairyRingNotSword()
    {
        for (int i = 0; i < 20; i++)
        {
            var quest = QuestCatalog.PickForFloor(5, huntAlreadyTriggered: true, swordAlreadySucceeded: true);
            Assert.AreNotEqual(QuestCatalog.SwordInStone, quest);
        }
    }
}
