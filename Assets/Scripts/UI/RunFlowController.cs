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
    VisualElement mapPanel;
    VisualElement mapGraphContainer;
    Label mapStatusLabel;
    Button mapEnterCurrentButton;
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

        // Boss framework (минимальный слайс) — reusable-телеграф специальной атаки: показывает
        // "готовит: <имя способности>" + полоску обратного отсчёта ДО того, как способность
        // срабатывает (см. BossEncounterState.PendingTelegraph). Не привязано к The Warden — читает
        // CombatantRuntime.BossEncounter, так что работает для любого будущего босса без правок этого
        // класса. У обычных врагов (BossEncounter == null) всегда скрыт.
        public Label TelegraphLabel;
        public VisualElement TelegraphBarFill;
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

    // Джингл открытия сундука (REAPER-проект: Audio/SFX/ChestOpen_Jingle.rpp) — длина ровно под
    // ChestRevealAnimator.PlayReel (4с), три версии финала по редкости награды. AudioSource не
    // обязателен: если не назначен, звук просто не проигрывается (не ломает флоу).
    [SerializeField] AudioSource chestOpenAudioSource;
    [SerializeField] AudioClip chestOpenCommonClip;
    [SerializeField] AudioClip chestOpenRareClip;
    [SerializeField] AudioClip chestOpenEpicClip;

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
    bool pendingCombatReward;
    bool pendingCombatWasBoss;
    bool pendingStandaloneChestReward;

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
        mapPanel = root.Q<VisualElement>("MapPanel");
        mapGraphContainer = root.Q<VisualElement>("MapGraphContainer");
        mapStatusLabel = root.Q<Label>("MapStatusLabel");
        mapEnterCurrentButton = root.Q<Button>("MapEnterCurrentButton");
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
        // Справка внутри забега показывает активный навык только той героини, которой играют.
        if (tutorialManager != null) tutorialManager.ActiveCharacterId = selectedCharacter != null ? selectedCharacter.characterId : null;
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
            floorManager.GenerateFloorMap(dungeonManager.CurrentFloorNumber);
            characterManager.BeginFloor(); // 8.5: сброс счётчика пройденных комнат этого этажа
            ResolveGeneratedFloorMapContent();
            totalRoomsThisFloorCached = floorManager.TotalRoomsOnFloor;
            UpdateTopBar();
            yield return MapPreviewFlow();

            bool floorLost = false;

            while (true)
            {
                floorManager.SetFloorState(FloorState.RoomEntry);
                FloorMapNode currentNode = floorManager.CurrentNode;
                bool isBossRoom = currentNode.Kind == FloorMapNodeKind.Boss;

                ResetPendingRoomRewards();
                yield return ResolveMapNode(currentNode);

                floorManager.MarkCurrentRoomCompleted();
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

                    yield return ResolvePendingRoomRewards();

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

                yield return ResolvePendingRoomRewards();

                // Even a single target (notably Boss) is selected on the map; navigation never
                // infers reachability from depth alone.
                yield return MapChoiceFlow();
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

    IEnumerator ResolveMapNode(FloorMapNode node)
    {
        yield return ResolveRoom(node);
    }

    IEnumerator ResolveRoom(FloorMapNode node)
    {
        switch (node.RoomType)
        {
            case RoomType.Combat:
                floorManager.SetFloorState(FloorState.CombatResolve);
                yield return CombatRoomFlow(false, node);
                break;
            case RoomType.Boss:
                floorManager.SetFloorState(FloorState.CombatResolve);
                yield return CombatRoomFlow(true, node);
                break;
            case RoomType.Merchant:
                floorManager.SetFloorState(FloorState.MerchantResolve);
                yield return MerchantRoomFlow(node);
                break;
            case RoomType.Trap:
                floorManager.SetFloorState(FloorState.TrapResolve);
                yield return TrapRoomFlow(node);
                break;
            case RoomType.Special:
                floorManager.SetFloorState(FloorState.EventResolve);
                yield return EventRoomFlow(node);
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
        // Ярость/Скрытность/Берсерк/активный навык зависят от того, кем играет игрок и что у неё
        // сейчас происходит, поэтому текст собирается на лету, а не берётся константой.
        tutorialManager.BindTooltip(rageIndicator, "Ярость",
            () => TutorialContent.RageTooltip(combatManager != null && combatManager.Player != null ? combatManager.Player.Rage : 0f));
        tutorialManager.BindTooltip(stealthIndicator, "Скрытность", () =>
        {
            var player = combatManager != null ? combatManager.Player : null;
            return player == null
                ? TutorialContent.StealthTooltip(0f, 0)
                : TutorialContent.StealthTooltip(player.StealthTimer, player.SmokeBombGuaranteedCritsRemaining);
        });
        tutorialManager.BindTooltip(autoModeToggle, "Авто-режим", TutorialContent.TooltipAuto);
        tutorialManager.BindTooltip(activeSkillButton, "Активный навык",
            () => TutorialContent.ActiveSkillTooltip(characterManager?.Character?.characterId));
        tutorialManager.BindTooltip(berserkToggle, "Берсерк",
            () => TutorialContent.BerserkTooltip(combatManager != null && combatManager.Player != null && combatManager.Player.IsBerserkActive
                ? combatManager.Player.PhysicalResistancePercent
                : 0f));
        tutorialManager.BindTooltip(levelUpRerollButton, "Перебросы", TutorialContent.TooltipReroll);
        tutorialManager.BindTooltip(merchantCurrencyLabel, "Валюта забега", TutorialContent.TooltipRunCurrency);
        tutorialManager.BindTooltip(trapChanceLabel, "Шанс успеха", TutorialContent.TooltipTrapChance);
        tutorialManager.BindTooltip(runLogScroll, "Журнал забега", TutorialContent.TooltipRunLog);
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
        foreach (var panel in new[] { combatPanel, mapPanel, eventPopup, trapPopup, levelUpPanel, campPanel, merchantPanel, itemComparePanel })
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
