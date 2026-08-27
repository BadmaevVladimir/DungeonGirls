using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

// Фаза 4: единственный оркестратор всего цикла забега (7.2), связывающий UI Toolkit с уже
// реализованными менеджерами (Фазы 1-3.5). Хаб/меню зданий/гача вне скоупа; привал и
// ловушки/квесты — минимальным текстовым UI (3.8: только плоские прямоугольники/лейблы).
public class RunFlowController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] UIDocument uiDocument;

    [Header("Контент (Фаза 2)")]
    // ФИКС (Codex P1 2026-08-27): единственное поле jenniferCharacter заменено массивом выбираемых
    // персонажей + экраном выбора — раньше BeginRun ВСЕГДА стартовал Дженифер, Плут/Варвар были
    // недостижимы из реального флоу забега несмотря на то, что их данные/классовые пулы существуют.
    [SerializeField] CharacterData[] selectableCharacters;
    [SerializeField] MentorData mentorData;
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

    // --- Экраны верхнего уровня ---
    VisualElement mainMenuScreen;
    Button startRunButton;
    VisualElement characterSelectScreen;
    VisualElement characterSelectCardsContainer;
    Button characterSelectBackButton;
    VisualElement runScreen;
    VisualElement resultsScreen;
    Label resultsTitleLabel;
    Label resultsBodyLabel;
    Button resultsContinueButton;

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
    Label playerStatusLabel;
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

    CharacterData selectedCharacter;
    public CharacterData SelectedCharacter => selectedCharacter;

    void OnEnable()
    {
        var root = uiDocument.rootVisualElement;
        CacheElements(root);
        startRunButton.clicked += OpenCharacterSelect;
        characterSelectBackButton.clicked += () =>
        {
            characterSelectScreen.style.display = DisplayStyle.None;
            mainMenuScreen.style.display = DisplayStyle.Flex;
        };
        resultsContinueButton.clicked += ReturnToMainMenu;
        autoModeToggle.RegisterValueChangedCallback(evt => combatManager.SetActiveSkillAutoMode(evt.newValue));
        activeSkillButton.clicked += () => combatManager.TryActivateUniqueActiveSkill();
        berserkToggle.RegisterValueChangedCallback(evt => combatManager.SetBerserkActive(evt.newValue));
    }

    public void OpenCharacterSelect()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        characterSelectScreen.style.display = DisplayStyle.Flex;

        characterSelectCardsContainer.Clear();
        foreach (var character in selectableCharacters)
        {
            if (character == null) continue;

            var card = new VisualElement();
            card.AddToClassList("building-card");

            var portraitImage = new Image { sprite = character.portrait };
            portraitImage.style.width = 64;
            portraitImage.style.height = 64;
            card.Add(portraitImage);

            var nameLabel = new Label(character.characterName);
            nameLabel.AddToClassList("building-card-title");
            card.Add(nameLabel);

            var classLabel = new Label(character.characterClass.ToString());
            classLabel.AddToClassList("body-label");
            card.Add(classLabel);

            var pickButton = new Button { text = "Выбрать" };
            pickButton.AddToClassList("button-primary");
            pickButton.clicked += () => BeginRunWithCharacter(character);
            card.Add(pickButton);

            characterSelectCardsContainer.Add(card);
        }
    }

    void BeginRunWithCharacter(CharacterData character)
    {
        selectedCharacter = character;
        characterSelectScreen.style.display = DisplayStyle.None;
        StartCoroutine(RunLoop());
    }

    public void ReturnToMainMenu()
    {
        resultsScreen.style.display = DisplayStyle.None;
        runScreen.style.display = DisplayStyle.None;
        mainMenuScreen.style.display = DisplayStyle.Flex;
    }

    void CacheElements(VisualElement root)
    {
        mainMenuScreen = root.Q<VisualElement>("MainMenuScreen");
        startRunButton = root.Q<Button>("StartRunButton");
        characterSelectScreen = root.Q<VisualElement>("CharacterSelectScreen");
        characterSelectCardsContainer = root.Q<VisualElement>("CharacterSelectCardsContainer");
        characterSelectBackButton = root.Q<Button>("CharacterSelectBackButton");
        runScreen = root.Q<VisualElement>("RunScreen");
        resultsScreen = root.Q<VisualElement>("ResultsScreen");
        resultsTitleLabel = root.Q<Label>("ResultsTitleLabel");
        resultsBodyLabel = root.Q<Label>("ResultsBodyLabel");
        resultsContinueButton = root.Q<Button>("ResultsContinueButton");

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
        playerStatusLabel = new Label();
        playerStatusLabel.AddToClassList("stage-status-label");
        playerStatusLabel.enableRichText = true;
        playerStageWrapper.Add(playerStatusLabel);
        enemyStageRow = root.Q<VisualElement>("EnemyStageRow");
        skillActivationBanner = root.Q<Label>("SkillActivationBanner");
        playerNameLabel = root.Q<Label>("PlayerNameLabel");
        playerHpFill = root.Q<VisualElement>("PlayerHpFill");
        playerHpText = root.Q<Label>("PlayerHpText");
        playerDefenseText = root.Q<Label>("PlayerDefenseText");
        playerShieldText = root.Q<Label>("PlayerShieldText");
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

        levelUpManager.GeneralSkillPool = generalSkillPool;
        levelUpManager.WarriorSkillPool = warriorSkillPool;
        levelUpManager.RogueSkillPool = rogueSkillPool;
        levelUpManager.BarbarianSkillPool = barbarianSkillPool;

        characterManager.BeginRun(selectedCharacter, equipmentManager, saveManager);

        // 1, п.3: наставник передаёт свой основной пассив (постоянный бонус) + свои остальные
        // навыки в общий пул левел-апа (см. design note в плане Task 3).
        characterManager.Progress.MentorMagicDamageBonusPercent = mentorData != null ? mentorData.mainPassiveMagicDamageBonusPercent : 0f;
        characterManager.RefreshCombatStats(); // применить бонус к уже собранному Combatant

        levelUpManager.MentorSkillPool = mentorData != null && mentorData.otherKnownSkills != null
            ? new List<PassiveSkillData>(mentorData.otherKnownSkills)
            : new List<PassiveSkillData>();

        if (mentorData != null)
        {
            LogEvent($"[Наставник] {mentorData.mentorName} передаёт «{(mentorData.mainPassiveSkill != null ? mentorData.mainPassiveSkill.skillName : "?")}»: +{mentorData.mainPassiveMagicDamageBonusPercent:F0}% магического урона.");
        }

        campManager.BeginRun(characterManager.TavernLevelThisRun);
        dungeonManager.SetRunState(RunState.RunSetup);
        dungeonManager.GenerateDungeon();
        dungeonManager.SetRunState(RunState.InFloor);

        bool victory = false;

        while (true)
        {
            floorManager.SetFloorState(FloorState.FloorStart);
            floorManager.GenerateRoomBag();
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

    // ==================== Бой (раздел 4, 7.2) ====================

    // 4.1 [ОБНОВЛЕНО после третьего плейтеста]: пороги количества монстров в обычной боевой
    // комнате снижены — старый порог в 7 уровня для 3 монстров был слишком поздним.
    int RollMonsterCount(int level)
    {
        if (level <= 2) return 1;
        if (level <= 5) return Random.Range(1, 3); // 1-2
        return Random.Range(1, 4); // 1-3 (уровень 6+)
    }

    // Codex P1 (ФИКС, 2026-08-27): раньше CombatRoomFlow всегда передавал hitCount=3 и конфиг из
    // jenniferCharacter.uniqueActiveSkill — Плут получал бы конфигурацию Дженифер (неверный
    // hitCount/имя навыка), а Варвар вообще не имеет кулдаун-активки (Берсерк — ручной тумблер, см.
    // ниже). Единственный текущий кейс с hitCount != 3 — Дымовая граната Плута (не бьёт сама, см.
    // CombatManager.TryActivateUniqueActiveSkill, которое жёстко возвращает до hit-loop для неё
    // независимо от переданного числа) — hitCount=0 здесь просто отражает намерение корректно.
    public static int ResolveActiveSkillHitCount(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Rogue => 0, // Дымовая граната — не бьёт сама
        _ => 3 // "3 быстрые атаки" (Дженифер/Воин) — единственный hit-loop навык прототипа кроме Дымовой гранаты
    };

    IEnumerator CombatRoomFlow(bool isBoss)
    {
        var enemies = new List<CombatantRuntime>();
        if (isBoss)
        {
            enemies.Add(CombatantFactory.CreateMonsterCombatant(bossData, dungeonManager.CurrentFloorNumber));
        }
        else
        {
            // 2.7/8.4: уровень монстра растёт с позицией уже пройденных комнат этажа в мешке.
            int monsterLevel = 1 + floorManager.RoomsCompletedOnFloor / 3;
            int count = RollMonsterCount(characterManager.Level);

            // 2.4: тиры суммируются — этаж 5 видит и тир-1, и тир-4 монстров, не только последний
            // открытый тир (см. "черновое распределение по этажам").
            var eligibleMonsters = regularMonsterPool.FindAll(m => m != null && m.minFloorTier <= dungeonManager.CurrentFloorNumber);
            if (eligibleMonsters.Count == 0)
            {
                eligibleMonsters = regularMonsterPool;
            }

            for (int i = 0; i < count; i++)
            {
                var data = eligibleMonsters[Random.Range(0, eligibleMonsters.Count)];
                enemies.Add(CombatantFactory.CreateMonsterCombatant(data, dungeonManager.CurrentFloorNumber, monsterLevel));
            }
        }

        // 5.5 "Сигнализация" (провал): бой начинается с бафом +10% урона монстрам.
        if (characterManager.Modifiers.ConsumeMonsterDamageBuff())
        {
            foreach (var enemy in enemies)
            {
                foreach (var weapon in enemy.Weapons)
                {
                    weapon.DamageMin *= 1.1f;
                    weapon.DamageMax *= 1.1f;
                }
            }
        }

        // 5.5 "Идол" / 5.4 "Меч в камне" (провал): временные штрафы урона/скорости атаки на бой.
        float dmgMult = characterManager.Modifiers.ConsumeCombatDamageMultiplier();
        float spdMult = characterManager.Modifiers.ConsumeCombatAttackSpeedMultiplier();
        var originalStats = new List<(float min, float max, float spd)>();
        foreach (var weapon in characterManager.Combatant.Weapons)
        {
            originalStats.Add((weapon.DamageMin, weapon.DamageMax, weapon.AttackSpeed));
            weapon.DamageMin *= dmgMult;
            weapon.DamageMax *= dmgMult;
            weapon.AttackSpeed *= spdMult;
        }

        var activeCharacter = characterManager.Progress.Character;
        bool isBarbarian = activeCharacter.characterClass == CharacterClass.Barbarian;

        if (isBarbarian)
        {
            // 3.11 (Варвар) — Берсерк — ручной тумблер, не кулдаун-активка (см. ГДД 3.11, точная
            // цитата: "НЕ работает как обычный активный навык (нет кулдауна, нет авто-режима, нет
            // длительности)"). CombatManager.ConfigureUniqueActiveSkill/TryActivateUniqueActiveSkill
            // не используются для него вовсе — UI использует berserkToggle (см. ниже), не
            // activeSkillButton/autoModeToggle.
            combatManager.SetBerserkActive(false); // сброс на начало боя — тумблер не переносится между боями
        }
        else
        {
            int activeLevel = characterManager.Progress.UniqueActiveLevel;
            float activeMultiplier = activeLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f };
            int hitCount = ResolveActiveSkillHitCount(activeCharacter.characterClass);
            combatManager.ConfigureUniqueActiveSkill(hitCount, activeMultiplier, activeCharacter.uniqueActiveSkill.cooldownSeconds, autoModeToggle.value, activeCharacter.uniqueActiveSkill.skillName);
        }

        combatManager.LogMessage += OnCombatLog;
        combatManager.MonsterStoleCurrency += OnMonsterStoleCurrency;
        combatManager.HitResolved += OnHitResolved;
        combatManager.ActiveSkillActivated += OnActiveSkillActivated;
        ShowOnly(combatPanel);
        combatManager.StartCombat(characterManager.Combatant, enemies);
        BuildEnemyStageEntries(enemies);

        while (combatManager.IsCombatActive)
        {
            UpdateCombatUI();
            yield return null;
        }

        UpdateCombatUI();
        combatManager.LogMessage -= OnCombatLog;
        combatManager.MonsterStoleCurrency -= OnMonsterStoleCurrency;
        combatManager.HitResolved -= OnHitResolved;
        combatManager.ActiveSkillActivated -= OnActiveSkillActivated;

        for (int i = 0; i < characterManager.Combatant.Weapons.Count && i < originalStats.Count; i++)
        {
            characterManager.Combatant.Weapons[i].DamageMin = originalStats[i].min;
            characterManager.Combatant.Weapons[i].DamageMax = originalStats[i].max;
            characterManager.Combatant.Weapons[i].AttackSpeed = originalStats[i].spd;
        }

        if (!characterManager.IsAlive)
        {
            yield break;
        }

        // 8.2 (НОВОЕ): короткая пауза после победного удара — игрок успевает увидеть последний
        // эффект/всплывающее число урона (см. 4.7) до того, как сцена начнёт темнеть под награду.
        yield return new WaitForSeconds(0.45f);

        var levelsGained = characterManager.GrantExperience(rewardManager, isBoss ? ExperienceSource.Boss : ExperienceSource.CombatRoom, dungeonManager.CurrentFloorNumber);
        foreach (var _ in levelsGained)
        {
            yield return LevelUpFlow();
        }

        yield return ShowRewardChestFlow(dungeonManager.CurrentFloorNumber, isBoss);
    }

    void OnCombatLog(string message)
    {
        LogEvent(message);
    }

    // "Карманник" (2.4): монстр украл часть текущей валюты забега у персонажа.
    void OnMonsterStoleCurrency(CombatantRuntime victim, float percent)
    {
        characterManager.StealCurrencyPercent(percent);
    }

    // 7.2: общий персистентный лог забега — сюда пишутся боевые события (4.5), результаты
    // комнат/квестов/ловушек, левел-апы и т.д. Виден на отдельной панели вне зависимости от
    // текущей фазы забега (не только во время боя).
    void LogEvent(string message)
    {
        runLogLines.Add(message);
        if (runLogLines.Count > 200)
        {
            runLogLines.RemoveAt(0);
        }

        RefreshRunLog();
    }

    void RefreshRunLog()
    {
        runLogText.text = string.Join("\n", runLogLines);
        runLogScroll.schedule.Execute(() => runLogScroll.scrollOffset = new Vector2(0f, float.MaxValue));
    }

    void UpdateCombatUI()
    {
        ShowOnly(combatPanel);

        var player = combatManager.Player;
        playerStageSprite.sprite = player.Sprite;
        playerNameLabel.text = $"{player.DisplayName} (ур. {characterManager.Level})";
        float playerHpPercent = player.MaxHP > 0f ? Mathf.Clamp01(player.CurrentHP / player.MaxHP) * 100f : 0f;
        playerHpFill.style.width = new Length(playerHpPercent, LengthUnit.Percent);
        playerHpText.text = $"{Mathf.Max(player.CurrentHP, 0f):F0}/{player.MaxHP:F0}";
        playerDefenseText.text = $"Защита: {Mathf.Max(player.PhysicalDefenseCurrent, 0f):F0}/{player.PhysicalDefenseMax:F0}";
        playerShieldText.text = $"Щит: {Mathf.Max(player.MagicShieldCurrent, 0f):F0}/{player.MagicShieldMax:F0}";

        enemyListContainer.Clear();
        foreach (var enemy in combatManager.Enemies)
        {
            var box = new VisualElement();
            box.AddToClassList("combatant-box");
            if (enemy == player.Target && enemy.IsAlive)
            {
                box.AddToClassList("combatant-box-target");
            }

            var nameLabel = new Label(enemy.IsAlive ? enemy.DisplayName : $"{enemy.DisplayName} (погиб)");
            nameLabel.AddToClassList("combatant-name");
            box.Add(nameLabel);

            var hpBg = new VisualElement();
            hpBg.AddToClassList("hp-bar-bg");
            var hpFill = new VisualElement();
            hpFill.AddToClassList("hp-bar-fill");
            float hpPercent = enemy.MaxHP > 0f ? Mathf.Clamp01(enemy.CurrentHP / enemy.MaxHP) * 100f : 0f;
            hpFill.style.width = new Length(hpPercent, LengthUnit.Percent);
            hpBg.Add(hpFill);
            box.Add(hpBg);

            var hpText = new Label($"{Mathf.Max(enemy.CurrentHP, 0f):F0}/{enemy.MaxHP:F0}");
            hpText.AddToClassList("hp-text");
            box.Add(hpText);

            var statsText = new Label($"Защита: {Mathf.Max(enemy.PhysicalDefenseCurrent, 0f):F0}/{enemy.PhysicalDefenseMax:F0}  Щит: {Mathf.Max(enemy.MagicShieldCurrent, 0f):F0}/{enemy.MagicShieldMax:F0}");
            statsText.AddToClassList("stat-text");
            box.Add(statsText);

            if (enemy.IsAlive)
            {
                box.RegisterCallback<ClickEvent>(_ => combatManager.SetPlayerTarget(enemy));
            }

            enemyListContainer.Add(box);
        }

        // 7.2/10.6: крупные спрайты на "земле" сцены боя, отдельно от карточек имени/HP выше.
        // Персистентные элементы (построены один раз в BuildEnemyStageEntries) — тут только
        // обновление состояния кадр к кадру, без Clear()/пересоздания (иначе анимации на
        // дочерних элементах, вроде всплывающих цифр урона, уничтожались бы каждый тик).
        float stageFloorGap = GetStageFloorGapFromBottom();
        playerStageWrapper.style.marginBottom = stageFloorGap;
        UpdateStatusLabel(playerStatusLabel, player);

        foreach (var entry in enemyStageEntries)
        {
            entry.Wrapper.style.marginBottom = stageFloorGap;
            entry.Sprite.EnableInClassList("enemy-stage-sprite-dead", !entry.Combatant.IsAlive);
            UpdateStatusLabel(entry.StatusLabel, entry.Combatant);
        }

        bool isBarbarianCombat = characterManager.Progress.Character.characterClass == CharacterClass.Barbarian;
        activeSkillButton.EnableInClassList("hidden", isBarbarianCombat);
        autoModeToggle.EnableInClassList("hidden", isBarbarianCombat);
        berserkToggle.EnableInClassList("hidden", !isBarbarianCombat);

        if (!isBarbarianCombat)
        {
            bool ready = combatManager.IsActiveSkillReady;
            activeSkillButton.SetEnabled(!autoModeToggle.value && ready);
            activeSkillButton.text = ready ? "Активный навык (готов)" : $"Активный навык ({combatManager.ActiveSkillCooldownRemaining:F1}с)";
        }
        else
        {
            berserkToggle.SetValueWithoutNotify(player.IsBerserkActive);
        }
    }

    // 4.7: строится один раз при старте боя (список противников не меняется в процессе боя,
    // только их IsAlive) — размер спрайта зависит от количества (4.1: 1-3 в обычной комнате).
    void BuildEnemyStageEntries(List<CombatantRuntime> enemies)
    {
        enemyStageRow.Clear();
        enemyStageEntries.Clear();

        int enemyCount = enemies.Count;
        float enemySpriteSize = enemyCount switch
        {
            <= 1 => 384f,
            2 => 260f,
            _ => 190f
        };

        foreach (var enemy in enemies)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("enemy-stage-sprite-wrapper");
            wrapper.style.width = enemySpriteSize;
            wrapper.style.height = enemySpriteSize;

            var sprite = new Image { sprite = enemy.Sprite };
            sprite.AddToClassList("stage-sprite");
            sprite.AddToClassList("enemy-stage-sprite");
            wrapper.Add(sprite);

            var statusLabel = new Label();
            statusLabel.AddToClassList("stage-status-label");
            statusLabel.enableRichText = true;
            wrapper.Add(statusLabel);

            enemyStageRow.Add(wrapper);
            enemyStageEntries.Add(new EnemyStageEntry { Combatant = enemy, Wrapper = wrapper, Sprite = sprite, StatusLabel = statusLabel });
        }
    }

    // 4.7 [ОБНОВЛЕНО]: средне-насыщенные (не пастель, не кислотные) баф/дебафф-подписи — rich-text
    // цвет прямо в тексте лейбла, отдельного лейбла на строку не нужно. Пилюля-подложка (см. USS
    // .stage-status-label) скрывается целиком, когда эффектов нет — иначе висела бы пустой фон.
    void UpdateStatusLabel(Label label, CombatantRuntime combatant)
    {
        var effects = CombatantStatusEffects.GetActiveEffects(combatant);
        label.EnableInClassList("hidden", effects.Count == 0);
        if (effects.Count == 0)
        {
            label.text = string.Empty;
            return;
        }

        label.text = string.Join("\n", effects.ConvertAll(e => $"<color={(e.isBuff ? "#7CD66B" : "#E2645F")}>{e.label}</color>"));
    }

    VisualElement FindStageWrapper(CombatantRuntime combatant)
    {
        if (combatant == combatManager.Player)
        {
            return playerStageWrapper;
        }

        foreach (var entry in enemyStageEntries)
        {
            if (entry.Combatant == combatant)
            {
                return entry.Wrapper;
            }
        }

        return null;
    }

    // 4.7: единая точка подписки на CombatManager.HitResolved — всплывающая цифра урона + тряска
    // спрайта цели (тряска пропускается при полном блоке, см. GDD 4.7).
    void OnHitResolved(CombatantRuntime target, float damageToHP, bool isCrit, bool wasBlocked)
    {
        var wrapper = FindStageWrapper(target);
        if (wrapper == null)
        {
            return;
        }

        string text = wasBlocked ? "БЛОК" : damageToHP.ToString("F0");
        StartCoroutine(SpawnFloatingCombatText(wrapper, text, isCrit, wasBlocked));

        if (!wasBlocked)
        {
            StartCoroutine(ChestRevealAnimator.Shake(wrapper, 0.2f, new Vector3(5f, 3f, 0f), 6));
        }
    }

    // 4.7 (НОВОЕ): небольшой случайный горизонтальный разброс точки появления + небольшая
    // вариация времени появления — иначе несколько одновременных чисел (от пары монстров сразу,
    // от "3 быстрых атак") сливаются в одну нечитаемую массу.
    IEnumerator SpawnFloatingCombatText(VisualElement wrapper, string text, bool isCrit, bool isBlock)
    {
        yield return new WaitForSeconds(Random.Range(0f, 0.12f));

        var label = new Label(text);
        label.AddToClassList("floating-combat-text");
        if (isCrit)
        {
            label.AddToClassList("floating-combat-text-crit");
        }
        else if (isBlock)
        {
            label.AddToClassList("floating-combat-text-block");
        }

        float horizontalJitterPercent = Random.Range(-14f, 14f);
        label.style.left = new Length(50f + horizontalJitterPercent, LengthUnit.Percent);

        wrapper.Add(label);
        yield return FloatAndFadeOut(label);
    }

    IEnumerator FloatAndFadeOut(VisualElement label)
    {
        const float duration = 0.8f;
        const float riseDistance = 40f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            label.style.translate = new Translate(Length.Percent(-50), -riseDistance * progress, 0);
            label.style.opacity = 1f - progress;
            yield return null;
        }

        if (label.parent != null)
        {
            label.RemoveFromHierarchy();
        }
    }

    // 4.7: баннер активации уникального активного навыка — общий на всю боевую сцену, не
    // per-combatant. ~1.15с (fade in 0.15 / hold 0.85 / fade out 0.15), в пределах ГДД 1-1.2с.
    void OnActiveSkillActivated(CombatantRuntime user, string skillName)
    {
        if (skillBannerCoroutine != null)
        {
            StopCoroutine(skillBannerCoroutine);
        }
        skillBannerCoroutine = StartCoroutine(ShowSkillBanner(skillName));
    }

    IEnumerator ShowSkillBanner(string skillName)
    {
        const float fadeIn = 0.15f;
        const float hold = 0.85f;
        const float fadeOut = 0.15f;

        skillActivationBanner.RemoveFromClassList("hidden");
        skillActivationBanner.text = skillName;

        float elapsed = 0f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.deltaTime;
            skillActivationBanner.style.opacity = Mathf.Clamp01(elapsed / fadeIn);
            yield return null;
        }
        skillActivationBanner.style.opacity = 1f;

        yield return new WaitForSeconds(hold);

        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.deltaTime;
            skillActivationBanner.style.opacity = 1f - Mathf.Clamp01(elapsed / fadeOut);
            yield return null;
        }

        skillActivationBanner.style.opacity = 0f;
        skillActivationBanner.AddToClassList("hidden");
        skillBannerCoroutine = null;
    }

    // Баг (2026-08-26): фон боя (Dungeon.png, 1536x1024) рендерится через ScaleAndCrop — на
    // экранах шире исходного соотношения (16:9-21:9 против 3:2 фона, платформа PC standalone)
    // кроп идёт по центру, и линия пола на фоне (~77.8% высоты исходного изображения, найдено
    // измерением пикселей — ряд ~797 из 1024) смещается относительно НИЖНЕГО края контейнера тем
    // сильнее, чем шире экран. Статичный процент в USS не может угнаться за этим на всём диапазоне
    // 16:9-21:9 (в 16:9 пол оказывается на ~17% высоты от низа, в 21:9 — уже на ~6%), поэтому
    // пересчитывается здесь по формуле "cover"-кропа при каждом обновлении боевого UI и
    // применяется как отступ снизу (margin-bottom) к спрайтам поверх их обычного
    // align-items: flex-end позиционирования (влево/вправо не меняется, только высота "ступней").
    const float combatBackgroundImageWidth = 1536f;
    const float combatBackgroundImageHeight = 1024f;
    const float combatBackgroundFloorRowFromTop = 797f;

    float GetStageFloorGapFromBottom()
    {
        float boxWidth = combatPanel.resolvedStyle.width;
        float boxHeight = combatPanel.resolvedStyle.height;
        if (boxWidth <= 0f || boxHeight <= 0f)
        {
            // Первый кадр после ShowOnly(combatPanel) — Yoga-layout ещё не посчитан
            // (resolvedStyle временно 0x0). Само-корректируется на следующем кадре.
            return 0f;
        }

        float imageAspect = combatBackgroundImageWidth / combatBackgroundImageHeight;
        float boxAspect = boxWidth / boxHeight;

        float scale;
        float cropTop;
        if (boxAspect > imageAspect)
        {
            // Контейнер шире фона (типичный случай 16:9-21:9 против 3:2) — фон растягивается по
            // ширине контейнера, высота обрезается сверху и снизу поровну (центр-кроп).
            scale = boxWidth / combatBackgroundImageWidth;
            float scaledHeight = combatBackgroundImageHeight * scale;
            cropTop = (scaledHeight - boxHeight) / 2f;
        }
        else
        {
            // Контейнер уже фона (не целевой диапазон платформы, но не должен ломаться) — кроп по
            // высоте, вертикального кропа нет вовсе.
            scale = boxHeight / combatBackgroundImageHeight;
            cropTop = 0f;
        }

        float floorFromTop = combatBackgroundFloorRowFromTop * scale - cropTop;
        return Mathf.Max(0f, boxHeight - floorFromTop);
    }

    // ==================== Ловушка (5.5) и квесты TryOrSkip (5.4) — общий попап ====================

    IEnumerator TrapRoomFlow()
    {
        var trap = TrapCatalog.All[Random.Range(0, TrapCatalog.All.Length)];
        trapPopupTitle.text = "Ловушка";
        yield return ShowChancePopupAndWait(trap.DescriptionText, trap.Level, trap.SuccessText, trap.FailText, "Попытаться пройти ловушку", "Пойти дальше");

        if (!chanceAttempted)
        {
            yield break; // 5.5: "Пойти дальше" — риска и награды нет
        }

        if (chanceSucceeded)
        {
            if (trap == TrapCatalog.Idol)
            {
                characterManager.AddCurrency(500);
            }
            else
            {
                yield return ShowRewardChestFlow(dungeonManager.CurrentFloorNumber, false);
            }
        }
        else
        {
            if (trap == TrapCatalog.MinedChest)
            {
                // "Крепкая подошва" (3.10, Бронированные сапоги): -1% урона от сработавших ловушек за уровень предмета.
                float toughSoleReduction = characterManager.Combatant.ItemToughSoleLevel * 0.01f;
                characterManager.ApplyDirectDamage(15 * (1f - toughSoleReduction));
                characterManager.ApplyDirectArmorLoss(20);
            }
            else if (trap == TrapCatalog.Alarm)
            {
                characterManager.Modifiers.NextCombatMonsterDamageBuff10Percent = true;
                if (characterManager.IsAlive)
                {
                    yield return CombatRoomFlow(false);
                }
            }
            else if (trap == TrapCatalog.Idol)
            {
                characterManager.Modifiers.NextCombatDamageMultiplier = (characterManager.Modifiers.NextCombatDamageMultiplier ?? 1f) * 0.9f;
                characterManager.Modifiers.NextCombatAttackSpeedMultiplier = (characterManager.Modifiers.NextCombatAttackSpeedMultiplier ?? 1f) * 0.9f;
            }
        }
    }

    IEnumerator ShowChancePopupAndWait(string description, int level, string successText, string failText, string attemptLabel, string skipLabel)
    {
        ShowOnly(trapPopup);
        trapDescriptionLabel.text = description;

        int luckLevel = characterManager.Progress.GetSkillLevel(SkillEffectMap.Luck);
        float chance = SuccessChanceCalculator.CalculateSuccessChancePercent(characterManager.Level, level, SuccessChanceCalculator.GetLuckBonusPercent(luckLevel));
        trapChanceLabel.text = $"Шанс успеха: {chance:F0}%";

        trapAttemptButton.text = attemptLabel;
        trapSkipButton.text = skipLabel;
        trapChoiceRow.style.display = DisplayStyle.Flex;
        trapOutcomeLabel.AddToClassList("hidden");
        trapContinueButton.AddToClassList("hidden");

        yield return WaitForAnyClick(trapAttemptButton, trapSkipButton);
        bool attempted = clickedIndex == 0;
        trapChoiceRow.style.display = DisplayStyle.None;

        chanceAttempted = attempted;
        chanceSucceeded = false;
        string outcome;

        if (!attempted)
        {
            outcome = "Вы решаете не рисковать и идёте дальше.";
        }
        else
        {
            chanceSucceeded = Random.value * 100f < chance;
            outcome = chanceSucceeded ? successText : failText;
        }

        LogEvent($"[{trapPopupTitle.text}] {outcome}");

        trapOutcomeLabel.text = outcome;
        trapOutcomeLabel.RemoveFromClassList("hidden");
        trapContinueButton.RemoveFromClassList("hidden");
        yield return WaitForClick(trapContinueButton);
    }

    // ==================== Особая комната / квест (5.3-5.4) ====================

    static QuestDefinition PickQuestForFloor(int floor)
    {
        switch (floor)
        {
            case 1: return QuestCatalog.Sphinx;
            case 2: return QuestCatalog.FairyRing;
            default: return QuestCatalog.SwordInStone;
        }
    }

    IEnumerator EventRoomFlow()
    {
        var quest = PickQuestForFloor(dungeonManager.CurrentFloorNumber);

        if (quest.InteractionType == QuestInteractionType.MultipleChoice)
        {
            ShowOnly(eventPopup);
            eventDescriptionLabel.text = quest.DescriptionText;
            eventChoicesContainer.Clear();

            var buttons = new List<Button>();
            foreach (var choice in quest.Choices)
            {
                var btn = new Button { text = choice.ButtonText };
                btn.AddToClassList("choice-card");
                eventChoicesContainer.Add(btn);
                buttons.Add(btn);
            }

            yield return WaitForAnyClick(buttons.ToArray());
            var picked = quest.Choices[clickedIndex];

            LogEvent($"[Событие] {picked.OutcomeText}");

            eventChoicesContainer.Clear();
            eventDescriptionLabel.text = picked.OutcomeText;
            var continueButton = new Button { text = "Продолжить" };
            continueButton.AddToClassList("button-primary");
            eventChoicesContainer.Add(continueButton);
            yield return WaitForClick(continueButton);

            if (picked.IsCorrect)
            {
                // ГДД 5.4: верный ответ на загадку сфинкса — +200 валюты забега в следующем бою.
                characterManager.Modifiers.NextChestCurrencyBonus = (characterManager.Modifiers.NextChestCurrencyBonus ?? 0) + 200;
            }
            else
            {
                characterManager.Modifiers.NextChestNoCurrency = true;
            }
        }
        else
        {
            trapPopupTitle.text = "Событие";
            yield return ShowChancePopupAndWait(quest.DescriptionText, quest.Level, quest.SuccessText, quest.FailText, "Попытаться", "Пройти мимо");
            trapPopupTitle.text = "Ловушка";

            if (quest == QuestCatalog.FairyRing)
            {
                if (chanceAttempted && campManager.CanCamp)
                {
                    // ГДД 5.4: успех — на 20% больше здоровья, чем базовый отдых (70% вместо
                    // базовых 50%, т.е. x1.4); провал — половина обычного объёма привала.
                    float healMultiplier = chanceSucceeded ? 1.4f : 0.5f;
                    floorManager.SetFloorState(FloorState.CampPhase);
                    yield return CampPhaseCoroutine(healMultiplier);
                    skipNextAutoCamp = true;
                }
            }
            else if (quest == QuestCatalog.SwordInStone)
            {
                if (chanceAttempted && !chanceSucceeded)
                {
                    characterManager.Modifiers.NextCombatDamageMultiplier = (characterManager.Modifiers.NextCombatDamageMultiplier ?? 1f) * 0.9f;
                }
            }
        }
    }

    // ==================== Левел-ап (3.5) ====================

    IEnumerator LevelUpFlow()
    {
        floorManager.SetFloorState(FloorState.LevelUpChoice);
        var options = levelUpManager.GenerateLevelUpOptions(characterManager.Progress);
        if (options.Count == 0)
        {
            yield break;
        }

        ShowOnly(levelUpPanel);
        levelUpCardsContainer.Clear();
        var buttons = new List<Button>();
        foreach (var option in options)
        {
            string description = option.Description;
            string cardText = string.IsNullOrWhiteSpace(description) ? option.ToString() : $"{option}\n{description}";
            var btn = new Button { text = cardText };
            btn.AddToClassList("choice-card");
            levelUpCardsContainer.Add(btn);
            buttons.Add(btn);
        }

        yield return WaitForAnyClick(buttons.ToArray());
        var chosen = options[clickedIndex];
        levelUpManager.ApplyChoice(characterManager.Progress, chosen);
        characterManager.RefreshCombatStats();
        LogEvent($"[Левел-ап] {chosen} (уровень {characterManager.Level}).");
    }

    // ==================== Привал (раздел 6) ====================

    // 6.1: триггер привала — явное решение игрока. Игра предлагает встать на привал; если
    // игрок отказывается, рацион не тратится и автоматика 6.2 не запускается. Показывает текущее
    // HP, чтобы решение о трате рациона было осознанным.
    IEnumerator CampOfferAndPhaseCoroutine()
    {
        ShowOnly(campPanel);
        var combatant = characterManager.Combatant;
        campText.text = $"Можно встать на привал (потратит 1 рацион). Здоровье: {Mathf.Max(combatant.CurrentHP, 0f):F0}/{combatant.MaxHP:F0}. Осталось рационов: {campManager.RationsRemaining}.";
        SetCampOfferButtonsVisible(true);

        yield return WaitForAnyClick(campAcceptButton, campDeclineButton);
        SetCampOfferButtonsVisible(false);

        bool accepted = clickedIndex == 0;
        if (!accepted)
        {
            LogEvent("[Привал] Игрок отказался от привала.");
            yield break;
        }

        yield return CampPhaseCoroutine();
    }

    void SetCampOfferButtonsVisible(bool visible)
    {
        campAcceptButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        campDeclineButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        campContinueButton.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
    }

    IEnumerator CampPhaseCoroutine(float healMultiplierOverride = -1f)
    {
        ShowOnly(campPanel);
        float multiplier = healMultiplierOverride > 0f ? healMultiplierOverride : characterManager.Modifiers.ConsumeCampHealMultiplier();
        var result = campManager.RestAtCamp(characterManager, multiplier);

        campText.text = $"{characterManager.Character.characterName} отдыхает у привала..." +
            $"\n+{result.HpRestored:F0} HP" +
            (result.ArmorRestored > 0f ? $", +{result.ArmorRestored:F0} физ. защиты (Полевой ремонт)" : string.Empty) +
            $"\nОсталось рационов: {campManager.RationsRemaining}";
        LogEvent($"[Привал] +{result.HpRestored:F0} HP{(result.ArmorRestored > 0f ? $", +{result.ArmorRestored:F0} физ. защиты" : string.Empty)}. Осталось рационов: {campManager.RationsRemaining}.");

        yield return WaitForClick(campContinueButton);
    }

    // ==================== Торговец (5.2) ====================

    IEnumerator MerchantRoomFlow()
    {
        var offers = rewardManager.GenerateMerchantOffers(characterManager.Level);

        bool leave = false;
        while (!leave)
        {
            ShowOnly(merchantPanel);
            merchantCurrencyLabel.text = $"Валюта забега: {characterManager.RunCurrency}";
            merchantOffersContainer.Clear();

            var buttons = new List<Button>();
            foreach (var offer in offers)
            {
                var card = new VisualElement();
                card.AddToClassList("merchant-offer-card");

                if (offer.Item == null)
                {
                    card.Add(new Label("Пусто") { });
                    merchantOffersContainer.Add(card);
                    continue;
                }

                var nameLabel = new Label(offer.Item.itemName);
                nameLabel.AddToClassList("item-card-name");
                SetRarityClass(nameLabel, offer.Item.tier);
                card.Add(nameLabel);

                var statsLabel = new Label(ItemStatsText(offer.Item));
                statsLabel.AddToClassList("body-label");
                card.Add(statsLabel);

                if (offer.HasDiscount)
                {
                    var originalPriceLabel = new Label($"{offer.OriginalPrice} монет");
                    originalPriceLabel.AddToClassList("merchant-offer-price-original");
                    card.Add(originalPriceLabel);
                    var discountTag = new Label("СКИДКА!");
                    discountTag.AddToClassList("merchant-offer-discount-tag");
                    card.Add(discountTag);
                }

                var priceLabel = new Label($"{offer.Price} монет");
                priceLabel.AddToClassList("merchant-offer-price");
                card.Add(priceLabel);

                var buyButton = new Button { text = "Купить" };
                buyButton.AddToClassList("button-primary");
                buyButton.AddToClassList("merchant-offer-buy-button");
                buyButton.SetEnabled(characterManager.RunCurrency >= offer.Price);
                card.Add(buyButton);

                merchantOffersContainer.Add(card);
                buttons.Add(buyButton);
            }

            buttons.Add(merchantContinueButton);

            yield return WaitForAnyClick(buttons.ToArray());

            if (clickedIndex == buttons.Count - 1)
            {
                leave = true; // "Уйти от торговца"
                continue;
            }

            // clickedIndex maps 1:1 into `offers` because empty-item offers still add a card but never
            // a button — so `buttons` only ever contains as many entries as offers WITH an item, plus
            // the leave button. Map back by re-walking offers with a running non-null index.
            int runningIndex = -1;
            MerchantOffer purchased = null;
            foreach (var offer in offers)
            {
                if (offer.Item == null) continue;
                runningIndex++;
                if (runningIndex == clickedIndex)
                {
                    purchased = offer;
                    break;
                }
            }

            if (purchased != null && characterManager.TrySpendCurrency(purchased.Price))
            {
                LogEvent($"[Торговец] Куплено: {purchased.Item.itemName} за {purchased.Price} валюты забега.");
                offers.Remove(purchased);
                yield return ItemCompareFlow(purchased.Item);
            }
        }
    }

    // ==================== Награда / сундук (8.2, только текстовый результат) ====================

    static string RarityLabel(ItemTier tier)
    {
        switch (tier)
        {
            case ItemTier.Common: return "Обычный";
            case ItemTier.Rare: return "Редкий";
            default: return "Эпический";
        }
    }

    static void SetRarityClass(VisualElement element, ItemTier tier)
    {
        element.RemoveFromClassList("rarity-common");
        element.RemoveFromClassList("rarity-rare");
        element.RemoveFromClassList("rarity-epic");
        element.AddToClassList(tier switch
        {
            ItemTier.Common => "rarity-common",
            ItemTier.Rare => "rarity-rare",
            _ => "rarity-epic"
        });
    }

    IEnumerator ShowRewardChestFlow(int floorNumber, bool isBoss)
    {
        floorManager.SetFloorState(FloorState.RewardChest);

        int luckLevel = characterManager.Progress.GetSkillLevel(SkillEffectMap.Luck);
        int currencyBonus = characterManager.Modifiers.ConsumeChestCurrencyBonus();
        bool noCurrency = characterManager.Modifiers.ConsumeChestNoCurrency();

        int goldenTouchLevel = characterManager.Combatant.ItemGoldenTouchLevel;
        var reward = rewardManager.CalculateRewards(floorNumber, isBoss, characterManager.Level, luckLevel, currencyBonus, noCurrency, goldenTouchLevel);

        // 7.2/8.2 (НОВОЕ): модальное окно поверх текущей сцены — не ShowOnly, сцена позади (обычно
        // бой) остаётся видна затемнённой, а не скрывается целиком.
        yield return ShowRewardOverlay();
        // Баг (2026-08-26): описание награды из прошлой комнаты оставалось видимым поверх новой
        // анимации сундука (текст очищался только после ChestRevealFlow) — очищаем сразу, до тряски.
        rewardText.text = string.Empty;
        yield return ChestRevealFlow(reward);

        characterManager.AddCurrency(reward.Currency); // счётчик валюты — начисление происходит здесь,
            // ПОСЛЕ ленты (не до), чтобы RunCurrency в rewardText ниже уже отражал начисленную сумму —
            // порядок сознательно переставлен относительно исходного кода (было до ShowOnly).
        rewardText.text = $"Получено: {reward.Currency} монет забега, {RarityLabel(reward.ItemRarity)} предмет" +
            (reward.BonusReward ? "\n+ дополнительная награда (Удача)" : string.Empty) +
            $"\nВсего валюты забега: {characterManager.RunCurrency}";
        SetRarityClass(rewardText, reward.ItemRarity);
        LogEvent($"[Награда] +{reward.Currency} валюты забега, {RarityLabel(reward.ItemRarity)} предмет{(reward.Item != null ? $" ({reward.Item.itemName})" : string.Empty)}{(reward.BonusReward ? ", + доп. награда (Удача)" : string.Empty)}.");

        yield return WaitForClick(rewardContinueButton);
        yield return HideRewardOverlay();

        if (reward.Item != null)
        {
            yield return ItemCompareFlow(reward.Item);
        }
    }

    // 7.2/8.2 (НОВОЕ): скрим темнеет + модальная карточка появляется scale(0.9→1)+fade за ~0.3с —
    // мягкое появление вместо резкого хлопка. RewardPanel сознательно не участвует в ShowOnly() —
    // сцена позади (обычно бой) должна остаться видна затемнённой, а не исчезнуть.
    IEnumerator ShowRewardOverlay()
    {
        const float duration = 0.3f;

        rewardPanel.RemoveFromClassList("hidden");
        rewardScrim.style.opacity = 0f;
        rewardModalCard.style.opacity = 0f;
        rewardModalCard.style.scale = new Scale(new Vector3(0.9f, 0.9f, 1f));

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            rewardScrim.style.opacity = progress;
            rewardModalCard.style.opacity = progress;
            float scale = Mathf.Lerp(0.9f, 1f, progress);
            rewardModalCard.style.scale = new Scale(new Vector3(scale, scale, 1f));
            yield return null;
        }

        rewardScrim.style.opacity = 1f;
        rewardModalCard.style.opacity = 1f;
        rewardModalCard.style.scale = new Scale(Vector3.one);
    }

    IEnumerator HideRewardOverlay()
    {
        const float duration = 0.25f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = 1f - Mathf.Clamp01(elapsed / duration);
            rewardScrim.style.opacity = progress;
            rewardModalCard.style.opacity = progress;
            yield return null;
        }

        rewardScrim.style.opacity = 0f;
        rewardModalCard.style.opacity = 0f;
        rewardPanel.AddToClassList("hidden");
    }

    // 8.2/10.6: тряска закрытого сундука, открытие, рулетка иконок предметов, скип, вспышка на приземлении.
    IEnumerator ChestRevealFlow(ChestReward reward)
    {
        chestRevealContainer.style.display = DisplayStyle.Flex;
        chestSpriteImage.image = chestClosedTexture;
        chestReelStrip.Clear();
        chestSpriteImage.style.translate = new Translate(0, 0, 0);

        // 8.2 (уточнено): сундук трясётся закрытым ~1с, затем переключается на открытый — и только
        // ПОСЛЕ этого начинается формирование ленты (не одновременно с открытием, как раньше).
        yield return ChestRevealAnimator.ShakeChest(chestSpriteImage);
        chestSpriteImage.image = chestOpenTexture;

        // 8.2: лента из ~20 иконок предметов, взятых из пула каталога (те же иконки, что уже
        // назначены в Task 2) — случайный подбор с повторами, если в каталоге меньше 20 предметов.
        var pool = rewardManager.itemCatalog != null ? rewardManager.itemCatalog.items : null;
        if (pool == null || pool.Length == 0)
        {
            // Пустой каталог — деградируем на мгновенный переход к итогу без ленты, не зависаем.
            chestRevealContainer.style.display = DisplayStyle.None;
            yield break;
        }

        // 8.2 (уточнено): паддинг-иконки с обеих сторон — та же "шумовая" логика, что и остальные
        // ~19 слотов (случайный предмет + случайная фальшивая редкость), просто вне видимого при
        // покое диапазона. Итоговый индекс победного слота в массиве смещён на chestReelPadding.
        int winningIndex = ChestRevealAnimator.ReelPadding + ChestRevealAnimator.WinningLogicalIndex;
        Sprite winningIcon = reward.Item != null ? reward.Item.icon : pool[0].icon;

        void BuildSlot(int index, bool isWinning)
        {
            Sprite iconSprite = isWinning ? winningIcon : pool[Random.Range(0, pool.Length)].icon;
            var icon = new Image { sprite = iconSprite };
            icon.AddToClassList("chest-reel-icon");
            icon.AddToClassList(isWinning ? ChestReelBgClassFor(reward.ItemRarity) : ChestReelBgClassFor(rewardManager.RollItemRarity(false)));
            chestReelStrip.Add(icon);
        }

        yield return ChestRevealAnimator.PlayReel(chestReelStrip, chestReelViewport, BuildSlot, chestSkipButton, winningIndex);

        // Вспышка/burst на приземлении (финальный ревью, замена world-space ParticleSystem — см.
        // SpawnChestBurst): UI Toolkit-нативные "искры" внутри chestRevealContainer.
        ChestRevealAnimator.SpawnBurst(chestSpriteImage, chestRevealContainer);

        yield return new WaitForSeconds(0.3f); // короткая пауза на "приземление" перед итоговым текстом

        chestRevealContainer.style.display = DisplayStyle.None;
    }

    // 8.2 (уточнено): фон слота ленты по редкости — переиспользует ту же палитру серый/синий/
    // фиолетовый, что и .rarity-common/.rarity-rare/.rarity-epic (там — цвет текста, здесь —
    // фон, поэтому отдельные CSS-классы, а не переиспользование тех же имён).
    static string ChestReelBgClassFor(ItemTier tier) => tier switch
    {
        ItemTier.Common => "chest-reel-icon-common",
        ItemTier.Rare => "chest-reel-icon-rare",
        _ => "chest-reel-icon-epic"
    };

    // ==================== Сравнение предмета (3.4, "Без инвентаря") ====================

    static string SlotLabel(EquipmentSlot slot)
    {
        switch (slot)
        {
            case EquipmentSlot.Helmet: return "Шлем";
            case EquipmentSlot.Armor: return "Броня";
            case EquipmentSlot.Boots: return "Сапоги";
            case EquipmentSlot.Weapon: return "Оружие";
            case EquipmentSlot.Ring: return "Кольцо";
            default: return "Аксессуар";
        }
    }

    static string ItemStatsText(ItemData item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var lines = new List<string> { $"{SlotLabel(item.slot)}, {RarityLabel(item.tier)}, ур. {item.itemLevel}" };

        if (item.slot == EquipmentSlot.Weapon && item.weaponSubtype != WeaponSubtype.None && item.weaponSubtype != WeaponSubtype.Shield)
        {
            DamageCalculator.ComputeDamageRange(item.EffectiveDamage, out float dmgMin, out float dmgMax);
            lines.Add($"Урон: {dmgMin:F0}-{dmgMax:F0} ({item.damageType}), скорость атаки: {item.attackSpeed:F2}");
        }

        if (item.physicalDefense > 0f)
        {
            lines.Add($"Физ. защита: {item.EffectiveDefense:F0}");
        }

        if (item.maxPhysicalDefenseBonus > 0f)
        {
            lines.Add($"+макс. физ. защита: {item.EffectiveMaxDefenseBonus:F0}");
        }

        if (item.bonusStat != null)
        {
            lines.Add($"{item.bonusStat.type}: {item.bonusStat.baseValue * item.itemLevel:F1}");
        }

        if (item.passiveSkill != null)
        {
            lines.Add($"Пассивка «{item.passiveSkill.skillName}»: {item.passiveSkill.effectDescription}");
        }

        return string.Join("\n", lines);
    }

    // 3.4: если новый предмет подходит сразу в несколько слотов (2 слота оружия/рук, 2 слота
    // колец) — показываем ВСЕ подходящие слоты с их текущим содержимым и даём игроку самому
    // выбрать, какой занять (или отказаться от нового предмета вовсе). Никакого автовыбора слота.
    IEnumerator ItemCompareFlow(ItemData newItem)
    {
        var candidates = characterManager.GetComparisonCandidates(newItem); // null-элемент = свободный слот

        ShowOnly(itemComparePanel);
        newItemName.text = newItem.itemName;
        newItemStats.text = ItemStatsText(newItem);

        slotChoicesContainer.Clear();
        var buttons = new List<Button>();
        foreach (var candidate in candidates)
        {
            var btn = new Button
            {
                text = candidate != null ? $"Заменить: {candidate.itemName}\n{ItemStatsText(candidate)}" : "Занять свободный слот"
            };
            btn.AddToClassList("choice-card");
            slotChoicesContainer.Add(btn);
            buttons.Add(btn);
        }
        buttons.Add(itemDiscardButton);

        yield return WaitForAnyClick(buttons.ToArray());

        if (clickedIndex < candidates.Count)
        {
            var replacing = candidates[clickedIndex];
            characterManager.EquipItem(newItem, replacing);
            LogEvent($"[Снаряжение] Надето: {newItem.itemName}{(replacing != null ? $" (заменён {replacing.itemName})" : string.Empty)}.");
        }
        else
        {
            LogEvent($"[Снаряжение] Выброшено: {newItem.itemName}.");
        }
    }

    // ==================== Результаты забега (1 п.7-8, 7.2 п.6) ====================

    IEnumerator ShowResultsFlow(bool victory)
    {
        LogEvent($"[Забег] Завершён: {(victory ? "победа" : "поражение")}.");

        runScreen.style.display = DisplayStyle.None;
        resultsScreen.style.display = DisplayStyle.Flex;

        var completion = rewardManager.CalculateRunCompletionReward(
            victory,
            characterManager.RoomsClearedThisRun,
            dungeonManager.CurrentFloorNumber,
            characterManager.RoomsClearedOnCurrentFloor);
        if (saveManager != null)
        {
            saveManager.CompleteRun(completion.MetaCurrency, completion.GachaCurrency, BuildVeteranSnapshot());
        }

        resultsTitleLabel.text = victory ? "Победа" : "Поражение";
        resultsTitleLabel.RemoveFromClassList(victory ? "results-defeat" : "results-victory");
        resultsTitleLabel.AddToClassList(victory ? "results-victory" : "results-defeat");

        resultsBodyLabel.text = $"{characterManager.Character.characterName} достигла {characterManager.Level} уровня.\n" +
            $"Валюта забега (сгорает): {characterManager.RunCurrency}\n\n" +
            "Награды за забег:\n" +
            $"+{completion.MetaCurrency} мета-валюты\n" +
            $"+{completion.GachaCurrency} гача-валюты";

        yield return WaitForClick(resultsContinueButton);
    }

    VeteranCharacter BuildVeteranSnapshot()
    {
        var veteran = new VeteranCharacter
        {
            characterId = characterManager.Character.characterId,
            // finalHP в схеме трактуется как финальный максимальный HP-стат персонажа, а не
            // оставшееся после последнего удара здоровье (при поражении оно всегда было бы 0).
            finalHP = characterManager.Combatant.MaxHP,
            // Формула PowerLevel остаётся открытым вопросом ГДД. Не подменяем решение дизайнера.
            powerLevel = 0
        };

        foreach (var pair in characterManager.Progress.KnownSkillLevels)
        {
            if (pair.Key != null)
            {
                veteran.finalSkills.Add(new VeteranSkillEntry { skillName = pair.Key.skillName, level = pair.Value });
            }
        }

        foreach (var item in characterManager.EquippedItems)
        {
            if (item != null) veteran.finalEquipment.Add(item.itemName);
        }

        return veteran;
    }

    // ==================== Общие UI-хелперы ====================

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
