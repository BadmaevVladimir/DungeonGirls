using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public class PostCombatFlowSmokeTests
{
    [Test]
    public void LootSummaryUi_HasRequiredElements()
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameRoot.uxml");
        var root = asset.CloneTree();
        Assert.IsNotNull(root.Q<VisualElement>("LootSummaryContainer"));
        Assert.IsNotNull(root.Q<VisualElement>("LootSummaryRows"));
        Assert.IsNotNull(root.Q<Button>("LootSummaryContinueButton"));
    }

    [Test]
    public void IntegratedFlow_OrdersSummaryBeforeOptionalChestBeforeLevelUp()
    {
        string source = File.ReadAllText("Assets/Scripts/UI/RunFlowController.MapContent.cs");
        int summary = source.IndexOf("ShowLootSummaryFlow(pendingRoomRewardGrant.Result)");
        int chest = source.IndexOf("ShowResolvedRewardChestFlow(pendingRoomRewardGrant.Result.Chest)");
        int levelUp = source.IndexOf("LevelUpFlow(activeUpgradeNotice)");
        Assert.That(summary, Is.GreaterThan(0));
        Assert.That(chest, Is.GreaterThan(summary));
        Assert.That(levelUp, Is.GreaterThan(chest));
        StringAssert.Contains("if (pendingRoomRewardGrant.Result.HasChest)", source);
    }
}
