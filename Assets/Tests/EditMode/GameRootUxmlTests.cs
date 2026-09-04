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

    [Test]
    public void ResultsScreen_HasVeteranAttestationCeremonyControls()
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameRoot.uxml");
        var root = asset.CloneTree();
        Assert.IsNotNull(root.Q<VisualElement>("ResultsAttestationPanel"));
        Assert.IsNotNull(root.Q<Label>("ResultsAttestationStageLabel"));
        Assert.IsNotNull(root.Q<Label>("ResultsFinalRankLabel"));
        Assert.IsNotNull(root.Q<Button>("ResultsSkipButton"));
    }

    [Test]
    public void SettingsScreen_HasVolumeControlsAndIsReachableFromMenuAndPause()
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameRoot.uxml");
        var root = asset.CloneTree();

        Assert.IsNotNull(root.Q<VisualElement>("SettingsScreen"));
        Assert.IsNotNull(root.Q<Slider>("MasterVolumeSlider"));
        Assert.IsNotNull(root.Q<Label>("MasterVolumeValueLabel"));
        Assert.IsNotNull(root.Q<Button>("SettingsCloseButton"));
        Assert.IsNotNull(root.Q<Button>("SettingsButton"), "Главное меню должно открывать настройки.");
        Assert.IsNotNull(root.Q<Button>("PauseSettingsButton"), "Меню паузы должно открывать настройки.");
    }
}
