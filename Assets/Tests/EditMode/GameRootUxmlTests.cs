using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public class GameRootUxmlTests
{
    [Test]
    public void GameRoot_HasSkillPanelContainer_NotOldCombatControlsRow()
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameRoot.uxml");
        var root = asset.CloneTree();

        Assert.IsNotNull(root.Q<VisualElement>("SkillPanelContainer"));
        Assert.IsNull(root.Q<Toggle>("AutoModeToggle"));
        Assert.IsNull(root.Q<Button>("ActiveSkillButton"));
        Assert.IsNull(root.Q<Toggle>("BerserkToggle"));
    }
}
