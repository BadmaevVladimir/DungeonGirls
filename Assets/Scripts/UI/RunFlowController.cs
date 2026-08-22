using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Фаза 4: единственный оркестратор всего цикла забега (7.2), связывающий UI Toolkit с уже
// реализованными менеджерами (Фазы 1-3.5). Хаб/меню зданий/гача/торговец вне скоупа — торговец
// показан заглушкой, привал и ловушки/квесты — минимальным текстовым UI (3.8: только плоские
// прямоугольники/лейблы).
public class RunFlowController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] UIDocument uiDocument;

    [Header("Контент (Фаза 2)")]
    [SerializeField] CharacterData jenniferCharacter;
    [SerializeField] List<PassiveSkillData> generalSkillPool;
    [SerializeField] List<PassiveSkillData> warriorSkillPool;
    [SerializeField] List<MonsterData> regularMonsterPool;
    [SerializeField] MonsterData bossData;

    [Header("Менеджеры")]
    [SerializeField] DungeonManager dungeonManager;
    [SerializeField] FloorManager floorManager;
    [SerializeField] CampManager campManager;
    [SerializeField] CombatManager combatManager;
    [SerializeField] RewardManager rewardManager;
    [SerializeField] LevelUpManager levelUpManager;
    [SerializeField] CharacterManager characterManager;

    // --- Экраны верхнего уровня ---
    VisualElement mainMenuScreen;
    Button startRunButton;
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
    VisualElement eventPopup;
    VisualElement trapPopup;
    VisualElement levelUpPanel;
    VisualElement campPanel;
    VisualElement merchantPanel;
    VisualElement rewardPanel;

    // --- Бой ---
    Label playerNameLabel;
    VisualElement playerHpFill;
    Label playerHpText;
    Label playerDefenseText;
    Label playerShieldText;
    VisualElement enemyListContainer;
    ScrollView combatLogScroll;
    Label combatLogText;
    Toggle autoModeToggle;
    Button activeSkillButton;
    readonly List<string> combatLogLines = new List<string>();

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

    // --- Награда ---
    Label rewardText;
    Button rewardContinueButton;

    // --- Сравнение предмета (3.4, "Без инвентаря") ---
    VisualElement itemComparePanel;
    Label newItemName;
    Label newItemStats;
    Label currentItemName;
    Label currentItemStats;
    Button itemEquipButton;
    Button itemDiscardButton;

    // Служебное состояние ожидания клика/выбора между кадрами корутины.
    int clickedIndex;
    bool chanceAttempted;
    bool chanceSucceeded;
    bool skipNextAutoCamp;
    int totalRoomsThisFloorCached;

    void OnEnable()
    {
        var root = uiDocument.rootVisualElement;
        CacheElements(root);
        startRunButton.clicked += BeginRunFromMenu;
        resultsContinueButton.clicked += ReturnToMainMenu;
        autoModeToggle.RegisterValueChangedCallback(evt => combatManager.SetActiveSkillAutoMode(evt.newValue));
        activeSkillButton.clicked += () => combatManager.TryActivateUniqueActiveSkill();
    }

    public void BeginRunFromMenu()
    {
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
        runScreen = root.Q<VisualElement>("RunScreen");
        resultsScreen = root.Q<VisualElement>("ResultsScreen");
        resultsTitleLabel = root.Q<Label>("ResultsTitleLabel");
        resultsBodyLabel = root.Q<Label>("ResultsBodyLabel");
        resultsContinueButton = root.Q<Button>("ResultsContinueButton");

        floorLabel = root.Q<Label>("FloorLabel");
        rationsLabel = root.Q<Label>("RationsLabel");
        roomProgressContainer = root.Q<VisualElement>("RoomProgressContainer");

        combatPanel = root.Q<VisualElement>("CombatPanel");
        eventPopup = root.Q<VisualElement>("EventPopup");
        trapPopup = root.Q<VisualElement>("TrapPopup");
        levelUpPanel = root.Q<VisualElement>("LevelUpPanel");
        campPanel = root.Q<VisualElement>("CampPanel");
        merchantPanel = root.Q<VisualElement>("MerchantPanel");
        rewardPanel = root.Q<VisualElement>("RewardPanel");
        itemComparePanel = root.Q<VisualElement>("ItemComparePanel");

        playerNameLabel = root.Q<Label>("PlayerNameLabel");
        playerHpFill = root.Q<VisualElement>("PlayerHpFill");
        playerHpText = root.Q<Label>("PlayerHpText");
        playerDefenseText = root.Q<Label>("PlayerDefenseText");
        playerShieldText = root.Q<Label>("PlayerShieldText");
        enemyListContainer = root.Q<VisualElement>("EnemyListContainer");
        combatLogScroll = root.Q<ScrollView>("CombatLogScroll");
        combatLogText = root.Q<Label>("CombatLogText");
        autoModeToggle = root.Q<Toggle>("AutoModeToggle");
        activeSkillButton = root.Q<Button>("ActiveSkillButton");

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

        rewardText = root.Q<Label>("RewardText");
        rewardContinueButton = root.Q<Button>("RewardContinueButton");

        newItemName = root.Q<Label>("NewItemName");
        newItemStats = root.Q<Label>("NewItemStats");
        currentItemName = root.Q<Label>("CurrentItemName");
        currentItemStats = root.Q<Label>("CurrentItemStats");
        itemEquipButton = root.Q<Button>("ItemEquipButton");
        itemDiscardButton = root.Q<Button>("ItemDiscardButton");
    }

    // ==================== Главный цикл забега (Core Loop, раздел 1) ====================

    IEnumerator RunLoop()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        runScreen.style.display = DisplayStyle.Flex;

        levelUpManager.GeneralSkillPool = generalSkillPool;
        levelUpManager.WarriorSkillPool = warriorSkillPool;

        characterManager.BeginRun(jenniferCharacter);
        campManager.BeginRun();
        dungeonManager.SetRunState(RunState.RunSetup);
        dungeonManager.GenerateDungeon();
        dungeonManager.SetRunState(RunState.InFloor);

        bool victory = false;

        while (true)
        {
            floorManager.SetFloorState(FloorState.FloorStart);
            floorManager.GenerateRoomBag();
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

    int RollMonsterCount(int level)
    {
        if (level <= 3) return 1;
        if (level <= 6) return Random.Range(1, 3); // 1-2
        return Random.Range(1, 4); // 1-3
    }

    IEnumerator CombatRoomFlow(bool isBoss)
    {
        var enemies = new List<CombatantRuntime>();
        if (isBoss)
        {
            enemies.Add(CombatantFactory.CreateMonsterCombatant(bossData, dungeonManager.CurrentFloorNumber));
        }
        else
        {
            int count = RollMonsterCount(characterManager.Level);
            for (int i = 0; i < count; i++)
            {
                var data = regularMonsterPool[Random.Range(0, regularMonsterPool.Count)];
                enemies.Add(CombatantFactory.CreateMonsterCombatant(data, dungeonManager.CurrentFloorNumber));
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

        int activeLevel = characterManager.Progress.UniqueActiveLevel;
        float activeMultiplier = activeLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f };
        combatManager.ConfigureUniqueActiveSkill(3, activeMultiplier, jenniferCharacter.uniqueActiveSkill.cooldownSeconds, autoModeToggle.value);

        combatLogLines.Clear();
        combatManager.LogMessage += OnCombatLog;
        ShowOnly(combatPanel);
        combatManager.StartCombat(characterManager.Combatant, enemies);

        while (combatManager.IsCombatActive)
        {
            UpdateCombatUI();
            yield return null;
        }

        UpdateCombatUI();
        combatManager.LogMessage -= OnCombatLog;

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

        var levelsGained = characterManager.GrantExperience(rewardManager, isBoss ? ExperienceSource.Boss : ExperienceSource.CombatRoom);
        foreach (var _ in levelsGained)
        {
            yield return LevelUpFlow();
        }

        yield return ShowRewardChestFlow(dungeonManager.CurrentFloorNumber, isBoss);
    }

    void OnCombatLog(string message)
    {
        combatLogLines.Add(message);
        if (combatLogLines.Count > 60)
        {
            combatLogLines.RemoveAt(0);
        }
    }

    void UpdateCombatUI()
    {
        ShowOnly(combatPanel);

        var player = combatManager.Player;
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

        combatLogText.text = string.Join("\n", combatLogLines);
        combatLogScroll.schedule.Execute(() => combatLogScroll.scrollOffset = new Vector2(0f, float.MaxValue));

        bool ready = combatManager.IsActiveSkillReady;
        activeSkillButton.SetEnabled(!autoModeToggle.value && ready);
        activeSkillButton.text = ready ? "Активный навык (готов)" : $"Активный навык ({combatManager.ActiveSkillCooldownRemaining:F1}с)";
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
            var btn = new Button { text = option.ToString() };
            btn.AddToClassList("choice-card");
            levelUpCardsContainer.Add(btn);
            buttons.Add(btn);
        }

        yield return WaitForAnyClick(buttons.ToArray());
        levelUpManager.ApplyChoice(characterManager.Progress, options[clickedIndex]);
        characterManager.RefreshCombatStats();
    }

    // ==================== Привал (раздел 6) ====================

    // 6.1: триггер привала — явное решение игрока. Игра предлагает встать на привал; если
    // игрок отказывается, рацион не тратится и автоматика 6.2 не запускается.
    IEnumerator CampOfferAndPhaseCoroutine()
    {
        ShowOnly(campPanel);
        campText.text = $"Можно встать на привал (потратит 1 рацион). Осталось рационов: {campManager.RationsRemaining}.";
        SetCampOfferButtonsVisible(true);

        yield return WaitForAnyClick(campAcceptButton, campDeclineButton);
        SetCampOfferButtonsVisible(false);

        bool accepted = clickedIndex == 0;
        if (!accepted)
        {
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

        campText.text = "Дженифер отдыхает у привала..." +
            $"\n+{result.HpRestored:F0} HP" +
            (result.ArmorRestored > 0f ? $", +{result.ArmorRestored:F0} физ. защиты (Полевой ремонт)" : string.Empty) +
            $"\nОсталось рационов: {campManager.RationsRemaining}";

        yield return WaitForClick(campContinueButton);
    }

    // ==================== Торговец (заглушка, 5.2 вне скоупа) ====================

    IEnumerator MerchantRoomFlow()
    {
        ShowOnly(merchantPanel);
        yield return WaitForClick(merchantContinueButton);
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
        var reward = rewardManager.CalculateRewards(floorNumber, isBoss, luckLevel, currencyBonus, noCurrency, goldenTouchLevel);
        characterManager.AddCurrency(reward.Currency);

        ShowOnly(rewardPanel);
        rewardText.text = $"Получено: {reward.Currency} монет забега, {RarityLabel(reward.ItemRarity)} предмет" +
            (reward.BonusReward ? "\n+ дополнительная награда (Удача)" : string.Empty) +
            $"\nВсего валюты забега: {characterManager.RunCurrency}";
        SetRarityClass(rewardText, reward.ItemRarity);

        yield return WaitForClick(rewardContinueButton);

        if (reward.Item != null)
        {
            yield return ItemCompareFlow(reward.Item);
        }
    }

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
            lines.Add($"Урон: {item.baseDamage:F0} ({item.damageType}), скорость атаки: {item.attackSpeed:F2}");
        }

        if (item.physicalDefense > 0f)
        {
            lines.Add($"Физ. защита: {item.physicalDefense:F0}");
        }

        if (item.maxPhysicalDefenseBonus > 0f)
        {
            lines.Add($"+макс. физ. защита: {item.maxPhysicalDefenseBonus:F0}");
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

    // Показывает новый предмет рядом с текущим надетым в том же слоте (или "свободный слот", если
    // сравнивать не с чем) и ждёт выбора игрока: надеть или выбросить.
    IEnumerator ItemCompareFlow(ItemData newItem)
    {
        var currentItem = characterManager.GetComparisonTarget(newItem);

        ShowOnly(itemComparePanel);
        newItemName.text = newItem.itemName;
        newItemStats.text = ItemStatsText(newItem);
        currentItemName.text = currentItem != null ? currentItem.itemName : "(свободный слот)";
        currentItemStats.text = currentItem != null ? ItemStatsText(currentItem) : string.Empty;

        yield return WaitForAnyClick(itemEquipButton, itemDiscardButton);

        if (clickedIndex == 0)
        {
            characterManager.EquipItem(newItem, currentItem);
        }
    }

    // ==================== Результаты забега (1 п.7-8, 7.2 п.6) ====================

    IEnumerator ShowResultsFlow(bool victory)
    {
        runScreen.style.display = DisplayStyle.None;
        resultsScreen.style.display = DisplayStyle.Flex;

        var completion = rewardManager.CalculateRunCompletionReward(victory);

        resultsTitleLabel.text = victory ? "Победа" : "Поражение";
        resultsTitleLabel.RemoveFromClassList(victory ? "results-defeat" : "results-victory");
        resultsTitleLabel.AddToClassList(victory ? "results-victory" : "results-defeat");

        resultsBodyLabel.text = $"Дженифер достигла {characterManager.Level} уровня.\n" +
            $"Валюта забега (сгорает): {characterManager.RunCurrency}\n\n" +
            "Награды за забег:\n" +
            $"+{completion.MetaCurrency} мета-валюты\n" +
            $"+{completion.GachaCurrency} гача-валюты";

        yield return WaitForClick(resultsContinueButton);
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
        foreach (var panel in new[] { combatPanel, eventPopup, trapPopup, levelUpPanel, campPanel, merchantPanel, rewardPanel, itemComparePanel })
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
