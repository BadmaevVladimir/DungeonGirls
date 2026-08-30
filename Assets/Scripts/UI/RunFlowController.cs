using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Фаза 4: единственный оркестратор всего цикла забега (7.2), связывающий UI Toolkit с уже
// реализованными менеджерами (Фазы 1-3.5). Хаб/меню зданий/гача вне скоупа; привал и
// ловушки/квесты — минимальным текстовым UI (3.8: только плоские прямоугольники/лейблы).
public partial class RunFlowController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] UIDocument uiDocument;

    [Header("Контент (Фаза 2)")]
    // ФИКС (Codex P1 2026-08-27): единственное поле jenniferCharacter заменено массивом выбираемых
    // персонажей + экраном выбора — раньше BeginRun ВСЕГДА стартовал Дженифер, Плут/Варвар были
    // недостижимы из реального флоу забега несмотря на то, что их данные/классовые пулы существуют.
    [SerializeField] CharacterData[] selectableCharacters;
    [SerializeField] List<PassiveSkillData> generalSkillPool;
    [SerializeField] List<PassiveSkillData> warriorSkillPool;
    [SerializeField] List<PassiveSkillData> rogueSkillPool;
    [SerializeField] List<PassiveSkillData> barbarianSkillPool;
    [SerializeField] List<MonsterData> regularMonsterPool;
    [SerializeField] MonsterData bossData;
    // UXML ui:Image's src="project://database/..." does not resolve at runtime (confirmed via
    // PlayModeSmokeTest: Image.image/.sprite stayed null in Play Mode) — wired here in code instead,
    // same pattern as mentorData above.
    [SerializeField] Sprite combatBackgroundSprite;

    [Header("Менеджеры")]
    [SerializeField] DungeonManager dungeonManager;
    [SerializeField] FloorManager floorManager;
    [SerializeField] CampManager campManager;
    [SerializeField] CombatManager combatManager;
    [SerializeField] RewardManager rewardManager;
    [SerializeField] LevelUpManager levelUpManager;
    [SerializeField] CharacterManager characterManager;
    [SerializeField] EquipmentManager equipmentManager;
    [SerializeField] SaveManager saveManager;
    TutorialManager tutorialManager;
    VNManager vnManager;
    string pendingRunSceneId;
    bool pendingRunSceneWasUnseen;

    // --- Экраны верхнего уровня ---
    VisualElement mainMenuScreen;
    Button startRunButton;
    VisualElement characterSelectScreen;
    VisualElement characterSelectCardsContainer;
    Button characterSelectBackButton;
    VisualElement characterSkillTooltip;
    Label characterSkillTooltipText;
    VisualElement mentorSelectScreen;
    Label mentorSelectStudentLabel;
    ScrollView mentorSelectScrollView;
    Button mentorSelectNoneButton;
    Button mentorSelectBackButton;
    VisualElement runScreen;
    VisualElement resultsScreen;
    Label resultsTitleLabel;
    Label resultsBodyLabel;
    Button resultsContinueButton;
    VisualElement pauseScreen;
    Label pauseCharacterStatsLabel;
    ScrollView pauseSkillsScrollView;
    ScrollView pauseEquipmentScrollView;
    Button pauseResumeButton;
    Button pauseAbandonRunButton;
    Button pauseQuitGameButton;
    bool isRunPaused;

    // --- Хедер забега ---
    Label floorLabel;
    Label rationsLabel;
    VisualElement roomProgressContainer;

    // --- Панели контент-área ---
    VisualElement combatPanel;
    Image combatBackground;
    VisualElement eventPopup;
    VisualElement trapPopup;
    VisualElement levelUpPanel;
    VisualElement campPanel;
    VisualElement merchantPanel;
    VisualElement rewardPanel;

    // --- Бой ---
    Image playerStageSprite;
    VisualElement playerStageWrapper;
    VisualElement enemyStageRow;
    Label skillActivationBanner;
    Coroutine skillBannerCoroutine;

    // 4.7: персистентные per-fight элементы врагов (не пересобираются каждый кадр — иначе любой
    // анимированный дочерний элемент, вроде всплывающей цифры урона, уничтожался бы ~16мс спустя).
    class EnemyStageEntry
    {
        public CombatantRuntime Combatant;
        public VisualElement Wrapper;
        public Image Sprite;
        public Label StatusLabel;
    }
    readonly List<EnemyStageEntry> enemyStageEntries = new List<EnemyStageEntry>();

    Label playerNameLabel;
    VisualElement playerHpFill;
    Label playerHpText;
    Label playerDefenseText;
    Label playerShieldText;
    VisualElement rageIndicator;
    Label rageText;
    VisualElement rageFill;
    VisualElement stealthIndicator;
    Label stealthText;
    VisualElement playerStatusContainer;
    VisualElement enemyListContainer;
    Toggle autoModeToggle;
    Button activeSkillButton;
    Toggle berserkToggle;

    // --- Журнал забега (7.2: персистентный лог, не только боевой — виден и вне боя) ---
    ScrollView runLogScroll;
    Label runLogText;
    readonly List<string> runLogLines = new List<string>();

    // --- Событие (квест, MultipleChoice) ---
    Label eventDescriptionLabel;
    VisualElement eventChoicesContainer;

    // --- Ловушка / квест TryOrSkip (общий попап) ---
    Label trapPopupTitle;
    Label trapDescriptionLabel;
    Label trapChanceLabel;
    VisualElement trapChoiceRow;
    Button trapAttemptButton;
    Button trapSkipButton;
    Label trapOutcomeLabel;
    Button trapContinueButton;

    // --- Левел-ап ---
    VisualElement levelUpCardsContainer;
    Label levelUpTitle;
    Button levelUpRerollButton;

    // --- Привал ---
    Label campText;
    Button campAcceptButton;
    Button campDeclineButton;
    Button campContinueButton;

    // --- Торговец ---
    Button merchantContinueButton;
    Label merchantCurrencyLabel;
    VisualElement merchantOffersContainer;

    // --- Награда (7.2/8.2: модальное окно поверх текущей сцены, не отдельная ShowOnly-панель) ---
    VisualElement rewardScrim;
    VisualElement rewardModalCard;
    Label rewardText;
    Button rewardContinueButton;

    // --- Открытие сундука (8.2): лента из иконок предметов, сундук открывается визуально ---
    VisualElement chestRevealContainer;
    Image chestSpriteImage;
    VisualElement chestReelViewport;
    VisualElement chestReelStrip;
    Button chestSkipButton;

    // Хэнд-вайринг в Assets/Scenes/SampleScene.unity YAML — тот же паттерн, что и mentorData/
    // combatBackgroundSprite выше (в проекте нет папки Resources/).
    [SerializeField] Texture2D chestClosedTexture;
    [SerializeField] Texture2D chestOpenTexture;

    // --- Сравнение предмета (3.4, "Без инвентаря") ---
    VisualElement itemComparePanel;
    Label newItemName;
    Label newItemStats;
    VisualElement slotChoicesContainer;
    Button itemDiscardButton;

    // Служебное состояние ожидания клика/выбора между кадрами корутины.
    int clickedIndex;
    bool chanceAttempted;
    bool chanceSucceeded;
    bool skipNextAutoCamp;
    int totalRoomsThisFloorCached;
    bool campSceneTriggeredThisRun;
    bool hotSpringsTriggeredThisRun;
    bool violetTrapRoomTriggeredThisRun;
    bool sashaBeerCellarTriggeredThisRun;
    bool huntQuestTriggeredThisRun;
    bool swordInStoneSucceededThisRun;

    CharacterData selectedCharacter;
    VeteranCharacter selectedMentor;
    List<string> selectedTransferredSkills = new List<string>();
    public CharacterData SelectedCharacter => selectedCharacter;

    void OnEnable()
    {
        var root = uiDocument.rootVisualElement;
        CacheElements(root);
        tutorialManager = TutorialManager.GetOrCreate(uiDocument, saveManager);
        BindStaticTutorialTooltips();
        startRunButton.clicked += OpenCharacterSelect;
        characterSelectBackButton.clicked += () =>
        {
            HideCharacterSkillTooltip();
            characterSelectScreen.style.display = DisplayStyle.None;
            mainMenuScreen.style.display = DisplayStyle.Flex;
        };
        mentorSelectNoneButton.clicked += () => BeginRunWithMentor(null);
        mentorSelectBackButton.clicked += ReturnFromMentorSelection;
        resultsContinueButton.clicked += ReturnToMainMenu;
        pauseResumeButton.clicked += ResumeRun;
        pauseAbandonRunButton.clicked += AbandonRunFromPause;
        pauseQuitGameButton.clicked += QuitGame;
        autoModeToggle.RegisterValueChangedCallback(evt => combatManager.SetActiveSkillAutoMode(evt.newValue));
        activeSkillButton.clicked += () => combatManager.TryActivateUniqueActiveSkill();
        berserkToggle.RegisterValueChangedCallback(evt => combatManager.SetBerserkActive(evt.newValue));
    }

    void OnDisable()
    {
        Time.timeScale = 1f;
        isRunPaused = false;
        UnsubscribeCombatEvents();
        if (vnManager != null) vnManager.SceneCompleted -= OnRunVNSceneCompleted;
        vnManager = null;
        pendingRunSceneId = null;
    }

    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame || !IsRunInProgress()) return;
        if (isRunPaused) ResumeRun();
        else PauseRun();
    }

    bool IsRunInProgress() =>
        runScreen != null && runScreen.style.display == DisplayStyle.Flex &&
        resultsScreen != null && resultsScreen.style.display != DisplayStyle.Flex;

    // Точка подключения будущих ВН-сцен внутри забега. Сцена остаётся доступна для повторного
    // просмотра, но отношение начисляется лишь за её первое завершение (пропуск считается
    // просмотром по утверждённому правилу). Вступительная сцена хаба этот метод не использует.
    public bool TryPlayRunVNScene(string sceneId)
    {
        if (!IsRunInProgress() || string.IsNullOrWhiteSpace(sceneId)) return false;
        if (vnManager == null)
        {
            vnManager = uiDocument.GetComponent<VNManager>();
            if (vnManager == null) vnManager = FindAnyObjectByType<VNManager>();
            if (vnManager == null) return false;
            vnManager.SceneCompleted += OnRunVNSceneCompleted;
        }

        if (vnManager.IsPlaying || !vnManager.TryPlayScene(sceneId)) return false;
        pendingRunSceneId = sceneId;
        pendingRunSceneWasUnseen = true; // уточняется при завершении по данным сохранения до старта.
        if (saveManager != null && vnManager.CurrentScene != null)
        {
            pendingRunSceneWasUnseen = !saveManager.HasSeenVNScene(vnManager.CurrentScene.characterId, sceneId);
        }
        return true;
    }

    void OnRunVNSceneCompleted(NarrativeSceneData scene, bool skipped)
    {
        if (scene == null || !string.Equals(scene.id, pendingRunSceneId, System.StringComparison.OrdinalIgnoreCase)) return;
        pendingRunSceneId = null;
        if (saveManager == null || string.IsNullOrWhiteSpace(scene.characterId)) return;

        // Пропуск — тоже просмотр по утверждённому правилу. Сначала фиксируем сцену, чтобы
        // она не могла повторно сработать на следующем забеге; очки отношений полагаются лишь
        // за её первый просмотр.
        saveManager.MarkVNSceneSeen(scene.characterId, scene.id);
        if (!pendingRunSceneWasUnseen) return;

        const int relationshipPointsPerRunScene = 10;
        int added = saveManager.AddRelationshipPoints(scene.characterId, relationshipPointsPerRunScene);
        if (added <= 0) return;
        tutorialManager?.QueueOnce(TutorialContent.Relationships);
        LogEvent($"[Отношения] {scene.characterId}: +{added} за ВН-сцену.");
    }

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

    void CacheElements(VisualElement root)
    {
        mainMenuScreen = root.Q<VisualElement>("MainMenuScreen");
        startRunButton = root.Q<Button>("StartRunButton");
        characterSelectScreen = root.Q<VisualElement>("CharacterSelectScreen");
        characterSelectCardsContainer = root.Q<VisualElement>("CharacterSelectCardsContainer");
        characterSelectBackButton = root.Q<Button>("CharacterSelectBackButton");
        characterSkillTooltip = root.Q<VisualElement>("CharacterSkillTooltip");
        characterSkillTooltipText = root.Q<Label>("CharacterSkillTooltipText");
        if (characterSkillTooltip != null) characterSkillTooltip.pickingMode = PickingMode.Ignore;
        mentorSelectScreen = root.Q<VisualElement>("MentorSelectScreen");
        mentorSelectStudentLabel = root.Q<Label>("MentorSelectStudentLabel");
        mentorSelectScrollView = root.Q<ScrollView>("MentorSelectScrollView");
        mentorSelectNoneButton = root.Q<Button>("MentorSelectNoneButton");
        mentorSelectBackButton = root.Q<Button>("MentorSelectBackButton");
        runScreen = root.Q<VisualElement>("RunScreen");
        resultsScreen = root.Q<VisualElement>("ResultsScreen");
        resultsTitleLabel = root.Q<Label>("ResultsTitleLabel");
        resultsBodyLabel = root.Q<Label>("ResultsBodyLabel");
        resultsContinueButton = root.Q<Button>("ResultsContinueButton");
        pauseScreen = root.Q<VisualElement>("PauseScreen");
        pauseCharacterStatsLabel = root.Q<Label>("PauseCharacterStatsLabel");
        pauseSkillsScrollView = root.Q<ScrollView>("PauseSkillsScrollView");
        pauseEquipmentScrollView = root.Q<ScrollView>("PauseEquipmentScrollView");
        pauseResumeButton = root.Q<Button>("PauseResumeButton");
        pauseAbandonRunButton = root.Q<Button>("PauseAbandonRunButton");
        pauseQuitGameButton = root.Q<Button>("PauseQuitGameButton");

        floorLabel = root.Q<Label>("FloorLabel");
        rationsLabel = root.Q<Label>("RationsLabel");
        roomProgressContainer = root.Q<VisualElement>("RoomProgressContainer");

        combatPanel = root.Q<VisualElement>("CombatPanel");
        combatBackground = root.Q<Image>("CombatBackground");
        if (combatBackground != null && combatBackgroundSprite != null)
        {
            combatBackground.sprite = combatBackgroundSprite;
        }
        eventPopup = root.Q<VisualElement>("EventPopup");
        trapPopup = root.Q<VisualElement>("TrapPopup");
        levelUpPanel = root.Q<VisualElement>("LevelUpPanel");
        campPanel = root.Q<VisualElement>("CampPanel");
        merchantPanel = root.Q<VisualElement>("MerchantPanel");
        rewardPanel = root.Q<VisualElement>("RewardPanel");
        itemComparePanel = root.Q<VisualElement>("ItemComparePanel");

        playerStageSprite = root.Q<Image>("PlayerStageSprite");
        playerStageWrapper = root.Q<VisualElement>("PlayerStageWrapper");
        enemyStageRow = root.Q<VisualElement>("EnemyStageRow");
        skillActivationBanner = root.Q<Label>("SkillActivationBanner");
        playerNameLabel = root.Q<Label>("PlayerNameLabel");
        playerHpFill = root.Q<VisualElement>("PlayerHpFill");
        playerHpText = root.Q<Label>("PlayerHpText");
        playerDefenseText = root.Q<Label>("PlayerDefenseText");
        playerShieldText = root.Q<Label>("PlayerShieldText");
        rageIndicator = root.Q<VisualElement>("RageIndicator");
        rageText = root.Q<Label>("RageText");
        rageFill = root.Q<VisualElement>("RageFill");
        stealthIndicator = root.Q<VisualElement>("StealthIndicator");
        stealthText = root.Q<Label>("StealthText");
        playerStatusContainer = root.Q<VisualElement>("PlayerStatusContainer");
        enemyListContainer = root.Q<VisualElement>("EnemyListContainer");
        runLogScroll = root.Q<ScrollView>("RunLogScroll");
        runLogText = root.Q<Label>("RunLogText");
        autoModeToggle = root.Q<Toggle>("AutoModeToggle");
        activeSkillButton = root.Q<Button>("ActiveSkillButton");
        berserkToggle = root.Q<Toggle>("BerserkToggle");

        eventDescriptionLabel = root.Q<Label>("EventDescriptionLabel");
        eventChoicesContainer = root.Q<VisualElement>("EventChoicesContainer");

        trapPopupTitle = root.Q<Label>("TrapPopupTitle");
        trapDescriptionLabel = root.Q<Label>("TrapDescriptionLabel");
        trapChanceLabel = root.Q<Label>("TrapChanceLabel");
        trapChoiceRow = root.Q<VisualElement>("TrapChoiceRow");
        trapAttemptButton = root.Q<Button>("TrapAttemptButton");
        trapSkipButton = root.Q<Button>("TrapSkipButton");
        trapOutcomeLabel = root.Q<Label>("TrapOutcomeLabel");
        trapContinueButton = root.Q<Button>("TrapContinueButton");

        levelUpCardsContainer = root.Q<VisualElement>("LevelUpCardsContainer");
        levelUpTitle = root.Q<Label>("LevelUpTitle");
        levelUpRerollButton = root.Q<Button>("LevelUpRerollButton");

        campText = root.Q<Label>("CampText");
        campAcceptButton = root.Q<Button>("CampAcceptButton");
        campDeclineButton = root.Q<Button>("CampDeclineButton");
        campContinueButton = root.Q<Button>("CampContinueButton");
        SetCampOfferButtonsVisible(false);

        merchantContinueButton = root.Q<Button>("MerchantContinueButton");
        merchantCurrencyLabel = root.Q<Label>("MerchantCurrencyLabel");
        merchantOffersContainer = root.Q<VisualElement>("MerchantOffersContainer");

        rewardScrim = root.Q<VisualElement>("RewardScrim");
        rewardModalCard = root.Q<VisualElement>("RewardModalCard");
        rewardText = root.Q<Label>("RewardText");
        rewardContinueButton = root.Q<Button>("RewardContinueButton");

        chestRevealContainer = root.Q<VisualElement>("ChestRevealContainer");
        chestSpriteImage = root.Q<Image>("ChestSpriteImage");
        chestReelViewport = root.Q<VisualElement>("ChestReelViewport");
        chestReelStrip = root.Q<VisualElement>("ChestReelStrip");
        chestSkipButton = root.Q<Button>("ChestSkipButton");

        newItemName = root.Q<Label>("NewItemName");
        newItemStats = root.Q<Label>("NewItemStats");
        slotChoicesContainer = root.Q<VisualElement>("SlotChoicesContainer");
        itemDiscardButton = root.Q<Button>("ItemDiscardButton");
    }

    // ==================== Главный цикл забега (Core Loop, раздел 1) ====================

    IEnumerator RunLoop()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        runScreen.style.display = DisplayStyle.Flex;
        tutorialManager?.QueueOnce(TutorialContent.RunStart);

        levelUpManager.GeneralSkillPool = generalSkillPool;
        levelUpManager.WarriorSkillPool = warriorSkillPool;
        levelUpManager.RogueSkillPool = rogueSkillPool;
        levelUpManager.BarbarianSkillPool = barbarianSkillPool;

        characterManager.BeginRun(selectedCharacter, equipmentManager, saveManager);

        // 1, п.3: применяем уже выбранный и разыгранный снимок наследования ветерана.
        ApplySelectedMentorInheritance();

        campManager.BeginRun(characterManager.TavernLevelThisRun);
        campSceneTriggeredThisRun = false;
        hotSpringsTriggeredThisRun = false;
        violetTrapRoomTriggeredThisRun = false;
        sashaBeerCellarTriggeredThisRun = false;
        huntQuestTriggeredThisRun = false;
        swordInStoneSucceededThisRun = false;
        dungeonManager.SetRunState(RunState.RunSetup);
        dungeonManager.GenerateDungeon();
        dungeonManager.SetRunState(RunState.InFloor);

        bool victory = false;

        while (true)
        {
            floorManager.SetFloorState(FloorState.FloorStart);
            floorManager.GenerateRoomBag(dungeonManager.CurrentFloorNumber);
            characterManager.BeginFloor(); // 8.5: сброс счётчика пройденных комнат этого этажа
            totalRoomsThisFloorCached = floorManager.TotalRoomsOnFloor;
            UpdateTopBar();

            bool floorLost = false;

            while (true)
            {
                floorManager.SetFloorState(FloorState.RoomEntry);
                bool drewFromBag = floorManager.TryDrawNextRoom(out var roomType);
                bool isBossRoom = !drewFromBag;

                yield return ResolveRoom(roomType, isBossRoom);

                floorManager.MarkRoomCompleted();
                UpdateTopBar();

                if (!characterManager.IsAlive)
                {
                    floorLost = true;
                    break;
                }

                // 8.5: комната засчитывается в награду за поражение только если персонаж её пережил.
                characterManager.MarkRoomCleared();

                if (isBossRoom)
                {
                    // После босса игрок получает ещё одну возможность потратить рацион перед
                    // следующим этажом. На 10-м этаже привал уже ничего не меняет, поэтому его
                    // не предлагаем после финального босса.
                    if (dungeonManager.CurrentFloorNumber < DungeonManager.TotalFloors && campManager.CanCamp)
                    {
                        floorManager.SetFloorState(FloorState.CampPhase);
                        yield return CampOfferAndPhaseCoroutine();

                        if (!characterManager.IsAlive)
                        {
                            floorLost = true;
                            break;
                        }
                    }

                    break; // этаж пройден (2.5: комната босса всегда последняя)
                }

                if (skipNextAutoCamp)
                {
                    skipNextAutoCamp = false;
                }
                else if (campManager.CanCamp)
                {
                    floorManager.SetFloorState(FloorState.CampPhase);
                    yield return CampOfferAndPhaseCoroutine();

                    if (!characterManager.IsAlive)
                    {
                        floorLost = true;
                        break;
                    }
                }
            }

            if (floorLost)
            {
                victory = false;
                break;
            }

            floorManager.SetFloorState(FloorState.FloorEnd);

            if (!dungeonManager.AdvanceToNextFloor())
            {
                victory = true;
                break;
            }
        }

        dungeonManager.SetRunState(victory ? RunState.RunComplete : RunState.RunFailed);
        yield return ShowResultsFlow(victory);
    }

    IEnumerator ResolveRoom(RoomType roomType, bool isBoss)
    {
        switch (roomType)
        {
            case RoomType.Combat:
                floorManager.SetFloorState(FloorState.CombatResolve);
                yield return CombatRoomFlow(false);
                break;
            case RoomType.Boss:
                floorManager.SetFloorState(FloorState.CombatResolve);
                yield return CombatRoomFlow(true);
                break;
            case RoomType.Merchant:
                floorManager.SetFloorState(FloorState.MerchantResolve);
                yield return MerchantRoomFlow();
                break;
            case RoomType.Trap:
                floorManager.SetFloorState(FloorState.TrapResolve);
                yield return TrapRoomFlow();
                break;
            case RoomType.Special:
                floorManager.SetFloorState(FloorState.EventResolve);
                yield return EventRoomFlow();
                break;
        }
    }







    // ==================== Общие UI-хелперы ====================

    void BindStaticTutorialTooltips()
    {
        if (tutorialManager == null) return;
        tutorialManager.BindTooltip(floorLabel, "Этаж и маршрут", TutorialContent.TooltipFloor);
        tutorialManager.BindTooltip(roomProgressContainer, "Комнаты этажа", TutorialContent.TooltipFloor);
        tutorialManager.BindTooltip(rationsLabel, "Рационы", TutorialContent.TooltipRations);
        tutorialManager.BindTooltip(playerHpText, "Здоровье", TutorialContent.TooltipHp);
        tutorialManager.BindTooltip(playerDefenseText, "Физическая защита", TutorialContent.TooltipArmor);
        tutorialManager.BindTooltip(playerShieldText, "Магический щит", TutorialContent.TooltipShield);
        tutorialManager.BindTooltip(rageIndicator, "Ярость", TutorialContent.TooltipRage);
        tutorialManager.BindTooltip(stealthIndicator, "Скрытность", TutorialContent.TooltipStealth);
        tutorialManager.BindTooltip(autoModeToggle, "Авто-режим", TutorialContent.TooltipAuto);
        tutorialManager.BindTooltip(activeSkillButton, "Активный навык", TutorialContent.TooltipActive);
        tutorialManager.BindTooltip(berserkToggle, "Берсерк", TutorialContent.TooltipBerserk);
        tutorialManager.BindTooltip(levelUpRerollButton, "Перебросы", TutorialContent.TooltipReroll);
        tutorialManager.BindTooltip(merchantCurrencyLabel, "Валюта забега", TutorialContent.TooltipRunCurrency);
        tutorialManager.BindTooltip(newItemStats, "Ранг эффекта", TutorialContent.TooltipItemRank);
        tutorialManager.BindTooltip(runLogScroll, "Журнал забега", "Здесь сохраняются важные события текущего забега: исходы комнат, награды, срабатывания навыков и изменения состояния персонажа.");
    }

    void UpdateTopBar()
    {
        floorLabel.text = $"Этаж {dungeonManager.CurrentFloorNumber}/{DungeonManager.TotalFloors}";
        rationsLabel.text = $"Рационы: {campManager.RationsRemaining}";

        roomProgressContainer.Clear();
        int completed = floorManager.RoomsCompletedOnFloor;
        for (int i = 0; i < totalRoomsThisFloorCached; i++)
        {
            var pip = new VisualElement();
            pip.AddToClassList("room-pip");
            bool isBossPip = i == totalRoomsThisFloorCached - 1;
            if (isBossPip)
            {
                pip.AddToClassList("room-pip-boss");
            }
            else if (i < completed)
            {
                pip.AddToClassList("room-pip-done");
            }
            else if (i == completed)
            {
                pip.AddToClassList("room-pip-current");
            }

            roomProgressContainer.Add(pip);
        }
    }

    void ShowOnly(VisualElement panelToShow)
    {
        // RewardPanel (7.2/8.2) сознательно исключён — это не ShowOnly-переключаемая панель, а
        // модальный оверлей поверх текущей сцены, видимостью которого управляют
        // ShowRewardOverlay/HideRewardOverlay через класс "hidden", а не через style.display
        // отсюда (иначе инлайновый display:none из этого метода забивал бы класс насовсем).
        foreach (var panel in new[] { combatPanel, eventPopup, trapPopup, levelUpPanel, campPanel, merchantPanel, itemComparePanel })
        {
            panel.style.display = panel == panelToShow ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    IEnumerator WaitForClick(Button button)
    {
        bool clicked = false;
        void Handler() => clicked = true;
        button.clicked += Handler;
        yield return new WaitUntil(() => clicked);
        button.clicked -= Handler;
    }

    IEnumerator WaitForAnyClick(params Button[] buttons)
    {
        clickedIndex = -1;
        var handlers = new System.Action[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            handlers[i] = () => clickedIndex = index;
            buttons[i].clicked += handlers[i];
        }

        yield return new WaitUntil(() => clickedIndex >= 0);

        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].clicked -= handlers[i];
        }
    }
}
