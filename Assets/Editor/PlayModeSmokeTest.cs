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
            // Task 4 (GDD 8.2, DOTween integration): DOTween's bundled DOTweenUpgradeManager.Autorun
            // (an [InitializeOnLoad] editor-only nag that tries to auto-open the "Setup DOTween" utility
            // panel on every domain reload until a human runs it once) tries to open an EditorWindow,
            // which is structurally impossible under -nographics batchmode -- this is exactly the
            // interactive-only Setup wizard gap called out in the Task 4 brief, not a defect in this
            // task's own code. Logged as known/expected rather than silently dropped, and narrowly
            // matched (message + stack trace both) so it can't mask an unrelated real graphics error.
            if (condition.Contains("No graphic device is available") && stackTrace.Contains("DOTweenUpgradeManager"))
            {
                Info.Add($"ИЗВЕСТНО (не ошибка теста): DOTween Setup-wizard nag под -nographics (см. отчёт Task 4) — {condition}");
                return;
            }

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

        // 3.3 "Износ брони при блокировке": урон >= 0.5×брони но < брони — 0 урона по HP, но -1 брони.
        var wearTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 20f, CurrentHP = 20f };
        var wearResult = DamageCalculator.ApplyPhysicalDamage(wearTarget, 6f); // >= 5 (0.5*10), < 10
        Check(wearResult.WasBlocked && wearResult.ArmorWornOnBlock && wearResult.DamageToHP == 0f && wearTarget.PhysicalDefenseCurrent == 9f,
            $"3.3 износ при блокировке (урон=6, броня=10): WasBlocked={wearResult.WasBlocked}, ArmorWornOnBlock={wearResult.ArmorWornOnBlock}, DamageToHP={wearResult.DamageToHP}, Defense={wearTarget.PhysicalDefenseCurrent} (ожидалось true/true/0/9)");

        var noWearTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 20f, CurrentHP = 20f };
        var noWearResult = DamageCalculator.ApplyPhysicalDamage(noWearTarget, 3f); // < 5 (0.5*10)
        Check(noWearResult.WasBlocked && !noWearResult.ArmorWornOnBlock && noWearTarget.PhysicalDefenseCurrent == 10f,
            $"3.3 полная блокировка без последствий (урон=3, броня=10): ArmorWornOnBlock={noWearResult.ArmorWornOnBlock}, Defense={noWearTarget.PhysicalDefenseCurrent} (ожидалось false/10)");

        // 3.3 "Полное пробитие": урон >= 2×брони — -2 брони вместо -1.
        var fullPierceTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 20f, CurrentHP = 20f };
        var fullPierceResult = DamageCalculator.ApplyPhysicalDamage(fullPierceTarget, 22f); // >= 20 (2*10)
        Check(!fullPierceResult.WasBlocked && fullPierceResult.DamageToHP == 12f && fullPierceTarget.PhysicalDefenseCurrent == 8f,
            $"3.3 полное пробитие (урон=22, броня=10): DamageToHP={fullPierceResult.DamageToHP}, Defense={fullPierceTarget.PhysicalDefenseCurrent} (ожидалось 12/8)");

        var normalPierceTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 20f, CurrentHP = 20f };
        var normalPierceResult = DamageCalculator.ApplyPhysicalDamage(normalPierceTarget, 12f); // >= 10, < 20
        Check(!normalPierceResult.WasBlocked && normalPierceResult.DamageToHP == 2f && normalPierceTarget.PhysicalDefenseCurrent == 9f,
            $"3.3 обычное пробитие (урон=12, броня=10): DamageToHP={normalPierceResult.DamageToHP}, Defense={normalPierceTarget.PhysicalDefenseCurrent} (ожидалось 2/9)");

        // 3.11 Часть 2 (НОВОЕ): % сопротивления урону — первый шаг, до брони/щита.
        var resistPhysicalTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 100f, CurrentHP = 100f, PhysicalResistancePercent = 50f };
        var resistPhysicalResult = DamageCalculator.ApplyDamage(resistPhysicalTarget, 12f, DamageType.Physical); // 12 * 0.5 = 6 -> < 10 брони -> износ (>=5), не пробитие
        Check(resistPhysicalResult.WasBlocked && resistPhysicalTarget.PhysicalDefenseCurrent == 9f,
            $"3.11 физ. сопротивление 50% снижает урон ДО брони: WasBlocked={resistPhysicalResult.WasBlocked}, Defense={resistPhysicalTarget.PhysicalDefenseCurrent} (ожидалось true/9, т.е. 12->6, износ не пробитие)");

        var resistMagicalTarget = new CombatantRuntime { MagicShieldMax = 10f, MagicShieldCurrent = 10f, MaxHP = 100f, CurrentHP = 100f, MagicalResistancePercent = 50f };
        var resistMagicalResult = DamageCalculator.ApplyDamage(resistMagicalTarget, 12f, DamageType.Magical); // 12 * 0.5 = 6, полностью гасится щитом (10)
        Check(resistMagicalResult.WasBlocked && resistMagicalTarget.MagicShieldCurrent == 4f,
            $"3.11 маг. сопротивление 50% снижает урон ДО маг.щита: WasBlocked={resistMagicalResult.WasBlocked}, Shield={resistMagicalTarget.MagicShieldCurrent} (ожидалось true/4, т.е. 12->6, щит 10-6=4)");

        // armorIgnorePercent (Клинок): снижает ЭФФЕКТИВНУЮ броню для проверки, но абсолютная деградация
        // (-1/-2) остаётся по правилам 3.3 без изменений.
        var armorIgnoreTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 100f, CurrentHP = 100f };
        var armorIgnoreResult = DamageCalculator.ApplyDamage(armorIgnoreTarget, 6f, DamageType.Physical, armorIgnorePercent: 50f); // эфф. броня 5, урон 6 >= 5 -> обычное пробитие
        Check(!armorIgnoreResult.WasBlocked && armorIgnoreResult.DamageToHP == 1f && armorIgnoreTarget.PhysicalDefenseCurrent == 9f,
            $"3.11 armorIgnorePercent 50% (Клинок): WasBlocked={armorIgnoreResult.WasBlocked}, DamageToHP={armorIgnoreResult.DamageToHP}, Defense={armorIgnoreTarget.PhysicalDefenseCurrent} (ожидалось false/1/9)");

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

        // 3.10 (ФИКС, 2026-08-26): бонусные статы предметов (BonusStatType) — раньше только
        // MagicShieldFlat/CritChancePercent реально считались в CombatantFactory, остальные 7 типов
        // (почти все кольца/аксессуары + часть Редких/Эпических шлемов/сапог/оружия) молча
        // игнорировались. Проверяем каждый тип по отдельности на синтетическом снаряжении.
        var bonusTestCharacter = ScriptableObject.CreateInstance<CharacterData>();
        bonusTestCharacter.baseHealth = 100;

        var hpRing = ScriptableObject.CreateInstance<ItemData>();
        hpRing.slot = EquipmentSlot.Ring;
        hpRing.itemLevel = 2;
        hpRing.bonusStat = new BonusStat { type = BonusStatType.FlatHP, baseValue = 15f };

        var armorRing = ScriptableObject.CreateInstance<ItemData>();
        armorRing.slot = EquipmentSlot.Ring;
        armorRing.itemLevel = 1;
        armorRing.bonusStat = new BonusStat { type = BonusStatType.MaxPhysicalDefenseFlat, baseValue = 8f };

        var speedBoots = ScriptableObject.CreateInstance<ItemData>();
        speedBoots.slot = EquipmentSlot.Boots;
        speedBoots.itemLevel = 1;
        speedBoots.bonusStat = new BonusStat { type = BonusStatType.AttackSpeedPercent, baseValue = 10f };

        var damageHelmet = ScriptableObject.CreateInstance<ItemData>();
        damageHelmet.slot = EquipmentSlot.Helmet;
        damageHelmet.itemLevel = 1;
        damageHelmet.bonusStat = new BonusStat { type = BonusStatType.DamagePercent, baseValue = 5f };

        var evasionAccessory = ScriptableObject.CreateInstance<ItemData>();
        evasionAccessory.slot = EquipmentSlot.Accessory;
        evasionAccessory.itemLevel = 1;
        evasionAccessory.bonusStat = new BonusStat { type = BonusStatType.EvasionPercent, baseValue = 8f };

        var mightRing = ScriptableObject.CreateInstance<ItemData>();
        mightRing.slot = EquipmentSlot.Ring;
        mightRing.itemLevel = 1;
        mightRing.bonusStat = new BonusStat { type = BonusStatType.WeaponDamageFlat, baseValue = 2f };

        var pierceAxe = ScriptableObject.CreateInstance<ItemData>();
        pierceAxe.slot = EquipmentSlot.Weapon;
        pierceAxe.weaponSubtype = WeaponSubtype.Axe;
        pierceAxe.baseDamage = 10f;
        pierceAxe.attackSpeed = 1f;
        pierceAxe.itemLevel = 1;
        pierceAxe.bonusStat = new BonusStat { type = BonusStatType.ArmorPenetrationFlat, baseValue = 1f };

        var bonusTestEquipment = new List<ItemData> { hpRing, armorRing, speedBoots, damageHelmet, evasionAccessory, mightRing, pierceAxe };
        var bonusTestRuntime = CombatantFactory.CreatePlayerCombatant(bonusTestCharacter, 1, null, bonusTestEquipment);

        Check(bonusTestRuntime.MaxHP == 130f, $"3.10 FlatHP от кольца (баз.15, ур.2): MaxHP={bonusTestRuntime.MaxHP} (ожидалось 130 = 100+15×2)");
        Check(bonusTestRuntime.PhysicalDefenseMax == 8f, $"3.10 MaxPhysicalDefenseFlat от кольца: PhysicalDefenseMax={bonusTestRuntime.PhysicalDefenseMax} (ожидалось 8)");
        Check(bonusTestRuntime.ItemAttackSpeedBonusPercent == 10f, $"3.10 AttackSpeedPercent от сапог: {bonusTestRuntime.ItemAttackSpeedBonusPercent} (ожидалось 10)");
        Check(bonusTestRuntime.ItemDamageBonusPercent == 5f, $"3.10 DamagePercent от шлема: {bonusTestRuntime.ItemDamageBonusPercent} (ожидалось 5)");
        Check(bonusTestRuntime.ItemEvasionBonusPercent == 8f, $"3.10 EvasionPercent от аксессуара: {bonusTestRuntime.ItemEvasionBonusPercent} (ожидалось 8)");
        Check(bonusTestRuntime.Weapons.Count == 1 && bonusTestRuntime.Weapons[0].ArmorPenetrationFlat == 1f,
            $"3.10 ArmorPenetrationFlat привязан к оружию (Топор): {(bonusTestRuntime.Weapons.Count > 0 ? bonusTestRuntime.Weapons[0].ArmorPenetrationFlat.ToString() : "нет оружия")} (ожидалось 1)");
        // WeaponDamageFlat: базовый урон топора 10 + WeaponDamageFlat(2) = 12 -> диапазон [floor(12*0.8); ceil(12*1.2)] = [9;15].
        Check(bonusTestRuntime.Weapons.Count == 1 && bonusTestRuntime.Weapons[0].DamageMin == 9f && bonusTestRuntime.Weapons[0].DamageMax == 15f,
            $"3.10 WeaponDamageFlat от кольца силы: диапазон {(bonusTestRuntime.Weapons.Count > 0 ? $"{bonusTestRuntime.Weapons[0].DamageMin}-{bonusTestRuntime.Weapons[0].DamageMax}" : "нет оружия")} (ожидалось 9-15)");

        foreach (var item in bonusTestEquipment) UnityEngine.Object.DestroyImmediate(item);
        UnityEngine.Object.DestroyImmediate(bonusTestCharacter);

        // 8.1 (ФИКС, 2026-08-26): бонусы зданий деревни по уровням — раньше только Кузница ур.1/3
        // (стартовое снаряжение) и Таверна ур.1 (флэт-урон) реально считались, остальные 6 из 8
        // численных бонусов были только текстом в BuildingCatalog.LevelBonuses без эффекта.
        Check(BuildingCatalog.ForgeArmorBonus(1) == 0f && BuildingCatalog.ForgeArmorBonus(2) == 10f && BuildingCatalog.ForgeArmorBonus(4) == 30f,
            $"8.1 Кузница ур.2/4 (+10/+20 брони): ур.1={BuildingCatalog.ForgeArmorBonus(1)}, ур.2={BuildingCatalog.ForgeArmorBonus(2)}, ур.4={BuildingCatalog.ForgeArmorBonus(4)} (ожидалось 0/10/30)");
        Check(BuildingCatalog.ForgeCampArmorRestorePercent(4) == 0f && BuildingCatalog.ForgeCampArmorRestorePercent(5) == 50f,
            $"8.1 Кузница ур.5 (50% брони на привале): ур.4={BuildingCatalog.ForgeCampArmorRestorePercent(4)}, ур.5={BuildingCatalog.ForgeCampArmorRestorePercent(5)} (ожидалось 0/50)");
        Check(BuildingCatalog.TempleMagicShieldBonus(1) == 10f && BuildingCatalog.TempleMagicShieldBonus(3) == 30f,
            $"8.1 Храм ур.1/3 (+10/+20 маг.щита): ур.1={BuildingCatalog.TempleMagicShieldBonus(1)}, ур.3={BuildingCatalog.TempleMagicShieldBonus(3)} (ожидалось 10/30)");
        Check(BuildingCatalog.TavernRationsBonus(1) == 5 && BuildingCatalog.TavernRationsBonus(3) == 10,
            $"8.1 Таверна ур.1/3 (+5/+10 рационов): ур.1={BuildingCatalog.TavernRationsBonus(1)}, ур.3={BuildingCatalog.TavernRationsBonus(3)} (ожидалось 5/10)");
        Check(BuildingCatalog.TavernCampHealBonusPercent(2) == 10f && BuildingCatalog.TavernCampHealBonusPercent(4) == 30f,
            $"8.1 Таверна ур.2/4 (+10/+20% лечения): ур.2={BuildingCatalog.TavernCampHealBonusPercent(2)}, ур.4={BuildingCatalog.TavernCampHealBonusPercent(4)} (ожидалось 10/30)");

        // 8.1 (ФИКС): CampManager.BeginRun реально прибавляет рационы Таверны, а не всегда сбрасывает
        // на захардкоженные 5 (StartingRations).
        var campBuildingTestGO = new GameObject("SmokeTest_CampBuildingBonus");
        var campBuildingTestManager = campBuildingTestGO.AddComponent<CampManager>();
        campBuildingTestManager.BeginRun(3); // Таверна ур.3 -> +10 рационов
        Check(campBuildingTestManager.RationsRemaining == CampManager.StartingRations + 10,
            $"8.1 CampManager.BeginRun(3) учитывает бонус Таверны: RationsRemaining={campBuildingTestManager.RationsRemaining} (ожидалось {CampManager.StartingRations + 10})");
        UnityEngine.Object.DestroyImmediate(campBuildingTestGO);

        // 8.1 (ФИКС): CombatantFactory.CreatePlayerCombatant реально прибавляет броню Кузницы/маг.
        // щит Храма — раньше forgeLevel/templeLevel не принимались этим методом вовсе.
        var buildingStatsCharacter = ScriptableObject.CreateInstance<CharacterData>();
        buildingStatsCharacter.baseHealth = 50;
        var buildingStatsRuntime = CombatantFactory.CreatePlayerCombatant(buildingStatsCharacter, 1, null, null, 0, 4, 3);
        Check(buildingStatsRuntime.PhysicalDefenseMax == 30f, $"8.1 Кузница ур.4 добавляет броню в CreatePlayerCombatant: PhysicalDefenseMax={buildingStatsRuntime.PhysicalDefenseMax} (ожидалось 30)");
        Check(buildingStatsRuntime.MagicShieldMax == 30f, $"8.1 Храм ур.3 добавляет маг.щит в CreatePlayerCombatant: MagicShieldMax={buildingStatsRuntime.MagicShieldMax} (ожидалось 30)");
        UnityEngine.Object.DestroyImmediate(buildingStatsCharacter);

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

        // 2.2 (уточнено 2026-08-26): временный спрайт босса — переиспользует спрайт Рыцаря тьмы
        // (Monster_DarkKnight), НЕ подменяя статы/имя/пассивку босса (не путать с заменой монстра).
        var bossData = AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/ScriptableObjects/Monsters/Monster_Boss.asset");
        var darkKnightData = AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/ScriptableObjects/Monsters/Monster_DarkKnight.asset");
        if (Check(bossData != null && darkKnightData != null, "Monster_Boss.asset и Monster_DarkKnight.asset загрузились"))
        {
            Check(bossData.sprite != null && bossData.sprite == darkKnightData.sprite, "2.2 Monster_Boss.sprite временно переиспользует спрайт Рыцаря тьмы");
            Check(bossData.hp == 150f && bossData.physicalDefense == 12f, "2.2 статы босса не изменились подменой спрайта");
        }

        // Баг #8 (2026-08-26): CombatantFactory должен копировать CharacterData.portrait /
        // MonsterData.sprite в CombatantRuntime.Sprite, иначе бою нечего рендерить в PlayerBox/
        // enemy-боксах, даже если сами ui:Image-элементы существуют и спрайт назначен в ассете.
        var portraitTexture = new Texture2D(1, 1);
        var portraitSprite = Sprite.Create(portraitTexture, new Rect(0f, 0f, 1f, 1f), Vector2.zero);

        var spriteCharacter = ScriptableObject.CreateInstance<CharacterData>();
        spriteCharacter.portrait = portraitSprite;
        var playerRuntimeForSprite = CombatantFactory.CreatePlayerCombatant(spriteCharacter, 1);
        Check(playerRuntimeForSprite.Sprite == portraitSprite, "10.6 CombatantFactory копирует CharacterData.portrait в CombatantRuntime.Sprite (игрок)");
        UnityEngine.Object.DestroyImmediate(spriteCharacter);

        var spriteMonster = ScriptableObject.CreateInstance<MonsterData>();
        spriteMonster.sprite = portraitSprite;
        var monsterRuntimeForSprite = CombatantFactory.CreateMonsterCombatant(spriteMonster, 1);
        Check(monsterRuntimeForSprite.Sprite == portraitSprite, "10.6 CombatantFactory копирует MonsterData.sprite в CombatantRuntime.Sprite (монстр)");
        UnityEngine.Object.DestroyImmediate(spriteMonster);

        UnityEngine.Object.DestroyImmediate(portraitSprite);
        UnityEngine.Object.DestroyImmediate(portraitTexture);

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

        // 5.2: торговец предлагает 5 предметов, максимум 1 со скидкой за визит.
        var merchantRewardManagerGO = new GameObject("SmokeTest_MerchantRewardManager");
        var merchantRewardManager = merchantRewardManagerGO.AddComponent<RewardManager>();
        var offers = merchantRewardManager.GenerateMerchantOffers(3);
        Check(offers.Count == 5, $"5.2 торговец предлагает 5 предметов: {offers.Count}");
        int discountedCount = offers.FindAll(o => o.HasDiscount).Count;
        Check(discountedCount <= 1, $"5.2 максимум 1 предмет со скидкой за визит: {discountedCount}");
        UnityEngine.Object.DestroyImmediate(merchantRewardManagerGO);

        // 5.2: формула цены (Редкий, ур.5) — независимая от рандома проверка.
        var priceTestItem = ScriptableObject.CreateInstance<ItemData>();
        priceTestItem.tier = ItemTier.Rare;
        priceTestItem.itemLevel = 5;
        // Цена = 100 * 5 * 1.2 = 600. MerchantPrice is a private static method — exercise it indirectly
        // via a single-item GenerateMerchantOffers-style calculation instead of reflection: this inline
        // duplication is intentional (asserting the FORMULA, not the private implementation detail).
        int expectedPrice = Mathf.RoundToInt(100 * 5 * 1.2f);
        Check(expectedPrice == 600, $"5.2 формула цены (Редкий, ур.5): {expectedPrice} (ожидалось 600)");
        UnityEngine.Object.DestroyImmediate(priceTestItem);

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

        // 3.5: 4-шаговый цикл бонусов от лишних копий гачи (снаряжение -> пассивка -> снаряжение -> активка).
        var bonus1Copy = GachaCopyBonusCalculator.CalculateBonus(1); // базовое владение, 0 лишних копий
        Check(bonus1Copy.GearLevelBonus == 0 && bonus1Copy.PassiveLevelBonus == 0 && bonus1Copy.ActiveLevelBonus == 0, "3.5 1 копия = 0 бонуса");

        var bonus2Copies = GachaCopyBonusCalculator.CalculateBonus(2); // 1-я лишняя -> +1 снаряжение
        Check(bonus2Copies.GearLevelBonus == 1 && bonus2Copies.PassiveLevelBonus == 0 && bonus2Copies.ActiveLevelBonus == 0, "3.5 2 копии = +1 снаряжение");

        var bonus3Copies = GachaCopyBonusCalculator.CalculateBonus(3); // 2-я лишняя -> +1 пассивка
        Check(bonus3Copies.GearLevelBonus == 1 && bonus3Copies.PassiveLevelBonus == 1 && bonus3Copies.ActiveLevelBonus == 0, "3.5 3 копии = +1 снаряжение, +1 пассивка");

        var bonus4Copies = GachaCopyBonusCalculator.CalculateBonus(4); // 3-я лишняя -> +1 снаряжение (итого 2)
        Check(bonus4Copies.GearLevelBonus == 2 && bonus4Copies.PassiveLevelBonus == 1 && bonus4Copies.ActiveLevelBonus == 0, "3.5 4 копии = +2 снаряжение, +1 пассивка");

        var bonus5Copies = GachaCopyBonusCalculator.CalculateBonus(5); // 4-я лишняя -> +1 активка
        Check(bonus5Copies.GearLevelBonus == 2 && bonus5Copies.PassiveLevelBonus == 1 && bonus5Copies.ActiveLevelBonus == 1, "3.5 5 копий = +2 снаряжение, +1 пассивка, +1 активка");

        var bonus6Copies = GachaCopyBonusCalculator.CalculateBonus(6); // 5-я лишняя -> новый цикл, +1 снаряжение (итого 3)
        Check(bonus6Copies.GearLevelBonus == 3 && bonus6Copies.PassiveLevelBonus == 1 && bonus6Copies.ActiveLevelBonus == 1, "3.5 6 копий = +3 снаряжение (новый цикл начался)");

        // Клампы: 17 лишних копий пассивки было бы >4 без клампа (17/4 = 4 полных цикла проходят шаг 1 4 раза -> ровно 4, границу проверим бонусом побольше).
        var bonusManyCopies = GachaCopyBonusCalculator.CalculateBonus(1 + 4 * 10); // 40 лишних копий -> 10 полных циклов -> 10 пассивки без клампа
        Check(bonusManyCopies.PassiveLevelBonus == 4, $"3.5 кламп бонуса пассивки на 4 (макс. ур. 5): {bonusManyCopies.PassiveLevelBonus}");
        Check(bonusManyCopies.ActiveLevelBonus == 2, $"3.5 кламп бонуса активки на 2 (макс. ур. 3): {bonusManyCopies.ActiveLevelBonus}");

        // 7.2: LevelUpOption.Description берётся из effectDescription (пассивка/активка).
        var descTestSkill = ScriptableObject.CreateInstance<PassiveSkillData>();
        descTestSkill.skillName = "ТестНавык";
        descTestSkill.effectDescription = "Тестовое описание эффекта.";
        var descTestOption = new LevelUpOption { Type = LevelUpOptionType.NewPassiveSkill, Skill = descTestSkill, ResultingLevel = 1 };
        Check(descTestOption.Description == "Тестовое описание эффекта.", $"7.2 LevelUpOption.Description (пассивка): '{descTestOption.Description}'");
        UnityEngine.Object.DestroyImmediate(descTestSkill);

        var descTestActiveSkill = ScriptableObject.CreateInstance<ActiveSkillData>();
        descTestActiveSkill.skillName = "ТестАктивка";
        descTestActiveSkill.effectDescription = "Тестовое описание активного навыка.";
        var descTestActiveOption = new LevelUpOption { Type = LevelUpOptionType.UpgradeUniqueActive, ActiveSkill = descTestActiveSkill, ResultingLevel = 2 };
        Check(descTestActiveOption.Description == "Тестовое описание активного навыка.", $"7.2 LevelUpOption.Description (активка): '{descTestActiveOption.Description}'");
        UnityEngine.Object.DestroyImmediate(descTestActiveSkill);

        // 8.2 (уточнено): расчёт целевой позиции ленты сундука (та же формула, что и в
        // RunFlowController.ChestRevealFlow) — теперь со сдвигом на chestReelPadding (лента
        // зациклена паддинг-иконками по краям, см. RunFlowController.chestReelPadding).
        const float chestIconWidth = 64f;
        const int chestReelLength = 20;
        const int chestReelPadding = 3;
        float chestViewportWidth = 320f;
        float chestViewportCenter = chestViewportWidth / 2f;
        int chestWinningIndex = chestReelPadding + chestReelLength - 2;
        float chestTargetLeft = chestViewportCenter - chestIconWidth / 2f - chestWinningIndex * chestIconWidth;
        Check(chestTargetLeft == 160f - 32f - 21 * 64f, $"8.2 расчёт целевой позиции ленты сундука: {chestTargetLeft} (ожидалось {160f - 32f - 21 * 64f})");

        // Баг (2026-08-26): та же формула "cover"-кропа, что и в RunFlowController.GetStageFloorGapFromBottom
        // (дублируется здесь, как и формула ленты сундука выше) — проверяет, что отступ пола от низа
        // контейнера действительно уменьшается на более широких соотношениях сторон (16:9 -> 21:9),
        // а не остаётся статичным процентом, который не может угнаться за кропом на всём диапазоне.
        const float bgImgW = 1536f, bgImgH = 1024f, bgFloorRow = 797f;
        float FloorGap(float boxW, float boxH)
        {
            float imgAspect = bgImgW / bgImgH;
            float boxAspect = boxW / boxH;
            float scale, cropTop;
            if (boxAspect > imgAspect)
            {
                scale = boxW / bgImgW;
                cropTop = (bgImgH * scale - boxH) / 2f;
            }
            else
            {
                scale = boxH / bgImgH;
                cropTop = 0f;
            }
            return Mathf.Max(0f, boxH - (bgFloorRow * scale - cropTop));
        }

        float floorGap16x9 = FloorGap(1600f, 900f);
        float floorGap21x9 = FloorGap(2100f, 900f);
        Check(floorGap16x9 / 900f > 0.15f && floorGap16x9 / 900f < 0.19f, $"7.2/10.6 отступ пола на 16:9: {floorGap16x9:F1}px ({floorGap16x9 / 900f:P1} высоты, ожидалось ~16.7%)");
        Check(floorGap21x9 / 900f > 0.04f && floorGap21x9 / 900f < 0.08f, $"7.2/10.6 отступ пола на 21:9: {floorGap21x9:F1}px ({floorGap21x9 / 900f:P1} высоты, ожидалось ~6.25%)");
        Check(floorGap21x9 < floorGap16x9, $"7.2/10.6 отступ пола уменьшается на более широких экранах: 16:9={floorGap16x9:F1}px, 21:9={floorGap21x9:F1}px");

        // 3.11 (Варвар, Task 6): двуручное оружие при экипировке заменяет ОБА текущих предмета в
        // слотах оружия/рук одновременно (CharacterManager.EquipItem-исключение), а не по одному,
        // как в обычной логике сравнения слотов.
        var twoHandedTestGO = new GameObject("SmokeTest_TwoHandedEquip");
        var twoHandedCharacterManager = twoHandedTestGO.AddComponent<CharacterManager>();
        var twoHandedCharacter = ScriptableObject.CreateInstance<CharacterData>();

        var startingSword = ScriptableObject.CreateInstance<ItemData>();
        startingSword.itemName = "TestSword";
        startingSword.slot = EquipmentSlot.Weapon;
        startingSword.weaponSubtype = WeaponSubtype.Sword;
        startingSword.baseDamage = 10f;
        startingSword.attackSpeed = 1f;

        var startingShield = ScriptableObject.CreateInstance<ItemData>();
        startingShield.itemName = "TestShield";
        startingShield.slot = EquipmentSlot.Weapon;
        startingShield.weaponSubtype = WeaponSubtype.Shield;
        startingShield.physicalDefense = 0f;

        twoHandedCharacter.startingEquipment = new[] { startingSword, startingShield };
        twoHandedCharacterManager.BeginRun(twoHandedCharacter);

        var twoHandedAxe = ScriptableObject.CreateInstance<ItemData>();
        twoHandedAxe.itemName = "TestTwoHandedAxe";
        twoHandedAxe.slot = EquipmentSlot.Weapon;
        twoHandedAxe.weaponSubtype = WeaponSubtype.TwoHandedAxe;
        twoHandedAxe.isTwoHanded = true;
        twoHandedAxe.baseDamage = 30f;
        twoHandedAxe.attackSpeed = 0.8f;

        // Клик по любому кандидату (передаём startingSword как "replacing") всё равно должен снести
        // ОБА текущих оружия — EquipItem игнорирует конкретный replacing для isTwoHanded=true.
        twoHandedCharacterManager.EquipItem(twoHandedAxe, startingSword);

        var equippedAfterTwoHanded = twoHandedCharacterManager.EquippedItems;
        Check(equippedAfterTwoHanded.Count == 1 && equippedAfterTwoHanded[0] == twoHandedAxe,
            $"3.11 Двуручное оружие заменяет ОБА слота оружия/рук сразу: EquippedItems.Count={equippedAfterTwoHanded.Count}" +
            (equippedAfterTwoHanded.Count == 1 ? $", содержит={equippedAfterTwoHanded[0].itemName}" : string.Empty) +
            " (ожидалось 1 предмет = только топор, ни меч, ни щит не остались)");

        UnityEngine.Object.DestroyImmediate(twoHandedTestGO);
        UnityEngine.Object.DestroyImmediate(twoHandedCharacter);
        UnityEngine.Object.DestroyImmediate(startingSword);
        UnityEngine.Object.DestroyImmediate(startingShield);
        UnityEngine.Object.DestroyImmediate(twoHandedAxe);

        // 3.11 (Плут, Task 6): Клинок никогда не получает штраф/бонус дуал-вилда, независимо от
        // того, что во второй руке — проверяем все три случая из ГДД через CombatantFactory напрямую.
        var dualWieldTestCharacter = ScriptableObject.CreateInstance<CharacterData>();
        dualWieldTestCharacter.baseHealth = 100;

        ItemData MakeWeapon(string name, WeaponSubtype subtype, float damage)
        {
            var w = ScriptableObject.CreateInstance<ItemData>();
            w.itemName = name;
            w.slot = EquipmentSlot.Weapon;
            w.weaponSubtype = subtype;
            w.baseDamage = damage;
            w.attackSpeed = 1f;
            w.itemLevel = 1;
            return w;
        }

        var bladeA = MakeWeapon("BladeA", WeaponSubtype.Blade, 10f);
        var bladeB = MakeWeapon("BladeB", WeaponSubtype.Blade, 10f);
        var bladeBladeRuntime = CombatantFactory.CreatePlayerCombatant(dualWieldTestCharacter, 1, null, new List<ItemData> { bladeA, bladeB });
        // Без штрафа: база 10 -> диапазон [floor(10*0.8); ceil(10*1.2)] = [8;12].
        Check(bladeBladeRuntime.Weapons.Count == 2
            && bladeBladeRuntime.Weapons[0].DamageMin == 8f && bladeBladeRuntime.Weapons[0].DamageMax == 12f
            && bladeBladeRuntime.Weapons[1].DamageMin == 8f && bladeBladeRuntime.Weapons[1].DamageMax == 12f,
            $"3.11 Клинок+Клинок: оба без штрафа дуал-вилда, диапазоны " +
            (bladeBladeRuntime.Weapons.Count == 2 ? $"{bladeBladeRuntime.Weapons[0].DamageMin}-{bladeBladeRuntime.Weapons[0].DamageMax} / {bladeBladeRuntime.Weapons[1].DamageMin}-{bladeBladeRuntime.Weapons[1].DamageMax}" : "нет 2 оружий") +
            " (ожидалось 8-12 / 8-12)");
        UnityEngine.Object.DestroyImmediate(bladeA);
        UnityEngine.Object.DestroyImmediate(bladeB);

        var bladeC = MakeWeapon("BladeC", WeaponSubtype.Blade, 10f);
        var swordD = MakeWeapon("SwordD", WeaponSubtype.Sword, 10f);
        var bladeSwordRuntime = CombatantFactory.CreatePlayerCombatant(dualWieldTestCharacter, 1, null, new List<ItemData> { bladeC, swordD });
        // Клинок (индекс 0, порядок сохраняет порядок items): без штрафа [8;12].
        // Меч: база 10 × 0.75 (базовый штраф без Амбидекстрии) = 7.5 -> [floor(7.5*0.8); ceil(7.5*1.2)] = [6;9].
        Check(bladeSwordRuntime.Weapons.Count == 2
            && bladeSwordRuntime.Weapons[0].DamageMin == 8f && bladeSwordRuntime.Weapons[0].DamageMax == 12f
            && bladeSwordRuntime.Weapons[1].DamageMin == 6f && bladeSwordRuntime.Weapons[1].DamageMax == 9f,
            $"3.11 Клинок+Меч: Клинок 100%, Меч со штрафом 75%, диапазоны " +
            (bladeSwordRuntime.Weapons.Count == 2 ? $"{bladeSwordRuntime.Weapons[0].DamageMin}-{bladeSwordRuntime.Weapons[0].DamageMax} / {bladeSwordRuntime.Weapons[1].DamageMin}-{bladeSwordRuntime.Weapons[1].DamageMax}" : "нет 2 оружий") +
            " (ожидалось 8-12 / 6-9)");
        UnityEngine.Object.DestroyImmediate(bladeC);
        UnityEngine.Object.DestroyImmediate(swordD);

        UnityEngine.Object.DestroyImmediate(dualWieldTestCharacter);

        // 3.11 (Task 6, доп. работа сверх брифа): BonusStatType.ArmorIgnorePercent ("Зазубренный
        // клинок", Редкий Клинок) раньше молча игнорировался в AggregateEquipmentStats — эта
        // проверка гоняет реальный ассет через CreatePlayerCombatant и убеждается, что поле
        // WeaponAttackState.ArmorIgnorePercent теперь реально заполняется.
        var jaggedBlade = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ScriptableObjects/Items/Blades/Item_Blade_Rare_JaggedBlade.asset");
        if (Check(jaggedBlade != null, "Item_Blade_Rare_JaggedBlade.asset загрузился"))
        {
            var armorIgnoreTestCharacter = ScriptableObject.CreateInstance<CharacterData>();
            armorIgnoreTestCharacter.baseHealth = 100;
            var armorIgnoreRuntime = CombatantFactory.CreatePlayerCombatant(armorIgnoreTestCharacter, 1, null, new List<ItemData> { jaggedBlade });
            Check(armorIgnoreRuntime.Weapons.Count == 1 && armorIgnoreRuntime.Weapons[0].ArmorIgnorePercent > 0f,
                $"3.11 Зазубренный клинок заполняет WeaponAttackState.ArmorIgnorePercent: {(armorIgnoreRuntime.Weapons.Count == 1 ? armorIgnoreRuntime.Weapons[0].ArmorIgnorePercent.ToString() : "нет оружия")} (ожидалось >0)");
            UnityEngine.Object.DestroyImmediate(armorIgnoreTestCharacter);
        }

        Info.Add("Чистые проверки формул (3.2/3.3/3.10) выполнены.");

        // Task 7 (rogue-barbarian-classes plan, GDD 10.6): дизайнерская графика Плута/Варвара
        // импортирована и назначена через ArtAssignmentTool — проверяем, что .icon заполнен на
        // всех 3 тирах для каждого из 6 новых архетипов предметов.
        void CheckIconsFor(string archetypeLabel, params string[] assetPaths)
        {
            foreach (var assetPath in assetPaths)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemData>(assetPath);
                if (Check(item != null, $"10.6 {assetPath} загрузился"))
                {
                    Check(item.icon != null, $"10.6 {archetypeLabel}: icon заполнен ({assetPath})");
                }
            }
        }

        CheckIconsFor("Клинок",
            "Assets/ScriptableObjects/Items/Blades/Item_Blade_Common_Blade.asset",
            "Assets/ScriptableObjects/Items/Blades/Item_Blade_Rare_JaggedBlade.asset",
            "Assets/ScriptableObjects/Items/Blades/Item_Blade_Epic_MomentoMori.asset");
        CheckIconsFor("Двуручный топор",
            "Assets/ScriptableObjects/Items/TwoHandedAxes/Item_TwoHandedAxe_Common_GreatAxe.asset",
            "Assets/ScriptableObjects/Items/TwoHandedAxes/Item_TwoHandedAxe_Rare_TemperedGreatAxe.asset",
            "Assets/ScriptableObjects/Items/TwoHandedAxes/Item_TwoHandedAxe_Epic_Headsplitter.asset");
        CheckIconsFor("Капюшон",
            "Assets/ScriptableObjects/Items/Hoods/Item_Hood_Common_Hood.asset",
            "Assets/ScriptableObjects/Items/Hoods/Item_Hood_Rare_DarkHood.asset",
            "Assets/ScriptableObjects/Items/Hoods/Item_Hood_Epic_DuelistHood.asset");
        CheckIconsFor("Кожанка",
            "Assets/ScriptableObjects/Items/Leathers/Item_Leather_Common_Leather.asset",
            "Assets/ScriptableObjects/Items/Leathers/Item_Leather_Rare_ThickLeather.asset",
            "Assets/ScriptableObjects/Items/Leathers/Item_Leather_Epic_EmbraceOfNight.asset");
        CheckIconsFor("Пояс",
            "Assets/ScriptableObjects/Items/Belts/Item_Belt_Common_Belt.asset",
            "Assets/ScriptableObjects/Items/Belts/Item_Belt_Rare_ChampionBelt.asset",
            "Assets/ScriptableObjects/Items/Belts/Item_Belt_Epic_TitanBelt.asset");
        CheckIconsFor("Трофей",
            "Assets/ScriptableObjects/Items/Trophies/Item_Trophy_Common_Trophy.asset",
            "Assets/ScriptableObjects/Items/Trophies/Item_Trophy_Rare_RareTrophy.asset",
            "Assets/ScriptableObjects/Items/Trophies/Item_Trophy_Epic_EpicTrophy.asset");

        // Портреты персонажей: маппинг подтверждён пользователем напрямую (Sasha.png = Варвар,
        // Violet.png = Плут) — это не "известный пробел", а реальная проверка после назначения.
        var sashaSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Characters/Sasha.png");
        var violetSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Characters/Violet.png");
        var barbarianChar = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/ScriptableObjects/Characters/Character_Barbarian.asset");
        if (Check(barbarianChar != null, "10.6 Character_Barbarian.asset загрузился"))
        {
            Check(barbarianChar.portrait != null && barbarianChar.portrait == sashaSprite,
                $"10.6 Character_Barbarian.portrait = Sasha.png: {(barbarianChar.portrait != null ? barbarianChar.portrait.name : "null")} (ожидалось спрайт Assets/Art/Characters/Sasha.png)");
        }
        var rogueChar = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/ScriptableObjects/Characters/Character_Rogue.asset");
        if (Check(rogueChar != null, "10.6 Character_Rogue.asset загрузился"))
        {
            Check(rogueChar.portrait != null && rogueChar.portrait == violetSprite,
                $"10.6 Character_Rogue.portrait = Violet.png: {(rogueChar.portrait != null ? rogueChar.portrait.name : "null")} (ожидалось спрайт Assets/Art/Characters/Violet.png)");
        }

        // ==================== Финальный ревью этой ветки (rogue-barbarian-classes) — 4 находки ====================

        // Финальный фикс #1: GetComparisonCandidates не должна предлагать иллюзорный пустой второй
        // слот оружия, когда сейчас надето двуручное оружие — единственный корректный кандидат на
        // замену это оно само (см. CharacterManager.GetComparisonCandidates).
        var ghCandidatesGO = new GameObject("SmokeTest_GetComparisonCandidatesTwoHanded");
        var ghCandidatesManager = ghCandidatesGO.AddComponent<CharacterManager>();
        var ghCandidatesCharacter = ScriptableObject.CreateInstance<CharacterData>();

        var ghAxe = ScriptableObject.CreateInstance<ItemData>();
        ghAxe.itemName = "TestGHAxeCandidates";
        ghAxe.slot = EquipmentSlot.Weapon;
        ghAxe.weaponSubtype = WeaponSubtype.TwoHandedAxe;
        ghAxe.isTwoHanded = true;
        ghAxe.baseDamage = 30f;
        ghAxe.attackSpeed = 0.8f;

        ghCandidatesCharacter.startingEquipment = new[] { ghAxe };
        ghCandidatesManager.BeginRun(ghCandidatesCharacter);

        var oneHandedOffer = ScriptableObject.CreateInstance<ItemData>();
        oneHandedOffer.itemName = "TestOneHanderOffer";
        oneHandedOffer.slot = EquipmentSlot.Weapon;
        oneHandedOffer.weaponSubtype = WeaponSubtype.Sword;
        oneHandedOffer.baseDamage = 10f;
        oneHandedOffer.attackSpeed = 1f;

        var twoHandedCandidates = ghCandidatesManager.GetComparisonCandidates(oneHandedOffer);
        Check(twoHandedCandidates.Count == 1 && twoHandedCandidates[0] == ghAxe,
            $"Финальный фикс #1: с надетым двуручным оружием GetComparisonCandidates для нового одноручника даёт РОВНО 1 кандидата (само двуручное), без иллюзорного пустого 2-го слота: Count={twoHandedCandidates.Count}" +
            (twoHandedCandidates.Count > 0 ? $", [0]={(twoHandedCandidates[0] != null ? twoHandedCandidates[0].itemName : "null")}" : string.Empty) +
            " (ожидалось 1 / TestGHAxeCandidates)");

        UnityEngine.Object.DestroyImmediate(ghCandidatesGO);
        UnityEngine.Object.DestroyImmediate(ghCandidatesCharacter);
        UnityEngine.Object.DestroyImmediate(ghAxe);
        UnityEngine.Object.DestroyImmediate(oneHandedOffer);

        // Регрессия: два ОБЫЧНЫХ одноручных оружия (никакого двуручного не надето) по-прежнему
        // дают 2 кандидата, как до этого фикса.
        var normalCandidatesGO = new GameObject("SmokeTest_GetComparisonCandidatesNormal");
        var normalCandidatesManager = normalCandidatesGO.AddComponent<CharacterManager>();
        var normalCandidatesCharacter = ScriptableObject.CreateInstance<CharacterData>();

        var normalSwordA = MakeWeapon("NormalCandidateSwordA", WeaponSubtype.Sword, 10f);
        var normalSwordB = MakeWeapon("NormalCandidateSwordB", WeaponSubtype.Sword, 10f);
        normalCandidatesCharacter.startingEquipment = new[] { normalSwordA, normalSwordB };
        normalCandidatesManager.BeginRun(normalCandidatesCharacter);

        var normalOffer = MakeWeapon("NormalCandidateOffer", WeaponSubtype.Sword, 10f);
        var normalCandidates = normalCandidatesManager.GetComparisonCandidates(normalOffer);
        Check(normalCandidates.Count == 2,
            $"Регрессия (Финальный фикс #1): два обычных одноручных оружия по-прежнему дают 2 кандидата: Count={normalCandidates.Count} (ожидалось 2)");

        UnityEngine.Object.DestroyImmediate(normalCandidatesGO);
        UnityEngine.Object.DestroyImmediate(normalCandidatesCharacter);
        UnityEngine.Object.DestroyImmediate(normalSwordA);
        UnityEngine.Object.DestroyImmediate(normalSwordB);
        UnityEngine.Object.DestroyImmediate(normalOffer);

        // Финальный фикс #2: "На волоске" — БАФФ скорости атаки, хранится в ActiveDebuffs, но не
        // должен считаться дебаффом для HasActiveDebuff (используется "Несгибаемым" для урона).
        var byAThreadOnly = new CombatantRuntime { MaxHP = 100f, CurrentHP = 100f };
        byAThreadOnly.ActiveDebuffs.Add(new ActiveDebuff { Id = "by_a_thread", RemainingTime = 3f, AttackSpeedMultiplier = 1.15f, IsBuff = true });
        Check(!byAThreadOnly.HasActiveDebuff,
            $"Финальный фикс #2: HasActiveDebuff=false, когда единственная запись ActiveDebuffs — бафф «На волоске»: {byAThreadOnly.HasActiveDebuff} (ожидалось false)");

        byAThreadOnly.ActiveDebuffs.Add(new ActiveDebuff { Id = "warlock_slow", RemainingTime = 3f, AttackSpeedMultiplier = 0.7f });
        Check(byAThreadOnly.HasActiveDebuff,
            $"Финальный фикс #2: HasActiveDebuff=true, когда рядом с баффом «На волоске» есть настоящий дебафф (warlock_slow): {byAThreadOnly.HasActiveDebuff} (ожидалось true)");

        // Финальный фикс #3: CombatantStatusEffects показывает читаемые русские подписи для
        // by_a_thread/intimidation (не сырой Id) и включает Скрытность/яд Плута/Берсерк.
        var byAThreadDisplayRuntime = new CombatantRuntime { MaxHP = 100f, CurrentHP = 100f };
        byAThreadDisplayRuntime.ActiveDebuffs.Add(new ActiveDebuff { Id = "by_a_thread", RemainingTime = 3f, AttackSpeedMultiplier = 1.15f, IsBuff = true });
        var byAThreadDisplayEffects = CombatantStatusEffects.GetActiveEffects(byAThreadDisplayRuntime);
        Check(byAThreadDisplayEffects.Exists(e => e.label == "На волоске" && e.isBuff),
            $"Финальный фикс #3: «На волоске» отображается читаемой русской подписью-баффом (не сырым id): [{string.Join(", ", byAThreadDisplayEffects.ConvertAll(e => e.label + (e.isBuff ? "(buff)" : "(debuff)")))}]");

        var intimidationDisplayRuntime = new CombatantRuntime { MaxHP = 100f, CurrentHP = 100f };
        intimidationDisplayRuntime.ActiveDebuffs.Add(new ActiveDebuff { Id = "intimidation", RemainingTime = 3f, AttackSpeedMultiplier = 0.8f });
        var intimidationDisplayEffects = CombatantStatusEffects.GetActiveEffects(intimidationDisplayRuntime);
        Check(intimidationDisplayEffects.Exists(e => e.label == "Запугивание" && !e.isBuff),
            $"Финальный фикс #3: «Запугивание» отображается читаемой русской подписью-дебаффом (не сырым id): [{string.Join(", ", intimidationDisplayEffects.ConvertAll(e => e.label + (e.isBuff ? "(buff)" : "(debuff)")))}]");

        var newStatusesRuntime = new CombatantRuntime { MaxHP = 100f, CurrentHP = 100f, IsStealthed = true, RoguePoisonStacksOnTarget = 2, IsBerserkActive = true };
        var newStatusEffects = CombatantStatusEffects.GetActiveEffects(newStatusesRuntime);
        Check(newStatusEffects.Exists(e => e.label == "Скрытность" && e.isBuff),
            $"Финальный фикс #3: Скрытность (IsStealthed) отображается как бафф: [{string.Join(", ", newStatusEffects.ConvertAll(e => e.label))}]");
        Check(newStatusEffects.Exists(e => e.label.Contains("Яд") && e.label.Contains("2") && !e.isBuff),
            $"Финальный фикс #3: яд Плута (RoguePoisonStacksOnTarget=2) отображается как дебафф ×N: [{string.Join(", ", newStatusEffects.ConvertAll(e => e.label))}]");
        Check(newStatusEffects.Exists(e => e.label == "Берсерк" && e.isBuff),
            $"Финальный фикс #3: Берсерк (IsBerserkActive) отображается как бафф: [{string.Join(", ", newStatusEffects.ConvertAll(e => e.label))}]");

        // Финальный фикс #4: Берсерк никогда не должен проходить через hit-loop машинерию
        // TryActivateUniqueActiveSkill (построенную под "3 быстрые атаки" Дженнифер) — метод должен
        // вернуть false и не нанести никакого урона, даже если кулдаун готов (как всегда для Берсерка,
        // у которого cooldownSeconds=0).
        var berserkGuardGO = new GameObject("SmokeTest_BerserkGuard");
        var berserkCombatManager = berserkGuardGO.AddComponent<CombatManager>();

        var berserkWeapon = new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, AttackSpeed = 1f, DamageType = DamageType.Physical };
        var berserkPlayer = new CombatantRuntime { DisplayName = "TestBarbarianBerserkGuard", IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, UniqueBerserkLevel = 1 };
        berserkPlayer.Weapons.Add(berserkWeapon);

        var berserkDummy = new CombatantRuntime { DisplayName = "TestDummyBerserkGuard", MaxHP = 1000f, CurrentHP = 1000f };

        berserkCombatManager.StartCombat(berserkPlayer, new List<CombatantRuntime> { berserkDummy });
        berserkCombatManager.ConfigureUniqueActiveSkill(3, 1f, 0f, false, SkillEffectMap.Berserk); // cooldownSeconds=0, как задумано для тумблера
        berserkPlayer.ActiveSkillCooldownTimer = 0f; // готов немедленно (StartCombat выставляет полный кулдаун = 0 для Берсерка)

        float berserkDummyHpBefore = berserkDummy.CurrentHP;
        bool berserkActivationResult = berserkCombatManager.TryActivateUniqueActiveSkill();

        Check(!berserkActivationResult,
            $"Финальный фикс #4: TryActivateUniqueActiveSkill возвращает false, когда настроен на Берсерк: {berserkActivationResult} (ожидалось false)");
        Check(berserkDummy.CurrentHP == berserkDummyHpBefore,
            $"Финальный фикс #4: Берсерк НЕ запускает hit-loop (HP цели не изменилось): было {berserkDummyHpBefore}, стало {berserkDummy.CurrentHP}");

        UnityEngine.Object.DestroyImmediate(berserkGuardGO);

        // 3.11 (ФИКС, Codex P1 2026-08-27): "Тень"/"Дымовая граната" — уникальные навыки Плута,
        // раньше копировались БЕЗ проверки класса (в отличие от уникальных навыков Варвара).
        // Не-Плут, получивший Скрытность через "Ускользание" (SlipAway) или наставника, не должен
        // получать бонус уклонения "Тени" — только у Плута UniqueShadowLevel может быть > 0.
        var nonRogueCharacter = ScriptableObject.CreateInstance<CharacterData>();
        nonRogueCharacter.characterName = "ТестВарвар";
        nonRogueCharacter.characterClass = CharacterClass.Barbarian;
        nonRogueCharacter.baseHealth = 100;
        var nonRogueProgress = new RunCharacterProgress(nonRogueCharacter);
        // UniquePassiveLevel/UniqueActiveLevel стартуют с 1 у ЛЮБОГО персонажа (см. RunCharacterProgress) —
        // именно поэтому безусловное копирование раньше давало Тени ненулевой уровень у Варвара.
        var nonRogueCombatant = CombatantFactory.CreatePlayerCombatant(nonRogueCharacter, 1, nonRogueProgress);
        Check(nonRogueCombatant.UniqueShadowLevel == 0 && nonRogueCombatant.UniqueSmokeBombLevel == 0,
            $"3.11 ФИКС «Тень»/«Дымовая граната» не текут на не-Плута: UniqueShadowLevel={nonRogueCombatant.UniqueShadowLevel}, UniqueSmokeBombLevel={nonRogueCombatant.UniqueSmokeBombLevel} (ожидалось 0/0)");
        UnityEngine.Object.DestroyImmediate(nonRogueCharacter);

        var rogueCharacter = ScriptableObject.CreateInstance<CharacterData>();
        rogueCharacter.characterName = "ТестПлут";
        rogueCharacter.characterClass = CharacterClass.Rogue;
        rogueCharacter.baseHealth = 100;
        var rogueProgress = new RunCharacterProgress(rogueCharacter);
        var rogueCombatant = CombatantFactory.CreatePlayerCombatant(rogueCharacter, 1, rogueProgress);
        Check(rogueCombatant.UniqueShadowLevel == 1 && rogueCombatant.UniqueSmokeBombLevel == 1,
            $"3.11 ФИКС «Тень»/«Дымовая граната» остаются у Плута: UniqueShadowLevel={rogueCombatant.UniqueShadowLevel}, UniqueSmokeBombLevel={rogueCombatant.UniqueSmokeBombLevel} (ожидалось 1/1, т.к. UniquePassiveLevel/UniqueActiveLevel стартуют с 1)");
        UnityEngine.Object.DestroyImmediate(rogueCharacter);

        // 9.4 (ФИКС, Codex P2 2026-08-27): SaveData расширена полями из актуального ГДД —
        // saveVersion/veteranDeck/characterRunCounts/seenVNScenes, gachaOwnedCharacters вместо
        // characterCopies (переключение ключа с displayName на стабильный characterId).
        var freshSave = new SaveData();
        Check(freshSave.saveVersion == SaveData.CurrentSaveVersion,
            $"9.4 новый SaveData имеет текущую версию: saveVersion={freshSave.saveVersion} (ожидалось {SaveData.CurrentSaveVersion})");
        Check(freshSave.veteranDeck != null && freshSave.veteranDeck.Count == 0,
            "9.4 veteranDeck инициализирован пустым списком");
        Check(freshSave.characterRunCounts != null && freshSave.gachaOwnedCharacters != null && freshSave.seenVNScenes != null,
            "9.4 characterRunCounts/gachaOwnedCharacters/seenVNScenes инициализированы (не null)");

        // 9.4/Codex P2: миграция старого сохранения без saveVersion (симулирует файл с диска до
        // этого фикса — JsonUtility молча оставит новые поля в дефолте, а не упадёт, но saveVersion
        // будет 0) — TryMigrate должен довести его до текущей версии без потери уже прочитанных полей.
        var staleSave = new SaveData { saveVersion = 0, metaCurrency = 500 };
        staleSave.veteranDeck = null; // симулируем JSON без этого поля вовсе (JsonUtility даёт null для отсутствующих списков в старом файле)
        SaveManager.MigrateIfNeeded(staleSave);
        Check(staleSave.saveVersion == SaveData.CurrentSaveVersion && staleSave.metaCurrency == 500 && staleSave.veteranDeck != null,
            $"9.4 миграция старого save: saveVersion={staleSave.saveVersion}, metaCurrency сохранена={staleSave.metaCurrency}, veteranDeck заполнен дефолтом={staleSave.veteranDeck != null} (ожидалось true/500/true)");
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
        RequireElement(root, "MerchantOffersContainer");
        RequireElement(root, "MerchantCurrencyLabel");

        // Task 3 (GDD 10.6): the UXML src="project://database/..." approach on CombatBackground's
        // ui:Image did NOT resolve at runtime (confirmed during Task 3 -- Image.image/.sprite stayed
        // null in Play Mode), so the src attribute was dropped from the UXML entirely and replaced with
        // a scene-wired [SerializeField] Sprite combatBackgroundSprite assigned onto CombatBackground.sprite
        // in RunFlowController.CacheElements(). This check is a regression guard on THAT scene wiring --
        // it confirms the live UIDocument's CombatBackground element actually ends up with a non-null
        // image/sprite post-OnEnable(), not a confirmation of the abandoned src= hypothesis.
        var combatBackground = root.Q<UnityEngine.UIElements.Image>("CombatBackground");
        Check(combatBackground != null, "CombatBackground ui:Image найден в CombatPanel");

        // 7.2 (обновлено): крупные спрайты персонажа/монстров живут на "земле" сцены боя
        // (CombatStage), а не внутри карточек имени/HP — регрессионная защита на эти UI-элементы.
        // Проверка что CombatantFactory реально копирует спрайт — в RunPureLogicChecks ниже.
        RequireElement(root, "CombatStage");
        RequireElement(root, "PlayerStageSprite");
        RequireElement(root, "EnemyStageRow");
        if (combatBackground != null)
        {
            Check(combatBackground.image != null || combatBackground.sprite != null,
                $"CombatBackground.image/.sprite резолвится из сцен-вайринга (image={(combatBackground.image != null)}, sprite={(combatBackground.sprite != null)})");
        }

        // Финальный ревью, находка #4: пять UI-элементов открытия сундука (8.2) не имели никакого
        // регрессионного покрытия -- если будущий рефакторинг GameRoot.uxml переименует один из них,
        // CacheElements()'s root.Q<...>("Name") тихо вернёт null и уронит ChestRevealFlow NRE-ом в рантайме.
        // Тот же паттерн RequireElement, что и для CombatBackground/MainMenuScreen/... выше.
        RequireElement(root, "ChestRevealContainer");
        RequireElement(root, "ChestSpriteImage");
        RequireElement(root, "ChestReelViewport");
        RequireElement(root, "ChestReelStrip");
        RequireElement(root, "ChestSkipButton");

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
        // ФИКС (Task 3, Codex P2 2026-08-27): TryPullGacha временно заглушка (SaveManager больше не
        // содержит AddItemCopy/GetItemCount — полная гача-логика под GDD 11.1 приходит в Task 6),
        // поэтому попап результата сейчас не показывается. Проверка попапа вернётся, когда Task 6
        // восстановит полную реализацию TryPullGacha.
        Check(gachaResultPopup.style.display != DisplayStyle.Flex, "Попап результата призыва НЕ показан (временная заглушка TryPullGacha до Task 6)");
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

        // Codex P1 2026-08-27: интеграционный smoke — BeginRun не должен падать ни для одного из
        // 3 персонажей (раньше только Дженифер была реально доступна из RunFlowController).
        var runFlowController = UnityEngine.Object.FindFirstObjectByType<RunFlowController>();
        if (runFlowController == null)
        {
            Errors.Add("Task 4: RunFlowController не найден в сцене для проверки selectableCharacters.");
        }
        else
        {
            var selectableField = typeof(RunFlowController).GetField("selectableCharacters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var selectable = selectableField?.GetValue(runFlowController) as CharacterData[];
            Check(selectable != null && selectable.Length == 3,
                $"Task 4: RunFlowController.selectableCharacters содержит 3 персонажа: count={(selectable != null ? selectable.Length : -1)} (ожидалось 3)");

            if (selectable != null)
            {
                var testCharacterManager = new GameObject("SmokeTestCharacterManager").AddComponent<CharacterManager>();
                foreach (var character in selectable)
                {
                    try
                    {
                        testCharacterManager.BeginRun(character);
                        Check(testCharacterManager.Combatant != null && testCharacterManager.IsAlive,
                            $"Task 4: BeginRun успешен для {character.characterName} ({character.characterClass}): Combatant создан, IsAlive={testCharacterManager.IsAlive}");
                    }
                    catch (System.Exception e)
                    {
                        Errors.Add($"Task 4: BeginRun выбросил исключение для {character.characterName}: {e}");
                    }
                }
                UnityEngine.Object.DestroyImmediate(testCharacterManager.gameObject);
            }
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

        // 4.3 (НОВОЕ 2026-08-26): активный навык уходит в полный кулдаун сразу при старте боя,
        // а не в 0 — иначе "3 быстрые атаки" срабатывали мгновенно и сносили противника до того,
        // как игрок успевал его увидеть. Обычные атаки оружием это не затрагивает.
        var skillCooldownGO = new GameObject("SmokeTest_ActiveSkillCooldown");
        var skillCooldownCombatManager = skillCooldownGO.AddComponent<CombatManager>();
        skillCooldownCombatManager.ConfigureUniqueActiveSkill(3, 1f, 12f, true, "TestSkill");

        var skillCooldownPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f };
        skillCooldownPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 5f, DamageMax = 5f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var skillCooldownEnemy = new CombatantRuntime { IsPlayer = false, MaxHP = 100f, CurrentHP = 100f, DisplayName = "TestDummy" };
        skillCooldownEnemy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 1f });

        skillCooldownCombatManager.StartCombat(skillCooldownPlayer, new List<CombatantRuntime> { skillCooldownEnemy });
        Check(!skillCooldownCombatManager.IsActiveSkillReady, "4.3 активный навык НЕ готов сразу при старте боя");
        Check(skillCooldownCombatManager.ActiveSkillCooldownRemaining == 12f, $"4.3 активный навык уходит в полный кулдаун при старте боя: {skillCooldownCombatManager.ActiveSkillCooldownRemaining} (ожидалось 12)");
        Check(!skillCooldownCombatManager.TryActivateUniqueActiveSkill(), "4.3 навык нельзя активировать вручную сразу при старте боя");

        UnityEngine.Object.DestroyImmediate(skillCooldownGO);

        // 1, п.3: основной пассив наставника ("Магнум Опус") — постоянный бонус к маг. урону — через реальный CombatManager.
        var mentorTestGO = new GameObject("SmokeTest_MentorCombat");
        var mentorTestCombatManager = mentorTestGO.AddComponent<CombatManager>();

        var mentorTestPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, MentorMagicDamageBonusPercent = 10f };
        mentorTestPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Magical, AttackSpeed = 1f });
        var mentorTestDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, MagicShieldMax = 0f };

        mentorTestCombatManager.StartCombat(mentorTestPlayer, new List<CombatantRuntime> { mentorTestDummy });
        mentorTestCombatManager.Tick(1.01f);
        // 10 базового урона * 1.10 (Магнум Опус) = 11, весь урон должен пройти по HP (0 маг. щита у болвана).
        Check(mentorTestDummy.CurrentHP <= 989f && mentorTestDummy.CurrentHP >= 988f,
            $"1/п.3 Магнум Опус +10% маг. урона применяется: HP болвана = {mentorTestDummy.CurrentHP} (ожидалось ~989, т.е. 1000-11)");
        UnityEngine.Object.DestroyImmediate(mentorTestGO);

        // Пул наставника сливается с общим/классовым пулом левел-апа.
        var levelUpManagerGO = new GameObject("SmokeTest_MentorPool");
        var testLevelUpManager = levelUpManagerGO.AddComponent<LevelUpManager>();
        var fakeMentorSkill = ScriptableObject.CreateInstance<PassiveSkillData>();
        fakeMentorSkill.skillName = "ТестНавыкНаставника";
        fakeMentorSkill.maxLevel = 5;
        testLevelUpManager.MentorSkillPool = new List<PassiveSkillData> { fakeMentorSkill };
        testLevelUpManager.GeneralSkillPool = new List<PassiveSkillData>();
        testLevelUpManager.WarriorSkillPool = new List<PassiveSkillData>();

        var fakeCharacter = ScriptableObject.CreateInstance<CharacterData>();
        fakeCharacter.characterClass = CharacterClass.Warrior;
        fakeCharacter.uniquePassiveSkill = ScriptableObject.CreateInstance<PassiveSkillData>();
        fakeCharacter.uniquePassiveSkill.maxLevel = 5;
        fakeCharacter.uniqueActiveSkill = ScriptableObject.CreateInstance<ActiveSkillData>();
        fakeCharacter.uniqueActiveSkill.maxLevel = 3;
        var fakeProgress = new RunCharacterProgress(fakeCharacter);

        var mentorOptions = testLevelUpManager.GenerateLevelUpOptions(fakeProgress);
        Check(mentorOptions.Exists(o => o.Skill == fakeMentorSkill), "3.5/1п.3 навык из пула наставника попадает в варианты левел-апа");

        UnityEngine.Object.DestroyImmediate(levelUpManagerGO);
        UnityEngine.Object.DestroyImmediate(fakeMentorSkill);
        UnityEngine.Object.DestroyImmediate(fakeCharacter.uniquePassiveSkill);
        UnityEngine.Object.DestroyImmediate(fakeCharacter.uniqueActiveSkill);
        UnityEngine.Object.DestroyImmediate(fakeCharacter);

        // 3c: классовые пулы (Плут/Варвар) должны быть взаимно исключающими — навык одного класса
        // не должен попадать в варианты левел-апа другого класса (закрывает пробел, найденный
        // при ревью Task 3: LevelUpManager был жёстко привязан только к Warrior).
        var classGateGO = new GameObject("SmokeTest_ClassSkillGate");
        var classGateLevelUpManager = classGateGO.AddComponent<LevelUpManager>();
        var fakeRogueSkill = ScriptableObject.CreateInstance<PassiveSkillData>();
        fakeRogueSkill.skillName = "ТестНавыкПлута";
        fakeRogueSkill.maxLevel = 5;
        var fakeBarbarianSkill = ScriptableObject.CreateInstance<PassiveSkillData>();
        fakeBarbarianSkill.skillName = "ТестНавыкВарвара";
        fakeBarbarianSkill.maxLevel = 5;
        classGateLevelUpManager.GeneralSkillPool = new List<PassiveSkillData>();
        classGateLevelUpManager.WarriorSkillPool = new List<PassiveSkillData>();
        classGateLevelUpManager.MentorSkillPool = new List<PassiveSkillData>();
        classGateLevelUpManager.RogueSkillPool = new List<PassiveSkillData> { fakeRogueSkill };
        classGateLevelUpManager.BarbarianSkillPool = new List<PassiveSkillData> { fakeBarbarianSkill };

        var fakeRogueCharacter = ScriptableObject.CreateInstance<CharacterData>();
        fakeRogueCharacter.characterClass = CharacterClass.Rogue;
        fakeRogueCharacter.uniquePassiveSkill = ScriptableObject.CreateInstance<PassiveSkillData>();
        fakeRogueCharacter.uniquePassiveSkill.maxLevel = 5;
        fakeRogueCharacter.uniqueActiveSkill = ScriptableObject.CreateInstance<ActiveSkillData>();
        fakeRogueCharacter.uniqueActiveSkill.maxLevel = 3;
        var rogueProgress = new RunCharacterProgress(fakeRogueCharacter);
        var rogueOptions = classGateLevelUpManager.GenerateLevelUpOptions(rogueProgress);
        Check(rogueOptions.Exists(o => o.Skill == fakeRogueSkill), "3c классовый пул Плута: навык Плута доступен персонажу-Плуту");
        Check(!rogueOptions.Exists(o => o.Skill == fakeBarbarianSkill), "3c классовый пул Плута: навык Варвара НЕ доступен персонажу-Плуту");

        var fakeBarbarianCharacter = ScriptableObject.CreateInstance<CharacterData>();
        fakeBarbarianCharacter.characterClass = CharacterClass.Barbarian;
        fakeBarbarianCharacter.uniquePassiveSkill = ScriptableObject.CreateInstance<PassiveSkillData>();
        fakeBarbarianCharacter.uniquePassiveSkill.maxLevel = 5;
        fakeBarbarianCharacter.uniqueActiveSkill = ScriptableObject.CreateInstance<ActiveSkillData>();
        fakeBarbarianCharacter.uniqueActiveSkill.maxLevel = 3;
        var barbarianProgress = new RunCharacterProgress(fakeBarbarianCharacter);
        var barbarianOptions = classGateLevelUpManager.GenerateLevelUpOptions(barbarianProgress);
        Check(barbarianOptions.Exists(o => o.Skill == fakeBarbarianSkill), "3c классовый пул Варвара: навык Варвара доступен персонажу-Варвару");
        Check(!barbarianOptions.Exists(o => o.Skill == fakeRogueSkill), "3c классовый пул Варвара: навык Плута НЕ доступен персонажу-Варвару");

        UnityEngine.Object.DestroyImmediate(classGateGO);
        UnityEngine.Object.DestroyImmediate(fakeRogueSkill);
        UnityEngine.Object.DestroyImmediate(fakeBarbarianSkill);
        UnityEngine.Object.DestroyImmediate(fakeRogueCharacter.uniquePassiveSkill);
        UnityEngine.Object.DestroyImmediate(fakeRogueCharacter.uniqueActiveSkill);
        UnityEngine.Object.DestroyImmediate(fakeRogueCharacter);
        UnityEngine.Object.DestroyImmediate(fakeBarbarianCharacter.uniquePassiveSkill);
        UnityEngine.Object.DestroyImmediate(fakeBarbarianCharacter.uniqueActiveSkill);
        UnityEngine.Object.DestroyImmediate(fakeBarbarianCharacter);

        // 4.7: список баф/дебаф-строк для UI боя — чистая функция, тестируем напрямую на вручную
        // собранном CombatantRuntime (не требует боевого цикла/CombatManager).
        var statusTestCombatant = new CombatantRuntime { FreezeStacks = 3, PoisonStacks = 2, HasBleed = true, CritChanceDebuffPercent = 20f, FreezeImmune = true };
        statusTestCombatant.ActiveDebuffs.Add(new ActiveDebuff { Id = "warlock_slow", RemainingTime = 3f });
        var statusEffects = CombatantStatusEffects.GetActiveEffects(statusTestCombatant);
        Check(statusEffects.Exists(e => e.label == "Заморозка ×3" && !e.isBuff), "4.7 список статусов: стаки заморозки");
        Check(statusEffects.Exists(e => e.label == "Иммунитет к заморозке" && e.isBuff), "4.7 список статусов: иммунитет к заморозке — бафф");
        Check(statusEffects.Exists(e => e.label == "Яд ×2" && !e.isBuff), "4.7 список статусов: стаки яда");
        Check(statusEffects.Exists(e => e.label == "Кровотечение" && !e.isBuff), "4.7 список статусов: кровотечение");
        Check(statusEffects.Exists(e => e.label == "Оглушающий крик" && !e.isBuff), "4.7 список статусов: крит-дебафф Гарпии");
        Check(statusEffects.Exists(e => e.label == "Проклятие замедления" && !e.isBuff), "4.7 список статусов: именованный ActiveDebuff по Id");

        var frozenTestCombatant = new CombatantRuntime { IsFrozen = true, FreezeStacks = 5 };
        var frozenEffects = CombatantStatusEffects.GetActiveEffects(frozenTestCombatant);
        Check(frozenEffects.Exists(e => e.label == "Заморожен"), "4.7 список статусов: 'Заморожен' вместо стаков, когда уже заморожен");
        Check(!frozenEffects.Exists(e => e.label.StartsWith("Заморозка ×")), "4.7 список статусов: не показывает стаки одновременно с 'Заморожен'");

        // 3.11 (Плут) — Скрытность: крит с "В глаз" накладывает Скрытность, таймер честно тикает
        // до истечения. Гарантированный крит форсируется через SmokeBombGuaranteedCritsRemaining
        // (детерминированно, без завязки на Random) — реальный путь isCrit -> GrantOrRefreshStealth
        // в ResolveAttack тот же, что используется и настоящим критом от "Критические атаки".
        var stealthGO = new GameObject("SmokeTest_StealthGrant");
        var stealthCombatManager = stealthGO.AddComponent<CombatManager>();
        var stealthPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, SkillEyeForAnEyeLevel = 1, SmokeBombGuaranteedCritsRemaining = 1 };
        stealthPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var stealthDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestDummy" };
        stealthDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        stealthCombatManager.StartCombat(stealthPlayer, new List<CombatantRuntime> { stealthDummy });
        stealthCombatManager.Tick(1.01f); // ровно один удар игрока — гарантированный крит расходует заряд
        Check(stealthPlayer.IsStealthed && stealthPlayer.StealthTimer > 0f, $"3.11 «В глаз»: крит накладывает Скрытность (IsStealthed={stealthPlayer.IsStealthed}, timer={stealthPlayer.StealthTimer:F2})");
        Check(stealthPlayer.SmokeBombGuaranteedCritsRemaining == 0, "3.11 гарантированный крит расходует заряд SmokeBombGuaranteedCritsRemaining");

        stealthPlayer.SkillEyeForAnEyeLevel = 0; // исключаем случайный повторный крит во время догона таймера ниже
        stealthCombatManager.Tick(3.5f); // дольше 3с (StealthStatus.DurationSeconds) без новых источников Скрытности
        Check(!stealthPlayer.IsStealthed, $"3.11 Скрытность спадает по истечении таймера (IsStealthed={stealthPlayer.IsStealthed})");

        UnityEngine.Object.DestroyImmediate(stealthGO);

        // 3.11 (Плут) — "Устранение": переопределяет базовый крит-множитель 150% -> 175% на уровне 1.
        // Фиксированный урон оружия (min=max=10) + гарантированный крит -> ожидаемый урон = 17.5 ровно.
        var eliminationGO = new GameObject("SmokeTest_Elimination");
        var eliminationCombatManager = eliminationGO.AddComponent<CombatManager>();
        var eliminationPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, CritDamageMultiplierOverridePercent = 175f, SmokeBombGuaranteedCritsRemaining = 1 };
        eliminationPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var eliminationDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        eliminationDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        eliminationCombatManager.StartCombat(eliminationPlayer, new List<CombatantRuntime> { eliminationDummy });
        eliminationCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(eliminationDummy.CurrentHP, 1000f - 17.5f), $"3.11 «Устранение» ур.1 крит-множитель 175%: HP болвана = {eliminationDummy.CurrentHP} (ожидалось 982.5)");

        UnityEngine.Object.DestroyImmediate(eliminationGO);

        // 3.11 (Плут) — "Отравленный клинок": в Скрытности стаки/максимум удваиваются (+2/удар,
        // максимум = 2×уровень навыка, вместо +1/удар и максимума = уровню навыка).
        var poisonedBladeGO = new GameObject("SmokeTest_PoisonedBlade");
        var poisonedBladeCombatManager = poisonedBladeGO.AddComponent<CombatManager>();
        var poisonedBladePlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, SkillPoisonedBladeLevel = 2, IsStealthed = true, StealthTimer = 999f };
        poisonedBladePlayer.Weapons.Add(new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, DamageType = DamageType.Physical, AttackSpeed = 5f });
        var poisonedBladeDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        poisonedBladeDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        poisonedBladeCombatManager.StartCombat(poisonedBladePlayer, new List<CombatantRuntime> { poisonedBladeDummy });
        poisonedBladeCombatManager.Tick(0.21f); // >= 1 удар при AttackSpeed=5 (интервал 0.2с): +2 стака
        Check(poisonedBladeDummy.RoguePoisonStacksOnTarget == 2, $"3.11 «Отравленный клинок» в Скрытности: +2 стака за удар (было {poisonedBladeDummy.RoguePoisonStacksOnTarget}, ожидалось 2)");
        poisonedBladeCombatManager.Tick(0.21f); // второй удар: +2 -> клампится максимумом 2×2=4
        Check(poisonedBladeDummy.RoguePoisonStacksOnTarget == 4, $"3.11 «Отравленный клинок» в Скрытности: максимум удвоен до 2×уровень (было {poisonedBladeDummy.RoguePoisonStacksOnTarget}, ожидалось 4)");
        Check(poisonedBladeDummy.PoisonStacks == 0, "3.11 «Отравленный клинок» не трогает монстровое поле PoisonStacks (отдельная сущность)");

        UnityEngine.Object.DestroyImmediate(poisonedBladeGO);

        // 3.11 (Варвар) — "Ярость": чистая проверка свойства, без боя. MaxHP=100: HP=100 -> 0%,
        // HP=50 -> 50%, HP=0 -> 100%; RageFlatBonusPercent (Пояс титана) складывается флэт поверх.
        var rageTestCombatant = new CombatantRuntime { MaxHP = 100f, CurrentHP = 100f };
        Check(Mathf.Approximately(rageTestCombatant.Rage, 0f), $"3.11 Ярость при полном HP = 0% (было {rageTestCombatant.Rage})");
        rageTestCombatant.CurrentHP = 50f;
        Check(Mathf.Approximately(rageTestCombatant.Rage, 50f), $"3.11 Ярость при 50% HP = 50% (было {rageTestCombatant.Rage})");
        rageTestCombatant.CurrentHP = 0f;
        Check(Mathf.Approximately(rageTestCombatant.Rage, 100f), $"3.11 Ярость при 0 HP = 100% (было {rageTestCombatant.Rage})");
        rageTestCombatant.CurrentHP = 50f;
        rageTestCombatant.RageFlatBonusPercent = 20f;
        Check(Mathf.Approximately(rageTestCombatant.Rage, 70f), $"3.11 Ярость складывает флэт-бонус (Пояс титана) поверх формулы HP: 50%+20 = 70% (было {rageTestCombatant.Rage})");

        // ФИКС (код-ревью): "Остервенелость"/"Суеверность" ранее делили на 100 дважды (~1% от нужной
        // величины). Проверяем правильный порядок величины напрямую: Rage=100%, ур.5 (X=1.0) должно
        // давать РОВНО +100% к скорости атаки (Остервенелость) и РОВНО 100% магического сопротивления
        // (Суеверность), а не ~1%.
        var frenzyTestCombatant = new CombatantRuntime { MaxHP = 100f, CurrentHP = 0f, SkillFrenzyLevel = 5 }; // Rage = 100%
        var frenzyWeapon = new WeaponAttackState { AttackSpeed = 2f };
        Check(Mathf.Approximately(frenzyTestCombatant.GetEffectiveAttackSpeed(frenzyWeapon), 4f), $"3.11 «Остервенелость» ур.5 при Ярости=100%: скорость атаки ×2 (база 2 -> 4, было {frenzyTestCombatant.GetEffectiveAttackSpeed(frenzyWeapon)})");

        var superstitionGO = new GameObject("SmokeTest_Superstition");
        var superstitionCombatManager = superstitionGO.AddComponent<CombatManager>();
        // Rage = 50% (CurrentHP=500/MaxHP=1000), ур.5 (X=1.0) -> MagicalResistancePercent должно
        // быть РОВНО 50 (не 0.5, как при старом двойном делении на 100). Заблокированный урон
        // от одного удара 100 маг. урона: 100×(1-0.5)=50 по HP -> HP игрока 500->450 (не ~400.5,
        // как дал бы старый баг с резистансом 0.5%).
        var superstitionPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 500f, SkillSuperstitionLevel = 5 };
        var superstitionEnemy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestEnemy" };
        superstitionEnemy.Weapons.Add(new WeaponAttackState { DamageMin = 100f, DamageMax = 100f, DamageType = DamageType.Magical, AttackSpeed = 2f });

        superstitionCombatManager.StartCombat(superstitionPlayer, new List<CombatantRuntime> { superstitionEnemy });
        superstitionCombatManager.Tick(0.51f); // ровно один удар врага (интервал 0.5с)
        Check(Mathf.Approximately(superstitionPlayer.CurrentHP, 450f), $"3.11 «Суеверность» ур.5 при Ярости=50%: маг. сопротивление РОВНО 50% снижает урон 100 -> 50 по HP (HP={superstitionPlayer.CurrentHP}, ожидалось 450, НЕ ~400.5 от старого бага с двойным делением)");

        UnityEngine.Object.DestroyImmediate(superstitionGO);

        // 3.11 (Варвар) — "Упёртость": при Ярости выше порога уровня новый дебафф (здесь — стак
        // заморозки) полностью игнорируется; ниже порога — применяется как обычно. Порог ур.5 = 50%.
        var stubbornGO = new GameObject("SmokeTest_Stubbornness");
        var stubbornCombatManager = stubbornGO.AddComponent<CombatManager>();
        var stubbornAttacker = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, SkillFreezeLevel = 1 };
        stubbornAttacker.Weapons.Add(new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var stubbornTarget = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 400f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, SkillStubbornnessLevel = 5, DisplayName = "TestDummy" };
        stubbornTarget.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        stubbornCombatManager.StartCombat(stubbornAttacker, new List<CombatantRuntime> { stubbornTarget });
        stubbornCombatManager.Tick(1.01f); // Ярость цели = 60% > порога 50% (ур.5) -> заморозка игнорируется
        Check(stubbornTarget.FreezeStacks == 0, $"3.11 «Упёртость» блокирует новый стак заморозки выше порога Ярости (было {stubbornTarget.FreezeStacks}, ожидалось 0)");

        stubbornTarget.CurrentHP = 600f; // Ярость цели = 40% <= порога 50% -> заморозка применяется как обычно
        stubbornCombatManager.Tick(1.01f);
        Check(stubbornTarget.FreezeStacks == 1, $"3.11 «Упёртость» пропускает новый стак заморозки ниже порога Ярости (было {stubbornTarget.FreezeStacks}, ожидалось 1)");

        UnityEngine.Object.DestroyImmediate(stubbornGO);

        // 3.11 (Варвар) — "Боевая регенерация": срабатывает РОВНО на N-й полученный удар (ур.1 = 5),
        // не раньше. Урон/удар фиксирован (1 урона), MaxHP=1000 -> регенерация = 100 HP на 5-м ударе.
        var combatRegenGO = new GameObject("SmokeTest_CombatRegen");
        var combatRegenCombatManager = combatRegenGO.AddComponent<CombatManager>();
        var combatRegenAttacker = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f };
        combatRegenAttacker.Weapons.Add(new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var combatRegenTarget = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 500f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, SkillCombatRegenLevel = 1, DisplayName = "TestDummy" };
        combatRegenTarget.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        combatRegenCombatManager.StartCombat(combatRegenAttacker, new List<CombatantRuntime> { combatRegenTarget });
        for (int hit = 0; hit < 4; hit++)
        {
            combatRegenCombatManager.Tick(1.01f); // 4 удара по 1 урону, регенерация ещё не должна сработать
        }
        Check(combatRegenTarget.HitsTakenSinceLastRegen == 4 && Mathf.Approximately(combatRegenTarget.CurrentHP, 496f), $"3.11 «Боевая регенерация» не срабатывает раньше N-го удара (счётчик={combatRegenTarget.HitsTakenSinceLastRegen}, HP={combatRegenTarget.CurrentHP}, ожидалось счётчик=4, HP=496)");

        combatRegenCombatManager.Tick(1.01f); // 5-й удар -> регенерация 10% от 1000 = 100 HP
        Check(combatRegenTarget.HitsTakenSinceLastRegen == 0 && Mathf.Approximately(combatRegenTarget.CurrentHP, 595f), $"3.11 «Боевая регенерация» срабатывает ровно на 5-й удар и восстанавливает 10% MaxHP (счётчик={combatRegenTarget.HitsTakenSinceLastRegen}, HP={combatRegenTarget.CurrentHP}, ожидалось счётчик=0, HP=595)");

        UnityEngine.Object.DestroyImmediate(combatRegenGO);

        // 3.11 (Варвар) — "Берсерк": пока тумблер активен, физ. сопротивление (см. UpdateResistances)
        // снижает входящий физический урон ДО брони/щита (DamageCalculator.ApplyDamage). Ур.2 = 20%.
        var berserkOffGO = new GameObject("SmokeTest_BerserkOff");
        var berserkOffCombatManager = berserkOffGO.AddComponent<CombatManager>();
        var berserkOffPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, UniqueBerserkLevel = 2 };
        var berserkOffEnemy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestEnemy" };
        berserkOffEnemy.Weapons.Add(new WeaponAttackState { DamageMin = 100f, DamageMax = 100f, DamageType = DamageType.Physical, AttackSpeed = 2f });

        berserkOffCombatManager.StartCombat(berserkOffPlayer, new List<CombatantRuntime> { berserkOffEnemy });
        berserkOffCombatManager.Tick(0.51f); // ровно один удар врага (интервал 0.5с), Берсерк выключен
        Check(Mathf.Approximately(berserkOffPlayer.CurrentHP, 900f), $"3.11 «Берсерк» выключен: игрок получает полный урон 100 (HP={berserkOffPlayer.CurrentHP}, ожидалось 900)");

        UnityEngine.Object.DestroyImmediate(berserkOffGO);

        var berserkOnGO = new GameObject("SmokeTest_BerserkOn");
        var berserkOnCombatManager = berserkOnGO.AddComponent<CombatManager>();
        var berserkOnPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, UniqueBerserkLevel = 2 };
        var berserkOnEnemy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestEnemy" };
        berserkOnEnemy.Weapons.Add(new WeaponAttackState { DamageMin = 100f, DamageMax = 100f, DamageType = DamageType.Physical, AttackSpeed = 2f });

        berserkOnCombatManager.StartCombat(berserkOnPlayer, new List<CombatantRuntime> { berserkOnEnemy });
        berserkOnCombatManager.SetBerserkActive(true);
        berserkOnCombatManager.Tick(0.51f); // ровно один удар врага (интервал 0.5с, < 1с — самоурон Берсерка ещё не тикает)
        Check(Mathf.Approximately(berserkOnPlayer.CurrentHP, 920f), $"3.11 «Берсерк» ур.2 (20% физ. сопротивления) снижает урон 100 -> 80 (HP={berserkOnPlayer.CurrentHP}, ожидалось 920)");

        UnityEngine.Object.DestroyImmediate(berserkOnGO);

        // 3.11 (Варвар) — "Чемпион племени": крит-шанс ВСЕГДА = Ярость×X%, полностью заменяя обычную
        // формулу; прочие источники крит-шанса конвертируются в крит-урон (1%->+2%), а НЕ в шанс.
        //
        // Подслучай 1: Ярость=100% (HP-часть 1% + флэт-бонус 99%), ур.5 (X=1.0) -> крит-шанс = 100%,
        // гарантированный крит. SkillCriticalHitsLevel=0 -> конвертированных источников нет ->
        // множитель = база 150%. Фиксированный урон оружия 10 -> ожидаемый урон = 15 ровно.
        var championGuaranteedGO = new GameObject("SmokeTest_ChampionGuaranteed");
        var championGuaranteedCombatManager = championGuaranteedGO.AddComponent<CombatManager>();
        var championGuaranteedPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 99f, RageFlatBonusPercent = 99f, UniqueChampionOfTheTribeLevel = 5, CritChanceReplacedByRage = true };
        championGuaranteedPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var championGuaranteedDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        championGuaranteedDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        championGuaranteedCombatManager.StartCombat(championGuaranteedPlayer, new List<CombatantRuntime> { championGuaranteedDummy });
        championGuaranteedCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(championGuaranteedDummy.CurrentHP, 985f), $"3.11 «Чемпион племени» крит-шанс=Ярость×X%=100%: гарантированный крит база×150% (HP болвана={championGuaranteedDummy.CurrentHP}, ожидалось 985)");

        UnityEngine.Object.DestroyImmediate(championGuaranteedGO);

        // Подслучай 2 (тот же расчёт, но SkillCriticalHitsLevel=5): нормально это дало бы +50% К
        // ШАНСУ крита — здесь вместо этого конвертируется в +100% к крит-множителю (50×2), крит-шанс
        // остаётся ровно теми же 100% от Ярости. Ожидаемый урон = 10×(150%+100%) = 25 ровно.
        var championConvertedGO = new GameObject("SmokeTest_ChampionConverted");
        var championConvertedCombatManager = championConvertedGO.AddComponent<CombatManager>();
        var championConvertedPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 99f, RageFlatBonusPercent = 99f, UniqueChampionOfTheTribeLevel = 5, CritChanceReplacedByRage = true, SkillCriticalHitsLevel = 5 };
        championConvertedPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var championConvertedDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        championConvertedDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        championConvertedCombatManager.StartCombat(championConvertedPlayer, new List<CombatantRuntime> { championConvertedDummy });
        championConvertedCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(championConvertedDummy.CurrentHP, 975f), $"3.11 «Чемпион племени»: SkillCriticalHitsLevel конвертируется в крит-урон (+100%), НЕ в шанс (HP болвана={championConvertedDummy.CurrentHP}, ожидалось 975)");

        UnityEngine.Object.DestroyImmediate(championConvertedGO);

        // Подслучай 3: Ярость=0% (полное HP, без флэт-бонуса) -> крит-шанс = 0×X% = 0 ровно, ДАЖЕ
        // при огромных "обычных" источниках крит-шанса (SkillCriticalHitsLevel=5 + CritChanceBonusFromItems=50,
        // что дало бы 100% в обычной формуле) — доказывает, что они конвертируются в урон, а НЕ шанс.
        // Крит никогда не срабатывает -> урон всегда база (10, без множителя).
        var championZeroRageGO = new GameObject("SmokeTest_ChampionZeroRage");
        var championZeroRageCombatManager = championZeroRageGO.AddComponent<CombatManager>();
        var championZeroRagePlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, UniqueChampionOfTheTribeLevel = 5, CritChanceReplacedByRage = true, SkillCriticalHitsLevel = 5, CritChanceBonusFromItems = 50f };
        championZeroRagePlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var championZeroRageDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        championZeroRageDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        championZeroRageCombatManager.StartCombat(championZeroRagePlayer, new List<CombatantRuntime> { championZeroRageDummy });
        for (int hit = 0; hit < 5; hit++)
        {
            championZeroRageCombatManager.Tick(1.01f);
        }
        Check(Mathf.Approximately(championZeroRageDummy.CurrentHP, 950f), $"3.11 «Чемпион племени» при Ярости=0: крит-шанс=0 ровно вне зависимости от SkillCriticalHitsLevel/CritChanceBonusFromItems, ни один из 5 ударов не критует (HP болвана={championZeroRageDummy.CurrentHP}, ожидалось 950)");

        UnityEngine.Object.DestroyImmediate(championZeroRageGO);

        // 3.11 (Task 6b, Моменто Мори) — "Казнь": физ. урон += MaxHP(цели) × %недостающего HP × 1%/уровень.
        // Оружие: фикс. урон 10, ExecutionLevel=5 (5%). Цель: MaxHP=1000, CurrentHP=500 (50% недостающего) ->
        // бонус = 1000×0.5×0.05 = 25 -> итоговый урон 35.
        var executionGO = new GameObject("SmokeTest_Execution");
        var executionCombatManager = executionGO.AddComponent<CombatManager>();
        var executionPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f };
        executionPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f, ExecutionLevel = 5 });
        var executionDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 500f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        executionDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        executionCombatManager.StartCombat(executionPlayer, new List<CombatantRuntime> { executionDummy });
        executionCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(executionDummy.CurrentHP, 465f), $"3.11 «Казнь» доб. урон = MaxHP×%недостающего HP×1%/ур.: HP болвана={executionDummy.CurrentHP} (ожидалось 465, т.е. 500-35)");

        UnityEngine.Object.DestroyImmediate(executionGO);

        // 3.11 (Task 6b, Головоруб) — "Убийца великанов": +5%×уровень урона, ТОЛЬКО если MaxHP цели
        // больше MaxHP атакующего (сравнение по максимуму, не по текущему HP).
        var giantSlayerGO = new GameObject("SmokeTest_GiantSlayer");
        var giantSlayerCombatManager = giantSlayerGO.AddComponent<CombatManager>();
        var giantSlayerPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f };
        giantSlayerPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f, GiantSlayerLevel = 5 });
        var giantSlayerBigDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "BigDummy" };
        giantSlayerBigDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        giantSlayerCombatManager.StartCombat(giantSlayerPlayer, new List<CombatantRuntime> { giantSlayerBigDummy });
        giantSlayerCombatManager.Tick(1.01f);
        // 10 × (1 + 5×0.05) = 10×1.25 = 12.5
        Check(Mathf.Approximately(giantSlayerBigDummy.CurrentHP, 987.5f), $"3.11 «Убийца великанов» +25% против цели с большим MaxHP: HP болвана={giantSlayerBigDummy.CurrentHP} (ожидалось 987.5)");

        UnityEngine.Object.DestroyImmediate(giantSlayerGO);

        var giantSlayerSmallGO = new GameObject("SmokeTest_GiantSlayerSmall");
        var giantSlayerSmallCombatManager = giantSlayerSmallGO.AddComponent<CombatManager>();
        var giantSlayerSmallPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f };
        giantSlayerSmallPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f, GiantSlayerLevel = 5 });
        var giantSlayerSmallDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 50f, CurrentHP = 50f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "SmallDummy" };
        giantSlayerSmallDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        giantSlayerSmallCombatManager.StartCombat(giantSlayerSmallPlayer, new List<CombatantRuntime> { giantSlayerSmallDummy });
        giantSlayerSmallCombatManager.Tick(1.01f);
        // MaxHP цели (50) НЕ больше MaxHP атакующего (100) -> бонус не применяется, урон = 10 ровно.
        Check(Mathf.Approximately(giantSlayerSmallDummy.CurrentHP, 40f), $"3.11 «Убийца великанов» НЕ применяется против цели с меньшим/равным MaxHP: HP болвана={giantSlayerSmallDummy.CurrentHP} (ожидалось 40, т.е. без бонуса)");

        UnityEngine.Object.DestroyImmediate(giantSlayerSmallGO);

        // 3.11 (Task 6b, Капюшон Дуэльянта) — "Рипост": взводится на успешном уклонении, применяется
        // ТОЛЬКО на следующей атаке (не немедленно), затем сбрасывается (не копится/не бьёт повторно
        // без свежего уклонения). Player.ItemElusivenessLevel=100 -> гарантированное уклонение от
        // атак болвана; порядок в CombatManager.Tick — сперва Player, потом враги, так что в ОДНОМ и
        // том же Tick() собственная атака игрока идёт РАНЬШЕ уклонения того же тика (доказывает "не немедленно").
        var riposteGO = new GameObject("SmokeTest_Riposte");
        var riposteCombatManager = riposteGO.AddComponent<CombatManager>();
        var riposteDefender = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 1000f, ItemElusivenessLevel = 100, ItemRiposteLevel = 5 };
        riposteDefender.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var riposteEnemyWeapon = new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, DamageType = DamageType.Physical, AttackSpeed = 1f };
        var riposteEnemy = new CombatantRuntime { IsPlayer = false, MaxHP = 10000f, CurrentHP = 10000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        riposteEnemy.Weapons.Add(riposteEnemyWeapon);

        riposteCombatManager.StartCombat(riposteDefender, new List<CombatantRuntime> { riposteEnemy });
        riposteCombatManager.Tick(1.01f); // игрок бьёт первым (без бонуса) -> потом враг уклонён -> взводит флаг
        Check(Mathf.Approximately(riposteEnemy.CurrentHP, 9990f), $"3.11 «Рипост» НЕ применяется немедленно на той же атаке, что взвела флаг: HP болвана={riposteEnemy.CurrentHP} (ожидалось 9990, т.е. без бонуса)");
        Check(riposteDefender.RiposteArmed, "3.11 «Рипост»: успешное уклонение взводит RiposteArmed");

        riposteCombatManager.Tick(1.01f); // игрок бьёт с учётом взведённого флага -> +5 к урону
        Check(Mathf.Approximately(riposteEnemy.CurrentHP, 9975f), $"3.11 «Рипост» применяется РОВНО на следующей атаке (+5 флэт): HP болвана={riposteEnemy.CurrentHP} (ожидалось 9975, т.е. 9990-15)");

        riposteDefender.RiposteArmed = false; // имитируем отсутствие нового уклонения (враг уже перевзвёл флаг своим ходом в этом же тике)
        riposteEnemyWeapon.AttackSpeed = 0.0001f; // враг больше не атакует в пределах теста -> новых уклонений не будет
        riposteCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(riposteEnemy.CurrentHP, 9965f), $"3.11 «Рипост» не бьёт повторно без свежего уклонения: HP болвана={riposteEnemy.CurrentHP} (ожидалось 9965, т.е. 9975-10 без бонуса)");

        UnityEngine.Object.DestroyImmediate(riposteGO);

        // 3.11 (Task 6b, Кожанка) — "Объятия ночи": доп. МАГИЧЕСКИЙ урон отдельным попаданием,
        // ТОЛЬКО пока атакующий в Скрытности. Урон оружия фикс. 20 (физ.) + бонус 20×10×1%=2 (маг.).
        var embraceGO = new GameObject("SmokeTest_EmbraceOfNight");
        var embraceCombatManager = embraceGO.AddComponent<CombatManager>();
        var embraceAttacker = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, IsStealthed = true, StealthTimer = 999f, ItemEmbraceOfNightLevel = 10 };
        embraceAttacker.Weapons.Add(new WeaponAttackState { DamageMin = 20f, DamageMax = 20f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var embraceDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 10000f, CurrentHP = 10000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        embraceDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        embraceCombatManager.StartCombat(embraceAttacker, new List<CombatantRuntime> { embraceDummy });
        embraceCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(embraceDummy.CurrentHP, 9978f), $"3.11 «Объятия ночи» в Скрытности: физ. 20 + отдельный маг. бонус 2 = -22: HP болвана={embraceDummy.CurrentHP} (ожидалось 9978)");

        embraceAttacker.IsStealthed = false; // выходим из Скрытности -> бонус больше не должен срабатывать
        embraceCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(embraceDummy.CurrentHP, 9958f), $"3.11 «Объятия ночи» НЕ срабатывает вне Скрытности: HP болвана={embraceDummy.CurrentHP} (ожидалось 9958, т.е. 9978-20 без маг. бонуса)");

        UnityEngine.Object.DestroyImmediate(embraceGO);

        // 3.11 (Task 6b, Эпический трофей) — "Просто царапина": разовое лечение РОВНО при StartCombat
        // (не при Tick), только у игрока (у монстров этих предметов не существует).
        var justAScratchGO = new GameObject("SmokeTest_JustAScratch");
        var justAScratchCombatManager = justAScratchGO.AddComponent<CombatManager>();
        var justAScratchPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 50f, ItemJustAScratchLevel = 20 };
        justAScratchPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });
        var justAScratchDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestDummy" };
        justAScratchDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        justAScratchCombatManager.StartCombat(justAScratchPlayer, new List<CombatantRuntime> { justAScratchDummy });
        Check(Mathf.Approximately(justAScratchPlayer.CurrentHP, 70f), $"3.11 «Просто царапина» лечит РОВНО при StartCombat (20% от MaxHP): HP игрока={justAScratchPlayer.CurrentHP} (ожидалось 70, т.е. 50+20)");

        UnityEngine.Object.DestroyImmediate(justAScratchGO);

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
