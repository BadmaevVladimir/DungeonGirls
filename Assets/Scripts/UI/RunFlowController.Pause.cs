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
            $"Этаж: {dungeonManager.CurrentFloorNumber}/{DungeonManager.TotalFloors} | Валюта забега: {characterManager.RunCurrency} | Рационы: {campManager.RationsRemaining}";

        RefreshPauseStatsGrid(combatant);

        pauseSkillsScrollView.Clear();
        var uniquePassive = characterManager.Character.uniquePassiveSkill;
        AddPauseLine(pauseSkillsScrollView, $"Уникальный пассив: {uniquePassive.skillName} — ур. {progress.UniquePassiveLevel}", uniquePassive.skillName, uniquePassive.effectDescription);
        var uniqueActive = characterManager.Character.uniqueActiveSkill;
        AddPauseLine(pauseSkillsScrollView, $"Уникальный активный: {uniqueActive.skillName} — ур. {progress.UniqueActiveLevel}", uniqueActive.skillName, uniqueActive.effectDescription);
        if (!string.IsNullOrWhiteSpace(progress.MentorUniquePassiveSkillName))
        {
            AddPauseLine(pauseSkillsScrollView, $"Пассив наставника: {progress.MentorUniquePassiveSkillName} — ур. {progress.MentorUniquePassiveLevel}");
        }
        foreach (var pair in progress.KnownSkillLevels.OrderBy(pair => pair.Key != null ? pair.Key.skillName : string.Empty))
        {
            if (pair.Key != null) AddPauseLine(pauseSkillsScrollView, $"{pair.Key.skillName} — ур. {pair.Value}", pair.Key.skillName, pair.Key.effectDescription);
        }

        RefreshPauseEquipmentGrid();
    }

    void RefreshPauseStatsGrid(CombatantRuntime combatant)
    {
        pauseStatsGrid.Clear();
        AddPauseStatRow(pauseStatsGrid, "HP", $"{combatant.CurrentHP:F0}/{combatant.MaxHP:F0}");
        AddPauseStatRow(pauseStatsGrid, "Физ. защита", $"{combatant.PhysicalDefenseCurrent:F0}/{combatant.PhysicalDefenseMax:F0}");
        AddPauseStatRow(pauseStatsGrid, "Маг. щит", $"{combatant.MagicShieldCurrent:F0}/{combatant.MagicShieldMax:F0}");
        AddPauseStatRow(pauseStatsGrid, "Ярость", $"{combatant.Rage:F0}%");
        AddPauseStatRow(pauseStatsGrid, "Крит. шанс", $"{CombatCriticalRules.CalculateChancePercent(combatant):F0}%");
        AddPauseStatRow(pauseStatsGrid, "Уклонение", $"{CombatEvasionRules.CalculateChancePercent(combatant):F0}%");
        AddPauseStatRow(pauseStatsGrid, "Скорость атаки", $"+{combatant.GetPositiveAttackSpeedBonusPercent():F0}%");
        AddPauseStatRow(pauseStatsGrid, "Урон", $"+{Mathf.Max(0f, combatant.ItemDamageBonusPercent + combatant.FoodDamagePercent):F0}%");
        AddPauseStatRow(pauseStatsGrid, "Физ. сопротивление", $"{combatant.PhysicalResistancePercent:F0}%");
        AddPauseStatRow(pauseStatsGrid, "Маг. сопротивление", $"{combatant.MagicalResistancePercent:F0}%");
    }

    // Экран экипировки в стиле "герой в центре, слоты вокруг" (см. брейнсторм 2026-09-04):
    // портрет посередине, по 4 слота слева и справа. Оружие/кольца — до 2 предметов на слот.
    void RefreshPauseEquipmentGrid()
    {
        var equipped = characterManager.EquippedItems;
        ItemData helmet = equipped.Find(i => i != null && i.slot == EquipmentSlot.Helmet);
        ItemData armor = equipped.Find(i => i != null && i.slot == EquipmentSlot.Armor);
        ItemData boots = equipped.Find(i => i != null && i.slot == EquipmentSlot.Boots);
        ItemData accessory = equipped.Find(i => i != null && i.slot == EquipmentSlot.Accessory);
        var weapons = equipped.FindAll(i => i != null && i.slot == EquipmentSlot.Weapon);
        var rings = equipped.FindAll(i => i != null && i.slot == EquipmentSlot.Ring);

        pauseEquipmentGrid.Clear();

        var leftColumn = new VisualElement();
        leftColumn.AddToClassList("equipment-slot-column");
        leftColumn.AddToClassList("equipment-slot-column-left");
        leftColumn.Add(BuildEquipmentSlot(weapons.ElementAtOrDefault(0)));
        leftColumn.Add(BuildEquipmentSlot(rings.ElementAtOrDefault(0)));
        leftColumn.Add(BuildEquipmentSlot(helmet));
        leftColumn.Add(BuildEquipmentSlot(armor));
        pauseEquipmentGrid.Add(leftColumn);

        var portrait = new VisualElement();
        portrait.AddToClassList("equipment-portrait");
        var portraitSprite = characterManager.Character.portrait;
        if (portraitSprite != null)
        {
            var portraitImage = new Image { sprite = portraitSprite, scaleMode = ScaleMode.ScaleToFit };
            portraitImage.AddToClassList("equipment-portrait-image");
            portrait.Add(portraitImage);
        }
        pauseEquipmentGrid.Add(portrait);

        var rightColumn = new VisualElement();
        rightColumn.AddToClassList("equipment-slot-column");
        rightColumn.AddToClassList("equipment-slot-column-right");
        rightColumn.Add(BuildEquipmentSlot(weapons.ElementAtOrDefault(1)));
        rightColumn.Add(BuildEquipmentSlot(rings.ElementAtOrDefault(1)));
        rightColumn.Add(BuildEquipmentSlot(accessory));
        rightColumn.Add(BuildEquipmentSlot(boots));
        pauseEquipmentGrid.Add(rightColumn);
    }

    VisualElement BuildEquipmentSlot(ItemData item)
    {
        var slot = new VisualElement();
        slot.AddToClassList("equipment-slot");
        if (item != null)
        {
            if (item.icon != null)
            {
                var icon = new Image { sprite = item.icon, scaleMode = ScaleMode.ScaleToFit };
                icon.AddToClassList("equipment-slot-icon");
                slot.Add(icon);
            }
            tutorialManager?.BindTooltip(slot, $"{DisplayFormat.SlotLabel(item)}: {item.itemName}", DisplayFormat.ItemStatsText(item));
        }
        return slot;
    }

    void AddPauseLine(ScrollView container, string text, string tooltipTitle = null, string tooltipBody = null)
    {
        var line = new Label(text);
        line.AddToClassList("body-label");
        container.Add(line);
        if (!string.IsNullOrWhiteSpace(tooltipBody))
        {
            tutorialManager?.BindTooltip(line, tooltipTitle ?? text, tooltipBody);
        }
    }

    static void AddPauseStatRow(VisualElement container, string label, string value)
    {
        var row = new VisualElement();
        row.AddToClassList("pause-stat-row");
        var labelElement = new Label(label);
        labelElement.AddToClassList("pause-stat-row-label");
        row.Add(labelElement);
        var valueElement = new Label(value);
        valueElement.AddToClassList("pause-stat-row-value");
        row.Add(valueElement);
        container.Add(row);
    }
}
