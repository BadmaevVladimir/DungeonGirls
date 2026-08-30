using UnityEngine;
using UnityEngine.UIElements;

public partial class HubManager
{
    // ==================== Навигация (7.1) ====================

    public void OpenVillage()
    {
        buildingsScreen.style.display = DisplayStyle.None;
        gachaScreen.style.display = DisplayStyle.None;
        veteranDeckScreen.style.display = DisplayStyle.None;
        charactersScreen.style.display = DisplayStyle.None;
        CloseCheatMenu();
        mainMenuScreen.style.display = DisplayStyle.Flex;
    }

    void OpenCheatMenu()
    {
        cheatCommandField.value = string.Empty;
        cheatResultLabel.text = string.Empty;
        cheatResultLabel.AddToClassList("hidden");
        cheatMenuPopup.style.display = DisplayStyle.Flex;
        cheatCommandField.Focus();
    }

    void CloseCheatMenu()
    {
        if (cheatMenuPopup != null)
        {
            cheatMenuPopup.style.display = DisplayStyle.None;
        }
    }

    void SubmitCheatCommand()
    {
        if (string.Equals(cheatCommandField.value?.Trim(), GreedIsGoodCheat, System.StringComparison.OrdinalIgnoreCase))
        {
            saveManager.AddDebugCurrencies(GreedIsGoodReward, GreedIsGoodReward);
            cheatResultLabel.text = $"Получено: +{GreedIsGoodReward} мета-валюты и +{GreedIsGoodReward} гача-валюты.";
            cheatCommandField.value = string.Empty;
            RefreshBuildingsScreen();
            RefreshGachaScreen();
        }
        else
        {
            cheatResultLabel.text = "Неизвестная команда.";
        }

        cheatResultLabel.RemoveFromClassList("hidden");
    }

    void QuitGame()
    {
        Application.Quit();
    }

    public void OpenBuildings()
    {
        RefreshBuildingsScreen();
        mainMenuScreen.style.display = DisplayStyle.None;
        buildingsScreen.style.display = DisplayStyle.Flex;
        tutorialManager?.QueueOnce(TutorialContent.Buildings);
    }

    public void OpenGacha()
    {
        RefreshGachaScreen();
        mainMenuScreen.style.display = DisplayStyle.None;
        gachaScreen.style.display = DisplayStyle.Flex;
        tutorialManager?.QueueOnce(TutorialContent.Gacha);
    }

    public void OpenVeteranDeck()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        veteranDeckScreen.style.display = DisplayStyle.Flex;
        veteranDeckScrollView.Clear();

        foreach (var veteran in saveManager.Data.veteranDeck)
        {
            if (veteran == null) continue;
            string displayName = CharacterDisplayName(veteran.characterId);
            int skillCount = veteran.finalSkills != null ? veteran.finalSkills.Count : 0;
            int equipmentCount = veteran.finalEquipmentSnapshot != null && veteran.finalEquipmentSnapshot.Count > 0
                ? veteran.finalEquipmentSnapshot.Count
                : (veteran.finalEquipment != null ? veteran.finalEquipment.Count : 0);
            var row = new Label($"{displayName} — {veteran.grade}, этажей {veteran.floorsCleared}, HP {veteran.finalHP:F0}, неуникальных навыков {skillCount}, снаряжения {equipmentCount}");
            row.AddToClassList("body-label");
            tutorialManager?.BindTooltip(row, "Оценка ветерана", TutorialContent.TooltipGrade);
            veteranDeckScrollView.Add(row);
        }

