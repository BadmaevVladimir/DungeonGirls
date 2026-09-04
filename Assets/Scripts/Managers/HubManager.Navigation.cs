using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class HubManager
{
    // ==================== Навигация (7.1) ====================

    public void OpenVillage()
    {
        // Вне забега Справка снова показывает активные навыки всех героинь.
        if (tutorialManager != null) tutorialManager.ActiveCharacterId = null;
        buildingsScreen.style.display = DisplayStyle.None;
        gachaScreen.style.display = DisplayStyle.None;
        veteranDeckScreen.style.display = DisplayStyle.None;
        charactersScreen.style.display = DisplayStyle.None;
        tavernScreen.style.display = DisplayStyle.None;
        forgeScreen.style.display = DisplayStyle.None;
        CloseCheatMenu();
        RefreshVillagePlates();
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
        else if (string.Equals(cheatCommandField.value?.Trim(), IWantBitchesCheat, System.StringComparison.OrdinalIgnoreCase))
        {
            int granted = 0;
            foreach (var character in gachaCharacters)
            {
                if (character == null || string.IsNullOrEmpty(character.characterId)) continue;
                saveManager.AddCharacterCopy(character.characterId);
                granted++;
            }
            cheatResultLabel.text = $"Получено: +1 копия каждого персонажа ({granted}).";
            cheatCommandField.value = string.Empty;
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
            string text = $"{displayName} — оценка {veteran.grade}, этажей пройдено: {veteran.floorsCleared}, HP {veteran.finalHP:F0}, навыков: {skillCount}, предметов: {equipmentCount}";
            var portrait = FindCharacter(veteran.characterId)?.portrait;
            var row = AddIconRow(veteranDeckScrollView, portrait, text);
            tutorialManager?.BindTooltip(row, "Оценка ветерана", TutorialContent.TooltipGrade);
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
            string text = $"{character.characterName} ({DisplayFormat.CharacterClassDisplayName(character.characterClass)}) — копий: {copies}, прохождений: {runs}, {relationship}, открытых сцен: {seenScenes}";
            var row = AddIconRow(charactersScrollView, character.portrait, text);
            tutorialManager?.BindTooltip(row, "Отношения", TutorialContent.TooltipRelationships);
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
        tutorialManager.BindTooltip(gachaPullButton, "Призыв", TutorialContent.TooltipGachaPull);
        tutorialManager.BindTooltip(buildingBonusLabels[0], "Бонус Кузницы", () => CurrentBuildingBonusText(BuildingType.Forge));
        tutorialManager.BindTooltip(buildingBonusLabels[1], "Бонус Храма", () => CurrentBuildingBonusText(BuildingType.Temple));
        tutorialManager.BindTooltip(buildingBonusLabels[2], "Бонус Таверны", () => CurrentBuildingBonusText(BuildingType.Tavern));
    }

    // Тултип здания отвечает на один вопрос игрока: «что у меня есть сейчас и что даст следующий
    // уровень». Поэтому — только действующие бонусы простыми словами, без служебных формулировок
    // («плоская броня», «п.п.», «×1.20») и без упоминания того, чего в игре ещё нет.
    string CurrentBuildingBonusText(BuildingType building)
    {
        int level = saveManager.GetBuildingLevel(building);
        string now = BuildingBonusLines(building, level);
        if (level >= BuildingCatalog.MaxLevel)
        {
            return $"Уровень {level} из {BuildingCatalog.MaxLevel} — максимальный.\n{now}";
        }

        string next = BuildingBonusLines(building, level + 1);
        return $"Уровень {level} из {BuildingCatalog.MaxLevel}.\nСейчас:\n{now}\n\nНа уровне {level + 1}:\n{next}";
    }

    static string BuildingBonusLines(BuildingType building, int level)
    {
        var lines = new List<string>();
        switch (building)
        {
            case BuildingType.Forge:
                int startingBonus = BuildingCatalog.ForgeStartingEquipmentBonus(level);
                if (startingBonus > 0) lines.Add($"• стартовое снаряжение выше на {startingBonus} ур.");
                float armorBonus = BuildingCatalog.ForgeArmorBonus(level);
                if (armorBonus > 0f) lines.Add($"• +{armorBonus:F0} к физической защите");
                float armorMultiplier = BuildingCatalog.ForgeEquipmentArmorMultiplier(level);
                if (armorMultiplier > 1f) lines.Add($"• броня снаряжения больше на {(armorMultiplier - 1f) * 100f:F0}%");
                float campRestore = BuildingCatalog.ForgeCampArmorRestorePercent(level);
                if (campRestore > 0f) lines.Add($"• привал чинит {campRestore:F0}% брони");
                break;

            case BuildingType.Temple:
                float shield = BuildingCatalog.TempleMagicShieldBonus(level);
                if (shield > 0f) lines.Add($"• +{shield:F0} к магическому щиту");
                int rerolls = BuildingCatalog.TempleLevelUpRerolls(level);
                if (rerolls > 0) lines.Add($"• перебросов навыков за забег: {rerolls}");
                break;

            case BuildingType.Tavern:
                int rations = BuildingCatalog.TavernRationsBonus(level);
                if (rations > 0) lines.Add($"• +{rations} к рационам");
                float damage = BuildingCatalog.TavernFlatDamageBonus(level);
                if (damage > 0f) lines.Add($"• +{damage:F0} к урону каждого оружия");
                float heal = BuildingCatalog.TavernCampHealBonusPercent(level);
                if (heal > 0f) lines.Add($"• привал лечит на {heal:F0}% больше");
                break;
        }

        return lines.Count > 0 ? string.Join("\n", lines) : "• пока ничего";
    }

    string CharacterDisplayName(string characterId)
    {
        var character = FindCharacter(characterId);
        return character != null ? character.characterName : characterId;
    }

    static VisualElement AddIconRow(VisualElement container, Sprite icon, string text)
    {
        var row = new VisualElement();
        row.AddToClassList("icon-row");
        if (icon != null)
        {
            var image = new Image { sprite = icon, scaleMode = ScaleMode.ScaleToFit };
            image.AddToClassList("icon-row-icon-large");
            row.Add(image);
        }
        var label = new Label(text);
        label.AddToClassList("icon-row-text");
        row.Add(label);
        container.Add(row);
        return row;
    }

    CharacterData FindCharacter(string characterId)
    {
        if (gachaCharacters != null)
        {
            foreach (var character in gachaCharacters)
            {
                if (character != null && System.String.Equals(character.characterId, characterId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return character;
                }
            }
        }
        return null;
    }


    // ==================== Сброс прогресса (7.1) ====================

    void ConfirmResetProgress()
    {
        saveManager.ResetProgress();
        resetProgressConfirmPopup.style.display = DisplayStyle.None;
        RefreshBuildingsScreen();
        RefreshGachaScreen();
        RefreshVillagePlates();
        StartOpeningSequence();
    }
}
