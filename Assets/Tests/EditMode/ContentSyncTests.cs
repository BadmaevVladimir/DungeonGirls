using System.IO;
using NUnit.Framework;

public class ContentSyncTests
{
    [Test]
    public void W10_CombatRewardText_MatchesCurrentChestRules()
    {
        Assert.IsTrue(TutorialContent.TryGet(TutorialContent.Reward, out var reward));
        StringAssert.Contains("вероятностью 50%", reward.Body);
        StringAssert.Contains("Босс гарантирует сундук", reward.Body);
        StringAssert.Contains("вероятностью 50%", TutorialContent.RoomTypeHint(RoomType.Combat));
    }

    [Test]
    public void W10_CampText_ExplainsRationDishCostAndActivationTiming()
    {
        Assert.IsTrue(TutorialContent.TryGet(TutorialContent.Camp, out var camp));
        StringAssert.Contains("не заменяет рацион", camp.Body);
        StringAssert.Contains("после лечения текущего привала", camp.Body);
        StringAssert.Contains("следующие 3 комнаты", camp.Body);
        StringAssert.Contains("тратится дополнительно", TutorialContent.TooltipRations);
    }

    [Test]
    public void W10_VeteranGradeTooltip_DoesNotDeriveRankFromFloorCount()
    {
        StringAssert.Contains("боевые испытания", TutorialContent.TooltipGrade);
        StringAssert.Contains("Число этажей само по себе ранг не задаёт", TutorialContent.TooltipGrade);
        StringAssert.DoesNotContain("C− за", TutorialContent.TooltipGrade);
    }

    [Test]
    public void W10_SceneCatalog_ListsEveryRuntimeJsonIdAndNoRetiredAliases()
    {
        const string sceneDirectory = "Assets/StreamingAssets/Content/Scenes";
        const string catalogPath = "Docs/Narrative/SceneCatalog.md";
        string catalog = File.ReadAllText(catalogPath);
        string[] sceneFiles = Directory.GetFiles(sceneDirectory, "*.json");

        Assert.AreEqual(15, sceneFiles.Length, "Если добавилась сцена, каталог и ожидаемое число нужно обновить вместе.");
        foreach (string sceneFile in sceneFiles)
        {
            string id = Path.GetFileNameWithoutExtension(sceneFile);
            StringAssert.Contains($"`{id}`", catalog, $"SceneCatalog не содержит {id}.");
        }

        StringAssert.DoesNotContain("`jennifer_campfire`", catalog);
        StringAssert.DoesNotContain("`jennifer_hot_springs`", catalog);
    }
}