        if (veteranDeckScrollView.childCount == 0)
        {
            var empty = new Label("Пока нет завершённых забегов.");
            empty.AddToClassList("body-label");
            veteranDeckScrollView.Add(empty);
        }
        tutorialManager?.QueueOnce(TutorialContent.Veterans);
    }

    public void OpenCharacters()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        charactersScreen.style.display = DisplayStyle.Flex;
        charactersScrollView.Clear();

        foreach (var character in gachaCharacters)
        {
            if (character == null) continue;
            int copies = saveManager.GetCharacterCopies(character.characterId);
            if (copies <= 0) continue; // 7.1: экран содержит полученных в гаче персонажей.

            int runs = saveManager.GetRunCount(character.characterId);
            int relationshipPoints = saveManager.GetRelationshipPoints(character.characterId);
            int relationshipLevel = saveManager.GetRelationshipLevel(character.characterId);
            int nextThreshold = saveManager.GetRelationshipNextThreshold(character.characterId);
            var sceneEntry = saveManager.Data.seenVNScenes.Find(entry => entry != null && entry.characterId == character.characterId);
            int seenScenes = sceneEntry != null && sceneEntry.sceneIds != null ? sceneEntry.sceneIds.Count : 0;
            string relationship = relationshipLevel >= SaveManager.MaxRelationshipLevel
                ? $"отношения: ур. {relationshipLevel}/3 (макс.)"
                : $"отношения: ур. {relationshipLevel}/3, {relationshipPoints}/{nextThreshold}";
            var row = new Label($"{character.characterName} ({character.characterClass}) — копий: {copies}, прохождений: {runs}, {relationship}, открытых сцен: {seenScenes}");
            row.AddToClassList("body-label");
            tutorialManager?.BindTooltip(row, "Отношения", TutorialContent.TooltipRelationships);
            charactersScrollView.Add(row);
        }

        if (charactersScrollView.childCount == 0)
        {
            var empty = new Label("Полученных в гаче персонажей пока нет.");
            empty.AddToClassList("body-label");
            charactersScrollView.Add(empty);
        }
        tutorialManager?.QueueOnce(TutorialContent.Characters);
    }

    void BindTutorialTooltips()
    {
        if (tutorialManager == null) return;
        tutorialManager.BindTooltip(metaCurrencyLabel, "Мета-валюта", TutorialContent.TooltipMetaCurrency);
        tutorialManager.BindTooltip(gachaCurrencyLabel, "Гача-валюта", TutorialContent.TooltipGachaCurrency);
        tutorialManager.BindTooltip(gachaPullButton, "Призыв", "Стоит 50 гача-валюты. Шанс персонажа — 15%; pity-системы в демо нет.");
        tutorialManager.BindTooltip(buildingBonusLabels[0], "Бонус Кузницы", () => CurrentBuildingBonusText(BuildingType.Forge));
        tutorialManager.BindTooltip(buildingBonusLabels[1], "Бонус Храма", () => CurrentBuildingBonusText(BuildingType.Temple));
        tutorialManager.BindTooltip(buildingBonusLabels[2], "Бонус Таверны", () => CurrentBuildingBonusText(BuildingType.Tavern));
    }

    string CurrentBuildingBonusText(BuildingType building)
    {
        int level = saveManager.GetBuildingLevel(building);
        string active;
        switch (building)
        {
            case BuildingType.Forge:
                active = $"стартовое снаряжение +{BuildingCatalog.ForgeStartingEquipmentBonus(level)} ур.; " +
                         $"плоская броня +{BuildingCatalog.ForgeArmorBonus(level):F0}; " +
                         $"броня снаряжения ×{BuildingCatalog.ForgeEquipmentArmorMultiplier(level):F2}; " +
                         $"восстановление брони на привале {BuildingCatalog.ForgeCampArmorRestorePercent(level):F0}%";
                break;
            case BuildingType.Temple:
                active = $"магический щит +{BuildingCatalog.TempleMagicShieldBonus(level):F0}; " +
                         $"общих перебросов навыков: {BuildingCatalog.TempleLevelUpRerolls(level)}; " +
                         "перезапуск после смерти на 5 уровне пока не реализован";
                break;
            case BuildingType.Tavern:
                active = $"дополнительных рационов: {BuildingCatalog.TavernRationsBonus(level)}; " +
                         $"урон каждого оружия +{BuildingCatalog.TavernFlatDamageBonus(level):F0}; " +
                         $"лечение на привале +{BuildingCatalog.TavernCampHealBonusPercent(level):F0} п.п.; " +
                         "случайные бонусы после привала на 5 уровне пока не реализованы";
                break;
            default:
                active = "нет активных бонусов";
                break;
        }

        return $"Текущий уровень: {level}/{BuildingCatalog.MaxLevel}.\nАктивно: {active}.";
    }

    string CharacterDisplayName(string characterId)
    {
        if (gachaCharacters != null)
        {
            foreach (var character in gachaCharacters)
            {
                if (character != null && System.String.Equals(character.characterId, characterId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return character.characterName;
                }
            }
        }
        return characterId;
    }


    // ==================== Сброс прогресса (7.1) ====================

    void ConfirmResetProgress()
    {
        saveManager.ResetProgress();
        resetProgressConfirmPopup.style.display = DisplayStyle.None;
        RefreshBuildingsScreen();
        RefreshGachaScreen();
        StartOpeningSequence();
    }
}
