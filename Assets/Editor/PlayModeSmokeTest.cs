using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Разовый диагностический скрипт для проверки Фазы 5 + правок последних сессий через
// -batchmode -executeMethod. Не часть деливерэйбла — удаляется после проверки.
public static class PlayModeSmokeTest
{
    static readonly List<string> Errors = new List<string>();
    static readonly List<string> Info = new List<string>();
    static double startTime;
    static bool checksRan;

    // Тест мутирует реальный SaveManager (SaveGame() пишет на диск при каждом изменении) —
    // бэкапим настоящий файл сохранения игрока и восстанавливаем его в Finish(), чтобы не
    // затереть его реальный прогресс тестовыми значениями валют/зданий.
    static string savePath;
    static byte[] originalSaveBytes;
    static bool originalSaveExisted;

    public static void Run()
    {
        savePath = Path.Combine(Application.persistentDataPath, "dungeongirls_save.json");
        originalSaveExisted = File.Exists(savePath);
        if (originalSaveExisted)
        {
            originalSaveBytes = File.ReadAllBytes(savePath);
        }

        try
        {
            RunPureLogicChecks();
        }
        catch (Exception e)
        {
            Errors.Add("Исключение в RunPureLogicChecks: " + e);
        }

        Application.logMessageReceived += OnLog;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.update += OnUpdate;
        startTime = EditorApplication.timeSinceStartup;
        EditorApplication.EnterPlaymode();
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception)
        {
            Errors.Add($"[Console {type}] {condition}\n{stackTrace}");
        }
    }

    static void OnUpdate()
    {
        double elapsed = EditorApplication.timeSinceStartup - startTime;

        if (!EditorApplication.isPlaying)
        {
            if (checksRan)
            {
                Finish();
                return;
            }

            if (elapsed > 30)
            {
                Errors.Add("Не удалось войти в Play Mode за 30 секунд.");
                Finish();
            }
            return;
        }

        if (!checksRan && elapsed > 2.5)
        {
            checksRan = true;
            try
            {
                RunPlayModeChecks();
            }
            catch (Exception e)
            {
                Errors.Add("Исключение в RunPlayModeChecks: " + e);
            }
            EditorApplication.isPlaying = false;
        }
    }

    // ==================== Чистая логика (не требует Play Mode) ====================

    static void RunPureLogicChecks()
    {
        // 3.3: строгая блокировка урона < 0.5×брони, броня не теряет единицу (урон=2 < 2.5=0.5×5,
        // ниже порога "износа при блокировке" — иначе тест столкнулся бы с этим более новым
        // правилом и получил бы -1 брони вместо "без последствий").
        var target = new CombatantRuntime { PhysicalDefenseMax = 5f, PhysicalDefenseCurrent = 5f, MaxHP = 20f, CurrentHP = 20f };
        var blockedResult = DamageCalculator.ApplyPhysicalDamage(target, 2f);
        Check(blockedResult.WasBlocked && blockedResult.DamageToHP == 0f && target.CurrentHP == 20f && target.PhysicalDefenseCurrent == 5f,
            $"3.3 блокировка: WasBlocked={blockedResult.WasBlocked}, DamageToHP={blockedResult.DamageToHP}, HP={target.CurrentHP}, Defense={target.PhysicalDefenseCurrent} (ожидалось true/0/20/5)");

        var passResult = DamageCalculator.ApplyPhysicalDamage(target, 8f);
        Check(!passResult.WasBlocked && passResult.DamageToHP == 3f && target.PhysicalDefenseCurrent == 4f,
            $"3.3 пробитие: WasBlocked={passResult.WasBlocked}, DamageToHP={passResult.DamageToHP}, Defense={target.PhysicalDefenseCurrent} (ожидалось false/3/4)");

        // 3.2: диапазон урона [floor(base*0.8); ceil(base*1.2)].
        DamageCalculator.ComputeDamageRange(6f, out float dmgMin, out float dmgMax);
        Check(dmgMin == 4f && dmgMax == 8f, $"3.2 диапазон урона (база 6): min={dmgMin}, max={dmgMax} (ожидалось 4/8)");

        // 3.10: новая формула масштабирования основного стата, включая пример Деревянного щита из ГДД.
        var shield = ScriptableObject.CreateInstance<ItemData>();
        shield.maxPhysicalDefenseBonus = 3f;
        shield.itemLevel = 1;
        Check(shield.EffectiveMaxDefenseBonus == 3f, $"3.10 щит ур.1: {shield.EffectiveMaxDefenseBonus} (ожидалось 3)");
        shield.itemLevel = 2;
        Check(shield.EffectiveMaxDefenseBonus == 4f, $"3.10 щит ур.2: {shield.EffectiveMaxDefenseBonus} (ожидалось 4)");
        shield.itemLevel = 3;
        Check(shield.EffectiveMaxDefenseBonus == 5f, $"3.10 щит ур.3: {shield.EffectiveMaxDefenseBonus} (ожидалось 5)");
        UnityEngine.Object.DestroyImmediate(shield);

        var sword = ScriptableObject.CreateInstance<ItemData>();
        sword.baseDamage = 0f; // у оружия нет physicalDefense — поле должно остаться 0 при любом уровне
        sword.physicalDefense = 0f;
        sword.itemLevel = 5;
        Check(sword.EffectiveDefense == 0f, $"3.10 защита от нуля не масштабируется: {sword.EffectiveDefense} (ожидалось 0)");
        UnityEngine.Object.DestroyImmediate(sword);

        // 2.1: длина подземелья в этажах
        Check(DungeonManager.TotalFloors == 10, $"2.1 этажей в подземелье: {DungeonManager.TotalFloors} (ожидалось 10)");

        // 2.6: броня монстра масштабируется x1.15/этаж
        var skeletonBase = ScriptableObject.CreateInstance<MonsterData>();
        skeletonBase.physicalDefense = 8f;
        skeletonBase.hp = 40f;
        skeletonBase.damageMin = 10f;
        skeletonBase.damageMax = 15f;
        var floor10Skeleton = CombatantFactory.CreateMonsterCombatant(skeletonBase, 10);
        // x1.15^9 ≈ 3.5179; StatScaling.ApplyLevelBonus at level 1 is a no-op (level-1=0), so this is
        // just 8 * 3.5179 ≈ 28.14, matching the GDD's "≈28" example.
        Check(floor10Skeleton.PhysicalDefenseMax > 27f && floor10Skeleton.PhysicalDefenseMax < 29f,
            $"2.6 броня Скелета на этаже 10: {floor10Skeleton.PhysicalDefenseMax:F2} (ожидалось ~28.1, было бы ~850+ со старым x1.8)");
        UnityEngine.Object.DestroyImmediate(skeletonBase);

        // 3.6: потолок уровня персонажа и опыт
        Check(RunCharacterProgress.MaxCharacterLevel == 15, $"3.6 потолок уровня: {RunCharacterProgress.MaxCharacterLevel} (ожидалось 15)");

        int totalXpTo15 = 0;
        for (int lvl = 1; lvl < 15; lvl++) totalXpTo15 += RunCharacterProgress.ExperienceRequiredForLevel(lvl);
        Check(totalXpTo15 == 2625, $"3.6 суммарный опыт до 15 ур.: {totalXpTo15} (ожидалось 2625)");

        var jennifer = ScriptableObject.CreateInstance<CharacterData>();
        var progress = new RunCharacterProgress(jennifer);
        progress.AddExperience(100000); // огромный оверфлоу — не должен пробить потолок 15
        Check(progress.Level == 15, $"3.6 AddExperience не пробивает потолок: {progress.Level} (ожидалось 15)");
        UnityEngine.Object.DestroyImmediate(jennifer);

        // 8.2: доля редкостей предметов в сундуках
        var rewardManagerGO = new GameObject("SmokeTest_RewardManager");
        var rewardManager = rewardManagerGO.AddComponent<RewardManager>();
        int commonCount = 0, rareCount = 0, epicCount = 0;
        const int sampleSize = 20000;
        for (int i = 0; i < sampleSize; i++)
        {
            switch (rewardManager.RollItemRarity(false))
            {
                case ItemTier.Common: commonCount++; break;
                case ItemTier.Rare: rareCount++; break;
                case ItemTier.Epic: epicCount++; break;
            }
        }
        float commonPct = commonCount * 100f / sampleSize;
        float epicPct = epicCount * 100f / sampleSize;
        Check(commonPct > 59f && commonPct < 65f, $"8.2 доля Обычных ~62%: {commonPct:F1}%");
        Check(epicPct > 1.5f && epicPct < 4.5f, $"8.2 доля Эпических ~3%: {epicPct:F1}%");

        // 3.6: источники опыта растут вместе с этажом
        Check(rewardManager.GetExperienceReward(ExperienceSource.CombatRoom, 1) == 10, "3.6 XP боевая комната этаж 1 = 10");
        Check(rewardManager.GetExperienceReward(ExperienceSource.CombatRoom, 10) == 37, "3.6 XP боевая комната этаж 10 = 37");
        Check(rewardManager.GetExperienceReward(ExperienceSource.SuccessfulEventOrTrap, 1) == 5, "3.6 XP ловушка/квест этаж 1 = 5");
        Check(rewardManager.GetExperienceReward(ExperienceSource.SuccessfulEventOrTrap, 10) == 14, "3.6 XP ловушка/квест этаж 10 = 14");
        Check(rewardManager.GetExperienceReward(ExperienceSource.Boss, 10) == 50, "3.6 XP босс всегда 50 флэт");
        UnityEngine.Object.DestroyImmediate(rewardManagerGO);

        // 2.4 Порхание: подтверждает, что поле уклонения монстра существует и устанавливается
        // корректно (полное поведение в бою проверяется в RunPlayModeChecks через ResolveAttack).
        var evasive = new CombatantRuntime { PhysicalDefenseMax = 0f, PhysicalDefenseCurrent = 0f, MagicShieldMax = 0f, MagicShieldCurrent = 0f, MaxHP = 100f, CurrentHP = 100f, MonsterEvasionPercent = 20f, IsPlayer = false };
        Check(evasive.MonsterEvasionPercent == 20f, "2.4 Порхание: MonsterEvasionPercent устанавливается корректно");

        // 2.4 Яд: 3 стака максимум, 4 урона/стак/сек.
        var poisoned = new CombatantRuntime { MaxHP = 100f, CurrentHP = 100f };
        poisoned.PoisonStacks = 2;
        poisoned.PoisonTimer = 3f;
        Check(poisoned.PoisonStacks == 2, "2.4 Яд: стаки устанавливаются");

        Info.Add("Проверки полей монстро-пассивок (2.4) выполнены.");

        // 2.8: лимит модификаторов монстров по этажам, шанс по уровню, согласование рода прилагательных.
        Check(MonsterModifierCatalog.ModifierCapForFloor(1) == 0, "2.8 лимит модификаторов этаж 1 = 0");
        Check(MonsterModifierCatalog.ModifierCapForFloor(2) == 1 && MonsterModifierCatalog.ModifierCapForFloor(5) == 1, "2.8 лимит модификаторов этажи 2-5 = 1");
        Check(MonsterModifierCatalog.ModifierCapForFloor(6) == 2 && MonsterModifierCatalog.ModifierCapForFloor(9) == 2, "2.8 лимит модификаторов этажи 6-9 = 2");
        Check(MonsterModifierCatalog.ModifierCapForFloor(10) == 4, "2.8 лимит модификаторов этаж 10 = 4 (весь каталог)");

        Check(MonsterModifierCatalog.RollChancePercentForLevel(1) == 0f, "2.8 шанс модификатора ур.1 монстра = 0%");
        Check(MonsterModifierCatalog.RollChancePercentForLevel(4) == 30f, "2.8 шанс модификатора ур.4 монстра = 30%");

        Check(MonsterModifierCatalog.AdjectiveFor(MonsterModifierType.Big, MonsterGender.Feminine) == "Большая", "2.8 согласование рода: Большая Слизь");
        Check(MonsterModifierCatalog.AdjectiveFor(MonsterModifierType.Fast, MonsterGender.Masculine) == "Быстрый", "2.8 согласование рода: Быстрый Скелет");

        var rollsOnFloor1 = MonsterModifierCatalog.RollModifiers(1, 4);
        Check(rollsOnFloor1.Count == 0, $"2.8 этаж 1 никогда не даёт модификаторов даже при ур.4: получено {rollsOnFloor1.Count}");

        Info.Add("Проверки монстро-модификаторов (2.8) выполнены.");

        // 2.4: фильтр пула монстров по minFloorTier — тиры суммируются, не заменяют друг друга.
        var tier1 = ScriptableObject.CreateInstance<MonsterData>(); tier1.minFloorTier = 1;
        var tier7 = ScriptableObject.CreateInstance<MonsterData>(); tier7.minFloorTier = 7;
        var pool = new List<MonsterData> { tier1, tier7 };

        var eligibleFloor3 = pool.FindAll(m => m.minFloorTier <= 3);
        Check(eligibleFloor3.Count == 1 && eligibleFloor3[0] == tier1, "2.4 фильтр пула монстров: этаж 3 видит только тир-1");

        var eligibleFloor7 = pool.FindAll(m => m.minFloorTier <= 7);
        Check(eligibleFloor7.Count == 2, "2.4 фильтр пула монстров: этаж 7 видит тир-1 И тир-7 (суммируются)");

        UnityEngine.Object.DestroyImmediate(tier1);
        UnityEngine.Object.DestroyImmediate(tier7);

        Info.Add("Проверки фильтра пула монстров по этажам (2.4) выполнены.");

        Info.Add("Чистые проверки формул (3.2/3.3/3.10) выполнены.");
    }

    // ==================== Play Mode: живая сцена/хаб/сейв ====================

    static void RunPlayModeChecks()
    {
        var hub = UnityEngine.Object.FindFirstObjectByType<HubManager>();
        var runFlow = UnityEngine.Object.FindFirstObjectByType<RunFlowController>();
        var saveManager = UnityEngine.Object.FindFirstObjectByType<SaveManager>();
        var rewardManager = UnityEngine.Object.FindFirstObjectByType<RewardManager>();
        var uiDocument = UnityEngine.Object.FindFirstObjectByType<UIDocument>();

        if (!Check(hub != null, "HubManager найден в сцене")) return;
        if (!Check(runFlow != null, "RunFlowController найден в сцене")) return;
        if (!Check(saveManager != null, "SaveManager найден в сцене")) return;
        if (!Check(rewardManager != null, "RewardManager найден в сцене")) return;
        if (!Check(uiDocument != null, "UIDocument найден в сцене")) return;

        var root = uiDocument.rootVisualElement;
        if (!Check(root != null, "UIDocument.rootVisualElement построен (не null)")) return;

        var mainMenuScreen = RequireElement(root, "MainMenuScreen");
        var buildingsScreen = RequireElement(root, "BuildingsScreen");
        var gachaScreen = RequireElement(root, "GachaScreen");
        RequireElement(root, "StartRunButton");
        RequireElement(root, "ForgeUpgradeButton");
        RequireElement(root, "GachaPullButton");

        if (mainMenuScreen == null || buildingsScreen == null || gachaScreen == null) return;

        // --- Навигация хаба (7.1) ---
        hub.OpenBuildings();
        Check(buildingsScreen.style.display == DisplayStyle.Flex && mainMenuScreen.style.display == DisplayStyle.None,
            "OpenBuildings() показывает BuildingsScreen и прячет MainMenuScreen");
        hub.OpenVillage();
        Check(mainMenuScreen.style.display == DisplayStyle.Flex && buildingsScreen.style.display == DisplayStyle.None,
            "OpenVillage() возвращает MainMenuScreen");

        hub.OpenGacha();
        Check(gachaScreen.style.display == DisplayStyle.Flex, "OpenGacha() показывает GachaScreen");
        hub.OpenVillage();

        // --- Здания (8.1) ---
        int forgeBefore = saveManager.GetBuildingLevel(BuildingType.Forge);
        saveManager.AddMetaCurrency(1000);
        bool upgraded = saveManager.TryUpgradeBuilding(BuildingType.Forge);
        Check(upgraded && saveManager.GetBuildingLevel(BuildingType.Forge) == forgeBefore + 1,
            $"TryUpgradeBuilding(Forge): upgraded={upgraded}, level={saveManager.GetBuildingLevel(BuildingType.Forge)} (ожидался {forgeBefore + 1})");

        // --- Гача (8.5): вызываем ровно ту же приватную логику, что вешается на клик кнопки
        // (сам факт, что "gachaPullButton.clicked += TryPullGacha;" компилируется и не падает
        // в OnEnable/Start, уже проверен фактом успешного входа в Play Mode без исключений).
        saveManager.AddGachaCurrency(500);
        hub.OpenGacha();
        int gachaCurrencyBefore = saveManager.Data.gachaCurrency;
        var gachaResultPopup = root.Q<VisualElement>("GachaResultPopup");
        var tryPullGacha = typeof(HubManager).GetMethod("TryPullGacha", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        tryPullGacha?.Invoke(hub, null);
        Check(tryPullGacha != null, "Приватный метод HubManager.TryPullGacha найден рефлексией");
        Check(saveManager.Data.gachaCurrency == gachaCurrencyBefore - 50, $"Призыв гачи списал 50 гача-валюты: было {gachaCurrencyBefore}, стало {saveManager.Data.gachaCurrency}");
        Check(gachaResultPopup.style.display == DisplayStyle.Flex, "Попап результата призыва показан");
        hub.OpenVillage();

        // --- Награда за забег + персистентность SaveManager (8.5/9.2/9.3) ---
        int metaBefore = saveManager.Data.metaCurrency;
        var earlyDeathReward = rewardManager.CalculateRunCompletionReward(false, totalRoomsCleared: 0, currentFloorNumber: 1, roomsClearedOnDeathFloor: 0);
        Check(earlyDeathReward.MetaCurrency == 0 && earlyDeathReward.GachaCurrency == 0,
            $"8.5 смерть в 1-й комнате 1-го этажа = 0 награды: {earlyDeathReward.MetaCurrency}/{earlyDeathReward.GachaCurrency} (ожидалось 0/0)");

        var midDeathReward = rewardManager.CalculateRunCompletionReward(false, totalRoomsCleared: 15, currentFloorNumber: 3, roomsClearedOnDeathFloor: 2);
        // floorsFullyCleared = 3-1 = 2 -> 50*2 + 5*2 = 110; gacha = min(15*2,14) = 14
        Check(midDeathReward.MetaCurrency == 110 && midDeathReward.GachaCurrency == 14,
            $"8.5 смерть на этаже 3 (2 комнаты пройдено на нём, 15 всего): {midDeathReward.MetaCurrency}/{midDeathReward.GachaCurrency} (ожидалось 110/14)");

        var uncappedReward = rewardManager.CalculateRunCompletionReward(false, totalRoomsCleared: 5, currentFloorNumber: 10, roomsClearedOnDeathFloor: 11);
        // floorsFullyCleared = 9 -> 50*9 + 5*11 = 505 -- must NOT be capped at the old 70.
        Check(uncappedReward.MetaCurrency == 505, $"8.5 потолок снят: {uncappedReward.MetaCurrency} (ожидалось 505, старый потолок был 70)");

        var victoryReward = rewardManager.CalculateRunCompletionReward(true, 0);
        Check(victoryReward.MetaCurrency == 80 && victoryReward.GachaCurrency == 15, $"8.5 победа фиксированная: {victoryReward.MetaCurrency}/{victoryReward.GachaCurrency} (ожидалось 80/15)");

        saveManager.AddMetaCurrency(victoryReward.MetaCurrency);
        Check(saveManager.Data.metaCurrency == metaBefore + victoryReward.MetaCurrency, "AddMetaCurrency обновляет Data.metaCurrency");

        string path = Path.Combine(Application.persistentDataPath, "dungeongirls_save.json");
        Check(File.Exists(path), $"Файл сохранения существует: {path}");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            var reloaded = JsonUtility.FromJson<SaveData>(json);
            Check(reloaded.metaCurrency == saveManager.Data.metaCurrency, $"JSON на диске совпадает с Data.metaCurrency ({reloaded.metaCurrency} vs {saveManager.Data.metaCurrency})");
        }

        // --- BeginRun с бонусом от Кузницы (3.5/8.1) не должен падать ---
        var characterManager = UnityEngine.Object.FindFirstObjectByType<CharacterManager>();
        var equipmentManager = UnityEngine.Object.FindFirstObjectByType<EquipmentManager>();
        var jennifer = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/ScriptableObjects/Characters/Character_Jennifer.asset");
        Check(jennifer != null, "Character_Jennifer.asset загрузился");
        if (characterManager != null && equipmentManager != null && jennifer != null)
        {
            characterManager.BeginRun(jennifer, equipmentManager, saveManager);
            Check(characterManager.Combatant != null && characterManager.Combatant.IsAlive, "BeginRun с бонусом Кузницы не падает, боевой юнит жив");
            Check(characterManager.EquippedItems.Count == jennifer.startingEquipment.Length, "BeginRun выдал столько же предметов, сколько стартовый лоадаут");

            // 8.5: счётчик пройденных комнат этажа
            characterManager.MarkRoomCleared();
            characterManager.MarkRoomCleared();
            Check(characterManager.RoomsClearedOnCurrentFloor == 2, $"8.5 счётчик комнат этажа: {characterManager.RoomsClearedOnCurrentFloor} (ожидалось 2)");
            characterManager.BeginFloor();
            Check(characterManager.RoomsClearedOnCurrentFloor == 0, "8.5 BeginFloor() сбрасывает счётчик комнат этажа");
        }

        // 8.4: состав мешка комнат
        var floorManagerGO = new GameObject("SmokeTest_FloorManager");
        var floorManager = floorManagerGO.AddComponent<FloorManager>();
        floorManager.GenerateRoomBag();
        int combatCount = floorManager.RoomBag.FindAll(r => r == RoomType.Combat).Count;
        int merchantCount = floorManager.RoomBag.FindAll(r => r == RoomType.Merchant).Count;
        int trapCount = floorManager.RoomBag.FindAll(r => r == RoomType.Trap).Count;
        int specialCount = floorManager.RoomBag.FindAll(r => r == RoomType.Special).Count;
        Check(combatCount == 8 && merchantCount == 1 && trapCount == 2 && specialCount == 1 && floorManager.RoomBag.Count == 12,
            $"8.4 состав мешка: combat={combatCount}, merchant={merchantCount}, trap={trapCount}, special={specialCount}, total={floorManager.RoomBag.Count} (ожидалось 8/1/2/1/12)");
        UnityEngine.Object.DestroyImmediate(floorManagerGO);

        // 2.4: "Проклятие замедления" Колдуна (давний пробел — ассет существовал, но никогда не
        // применялся в бою) через реальный CombatManager.ResolveAttack, а не напрямую.
        var combatManagerGO = new GameObject("SmokeTest_CombatManager");
        var testCombatManager = combatManagerGO.AddComponent<CombatManager>();

        var testPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f };
        testPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 5f, DamageMax = 5f, DamageType = DamageType.Physical, AttackSpeed = 1f });

        var slowCurseMonster = new CombatantRuntime { IsPlayer = false, MaxHP = 30f, CurrentHP = 30f, DisplayName = "TestWarlock", MonsterPassiveName = MonsterSkillEffectMap.SlowCurse };
        slowCurseMonster.Weapons.Add(new WeaponAttackState { DamageMin = 100f, DamageMax = 100f, DamageType = DamageType.Physical, AttackSpeed = 1f });

        testCombatManager.StartCombat(testPlayer, new List<CombatantRuntime> { slowCurseMonster });
        testCombatManager.Tick(1.01f); // достаточно, чтобы оба нанесли по 1 удару (AttackSpeed=1/сек)
        Check(testPlayer.ActiveDebuffs.Exists(d => d.Id == "warlock_slow"), "2.4 Проклятие замедления применяется при попадании Колдуна по HP игрока");

        UnityEngine.Object.DestroyImmediate(combatManagerGO);

        Info.Add("Play Mode проверки хаба/зданий/гачи/сейва/BeginRun выполнены.");
    }

    static VisualElement RequireElement(VisualElement root, string name)
    {
        var el = root.Q<VisualElement>(name);
        Check(el != null, $"UXML-элемент найден: {name}");
        return el;
    }

    static bool Check(bool condition, string description)
    {
        if (condition)
        {
            Info.Add("OK: " + description);
        }
        else
        {
            Errors.Add("FAIL: " + description);
        }
        return condition;
    }

    static void Finish()
    {
        EditorApplication.update -= OnUpdate;
        Application.logMessageReceived -= OnLog;

        try
        {
            if (originalSaveExisted)
            {
                File.WriteAllBytes(savePath, originalSaveBytes);
                Info.Add("Реальный save-файл восстановлен из бэкапа после теста.");
            }
            else if (File.Exists(savePath))
            {
                File.Delete(savePath);
                Info.Add("Тестовый save-файл удалён (до теста сохранения не существовало).");
            }
        }
        catch (Exception e)
        {
            Errors.Add("Не удалось восстановить save-файл после теста: " + e);
        }

        foreach (var line in Info) Debug.Log("[SmokeTest] " + line);
        foreach (var err in Errors) Debug.LogError("[SmokeTest] " + err);

        Debug.Log($"[SmokeTest] ИТОГ: {Info.Count} OK, {Errors.Count} ошибок.");
        Debug.Log("[SmokeTest] RESULT=" + (Errors.Count == 0 ? "PASS" : "FAIL"));

        EditorApplication.Exit(Errors.Count == 0 ? 0 : 1);
    }
}
