using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    public void OpenCharacterSelect()
    {
        var availableCharacters = BuildAvailableCharacters(selectableCharacters, saveManager);
        if (availableCharacters.Count == 0)
        {
            Debug.LogError("[RunFlowController] Невозможно начать забег: не настроен ни один доступный CharacterData (Дженифер должна быть доступна всегда).");
            return;
        }

        // 1 п.2 / 7.2: отдельный этап не задерживает игрока, пока у него только стартовая
        // Дженифер. Как только из гачи получен хотя бы один другой герой, показываем весь
        // доступный состав — вместе с Дженифер, а не только новые выпадения.
        if (ShouldSkipCharacterSelection(availableCharacters))
        {
            BeginRunWithCharacter(availableCharacters[0]);
            return;
        }

        mainMenuScreen.style.display = DisplayStyle.None;
        characterSelectScreen.style.display = DisplayStyle.Flex;
        HideCharacterSkillTooltip();
        tutorialManager?.QueueOnce(TutorialContent.CharacterSelection);

        characterSelectCardsContainer.Clear();
        foreach (var character in availableCharacters)
        {
            var card = new VisualElement();
            card.name = $"CharacterSelectCard_{character.characterId}";
            card.AddToClassList("character-select-card");

            var portraitFrame = new VisualElement { name = $"CharacterSelectPortraitFrame_{character.characterId}" };
            portraitFrame.AddToClassList("character-select-portrait-frame");
            var portraitImage = new Image
            {
                name = $"CharacterSelectPortrait_{character.characterId}",
                sprite = character.selectionPortrait != null ? character.selectionPortrait : character.portrait,
                // Полный портрет важнее заполнения рамки: верх головы не должен теряться.
                // Свободное место внизу допустимо и не скрывает персонажа.
                scaleMode = ScaleMode.ScaleToFit
            };
            portraitImage.AddToClassList("character-select-portrait");
            portraitFrame.Add(portraitImage);
            card.Add(portraitFrame);

            var nameLabel = new Label(character.characterName);
            nameLabel.AddToClassList("building-card-title");
            card.Add(nameLabel);

            var classLabel = new Label($"Класс: {DisplayFormat.CharacterClassDisplayName(character.characterClass)}");
            classLabel.AddToClassList("character-select-class");
            card.Add(classLabel);

            var hpLabel = new Label($"HP: {character.baseHealth}");
            hpLabel.AddToClassList("character-select-stat");
            card.Add(hpLabel);

            var hpGrowthLabel = new Label($"Прирост HP: +{character.healthPerLevel} за уровень");
            hpGrowthLabel.AddToClassList("character-select-stat");
            card.Add(hpGrowthLabel);

            AddCharacterSkillLabel(card, character.characterId, "PassiveSkill", "Пассивный", character.uniquePassiveSkill != null ? character.uniquePassiveSkill.skillName : "—", character.uniquePassiveSkill != null ? character.uniquePassiveSkill.effectDescription : string.Empty);
            AddCharacterSkillLabel(card, character.characterId, "ActiveSkill", "Активный", character.uniqueActiveSkill != null ? character.uniqueActiveSkill.skillName : "—", character.uniqueActiveSkill != null ? character.uniqueActiveSkill.effectDescription : string.Empty);

            var pickButton = new Button { name = $"CharacterSelectButton_{character.characterId}", text = "Выбрать" };
            pickButton.AddToClassList("button-primary");
            pickButton.clicked += () => BeginRunWithCharacter(character);
            card.Add(pickButton);

            characterSelectCardsContainer.Add(card);
        }
    }

    public static bool IsCharacterAvailableForRun(CharacterData character, SaveManager currentSaveManager)
    {
        if (character == null || string.IsNullOrWhiteSpace(character.characterId)) return false;
        if (string.Equals(character.characterId, "jennifer", System.StringComparison.OrdinalIgnoreCase)) return true;
        return currentSaveManager != null && currentSaveManager.GetCharacterCopies(character.characterId) > 0;
    }

    public static List<CharacterData> BuildAvailableCharacters(IEnumerable<CharacterData> characters, SaveManager currentSaveManager)
    {
        var result = new List<CharacterData>();
        if (characters == null) return result;

        var addedIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var character in characters)
        {
            if (!IsCharacterAvailableForRun(character, currentSaveManager) || !addedIds.Add(character.characterId)) continue;
            result.Add(character);
        }
        return result;
    }

    public static bool ShouldSkipCharacterSelection(IReadOnlyList<CharacterData> availableCharacters) =>
        availableCharacters != null &&
        availableCharacters.Count == 1 &&
        availableCharacters[0] != null &&
        string.Equals(availableCharacters[0].characterId, "jennifer", System.StringComparison.OrdinalIgnoreCase);

    void AddCharacterSkillLabel(VisualElement card, string characterId, string elementSuffix, string typeLabel, string skillName, string description)
    {
        var skillLabel = new Label($"{typeLabel}: {skillName}")
        {
            name = $"CharacterSelect{elementSuffix}_{characterId}",
            tooltip = description ?? string.Empty
        };
        skillLabel.AddToClassList("character-select-skill");
        skillLabel.RegisterCallback<PointerEnterEvent>(evt => ShowCharacterSkillTooltip(description, evt.position));
        skillLabel.RegisterCallback<PointerMoveEvent>(evt => PositionCharacterSkillTooltip(evt.position));
        skillLabel.RegisterCallback<PointerLeaveEvent>(_ => HideCharacterSkillTooltip());
        card.Add(skillLabel);
    }

    void ShowCharacterSkillTooltip(string description, Vector3 pointerPosition)
    {
        if (characterSkillTooltip == null || characterSkillTooltipText == null || string.IsNullOrWhiteSpace(description)) return;
        characterSkillTooltipText.text = description;
        characterSkillTooltip.style.display = DisplayStyle.Flex;
        PositionCharacterSkillTooltip(pointerPosition);
    }

    void PositionCharacterSkillTooltip(Vector3 pointerPosition)
    {
        if (characterSkillTooltip == null || characterSkillTooltip.style.display == DisplayStyle.None) return;

        const float margin = 12f;
        const float offset = 18f;
        float tooltipWidth = characterSkillTooltip.resolvedStyle.width;
        float tooltipHeight = characterSkillTooltip.resolvedStyle.height;
        if (float.IsNaN(tooltipWidth) || tooltipWidth <= 0f) tooltipWidth = 360f;
        if (float.IsNaN(tooltipHeight) || tooltipHeight <= 0f) tooltipHeight = 140f;

        float screenWidth = characterSelectScreen.resolvedStyle.width;
        float screenHeight = characterSelectScreen.resolvedStyle.height;
        float left = Mathf.Clamp(pointerPosition.x + offset, margin, Mathf.Max(margin, screenWidth - tooltipWidth - margin));
        float top = Mathf.Clamp(pointerPosition.y + offset, margin, Mathf.Max(margin, screenHeight - tooltipHeight - margin));
        characterSkillTooltip.style.left = left;
        characterSkillTooltip.style.top = top;
    }

    void HideCharacterSkillTooltip()
    {
        if (characterSkillTooltip != null) characterSkillTooltip.style.display = DisplayStyle.None;
    }

    void BeginRunWithCharacter(CharacterData character)
    {
        selectedCharacter = character;
        HideCharacterSkillTooltip();
        characterSelectScreen.style.display = DisplayStyle.None;
        mainMenuScreen.style.display = DisplayStyle.None;
        OpenMentorSelect();
    }

    public static List<VeteranCharacter> BuildEligibleMentors(IEnumerable<VeteranCharacter> veterans, string studentCharacterId)
    {
        var result = new List<VeteranCharacter>();
        if (veterans == null) return result;
        foreach (var veteran in veterans)
        {
            if (VeteranSystem.IsEligibleMentor(veteran, studentCharacterId)) result.Add(veteran);
        }
        return result;
    }

    void OpenMentorSelect()
    {
        var eligible = BuildEligibleMentors(saveManager != null ? saveManager.Data.veteranDeck : null, selectedCharacter.characterId);
        if (eligible.Count == 0)
        {
            BeginRunWithMentor(null);
            return;
        }

        mentorSelectStudentLabel.text = $"Подопечный: {selectedCharacter.characterName}";
        mentorSelectScrollView.Clear();
        foreach (var veteran in eligible)
        {
            var card = new VisualElement { name = $"MentorSelectCard_{veteran.characterId}_{mentorSelectScrollView.childCount}" };
            card.AddToClassList("mentor-select-card");
            var title = new Label($"{CharacterDisplayName(veteran.characterId)} — {veteran.grade}");
            title.AddToClassList("mentor-select-grade");
            tutorialManager?.BindTooltip(title, "Оценка ветерана", TutorialContent.TooltipGrade);
            card.Add(title);
            card.Add(new Label($"Полностью зачищено этажей: {veteran.floorsCleared}"));
            card.Add(new Label($"Гарантированный пассив: {veteran.uniquePassiveSkillName}"));
            string candidates = veteran.finalSkills != null && veteran.finalSkills.Count > 0
                ? string.Join(", ", veteran.finalSkills.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.skillName)).Select(entry => entry.skillName).Distinct())
                : "нет";
            card.Add(new Label($"Дополнительный пул: {candidates}"));
            var pickButton = new Button { text = "Выбрать наставником" };
            pickButton.AddToClassList("button-primary");
            pickButton.clicked += () => BeginRunWithMentor(veteran);
            card.Add(pickButton);
            mentorSelectScrollView.Add(card);
        }

        mentorSelectScreen.style.display = DisplayStyle.Flex;
        tutorialManager?.QueueOnce(TutorialContent.MentorSelection);
    }

    string CharacterDisplayName(string characterId)
    {
        if (selectableCharacters != null)
        {
            foreach (var character in selectableCharacters)
            {
                if (character != null && string.Equals(character.characterId, characterId, System.StringComparison.OrdinalIgnoreCase)) return character.characterName;
            }
        }
        return characterId;
    }

    void BeginRunWithMentor(VeteranCharacter mentor)
    {
        runCompletionCommitted = false;
        resultsSkipRequested = false;
        currentRunCompletionId = System.Guid.NewGuid().ToString("N");
        selectedMentor = mentor;
        selectedTransferredSkills = mentor != null
            ? VeteranSystem.RollTransferredSkills(mentor, new System.Random(System.Environment.TickCount ^ mentor.GetHashCode()))
            : new List<string>();
        mentorSelectScreen.style.display = DisplayStyle.None;
        StartCoroutine(RunLoop());
    }

    void ReturnFromMentorSelection()
    {
        mentorSelectScreen.style.display = DisplayStyle.None;
        selectedMentor = null;
        selectedTransferredSkills.Clear();
        selectedCharacter = null;
        var availableCharacters = BuildAvailableCharacters(selectableCharacters, saveManager);
        if (ShouldSkipCharacterSelection(availableCharacters)) ReturnToMainMenu();
        else OpenCharacterSelect();
    }

    public void ReturnToMainMenu()
    {
        ResumeRun();
        HideCharacterSkillTooltip();
        characterSelectScreen.style.display = DisplayStyle.None;
        mentorSelectScreen.style.display = DisplayStyle.None;
        resultsScreen.style.display = DisplayStyle.None;
        runScreen.style.display = DisplayStyle.None;
        pauseScreen.style.display = DisplayStyle.None;
        mainMenuScreen.style.display = DisplayStyle.Flex;
    }
}
