using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    void PauseRun()
    {
        if (isRunPaused || characterManager == null || characterManager.Character == null) return;
        RefreshPauseInfo();
        isRunPaused = true;
        Time.timeScale = 0f;
        pauseScreen.style.display = DisplayStyle.Flex;
        tutorialManager?.QueueOnce(TutorialContent.Pause);
    }

    void ResumeRun()
    {
        if (!isRunPaused) return;
        isRunPaused = false;
        Time.timeScale = 1f;
        pauseScreen.style.display = DisplayStyle.None;
    }

    void AbandonRunFromPause()
    {
        if (!isRunPaused) return;
        ResumeRun();
        combatManager.AbortCombat();
        UnsubscribeCombatEvents();
        StopAllCoroutines();
        dungeonManager.SetRunState(RunState.RunFailed);
        StartCoroutine(ShowResultsFlow(false));
    }

    void QuitGame()
    {
        Time.timeScale = 1f;
        Application.Quit();
    }

    void RefreshPauseInfo()
    {
        var combatant = characterManager.Combatant;
        var progress = characterManager.Progress;
        pauseCharacterStatsLabel.text = $"{characterManager.Character.characterName} ({DisplayFormat.CharacterClassDisplayName(characterManager.Character.characterClass)}) — уровень {characterManager.Level}\n" +
            $"HP: {combatant.CurrentHP:F0}/{combatant.MaxHP:F0}\n" +
            $"Физ. защита: {combatant.PhysicalDefenseCurrent:F0}/{combatant.PhysicalDefenseMax:F0} | Магический щит: {combatant.MagicShieldCurrent:F0}/{combatant.MagicShieldMax:F0}\n" +
            $"Этаж: {dungeonManager.CurrentFloorNumber}/{DungeonManager.TotalFloors} | Валюта забега: {characterManager.RunCurrency} | Рационы: {campManager.RationsRemaining}";

        pauseSkillsScrollView.Clear();
        AddPauseLine(pauseSkillsScrollView, $"Уникальный пассив: {characterManager.Character.uniquePassiveSkill.skillName} — ур. {progress.UniquePassiveLevel}");
        AddPauseLine(pauseSkillsScrollView, $"Уникальный активный: {characterManager.Character.uniqueActiveSkill.skillName} — ур. {progress.UniqueActiveLevel}");
        if (!string.IsNullOrWhiteSpace(progress.MentorUniquePassiveSkillName))
        {
            AddPauseLine(pauseSkillsScrollView, $"Пассив наставника: {progress.MentorUniquePassiveSkillName} — ур. {progress.MentorUniquePassiveLevel}");
        }
        foreach (var pair in progress.KnownSkillLevels.OrderBy(pair => pair.Key != null ? pair.Key.skillName : string.Empty))
        {
            if (pair.Key != null) AddPauseLine(pauseSkillsScrollView, $"{pair.Key.skillName} — ур. {pair.Value}");
        }

        pauseEquipmentScrollView.Clear();
        foreach (var item in characterManager.EquippedItems)
        {
            if (item != null) AddPauseLine(pauseEquipmentScrollView, $"{DisplayFormat.SlotLabel(item)}: {item.itemName} (ур. {item.itemLevel})\n{DisplayFormat.ItemStatsText(item)}");
        }
        if (pauseEquipmentScrollView.childCount == 0) AddPauseLine(pauseEquipmentScrollView, "Нет снаряжения.");
    }

    static void AddPauseLine(ScrollView container, string text)
    {
        var line = new Label(text);
        line.AddToClassList("body-label");
        container.Add(line);
    }
}
