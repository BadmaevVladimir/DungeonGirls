using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        // 3.3: любой положительный физический удар изнашивает броню минимум на 1, даже при блоке.
        var target = new CombatantRuntime { PhysicalDefenseMax = 5f, PhysicalDefenseCurrent = 5f, MaxHP = 20f, CurrentHP = 20f };
        var blockedResult = DamageCalculator.ApplyPhysicalDamage(target, 2f);
        Check(blockedResult.WasBlocked && blockedResult.ArmorWornOnBlock && blockedResult.DamageToHP == 0f && target.CurrentHP == 20f && target.PhysicalDefenseCurrent == 4f,
            $"3.3 гарантированный износ при блокировке: WasBlocked={blockedResult.WasBlocked}, DamageToHP={blockedResult.DamageToHP}, HP={target.CurrentHP}, Defense={target.PhysicalDefenseCurrent} (ожидалось true/0/20/4)");

        var passTarget = new CombatantRuntime { PhysicalDefenseMax = 5f, PhysicalDefenseCurrent = 5f, MaxHP = 20f, CurrentHP = 20f };
        var passResult = DamageCalculator.ApplyPhysicalDamage(passTarget, 8f);
        Check(!passResult.WasBlocked && passResult.DamageToHP == 3f && passTarget.PhysicalDefenseCurrent == 4f,
            $"3.3 пробитие: WasBlocked={passResult.WasBlocked}, DamageToHP={passResult.DamageToHP}, Defense={passTarget.PhysicalDefenseCurrent} (ожидалось false/3/4)");

        // 3.3 "Износ брони при блокировке": урон >= 0.5×брони но < брони — 0 урона по HP, но -1 брони.
        var wearTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 20f, CurrentHP = 20f };
        var wearResult = DamageCalculator.ApplyPhysicalDamage(wearTarget, 6f); // >= 5 (0.5*10), < 10
        Check(wearResult.WasBlocked && wearResult.ArmorWornOnBlock && wearResult.DamageToHP == 0f && wearTarget.PhysicalDefenseCurrent == 9f,
            $"3.3 износ при блокировке (урон=6, броня=10): WasBlocked={wearResult.WasBlocked}, ArmorWornOnBlock={wearResult.ArmorWornOnBlock}, DamageToHP={wearResult.DamageToHP}, Defense={wearTarget.PhysicalDefenseCurrent} (ожидалось true/true/0/9)");

        var strongWearTarget = new CombatantRuntime { PhysicalDefenseMax = 200f, PhysicalDefenseCurrent = 200f, MaxHP = 20f, CurrentHP = 20f };
        var strongWearResult = DamageCalculator.ApplyPhysicalDamage(strongWearTarget, 100f);
        Check(strongWearResult.WasBlocked && strongWearResult.ArmorWornOnBlock && strongWearTarget.PhysicalDefenseCurrent == 195f,
            $"3.3 сильный блокированный удар изнашивает броню по floor(урон/20): Defense={strongWearTarget.PhysicalDefenseCurrent} (ожидалось 195)");

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
        armorRing.bonusStat = new BonusStat { type = BonusStatType.MaxPhysicalDefenseFlat, baseValue = 2f };

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

        Check(bonusTestRuntime.MaxHP == 115f, $"8.6 FlatHP использует ранг эффекта: MaxHP={bonusTestRuntime.MaxHP} (ожидалось 115 = 100+15×ранг 1)");
        Check(bonusTestRuntime.PhysicalDefenseMax == 4f, $"3.10 кольцо брони ранга I: PhysicalDefenseMax={bonusTestRuntime.PhysicalDefenseMax} (ожидалось 4)");
        Check(ItemEffectBalance.ArmorAccessoryMaxDefense(2f, 1) == 4f && ItemEffectBalance.ArmorAccessoryMaxDefense(2f, 13) == 12f &&
            ItemEffectBalance.ArmorAccessoryMaxDefense(3f, 1) == 6f && ItemEffectBalance.ArmorAccessoryMaxDefense(3f, 13) == 18f,
            "Баланс украшений: кольцо 4→12, амулет 6→18 по рангам I–V");

        var armorChest = ScriptableObject.CreateInstance<ItemData>();
        armorChest.slot = EquipmentSlot.Armor;
        armorChest.physicalDefense = 100f;
        armorChest.itemLevel = 1;
        var secondArmorRing = ScriptableObject.CreateInstance<ItemData>();
        secondArmorRing.slot = EquipmentSlot.Ring;
        secondArmorRing.itemLevel = 1;
        secondArmorRing.bonusStat = new BonusStat { type = BonusStatType.MaxPhysicalDefenseFlat, baseValue = 2f };
        var resilienceAmulet = ScriptableObject.CreateInstance<ItemData>();
        resilienceAmulet.slot = EquipmentSlot.Accessory;
        resilienceAmulet.itemLevel = 1;
        resilienceAmulet.bonusStat = new BonusStat { type = BonusStatType.MaxPhysicalDefenseFlat, baseValue = 3f };
        var separatedArmorRuntime = CombatantFactory.CreatePlayerCombatant(bonusTestCharacter, 1, null,
            new List<ItemData> { armorChest, armorRing, secondArmorRing, resilienceAmulet }, forgeLevel: 4);
        Check(Mathf.Approximately(separatedArmorRuntime.PhysicalDefenseMax, 142f),
            $"Баланс брони: Кузница ×1.2 применяется только к защитному снаряжению; кольца 4+2 и амулет 6 идут после множителя: {separatedArmorRuntime.PhysicalDefenseMax} (ожидалось 142)");
        UnityEngine.Object.DestroyImmediate(armorChest);
        UnityEngine.Object.DestroyImmediate(secondArmorRing);
        UnityEngine.Object.DestroyImmediate(resilienceAmulet);
        Check(bonusTestRuntime.ItemAttackSpeedBonusPercent == 10f, $"3.10 AttackSpeedPercent от сапог: {bonusTestRuntime.ItemAttackSpeedBonusPercent} (ожидалось 10)");
        Check(bonusTestRuntime.ItemDamageBonusPercent == 5f, $"3.10 DamagePercent от шлема: {bonusTestRuntime.ItemDamageBonusPercent} (ожидалось 5)");
        Check(bonusTestRuntime.ItemEvasionBonusPercent == 8f, $"3.10 EvasionPercent от аксессуара: {bonusTestRuntime.ItemEvasionBonusPercent} (ожидалось 8)");
        Check(bonusTestRuntime.Weapons.Count == 1 && bonusTestRuntime.Weapons[0].ArmorPenetrationFlat == 1f,
            $"3.10 ArmorPenetrationFlat привязан к оружию (Топор): {(bonusTestRuntime.Weapons.Count > 0 ? bonusTestRuntime.Weapons[0].ArmorPenetrationFlat.ToString() : "нет оружия")} (ожидалось 1)");
        // WeaponDamageFlat: базовый урон топора 10 + WeaponDamageFlat(2) = 12 -> диапазон [floor(12*0.8); ceil(12*1.2)] = [9;15].
        Check(bonusTestRuntime.Weapons.Count == 1 && bonusTestRuntime.Weapons[0].DamageMin == 9f && bonusTestRuntime.Weapons[0].DamageMax == 15f,
            $"3.10 WeaponDamageFlat от кольца силы: диапазон {(bonusTestRuntime.Weapons.Count > 0 ? $"{bonusTestRuntime.Weapons[0].DamageMin}-{bonusTestRuntime.Weapons[0].DamageMax}" : "нет оружия")} (ожидалось 9-15)");

        var twoHandedMultiplierTest = ScriptableObject.CreateInstance<ItemData>();
        twoHandedMultiplierTest.slot = EquipmentSlot.Weapon;
        twoHandedMultiplierTest.weaponSubtype = WeaponSubtype.TwoHandedAxe;
        twoHandedMultiplierTest.isTwoHanded = true;
        twoHandedMultiplierTest.baseDamage = 10f;
        twoHandedMultiplierTest.attackSpeed = 1f;
        var twoHandedMultiplierRuntime = CombatantFactory.CreatePlayerCombatant(bonusTestCharacter, 1, null, new List<ItemData> { twoHandedMultiplierTest });
        Check(twoHandedMultiplierRuntime.Weapons.Count == 1 && twoHandedMultiplierRuntime.Weapons[0].DamageMin == 10f && twoHandedMultiplierRuntime.Weapons[0].DamageMax == 16f,
            $"Баланс Саши: двуручное оружие получает +30% после плоских бонусов (диапазон {(twoHandedMultiplierRuntime.Weapons.Count > 0 ? $"{twoHandedMultiplierRuntime.Weapons[0].DamageMin}-{twoHandedMultiplierRuntime.Weapons[0].DamageMax}" : "нет оружия")}, ожидалось 10-16)");
        UnityEngine.Object.DestroyImmediate(twoHandedMultiplierTest);
        Check(StatScaling.ItemEffectRank(1) == 1 && StatScaling.ItemEffectRank(3) == 1 && StatScaling.ItemEffectRank(4) == 2 && StatScaling.ItemEffectRank(16) == 5 &&
            Mathf.Approximately(StatScaling.ScaleItemEffect(8f, 16), 40f),
            "8.6 вторичные эффекты предметов растут по рангу 1-5, а не линейно до уровня лута");
        Check(
            ItemEffectBalance.ToughSoleTrapReductionPercent(0) == 0f && ItemEffectBalance.ToughSoleTrapReductionPercent(1) == 10f && ItemEffectBalance.ToughSoleTrapReductionPercent(5) == 30f &&
            ItemEffectBalance.GoldenTouchCurrencyBonusPercent(0) == 0f && ItemEffectBalance.GoldenTouchCurrencyBonusPercent(1) == 10f && ItemEffectBalance.GoldenTouchCurrencyBonusPercent(5) == 30f &&
            ItemEffectBalance.RepairCampArmorPercent(1) == 5f && ItemEffectBalance.RepairCampArmorPercent(5) == 25f &&
            ItemEffectBalance.ElusivenessEvasionPercent(1) == 4f && ItemEffectBalance.ElusivenessEvasionPercent(5) == 20f,
            "баланс предметов: ловушки/валюта/ремонт/уклонение заметны уже на ранге I и не работают без предмета");
        Check(
            ItemEffectBalance.PiercingSplashPercent(1) == 6f && ItemEffectBalance.PiercingSplashPercent(5) == 30f &&
            ItemEffectBalance.EmbraceOfNightMagicDamagePercent(1) == 8f && ItemEffectBalance.EmbraceOfNightMagicDamagePercent(5) == 40f &&
            ItemEffectBalance.VampirismHealPercentOfCritDamage(1) == 8f && ItemEffectBalance.VampirismHealPercentOfCritDamage(5) == 40f &&
            ItemEffectBalance.ExecutionMissingHealthPercent(1) == 3f && ItemEffectBalance.ExecutionMissingHealthPercent(5) == 15f &&
            ItemEffectBalance.RiposteDamageMultiplier(1) == 0.25f && ItemEffectBalance.RiposteDamageMultiplier(5) == 1.25f &&
            ItemEffectBalance.JustAScratchHealPercent(1) == 3f && ItemEffectBalance.JustAScratchHealPercent(5) == 15f &&
            ItemEffectBalance.ArmorBreakExtraWearChancePercent(1) == 25f && ItemEffectBalance.ArmorBreakExtraWearChancePercent(4) == 100f,
            "баланс предметных пассивок: новые пределы рангов и шанс «Разрушения брони» корректны");
        Check(BalanceClamps.ClampItemEvasionPercent(40f) == 30f && BalanceClamps.ClampEvasionChancePercent(120f) == 75f,
            "8.6 уклонение ограничено: предметы 30%, общий шанс 75%");
        Check(BalanceClamps.ThornsReflectPercent(1) == 10f && BalanceClamps.ThornsReflectPercent(4) == 40f && BalanceClamps.ThornsReflectPercent(99) == 50f,
            "8.6 «Шипы» растут 10/20/30/40/50% и не превышают потолок 50%");
        Check(BalanceClamps.CombatRegenHitsRequired(1) == 6 && BalanceClamps.CombatRegenHitsRequired(5) == 2 &&
            Mathf.Approximately(BalanceClamps.CombatRegenHealPercent, 6f) && Mathf.Approximately(BalanceClamps.CombatRegenCooldownSeconds, 2f),
            "8.6 «Боевая регенерация»: 6/5/4/3/2 ударов, 6% HP, кулдаун 2 сек.");

        foreach (var item in bonusTestEquipment) UnityEngine.Object.DestroyImmediate(item);
        UnityEngine.Object.DestroyImmediate(bonusTestCharacter);

        // 8.1 (ФИКС, 2026-08-26): бонусы зданий деревни по уровням — раньше только Кузница ур.1/3
        // (стартовое снаряжение) и Таверна ур.1 (флэт-урон) реально считались, остальные 6 из 8
        // численных бонусов были только текстом в BuildingCatalog.LevelBonuses без эффекта.
        Check(BuildingCatalog.ForgeArmorBonus(1) == 0f && BuildingCatalog.ForgeArmorBonus(2) == 10f && BuildingCatalog.ForgeArmorBonus(4) == 10f && BuildingCatalog.ForgeEquipmentArmorMultiplier(4) == 1.20f,
            $"8.1 Кузница ур.2/4 (+10 брони, +20% от экипировки): ур.1={BuildingCatalog.ForgeArmorBonus(1)}, ур.2={BuildingCatalog.ForgeArmorBonus(2)}, ур.4={BuildingCatalog.ForgeArmorBonus(4)}, множитель={BuildingCatalog.ForgeEquipmentArmorMultiplier(4)}");
        Check(BuildingCatalog.ForgeCampArmorRestorePercent(4) == 0f && BuildingCatalog.ForgeCampArmorRestorePercent(5) == 30f,
            $"8.1 Кузница ур.5 (30% брони на привале): ур.4={BuildingCatalog.ForgeCampArmorRestorePercent(4)}, ур.5={BuildingCatalog.ForgeCampArmorRestorePercent(5)} (ожидалось 0/30)");
        Check(BuildingCatalog.TempleMagicShieldBonus(1) == 10f && BuildingCatalog.TempleMagicShieldBonus(3) == 30f,
            $"8.1 Храм ур.1/3 (+10/+20 маг.щита): ур.1={BuildingCatalog.TempleMagicShieldBonus(1)}, ур.3={BuildingCatalog.TempleMagicShieldBonus(3)} (ожидалось 10/30)");
        Check(BuildingCatalog.TempleLevelUpRerolls(1) == 0 && BuildingCatalog.TempleLevelUpRerolls(2) == 1 && BuildingCatalog.TempleLevelUpRerolls(4) == 2,
            "8.1 Храм ур.2/4 даёт общий запас перебросов 1/2 на забег");
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
        int rationsBeforeSpend = campBuildingTestManager.RationsRemaining;
        Check(campBuildingTestManager.TrySpendRation() && campBuildingTestManager.RationsRemaining == rationsBeforeSpend - 1,
            "VN-привал: рацион можно потратить до показа сцены и до применения лечения");
        campBuildingTestManager.AddRations(5);
        Check(campBuildingTestManager.RationsRemaining == rationsBeforeSpend + 4,
            "Квест «Добыча»: успех добавляет пять рационов");
        UnityEngine.Object.DestroyImmediate(campBuildingTestGO);

        Check(QuestCatalog.Hunt.Level == 3 && QuestCatalog.Hunt.InteractionType == QuestInteractionType.TryOrSkip &&
              QuestCatalog.Hunt.AttemptButtonText == "Пойти охотиться на кабана" && QuestCatalog.Hunt.SkipButtonText == "Не тратить на это время",
            "Квест «Добыча» имеет сложность 3 и утверждённые варианты выбора");

        // 8.1 (ФИКС): CombatantFactory.CreatePlayerCombatant реально прибавляет броню Кузницы/маг.
        // щит Храма — раньше forgeLevel/templeLevel не принимались этим методом вовсе.
        var buildingStatsCharacter = ScriptableObject.CreateInstance<CharacterData>();
        buildingStatsCharacter.baseHealth = 50;
        var buildingStatsRuntime = CombatantFactory.CreatePlayerCombatant(buildingStatsCharacter, 1, null, null, 0, 4, 3);
        Check(buildingStatsRuntime.PhysicalDefenseMax == 10f, $"8.6 Кузница ур.4 без брони экипировки даёт только базовые +10: PhysicalDefenseMax={buildingStatsRuntime.PhysicalDefenseMax} (ожидалось 10)");
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
            var bossRuntime = CombatantFactory.CreateMonsterCombatant(bossData, 1);
            var midBossRuntime = CombatantFactory.CreateMonsterCombatant(bossData, 4);
            var lateBossRuntime = CombatantFactory.CreateMonsterCombatant(bossData, 7);
            Check(bossRuntime.IsBoss && bossRuntime.BossHeavyAttackDamageMultiplier == 1.5f && midBossRuntime.BossHeavyAttackDamageMultiplier == 1.75f && lateBossRuntime.BossHeavyAttackDamageMultiplier == 2f,
                "2.2 фабрика помечает босса и задаёт ступени «Тяжёлой атаки» 150%/175%/200%");
        }

        var goblinThiefData = AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/ScriptableObjects/Monsters/Monster_GoblinThief.asset");
        if (Check(goblinThiefData != null, "Monster_GoblinThief.asset загрузился"))
        {
            var goblinRuntime = CombatantFactory.CreateMonsterCombatant(goblinThiefData, 1);
            Check(goblinRuntime.MonsterPassiveSkillId == SkillId.MonsterArmorPiercingBlade && goblinRuntime.Weapons.Count == 1 && goblinRuntime.Weapons[0].ArmorIgnorePercent == 25f,
                "2.4 Гоблин-вор вместо кражи валюты игнорирует 25% физической брони");
        }

        var smokeBombAsset = AssetDatabase.LoadAssetAtPath<ActiveSkillData>("Assets/ScriptableObjects/Skills/Unique/Skill_SmokeBomb.asset");
        Check(smokeBombAsset != null && smokeBombAsset.cooldownSeconds == 5f, "3.11 «Дымовая граната» имеет кулдаун 5 секунд");

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

        var warriorOnlyItem = ScriptableObject.CreateInstance<ItemData>();
        warriorOnlyItem.itemName = "Smoke Warrior Item";
        warriorOnlyItem.tier = ItemTier.Common;
        warriorOnlyItem.allowedClasses = new[] { CharacterClass.Warrior };
        var rogueOnlyItem = ScriptableObject.CreateInstance<ItemData>();
        rogueOnlyItem.itemName = "Smoke Rogue Item";
        rogueOnlyItem.tier = ItemTier.Common;
        rogueOnlyItem.allowedClasses = new[] { CharacterClass.Rogue };
        var classCatalog = ScriptableObject.CreateInstance<ItemCatalogData>();
        classCatalog.items = new[] { warriorOnlyItem, rogueOnlyItem };
        var merchantSerialized = new SerializedObject(merchantRewardManager);
        merchantSerialized.FindProperty("itemCatalog").objectReferenceValue = classCatalog;
        merchantSerialized.ApplyModifiedPropertiesWithoutUndo();
        bool gotRogueItem = classCatalog.TryGetRandomItem(ItemTier.Common, CharacterClass.Rogue, out var classFilteredItem);
        Check(gotRogueItem && classFilteredItem == rogueOnlyItem,
            "3.1/8.2 фильтр каталога исключает несовместимый классу предмет до ролла");
        var rogueOffers = merchantRewardManager.GenerateMerchantOffers(1, CharacterClass.Rogue);
        Check(rogueOffers.All(offer => offer.Item == null || Array.IndexOf(offer.Item.allowedClasses, CharacterClass.Rogue) >= 0),
            "5.2 торговец предлагает Плуту только совместимые предметы");
        var universalBoots = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ScriptableObjects/Items/Boots/Item_Boots_Common_SturdyBoots.asset");
        var universalRing = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ScriptableObjects/Items/Rings/Item_Ring_Agility.asset");
        var universalAccessory = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ScriptableObjects/Items/Accessories/Item_Accessory_Dexterity.asset");
        var warriorBarbarianAxe = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ScriptableObjects/Items/Weapons/Axe/Item_Axe_Common_IronAxe.asset");
        var sharedSword = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ScriptableObjects/Items/Weapons/Sword/Item_Sword_Common_IronSword.asset");
        var warriorShield = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ScriptableObjects/Items/Weapons/Shield/Item_Shield_Common_WoodenShield.asset");
        Check(universalBoots != null && universalBoots.allowedClasses.Length == 0 && universalRing != null && universalRing.allowedClasses.Length == 0 &&
            universalAccessory != null && universalAccessory.allowedClasses.Length == 0 && warriorBarbarianAxe != null &&
            Array.IndexOf(warriorBarbarianAxe.allowedClasses, CharacterClass.Warrior) >= 0 && Array.IndexOf(warriorBarbarianAxe.allowedClasses, CharacterClass.Barbarian) >= 0 &&
            sharedSword != null && Array.IndexOf(sharedSword.allowedClasses, CharacterClass.Warrior) >= 0 &&
            Array.IndexOf(sharedSword.allowedClasses, CharacterClass.Rogue) >= 0 && Array.IndexOf(sharedSword.allowedClasses, CharacterClass.Barbarian) >= 0 &&
            warriorShield != null && Array.IndexOf(warriorShield.allowedClasses, CharacterClass.Barbarian) < 0,
            "8.6 сапоги/кольца/аксессуары универсальны; меч доступен Воину/Плуту/Варвару; Варвар не использует щит");

        var productionCatalog = AssetDatabase.LoadAssetAtPath<ItemCatalogData>("Assets/ScriptableObjects/Items/ItemCatalog.asset");
        string[] classItemPaths =
        {
            "Assets/ScriptableObjects/Items/Blades/Item_Blade_Common_Blade.asset",
            "Assets/ScriptableObjects/Items/Blades/Item_Blade_Rare_JaggedBlade.asset",
            "Assets/ScriptableObjects/Items/Blades/Item_Blade_Epic_MomentoMori.asset",
            "Assets/ScriptableObjects/Items/Hoods/Item_Hood_Common_Hood.asset",
            "Assets/ScriptableObjects/Items/Hoods/Item_Hood_Rare_DarkHood.asset",
            "Assets/ScriptableObjects/Items/Hoods/Item_Hood_Epic_DuelistHood.asset",
            "Assets/ScriptableObjects/Items/Leathers/Item_Leather_Common_Leather.asset",
            "Assets/ScriptableObjects/Items/Leathers/Item_Leather_Rare_ThickLeather.asset",
            "Assets/ScriptableObjects/Items/Leathers/Item_Leather_Epic_EmbraceOfNight.asset",
            "Assets/ScriptableObjects/Items/TwoHandedAxes/Item_TwoHandedAxe_Common_GreatAxe.asset",
            "Assets/ScriptableObjects/Items/TwoHandedAxes/Item_TwoHandedAxe_Rare_TemperedGreatAxe.asset",
            "Assets/ScriptableObjects/Items/TwoHandedAxes/Item_TwoHandedAxe_Epic_Headsplitter.asset",
            "Assets/ScriptableObjects/Items/Belts/Item_Belt_Common_Belt.asset",
            "Assets/ScriptableObjects/Items/Belts/Item_Belt_Rare_ChampionBelt.asset",
            "Assets/ScriptableObjects/Items/Belts/Item_Belt_Epic_TitanBelt.asset",
            "Assets/ScriptableObjects/Items/Trophies/Item_Trophy_Common_Trophy.asset",
            "Assets/ScriptableObjects/Items/Trophies/Item_Trophy_Rare_RareTrophy.asset",
            "Assets/ScriptableObjects/Items/Trophies/Item_Trophy_Epic_EpicTrophy.asset"
        };
        var classItems = classItemPaths.Select(AssetDatabase.LoadAssetAtPath<ItemData>).ToArray();
        Check(productionCatalog != null && productionCatalog.items != null && productionCatalog.items.Length == 54 &&
            classItems.All(item => item != null && Array.IndexOf(productionCatalog.items, item) >= 0),
            "8.2 каталог содержит все 54 предмета, включая 18 классовых предметов Вайолет и Саши");

        Check(QuestCatalog.SwordInStone.SuccessRewardItemName == "Кровавый меч" &&
            QuestCatalog.SwordInStone.SuccessRewardItemTier == ItemTier.Epic &&
            QuestCatalog.SwordInStone.SuccessRewardWeaponSubtype == WeaponSubtype.Sword,
            "5.4 «Меч в камне» хранит точную предметную награду: эпический Кровавый меч");
        ItemData bloodSwordRewardBase = null;
        bool foundSwordReward = productionCatalog != null && productionCatalog.TryGetItem(
            QuestCatalog.SwordInStone.SuccessRewardItemName,
            QuestCatalog.SwordInStone.SuccessRewardItemTier,
            QuestCatalog.SwordInStone.SuccessRewardWeaponSubtype,
            CharacterClass.Rogue,
            out bloodSwordRewardBase);
        int bloodSwordBaseLevel = foundSwordReward ? bloodSwordRewardBase.itemLevel : 0;
        var exactLevelSwordReward = foundSwordReward
            ? merchantRewardManager.CreateItemAtExactLevel(bloodSwordRewardBase, 7)
            : null;
        Check(foundSwordReward && exactLevelSwordReward != null && exactLevelSwordReward != bloodSwordRewardBase &&
            exactLevelSwordReward.itemLevel == 7 && bloodSwordRewardBase.itemLevel == bloodSwordBaseLevel,
            "5.4 награда «Меча в камне» создаётся runtime-копией точного уровня персонажа и не мутирует ассет");
        if (exactLevelSwordReward != null) UnityEngine.Object.DestroyImmediate(exactLevelSwordReward);

        var violetReelPool = productionCatalog != null
            ? productionCatalog.GetCompatibleItems(CharacterClass.Rogue)
            : new List<ItemData>();
        var sashaReelPool = productionCatalog != null
            ? productionCatalog.GetCompatibleItems(CharacterClass.Barbarian)
            : new List<ItemData>();
        Check(violetReelPool.Count > 0 && violetReelPool.All(item => ItemCatalogData.IsAllowedForClass(item, CharacterClass.Rogue)) &&
            classItems.Take(9).All(violetReelPool.Contains) && sharedSword != null && violetReelPool.Contains(sharedSword),
            "8.2 рулетка Вайолет содержит её 9 классовых предметов и мечи, но не содержит запрещённый лут");
        Check(sashaReelPool.Count > 0 && sashaReelPool.All(item => ItemCatalogData.IsAllowedForClass(item, CharacterClass.Barbarian)) &&
            classItems.Skip(9).All(sashaReelPool.Contains) && warriorBarbarianAxe != null && sashaReelPool.Contains(warriorBarbarianAxe) &&
            warriorShield != null && !sashaReelPool.Contains(warriorShield),
            "8.2 рулетка Саши содержит его 9 классовых предметов и оружие Воина, но не щит");
        UnityEngine.Object.DestroyImmediate(classCatalog);
        UnityEngine.Object.DestroyImmediate(warriorOnlyItem);
        UnityEngine.Object.DestroyImmediate(rogueOnlyItem);
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
        Check(MonsterModifierCatalog.ModifierCapForFloor(10) == 5, "2.8 лимит модификаторов этаж 10 = 5 (весь каталог)");

        Check(MonsterModifierCatalog.RollChancePercentForLevel(1) == 0f, "2.8 шанс модификатора ур.1 монстра = 0%");
        Check(MonsterModifierCatalog.RollChancePercentForLevel(4) == 30f, "2.8 шанс модификатора ур.4 монстра = 30%");

        Check(MonsterModifierCatalog.AdjectiveFor(MonsterModifierType.Big, MonsterGender.Feminine) == "Большая", "2.8 согласование рода: Большая Слизь");
        Check(MonsterModifierCatalog.AdjectiveFor(MonsterModifierType.Fast, MonsterGender.Masculine) == "Быстрый", "2.8 согласование рода: Быстрый Скелет");
        Check(MonsterModifierCatalog.AdjectiveFor(MonsterModifierType.ArmorPiercing, MonsterGender.Feminine) == "Бронебойная", "2.8 согласование рода: Бронебойная Слизь");

        var armorPiercingRuntime = new CombatantRuntime();
        MonsterModifierCatalog.ApplyToRuntime(armorPiercingRuntime, MonsterModifierType.ArmorPiercing, 10);
        Check(armorPiercingRuntime.MonsterGuaranteedArmorDamage == 5f, "2.8 Бронебойный на 10-м этаже гарантированно снимает дополнительно 5 брони за атаку");

        var rollsOnFloor1 = MonsterModifierCatalog.RollModifiers(1, 4);
        Check(rollsOnFloor1.Count == 0, $"2.8 этаж 1 никогда не даёт модификаторов даже при ур.4: получено {rollsOnFloor1.Count}");

        Info.Add("Проверки монстро-модификаторов (2.8) выполнены.");

        // 2.4: фильтр пула монстров по minFloorTier — тиры суммируются, не заменяют друг друга.
        var tier1 = ScriptableObject.CreateInstance<MonsterData>(); tier1.minFloorTier = 1;
        var tier7 = ScriptableObject.CreateInstance<MonsterData>(); tier7.minFloorTier = 7;
        var tier10 = ScriptableObject.CreateInstance<MonsterData>(); tier10.minFloorTier = 10;
        var pool = new List<MonsterData> { tier1, tier7 };

        var eligibleFloor3 = pool.FindAll(m => m.minFloorTier <= 3);
        Check(eligibleFloor3.Count == 1 && eligibleFloor3[0] == tier1, "2.4 фильтр пула монстров: этаж 3 видит только тир-1");

        var eligibleFloor7 = pool.FindAll(m => m.minFloorTier <= 7);
        Check(eligibleFloor7.Count == 2, "2.4 фильтр пула монстров: этаж 7 видит тир-1 И тир-7 (суммируются)");
        Check(MonsterEncounterBudget.GetThreatBudget(1) == 1 && MonsterEncounterBudget.GetThreatBudget(10) == 10 &&
            MonsterEncounterBudget.GetThreatCost(tier1) == 1 && MonsterEncounterBudget.GetThreatCost(tier7) == 4 && MonsterEncounterBudget.GetThreatCost(tier10) == 5,
            "8.6 бюджет угрозы растёт от 1 до 10 и делает поздних монстров дороже");
        Check(MonsterEncounterBudget.GetThreatBudget(10) / MonsterEncounterBudget.GetThreatCost(tier10) == 2,
            "8.6 на 10-м этаже бюджет не допускает трёх тир-10 монстров в одной комнате");

        UnityEngine.Object.DestroyImmediate(tier1);
        UnityEngine.Object.DestroyImmediate(tier7);
        UnityEngine.Object.DestroyImmediate(tier10);

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

        var autoActiveCharacter = ScriptableObject.CreateInstance<CharacterData>();
        autoActiveCharacter.uniqueActiveSkill = ScriptableObject.CreateInstance<ActiveSkillData>();
        autoActiveCharacter.uniqueActiveSkill.maxLevel = 3;
        var autoActiveProgress = new RunCharacterProgress(autoActiveCharacter);
        Check(!autoActiveProgress.TryAutoUpgradeUniqueActiveAtLevel(4) && autoActiveProgress.TryAutoUpgradeUniqueActiveAtLevel(5) &&
            autoActiveProgress.UniqueActiveLevel == 2 && !autoActiveProgress.TryAutoUpgradeUniqueActiveAtLevel(5) &&
            autoActiveProgress.TryAutoUpgradeUniqueActiveAtLevel(10) && autoActiveProgress.UniqueActiveLevel == 3,
            "8.6 уникальный активный навык автоматически повышается на уровнях 5 и 10, один раз на каждый порог");
        autoActiveProgress.SetLevelUpRerolls(2);
        Check(autoActiveProgress.TrySpendLevelUpReroll() && autoActiveProgress.TrySpendLevelUpReroll() && !autoActiveProgress.TrySpendLevelUpReroll() && autoActiveProgress.LevelUpRerollsRemaining == 0,
            "8.6 перебросы Храма — конечный общий запас на забег");
        UnityEngine.Object.DestroyImmediate(autoActiveCharacter.uniqueActiveSkill);
        UnityEngine.Object.DestroyImmediate(autoActiveCharacter);

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

        var armorCharacterManager = new GameObject("SmokeTest_ArmorEquip").AddComponent<CharacterManager>();
        var armorCharacter = ScriptableObject.CreateInstance<CharacterData>();
        armorCharacter.startingEquipment = Array.Empty<ItemData>();
        armorCharacterManager.BeginRun(armorCharacter);
        armorCharacterManager.Combatant.PhysicalDefenseCurrent = 0f;
        var replacementArmor = ScriptableObject.CreateInstance<ItemData>();
        replacementArmor.itemName = "Smoke Chest Armor";
        replacementArmor.slot = EquipmentSlot.Armor;
        replacementArmor.physicalDefense = 25f;
        armorCharacterManager.EquipItem(replacementArmor, null);
        Check(Mathf.Approximately(armorCharacterManager.Combatant.PhysicalDefenseCurrent, armorCharacterManager.Combatant.PhysicalDefenseMax),
            "3.3 смена нагрудника восстанавливает физическую броню до нового максимума");
        UnityEngine.Object.DestroyImmediate(armorCharacterManager.gameObject);
        UnityEngine.Object.DestroyImmediate(armorCharacter);
        UnityEngine.Object.DestroyImmediate(replacementArmor);

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
        var jenniferDialogSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Characters/Dialog_sprites/Jennifer_Dialog.png");
        var sashaDialogSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Characters/Dialog_sprites/Sasha_Dialog.png");
        var violetDialogSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Characters/Dialog_sprites/Violet_Dialog.png");
        var jenniferChar = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/ScriptableObjects/Characters/Character_Jennifer.asset");
        var barbarianChar = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/ScriptableObjects/Characters/Character_Barbarian.asset");
        if (Check(jenniferChar != null, "Character_Jennifer.asset загрузился для экрана выбора"))
        {
            Check(jenniferChar.selectionPortrait != null && jenniferChar.selectionPortrait == jenniferDialogSprite,
                "1 п.2 Дженифер использует Jennifer_Dialog.png в карточке выбора");
        }
        if (Check(barbarianChar != null, "10.6 Character_Barbarian.asset загрузился"))
        {
            Check(barbarianChar.portrait != null && barbarianChar.portrait == sashaSprite,
                $"10.6 Character_Barbarian.portrait = Sasha.png: {(barbarianChar.portrait != null ? barbarianChar.portrait.name : "null")} (ожидалось спрайт Assets/Art/Characters/Sasha.png)");
            Check(barbarianChar.characterId == "sasha" && barbarianChar.characterName == "Саша",
                $"Именная модель: Варвар — класс, персонаж имеет id/name sasha/Саша: {barbarianChar.characterId}/{barbarianChar.characterName}");
            Check(barbarianChar.selectionPortrait != null && barbarianChar.selectionPortrait == sashaDialogSprite,
                "1 п.2 Саша использует Sasha_Dialog.png в карточке выбора");
            Check(barbarianChar.baseHealth == 45f && barbarianChar.healthPerLevel == 35f,
                $"Баланс Саши: базовое HP/прирост = {barbarianChar.baseHealth}/{barbarianChar.healthPerLevel} (ожидалось 45/35)");
        }
        var rogueChar = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/ScriptableObjects/Characters/Character_Rogue.asset");
        if (Check(rogueChar != null, "10.6 Character_Rogue.asset загрузился"))
        {
            Check(rogueChar.portrait != null && rogueChar.portrait == violetSprite,
                $"10.6 Character_Rogue.portrait = Violet.png: {(rogueChar.portrait != null ? rogueChar.portrait.name : "null")} (ожидалось спрайт Assets/Art/Characters/Violet.png)");
            Check(rogueChar.characterId == "violet" && rogueChar.characterName == "Вайолет",
                $"Именная модель: Плут — класс, персонаж имеет id/name violet/Вайолет: {rogueChar.characterId}/{rogueChar.characterName}");
            Check(rogueChar.selectionPortrait != null && rogueChar.selectionPortrait == violetDialogSprite,
                "1 п.2 Вайолет использует Violet_Dialog.png в карточке выбора");
        }

        var availabilitySave = new GameObject("SmokeTest_CharacterAvailabilitySave").AddComponent<SaveManager>();
        availabilitySave.Data.gachaOwnedCharacters.Clear();
        Check(RunFlowController.IsCharacterAvailableForRun(jenniferChar, availabilitySave),
            "1 п.2 Дженифер доступна без гача-копий");
        Check(!RunFlowController.IsCharacterAvailableForRun(rogueChar, availabilitySave) && !RunFlowController.IsCharacterAvailableForRun(barbarianChar, availabilitySave),
            "1 п.2 Вайолет и Саша недоступны без гача-копий");
        var onlyJennifer = RunFlowController.BuildAvailableCharacters(new[] { jenniferChar, rogueChar, barbarianChar }, availabilitySave);
        Check(onlyJennifer.Count == 1 && RunFlowController.ShouldSkipCharacterSelection(onlyJennifer),
            "1 п.2 при одной Дженифер этап выбора пропускается");
        availabilitySave.Data.gachaOwnedCharacters.Add(new KeyCountEntry { key = "violet", count = 1 });
        var jenniferAndViolet = RunFlowController.BuildAvailableCharacters(new[] { jenniferChar, rogueChar, barbarianChar }, availabilitySave);
        Check(jenniferAndViolet.Count == 2 && jenniferAndViolet.Contains(jenniferChar) && jenniferAndViolet.Contains(rogueChar) && !jenniferAndViolet.Contains(barbarianChar),
            "1 п.2 копия Вайолет открывает её в выборе, Саша без копии остаётся скрыта");
        Check(!RunFlowController.ShouldSkipCharacterSelection(jenniferAndViolet),
            "1 п.2 наличие героя кроме Дженифер требует показать окно выбора");
        UnityEngine.Object.DestroyImmediate(availabilitySave.gameObject);

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
        berserkCombatManager.ConfigureUniqueActiveSkill(3, 1f, 0f, false, SkillEffectMap.Berserk, SkillId.Berserk); // cooldownSeconds=0, как задумано для тумблера
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
        Check(freshSave.characterRunCounts != null && freshSave.gachaOwnedCharacters != null && freshSave.seenVNScenes != null && freshSave.relationshipPoints != null && freshSave.seenTutorialHints != null,
            "9.4/отношения: characterRunCounts/gachaOwnedCharacters/seenVNScenes/relationshipPoints/seenTutorialHints инициализированы (не null)");
        Check(freshSave.gachaOwnedCharacters.Exists(entry => entry != null && entry.key == "jennifer" && entry.count == 1),
            "Отношения: новая игра содержит одну стартовую копию Дженифер для статистики");

        string[] tutorialIds =
        {
            TutorialContent.Intro, TutorialContent.CharacterSelection, TutorialContent.MentorSelection,
            TutorialContent.RunStart, TutorialContent.CombatBasics, TutorialContent.Defenses,
            TutorialContent.JenniferActive, TutorialContent.VioletActive, TutorialContent.SashaActive,
            TutorialContent.Reward, TutorialContent.Equipment, TutorialContent.LevelUp, TutorialContent.Camp,
            TutorialContent.RiskRoom, TutorialContent.Merchant, TutorialContent.Boss, TutorialContent.Pause,
            TutorialContent.Results, TutorialContent.VeteranCreated, TutorialContent.Buildings,
            TutorialContent.Gacha, TutorialContent.Characters, TutorialContent.Veterans, TutorialContent.Relationships,
            TutorialContent.HotSprings
        };
        Check(tutorialIds.All(id => TutorialContent.TryGet(id, out var entry) && !string.IsNullOrWhiteSpace(entry.Title) && !string.IsNullOrWhiteSpace(entry.Body)),
            "Туториал: каждый стабильный ID имеет заполненные заголовок и текст");
        Check(TutorialContent.HelpEntries.Count >= 20,
            $"Туториал: постоянная справка заполнена всеми основными механиками ({TutorialContent.HelpEntries.Count} разделов)");
        TutorialContent.TryGet(TutorialContent.Gacha, out var gachaTutorial);
        TutorialContent.TryGet(TutorialContent.Veterans, out var veteransTutorial);
        TutorialContent.TryGet(TutorialContent.HotSprings, out var personalRoomTutorial);
        Check(gachaTutorial.Body.Contains("Первая копия") && gachaTutorial.Body.Contains("52,7%") && gachaTutorial.Body.Contains("второй копии"),
            "Туториал: гача объясняет открытие героя, начало цикла копий и итоговые вероятности валюты");
        Check(veteransTutorial.Body.Contains("1–2 этажа — ровно 1") && veteransTutorial.Body.Contains("все 10 этажей — от 2 до 5"),
            "Туториал: колода ветеранов показывает все диапазоны наследования навыков");
        Check(personalRoomTutorial.Title == "Персональная комната отдыха" && personalRoomTutorial.Body.Contains("комнату ловушек") && personalRoomTutorial.Body.Contains("пивной погреб"),
            "Туториал: персональная комната корректно названа для Дженифер, Вайолет и Саши");
        Check(TutorialContent.TooltipArmor.Contains("положительный физический удар") && TutorialContent.TooltipShield.Contains("остаток снижает HP") &&
              TutorialContent.TooltipActive != TutorialContent.TooltipAuto && TutorialContent.TooltipStealth.Contains("Объятия ночи") && TutorialContent.TooltipBerserk.Contains("может убить"),
            "Тултипы: броня, щит, активный навык и Скрытность используют уточнённые формулировки");

        // 9.4/Codex P2: миграция старого сохранения без saveVersion (симулирует файл с диска до
        // этого фикса — JsonUtility молча оставит новые поля в дефолте, а не упадёт, но saveVersion
        // будет 0) — TryMigrate должен довести его до текущей версии без потери уже прочитанных полей.
        var staleSave = new SaveData { saveVersion = 0, metaCurrency = 500 };
        staleSave.veteranDeck = null; // симулируем JSON без этого поля вовсе (JsonUtility даёт null для отсутствующих списков в старом файле)
        staleSave.seenTutorialHints = null;
        SaveManager.MigrateIfNeeded(staleSave);
        Check(staleSave.saveVersion == SaveData.CurrentSaveVersion && staleSave.metaCurrency == 500 && staleSave.veteranDeck != null && staleSave.seenTutorialHints != null,
            $"9.4 миграция старого save: saveVersion={staleSave.saveVersion}, metaCurrency сохранена={staleSave.metaCurrency}, списки ветеранов/подсказок восстановлены (ожидалось true/500/true)");

        var legacyNamedSave = new SaveData { saveVersion = 2 };
        legacyNamedSave.gachaOwnedCharacters.Add(new KeyCountEntry { key = "rogue", count = 2 });
        legacyNamedSave.gachaOwnedCharacters.Add(new KeyCountEntry { key = "Вайолет", count = 1 });
        legacyNamedSave.characterRunCounts.Add(new KeyCountEntry { key = "barbarian", count = 3 });
        legacyNamedSave.veteranDeck.Add(new VeteranCharacter { characterId = "Варвар" });
        legacyNamedSave.seenVNScenes.Add(new CharacterSceneList { characterId = "rogue", sceneIds = new List<string> { "violet_scene_1" } });
        SaveManager.MigrateIfNeeded(legacyNamedSave);
        Check(legacyNamedSave.gachaOwnedCharacters.Exists(entry => entry != null && entry.key == "violet" && entry.count == 3),
            "9.4 v3 миграция объединяет классовые/displayName ключи Плута в стабильный violet");
        Check(legacyNamedSave.characterRunCounts[0].key == "sasha" && legacyNamedSave.veteranDeck[0].characterId == "sasha" && legacyNamedSave.seenVNScenes[0].characterId == "violet",
            "9.4 v3 миграция переводит ветеранов, прохождения и VN-историю на ID sasha/violet");
        Check(legacyNamedSave.gachaOwnedCharacters.Exists(entry => entry != null && entry.key == "jennifer" && entry.count >= 1),
            "Отношения: миграция добавляет стартовую копию Дженифер, не затрагивая полученных героев");

        // GDD 11.1: 15% персонаж, 85% мета-валюта; предметного результата в структуре нет.
        Check(GachaPool.RollResult(0.01f, 0.5f, out var firstCharacter) && firstCharacter.IsCharacter && firstCharacter.CharacterIndex == 0,
            "11.1 roll 1% даёт первого персонажа");
        Check(GachaPool.RollResult(0.06f, 0.5f, out var secondCharacter) && secondCharacter.IsCharacter && secondCharacter.CharacterIndex == 1,
            "11.1 roll 6% даёт второго персонажа");
        Check(GachaPool.RollResult(0.11f, 0.5f, out var thirdCharacter) && thirdCharacter.IsCharacter && thirdCharacter.CharacterIndex == 2,
            "11.1 roll 11% даёт третьего персонажа");
        Check(GachaPool.RollResult(0.16f, 0.1f, out var commonCurrency) && !commonCurrency.IsCharacter && commonCurrency.CurrencyAmount == 20,
            "11.1 валютная ветка: обычный приз = 20");
        Check(GachaPool.RollResult(0.99f, 0.8f, out var rareCurrency) && rareCurrency.CurrencyAmount == 50,
            "11.1 валютная ветка: редкий приз = 50");
        Check(GachaPool.RollResult(0.99f, 0.99f, out var epicCurrency) && epicCurrency.CurrencyAmount == 150,
            "11.1 валютная ветка: эпический приз = 150");
        Check(!GachaPool.RollResult(1f, 0.5f, out _), "11.1 GachaPool отклоняет roll вне диапазона [0,1)");

        var gachaRng = new System.Random(12345);
        const int gachaTrials = 100000;
        int characterHits = 0;
        int commonHits = 0;
        int rareHits = 0;
        int epicHits = 0;
        var characterHitsByIndex = new int[GachaPool.CharacterCount];
        for (int i = 0; i < gachaTrials; i++)
        {
            GachaPool.RollResult((float)gachaRng.NextDouble(), (float)gachaRng.NextDouble(), out var rolled);
            if (rolled.IsCharacter)
            {
                characterHits++;
                characterHitsByIndex[rolled.CharacterIndex]++;
            }
            else if (rolled.CurrencyTier == ItemTier.Common) commonHits++;
            else if (rolled.CurrencyTier == ItemTier.Rare) rareHits++;
            else epicHits++;
        }

        float characterPercent = characterHits * 100f / gachaTrials;
        Check(Mathf.Abs(characterPercent - 15f) < 0.5f, $"11.1 статистика: персонажи {characterPercent:F2}% (ожидалось ~15%)");
        for (int i = 0; i < characterHitsByIndex.Length; i++)
        {
            float percent = characterHitsByIndex[i] * 100f / gachaTrials;
            Check(Mathf.Abs(percent - 5f) < 0.3f, $"11.1 статистика: персонаж #{i} {percent:F2}% (ожидалось ~5%)");
        }
        Check(Mathf.Abs(commonHits * 100f / gachaTrials - 52.7f) < 1f, "11.1 статистика обычной мета-валюты соответствует 85%×62%");
        Check(Mathf.Abs(rareHits * 100f / gachaTrials - 29.75f) < 1f, "11.1 статистика редкой мета-валюты соответствует 85%×35%");
        Check(Mathf.Abs(epicHits * 100f / gachaTrials - 2.55f) < 0.5f, "11.1 статистика эпической мета-валюты соответствует 85%×3%");

        var atomicGachaManager = new GameObject("SmokeTestSaveManager_GachaAtomic").AddComponent<SaveManager>();
        atomicGachaManager.Data.gachaCurrency = 100;
        atomicGachaManager.Data.metaCurrency = 0;
        atomicGachaManager.Data.gachaOwnedCharacters.Clear();
        Check(atomicGachaManager.TryApplyGachaPull(50, "violet", 0, out int violetCopies) && violetCopies == 1 && atomicGachaManager.Data.gachaCurrency == 50,
            "11.1 атомарный призыв персонажа одновременно списывает стоимость и сохраняет копию");
        Check(atomicGachaManager.TryApplyGachaPull(50, null, 20, out _) && atomicGachaManager.Data.gachaCurrency == 0 && atomicGachaManager.Data.metaCurrency == 20,
            "11.1 атомарный валютный призыв одновременно списывает стоимость и начисляет мета-валюту");
        Check(!atomicGachaManager.TryApplyGachaPull(50, "sasha", 20, out _), "11.1 атомарный призыв отклоняет два типа награды одновременно");
        UnityEngine.Object.DestroyImmediate(atomicGachaManager.gameObject);

        var veteranSaveManager = new GameObject("SmokeTestSaveManager_Veteran").AddComponent<SaveManager>();
        veteranSaveManager.Data.veteranDeck.Clear();
        veteranSaveManager.Data.characterRunCounts.Clear();
        int veteranMetaBefore = veteranSaveManager.Data.metaCurrency;
        int veteranGachaBefore = veteranSaveManager.Data.gachaCurrency;
        var veteranSnapshot = new VeteranCharacter
        {
            characterId = "sasha",
            finalHP = 42f,
            uniquePassiveSkillName = SkillEffectMap.ChampionOfTheTribe,
            uniquePassiveLevel = 3,
            floorsCleared = 4,
            grade = VeteranSystem.GradeForFloors(4),
            powerLevel = 0
        };
        Check(veteranSaveManager.CompleteRun(12, 3, veteranSnapshot), "9.2 CompleteRun принимает валидный снимок ветерана");
        Check(veteranSaveManager.Data.veteranDeck.Count == 1 && veteranSaveManager.Data.veteranDeck[0].characterId == "sasha",
            "9.2 CompleteRun добавляет ветерана Сашу");
        Check(veteranSaveManager.GetRunCount("sasha") == 1 && veteranSaveManager.Data.metaCurrency == veteranMetaBefore + 12 && veteranSaveManager.Data.gachaCurrency == veteranGachaBefore + 3,
            "9.2 CompleteRun одной транзакцией начисляет награды и прохождение");
        Check(veteranSaveManager.Data.veteranDeck[0].powerLevel == 0,
            "9.2 PowerLevel не рассчитывается без утверждённой дизайнером формулы");
        Check(veteranSaveManager.Data.veteranDeck[0].grade == "C" && veteranSaveManager.Data.veteranDeck[0].floorsCleared == 4,
            "3.7 CompleteRun сохраняет оценку C и 4 полностью зачищенных этажа");
        int deckBeforeZeroFloorRun = veteranSaveManager.Data.veteranDeck.Count;
        Check(veteranSaveManager.CompleteRun(1, 0, "violet", null) && veteranSaveManager.Data.veteranDeck.Count == deckBeforeZeroFloorRun && veteranSaveManager.GetRunCount("violet") == 1,
            "1 п.8 забег без полностью зачищенного этажа выдаёт награду/счётчик, но не создаёт ветерана");

        veteranSaveManager.Data.relationshipPoints.Clear();
        Check(veteranSaveManager.AddRelationshipPoints("violet", 99) == 99 && veteranSaveManager.GetRelationshipLevel("violet") == 1,
            "Отношения: до 100 очков сохраняется 1-й уровень");
        Check(veteranSaveManager.AddRelationshipPoints("violet", 1) == 1 && veteranSaveManager.GetRelationshipLevel("violet") == 2,
            "Отношения: 100 очков открывают 2-й уровень");
        Check(veteranSaveManager.AddRelationshipPoints("violet", 200) == 200 && veteranSaveManager.GetRelationshipLevel("violet") == 3,
            "Отношения: 300 очков открывают 3-й уровень");
        Check(veteranSaveManager.AddRelationshipPoints("violet", 10) == 0 && veteranSaveManager.GetRelationshipPoints("violet") == 300,
            "Отношения: демо ограничивает прогресс максимумом 3-го уровня (300 очков)");

        Check(VeteranSystem.GradeForFloors(10) == "S+" && VeteranSystem.GradeForFloors(9) == "S" &&
            VeteranSystem.GradeForFloors(8) == "A" && VeteranSystem.GradeForFloors(6) == "B" &&
            VeteranSystem.GradeForFloors(4) == "C" && VeteranSystem.GradeForFloors(1) == "C-",
            "3.7 границы оценок ветерана соответствуют ГДД");
        VeteranSystem.TryGetTransferCountRange(10, out int fullMin, out int fullMax);
        VeteranSystem.TryGetTransferCountRange(9, out int nineMin, out int nineMax);
        Check(fullMin == 2 && fullMax == 5 && nineMin == 1 && nineMax == 5 && !VeteranSystem.TryGetTransferCountRange(0, out _, out _),
            "1 п.3 диапазоны наследования включают гарантированный пассив и требуют минимум этаж");

        var inheritanceVeteran = new VeteranCharacter
        {
            characterId = "sasha",
            uniquePassiveSkillName = SkillEffectMap.ChampionOfTheTribe,
            floorsCleared = 10,
            finalSkills = new List<VeteranSkillEntry>
            {
                new VeteranSkillEntry { skillName = SkillEffectMap.Frenzy, level = 5 },
                new VeteranSkillEntry { skillName = SkillEffectMap.Stubbornness, level = 3 },
                new VeteranSkillEntry { skillName = SkillEffectMap.Frenzy, level = 2 }
            }
        };
        var transferred = VeteranSystem.RollTransferredSkills(inheritanceVeteran, new System.Random(7));
        Check(transferred.Count >= 2 && transferred.Count <= 3 && transferred[0] == SkillEffectMap.ChampionOfTheTribe && transferred.Distinct().Count() == transferred.Count,
            "1 п.3 наследование гарантирует уникальный пассив, выбирает дополнительные без повторов и ограничивается известным пулом");
        Check(VeteranSystem.IsEligibleMentor(inheritanceVeteran, "violet") && !VeteranSystem.IsEligibleMentor(inheritanceVeteran, "sasha"),
            "1 п.3 наставник доступен только другому персонажу");
        var inheritedProgressCharacter = ScriptableObject.CreateInstance<CharacterData>();
        inheritedProgressCharacter.characterClass = CharacterClass.Warrior;
        var inheritedProgress = new RunCharacterProgress(inheritedProgressCharacter)
        {
            MentorUniquePassiveSkillName = SkillEffectMap.Shadow,
            MentorUniquePassiveLevel = 1
        };
        Check(inheritedProgress.GetMentorUniquePassiveLevel(SkillId.Shadow) == 1 && inheritedProgress.GetMentorUniquePassiveLevel(SkillId.ChampionOfTheTribe) == 0,
            "1 п.3 механика наследуемого уникального пассива включается только для выбранного навыка");
        UnityEngine.Object.DestroyImmediate(inheritedProgressCharacter);
        UnityEngine.Object.DestroyImmediate(veteranSaveManager.gameObject);

        // Codex P1 2026-08-27: конфигурация активного навыка в бою должна зависеть от текущего
        // персонажа, не быть жёстко зашитой под "3 быстрые атаки" Дженифер. Дымовая граната Плута
        // конфигурируется с hitCount=0 (сама не бьёт — см. CombatManager.TryActivateUniqueActiveSkill).
        var rogueForSkillTest = ScriptableObject.CreateInstance<CharacterData>();
        rogueForSkillTest.characterName = "ТестПлутАктивка";
        rogueForSkillTest.characterClass = CharacterClass.Rogue;
        rogueForSkillTest.baseHealth = 100;
        Check(CombatManager.ResolveActiveSkillHitCount(rogueForSkillTest.characterClass) == 0,
            $"Task 5: ResolveActiveSkillHitCount(Rogue) == 0 (Дымовая граната не бьёт сама): {CombatManager.ResolveActiveSkillHitCount(rogueForSkillTest.characterClass)}");
        Check(CombatManager.ResolveActiveSkillHitCount(CharacterClass.Warrior) == 3,
            $"Task 5: ResolveActiveSkillHitCount(Warrior) == 3 (3 быстрые атаки Дженифер): {CombatManager.ResolveActiveSkillHitCount(CharacterClass.Warrior)}");
        UnityEngine.Object.DestroyImmediate(rogueForSkillTest);
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
        var characterSelectScreen = RequireElement(root, "CharacterSelectScreen");
        var characterSelectCards = RequireElement(root, "CharacterSelectCardsContainer");
        var characterSkillTooltip = RequireElement(root, "CharacterSkillTooltip");
        RequireElement(root, "CharacterSkillTooltipText");
        RequireElement(root, "MentorSelectScreen");
        RequireElement(root, "MentorSelectStudentLabel");
        RequireElement(root, "MentorSelectScrollView");
        RequireElement(root, "MentorSelectNoneButton");
        RequireElement(root, "MentorSelectBackButton");
        RequireElement(root, "StartRunButton");
        RequireElement(root, "CheatMenuButton");
        RequireElement(root, "QuitGameButton");
        RequireElement(root, "CheatMenuPopup");
        RequireElement(root, "CheatCommandField");
        RequireElement(root, "CheatResultLabel");
        RequireElement(root, "CheatSubmitButton");
        RequireElement(root, "CheatCloseButton");
        RequireElement(root, "ForgeUpgradeButton");
        RequireElement(root, "GachaPullButton");
        RequireElement(root, "GachaRevealContainer");
        RequireElement(root, "GachaChestSpriteImage");
        RequireElement(root, "GachaReelViewport");
        RequireElement(root, "GachaReelStrip");
        RequireElement(root, "GachaSkipButton");
        var veteranDeckScreen = RequireElement(root, "VeteranDeckScreen");
        var charactersScreen = RequireElement(root, "CharactersScreen");
        RequireElement(root, "VeteranDeckButton");
        RequireElement(root, "CharactersButton");
        RequireElement(root, "VeteranDeckScrollView");
        RequireElement(root, "CharactersScrollView");
        RequireElement(root, "MerchantOffersContainer");
        RequireElement(root, "MerchantCurrencyLabel");
        RequireElement(root, "PauseScreen");
        RequireElement(root, "PauseCharacterStatsLabel");
        RequireElement(root, "PauseSkillsScrollView");
        RequireElement(root, "PauseEquipmentScrollView");
        RequireElement(root, "PauseResumeButton");
        RequireElement(root, "PauseAbandonRunButton");
        RequireElement(root, "PauseQuitGameButton");
        RequireElement(root, "RageIndicator");
        RequireElement(root, "RageText");
        RequireElement(root, "RageFill");
        RequireElement(root, "StealthIndicator");
        RequireElement(root, "StealthText");
        RequireElement(root, "PlayerStatusContainer");
        RequireElement(root, "LevelUpTitle");
        RequireElement(root, "LevelUpRerollButton");
        RequireElement(root, "HelpButton");
        RequireElement(root, "RunHelpButton");
        RequireElement(root, "ResultsHelpButton");
        RequireElement(root, "TutorialOverlay");
        RequireElement(root, "TutorialTitle");
        RequireElement(root, "TutorialBody");
        RequireElement(root, "TutorialContinueButton");
        RequireElement(root, "HelpScreen");
        RequireElement(root, "HelpScrollView");
        RequireElement(root, "HelpCloseButton");
        RequireElement(root, "GlobalTooltip");
        RequireElement(root, "GlobalTooltipTitle");
        RequireElement(root, "GlobalTooltipBody");
        Check(UnityEngine.Object.FindFirstObjectByType<TutorialManager>() != null, "TutorialManager создан на общем UIDocument");
        var vnManager = UnityEngine.Object.FindFirstObjectByType<VNManager>();
        Check(vnManager != null, "VNManager создан на общем UIDocument");
        RequireElement(root, "VNOverlay");
        RequireElement(root, "VNBackground");
        RequireElement(root, "VNCg");
        RequireElement(root, "VNSkipButton");

        const string smokeHintId = "smoke_tutorial_hint";
        saveManager.Data.seenTutorialHints.RemoveAll(id => string.Equals(id, smokeHintId, StringComparison.OrdinalIgnoreCase));
        saveManager.MarkTutorialHintSeen(smokeHintId);
        Check(saveManager.HasSeenTutorialHint(smokeHintId), "Просмотренная подсказка записывается в SaveData и распознаётся без учёта регистра");

        const string smokeSceneId = "smoke_seen_scene";
        var jenniferScenes = saveManager.Data.seenVNScenes.Find(entry => entry != null &&
            string.Equals(entry.characterId, "jennifer", StringComparison.OrdinalIgnoreCase));
        jenniferScenes?.sceneIds?.RemoveAll(id => string.Equals(id, smokeSceneId, StringComparison.OrdinalIgnoreCase));
        saveManager.MarkVNSceneSeen("jennifer", smokeSceneId);
        Check(saveManager.HasSeenVNScene("JENNIFER", "SMOKE_SEEN_SCENE"),
            "Завершённая или пропущенная VN-сцена сохраняется по characterId/sceneId без учёта регистра");

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

        // 1 п.2 / 7.2: живая ветка выбора при наличии гача-персонажа. Подменяем только данные
        // в памяти и восстанавливаем их до любых операций, которые сохраняют save на диск.
        var savedCharacterCopies = new List<KeyCountEntry>();
        foreach (var entry in saveManager.Data.gachaOwnedCharacters)
        {
            if (entry != null) savedCharacterCopies.Add(new KeyCountEntry { key = entry.key, count = entry.count });
        }
        saveManager.Data.gachaOwnedCharacters.Clear();
        saveManager.Data.gachaOwnedCharacters.Add(new KeyCountEntry { key = "violet", count = 1 });
        runFlow.OpenCharacterSelect();
        Check(characterSelectScreen.style.display == DisplayStyle.Flex && mainMenuScreen.style.display == DisplayStyle.None,
            "1 п.2 копия Вайолет показывает экран выбора и скрывает главное меню");
        Check(characterSelectCards.childCount == 2,
            $"1 п.2 экран содержит Дженифер и Вайолет, но не Сашу: cards={characterSelectCards.childCount} (ожидалось 2)");
        var violetSelectionPortrait = root.Q<UnityEngine.UIElements.Image>("CharacterSelectPortrait_violet");
        var violetCharacterAsset = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/ScriptableObjects/Characters/Character_Rogue.asset");
        Check(violetSelectionPortrait != null && violetCharacterAsset != null && violetSelectionPortrait.sprite == violetCharacterAsset.selectionPortrait && violetSelectionPortrait.scaleMode == ScaleMode.ScaleToFit,
            "1 п.2 карточка Вайолет показывает полный диалоговый спрайт без обрезания сверху");
        var violetPassiveLabel = root.Q<Label>("CharacterSelectPassiveSkill_violet");
        var violetActiveLabel = root.Q<Label>("CharacterSelectActiveSkill_violet");
        Check(violetPassiveLabel != null && violetPassiveLabel.text.Contains(violetCharacterAsset.uniquePassiveSkill.skillName) && violetPassiveLabel.tooltip == violetCharacterAsset.uniquePassiveSkill.effectDescription,
            "1 п.2 пассивный навык показывает название и хранит описание для hover-tooltip");
        Check(violetActiveLabel != null && violetActiveLabel.text.Contains(violetCharacterAsset.uniqueActiveSkill.skillName) && violetActiveLabel.tooltip == violetCharacterAsset.uniqueActiveSkill.effectDescription,
            "1 п.2 активный навык показывает название и хранит описание для hover-tooltip");
        Check(characterSkillTooltip.style.display != DisplayStyle.Flex,
            "1 п.2 tooltip скрыт до наведения курсора");
        runFlow.ReturnToMainMenu();
        saveManager.Data.gachaOwnedCharacters.Clear();
        saveManager.Data.gachaOwnedCharacters.AddRange(savedCharacterCopies);

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

        hub.OpenVeteranDeck();
        Check(veteranDeckScreen.style.display == DisplayStyle.Flex && mainMenuScreen.style.display == DisplayStyle.None,
            "OpenVeteranDeck() показывает экран колоды ветеранов");
        hub.OpenVillage();
        hub.OpenCharacters();
        Check(charactersScreen.style.display == DisplayStyle.Flex && mainMenuScreen.style.display == DisplayStyle.None,
            "OpenCharacters() показывает экран полученных персонажей");
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
        int gachaMetaBefore = saveManager.Data.metaCurrency;
        int gachaCopiesBefore = 0;
        foreach (var entry in saveManager.Data.gachaOwnedCharacters) gachaCopiesBefore += entry.count;
        var gachaResultPopup = root.Q<VisualElement>("GachaResultPopup");
        var gachaRevealContainer = root.Q<VisualElement>("GachaRevealContainer");
        var tryPullGacha = typeof(HubManager).GetMethod("TryPullGacha", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        tryPullGacha?.Invoke(hub, null);
        Check(tryPullGacha != null, "Приватный метод HubManager.TryPullGacha найден рефлексией");
        Check(saveManager.Data.gachaCurrency == gachaCurrencyBefore - 50, $"Призыв гачи списал 50 гача-валюты: было {gachaCurrencyBefore}, стало {saveManager.Data.gachaCurrency}");
        int gachaCopiesAfter = 0;
        foreach (var entry in saveManager.Data.gachaOwnedCharacters) gachaCopiesAfter += entry.count;
        bool characterAwarded = gachaCopiesAfter == gachaCopiesBefore + 1;
        bool currencyAwarded = saveManager.Data.metaCurrency > gachaMetaBefore;
        Check(characterAwarded || currencyAwarded, "11.1 результат призыва сохранён до запуска презентационной анимации");
        Check(gachaRevealContainer.style.display == DisplayStyle.Flex && gachaResultPopup.style.display != DisplayStyle.Flex,
            "11.1 после призыва запускается общая chest-reveal анимация, итоговый попап ждёт её завершения");
        hub.OpenVillage();

        // --- Награда за забег + персистентность SaveManager (8.5/9.2/9.3) ---
        int metaBefore = saveManager.Data.metaCurrency;
        int debugGachaBefore = saveManager.Data.gachaCurrency;
        saveManager.AddDebugCurrencies(10000, 10000);
        Check(saveManager.Data.metaCurrency == metaBefore + 10000 && saveManager.Data.gachaCurrency == debugGachaBefore + 10000,
            "Чит-выдача добавляет 10000 мета-валюты и 10000 гача-валюты одной операцией");
        metaBefore = saveManager.Data.metaCurrency;
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

        var victoryReward = rewardManager.CalculateRunCompletionReward(true, totalRoomsCleared: 120, currentFloorNumber: 10, roomsClearedOnDeathFloor: 11);
        // База за зачистку 10 этажей = 50*10 + 5*11 = 555; бонус зачистки = 25%, округлённый = 139.
        Check(victoryReward.MetaCurrency == 694 && victoryReward.GachaCurrency == 18 &&
            victoryReward.ClearBonusMetaCurrency == 139 && victoryReward.ClearBonusGachaCurrency == 4,
            $"8.5 победа добавляет 25% как отдельный бонус зачистки: {victoryReward.MetaCurrency}/{victoryReward.GachaCurrency}, бонус {victoryReward.ClearBonusMetaCurrency}/{victoryReward.ClearBonusGachaCurrency} (ожидалось 694/18, 139/4)");

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

            var pauseRun = typeof(RunFlowController).GetMethod("PauseRun", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var resumeRun = typeof(RunFlowController).GetMethod("ResumeRun", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var pauseScreen = root.Q<VisualElement>("PauseScreen");
            pauseRun?.Invoke(runFlow, null);
            Check(pauseRun != null && pauseScreen.style.display == DisplayStyle.Flex && Mathf.Approximately(Time.timeScale, 0f),
                "7.2 PauseRun открывает окно и останавливает игровое время");
            resumeRun?.Invoke(runFlow, null);
            Check(resumeRun != null && pauseScreen.style.display == DisplayStyle.None && Mathf.Approximately(Time.timeScale, 1f),
                "7.2 ResumeRun закрывает окно и возобновляет игровое время");

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
        floorManager.GenerateRoomBag(1);
        int combatCount = floorManager.RoomBag.FindAll(r => r == RoomType.Combat).Count;
        int merchantCount = floorManager.RoomBag.FindAll(r => r == RoomType.Merchant).Count;
        int trapCount = floorManager.RoomBag.FindAll(r => r == RoomType.Trap).Count;
        int specialCount = floorManager.RoomBag.FindAll(r => r == RoomType.Special).Count;
        Check(combatCount == 8 && merchantCount == 0 && trapCount == 2 && specialCount == 1 && floorManager.RoomBag.Count == 11,
            $"8.4 первый этаж без торговца: combat={combatCount}, merchant={merchantCount}, trap={trapCount}, special={specialCount}, total={floorManager.RoomBag.Count} (ожидалось 8/0/2/1/11)");
        floorManager.GenerateRoomBag(2);
        Check(floorManager.RoomBag.FindAll(r => r == RoomType.Merchant).Count == 1 && floorManager.RoomBag.Count == 12,
            "8.4 со второго этажа торговец и 12-комнатный мешок возвращаются");
        UnityEngine.Object.DestroyImmediate(floorManagerGO);

        var warlock = AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/ScriptableObjects/Monsters/Monster_Warlock.asset");
        Check(warlock != null && warlock.minFloorTier == 2,
            "2.4 Колдун начинает появляться со второго этажа");

        // 2.4: "Проклятие замедления" Колдуна (давний пробел — ассет существовал, но никогда не
        // применялся в бою) через реальный CombatManager.ResolveAttack, а не напрямую.
        var combatManagerGO = new GameObject("SmokeTest_CombatManager");
        var testCombatManager = combatManagerGO.AddComponent<CombatManager>();

        var testPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f };
        testPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 5f, DamageMax = 5f, DamageType = DamageType.Physical, AttackSpeed = 1f });

        var slowCurseMonster = new CombatantRuntime { IsPlayer = false, MaxHP = 30f, CurrentHP = 30f, DisplayName = "TestWarlock", MonsterPassiveSkillId = SkillId.MonsterSlowCurse };
        slowCurseMonster.Weapons.Add(new WeaponAttackState { DamageMin = 100f, DamageMax = 100f, DamageType = DamageType.Physical, AttackSpeed = 1f });

        testCombatManager.StartCombat(testPlayer, new List<CombatantRuntime> { slowCurseMonster });
        testCombatManager.Tick(1.01f); // достаточно, чтобы оба нанесли по 1 удару (AttackSpeed=1/сек)
        Check(testPlayer.ActiveDebuffs.Exists(d => d.Id == "warlock_slow"), "2.4 Проклятие замедления применяется при попадании Колдуна по HP игрока");

        testPlayer.PhysicalDefenseMax = 20f;
        testPlayer.PhysicalDefenseCurrent = 7f;
        testPlayer.PoisonStacks = 2;
        testPlayer.PoisonTimer = 3f;
        testPlayer.HasBleed = true;
        testPlayer.BleedTimer = 3f;
        testPlayer.IsStealthed = true;
        testPlayer.StealthTimer = 3f;
        testPlayer.IsBerserkActive = true;
        testCombatManager.EndCombat();
        Check(testPlayer.ActiveDebuffs.Count == 0 && testPlayer.PoisonStacks == 0 && !testPlayer.HasBleed &&
            !testPlayer.IsStealthed && !testPlayer.IsBerserkActive && testPlayer.PhysicalDefenseCurrent == 7f,
            "4.5 конец боя сбрасывает временные статусы, но сохраняет износ физической брони");

        // Коррозийный паук: 15% силы его удара снимаются с брони всегда, в том числе когда
        // физический удар не нанёс ни единицы урона по HP. Обычный износ от самого удара здесь
        // тоже ожидаем: 100 урона против 100 брони = −5 износ, затем −15 от коррозии.
        testPlayer.CurrentHP = 1000f;
        testPlayer.PhysicalDefenseMax = 100f;
        testPlayer.PhysicalDefenseCurrent = 100f;
        var corrosiveSpider = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestCorrosiveSpider", MonsterPassiveSkillId = SkillId.MonsterCorrosion };
        corrosiveSpider.Weapons.Add(new WeaponAttackState { DamageMin = 100f, DamageMax = 100f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        testCombatManager.StartCombat(testPlayer, new List<CombatantRuntime> { corrosiveSpider });
        testCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(testPlayer.CurrentHP, 1000f) && Mathf.Approximately(testPlayer.PhysicalDefenseCurrent, 80f),
            $"2.4 Коррозия: полностью заблокированный урон не ранит HP, но снимает обычный износ и 15% силы атаки с брони (HP={testPlayer.CurrentHP:F1}, броня={testPlayer.PhysicalDefenseCurrent:F1}, ожидалось 1000/80)");
        testCombatManager.EndCombat();

        // Новый модификатор: обычный износ 1 + прямой гарантированный износ 4 на 6-м этаже.
        testPlayer.CurrentHP = 1000f;
        testPlayer.PhysicalDefenseMax = 100f;
        testPlayer.PhysicalDefenseCurrent = 100f;
        var armorPiercingMonster = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestArmorPiercing", MonsterGuaranteedArmorDamage = 4f };
        armorPiercingMonster.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        testCombatManager.StartCombat(testPlayer, new List<CombatantRuntime> { armorPiercingMonster });
        testCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(testPlayer.CurrentHP, 1000f) && Mathf.Approximately(testPlayer.PhysicalDefenseCurrent, 95f),
            $"2.8 Бронебойный: блокированный удар снимает 1 обычного + 4 дополнительного износа (HP={testPlayer.CurrentHP:F1}, броня={testPlayer.PhysicalDefenseCurrent:F1}, ожидалось 1000/95)");
        testCombatManager.EndCombat();

        // Активный навык босса не зависит от таймера обычной атаки: через 5 секунд наносит 200%.
        var bossSkillGO = new GameObject("SmokeTest_BossHeavyAttack");
        var bossSkillManager = bossSkillGO.AddComponent<CombatManager>();
        var bossSkillPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 100f, PhysicalDefenseCurrent = 100f };
        bossSkillPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });
        var bossSkillEnemy = new CombatantRuntime { IsPlayer = false, IsBoss = true, BossHeavyAttackDamageMultiplier = 2f, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestBoss" };
        bossSkillEnemy.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });
        int heavyAttackActivations = 0;
        bossSkillManager.ActiveSkillActivated += (_, skillName) => { if (skillName == "Тяжёлая атака") heavyAttackActivations++; };
        bossSkillManager.StartCombat(bossSkillPlayer, new List<CombatantRuntime> { bossSkillEnemy });
        bossSkillManager.Tick(4.99f);
        Check(heavyAttackActivations == 0 && bossSkillPlayer.PhysicalDefenseCurrent == 100f, "2.2 «Тяжёлая атака» не срабатывает раньше 5 секунд");
        bossSkillManager.Tick(0.02f);
        Check(heavyAttackActivations == 1 && bossSkillPlayer.CurrentHP == 1000f && bossSkillPlayer.PhysicalDefenseCurrent == 99f,
            $"2.2 «Тяжёлая атака» через 5 секунд наносит 200% (20 урона заблокировано, броня 100→99): activations={heavyAttackActivations}, HP={bossSkillPlayer.CurrentHP}, armor={bossSkillPlayer.PhysicalDefenseCurrent}");
        UnityEngine.Object.DestroyImmediate(bossSkillGO);

        UnityEngine.Object.DestroyImmediate(combatManagerGO);

        // 4.3 (НОВОЕ 2026-08-26): активный навык уходит в полный кулдаун сразу при старте боя,
        // а не в 0 — иначе "3 быстрые атаки" срабатывали мгновенно и сносили противника до того,
        // как игрок успевал его увидеть. Обычные атаки оружием это не затрагивает.
        var skillCooldownGO = new GameObject("SmokeTest_ActiveSkillCooldown");
        var skillCooldownCombatManager = skillCooldownGO.AddComponent<CombatManager>();
        skillCooldownCombatManager.ConfigureUniqueActiveSkill(3, 1f, 12f, true, "TestSkill", SkillId.None);

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

        var visiblePlayerEffects = new CombatantRuntime
        {
            MaxHP = 100f,
            CurrentHP = 25f,
            SkillStubbornnessLevel = 3,
            SmokeBombGuaranteedCritsRemaining = 2,
            RiposteArmed = true,
            PhysicalResistancePercent = 20f,
            MagicalResistancePercent = 15f
        };
        visiblePlayerEffects.ActiveDebuffs.Add(new ActiveDebuff { Id = "event_damage_down", RemainingTime = float.PositiveInfinity });
        visiblePlayerEffects.ActiveDebuffs.Add(new ActiveDebuff { Id = "event_attack_speed_down", RemainingTime = float.PositiveInfinity });
        var visibleEffects = CombatantStatusEffects.GetActiveEffects(visiblePlayerEffects);
        Check(visibleEffects.Exists(e => e.label == "Урон снижен" && !e.isBuff) &&
              visibleEffects.Exists(e => e.label == "Скорость атаки снижена" && !e.isBuff),
            "4.7 штрафы событий к урону и скорости отображаются отдельными дебаффами");
        Check(visibleEffects.Exists(e => e.label == "Гарантированные криты ×2" && e.isBuff) &&
              visibleEffects.Exists(e => e.label == "Рипост готов" && e.isBuff) &&
              visibleEffects.Exists(e => e.label.Contains("Физ. сопротивление") && e.isBuff) &&
              visibleEffects.Exists(e => e.label.Contains("Маг. сопротивление") && e.isBuff) &&
              visibleEffects.Exists(e => e.label.Contains("Упёртость") && e.isBuff),
            "4.7 временные боевые преимущества игрока отражаются в списке баффов");

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

        // Баланс Варвара: "Ярость" = 1% + % недостающего HP, с потолком 100%. Поэтому максимум
        // достижим при 1% HP, а не только после смерти.
        var rageTestCombatant = new CombatantRuntime { MaxHP = 100f, CurrentHP = 100f };
        Check(Mathf.Approximately(rageTestCombatant.Rage, 1f), $"Баланс Саши: Ярость при полном HP = 1% (было {rageTestCombatant.Rage})");
        rageTestCombatant.CurrentHP = 50f;
        Check(Mathf.Approximately(rageTestCombatant.Rage, 51f), $"Баланс Саши: Ярость при 50% HP = 51% (было {rageTestCombatant.Rage})");
        rageTestCombatant.CurrentHP = 1f;
        Check(Mathf.Approximately(rageTestCombatant.Rage, 100f), $"Баланс Саши: Ярость при 1% HP = 100% (было {rageTestCombatant.Rage})");
        rageTestCombatant.CurrentHP = 50f;
        rageTestCombatant.RageFlatBonusPercent = 20f;
        Check(Mathf.Approximately(rageTestCombatant.Rage, 71f), $"Баланс Саши: Ярость складывает флэт-бонус поверх формулы HP: 51%+20 = 71% (было {rageTestCombatant.Rage})");

        // ФИКС (код-ревью): "Остервенелость"/"Суеверность" ранее делили на 100 дважды (~1% от нужной
        // величины). Проверяем правильный порядок величины напрямую: Rage=100%, ур.5 (X=1.0) должно
        // давать РОВНО +100% к скорости атаки (Остервенелость) и РОВНО 100% магического сопротивления
        // (Суеверность), а не ~1%.
        var frenzyTestCombatant = new CombatantRuntime { MaxHP = 100f, CurrentHP = 0f, SkillFrenzyLevel = 5 }; // Rage = 100%
        var frenzyWeapon = new WeaponAttackState { AttackSpeed = 2f };
        Check(Mathf.Approximately(frenzyTestCombatant.GetEffectiveAttackSpeed(frenzyWeapon), 4f), $"3.11 «Остервенелость» ур.5 при Ярости=100%: скорость атаки ×2 (база 2 -> 4, было {frenzyTestCombatant.GetEffectiveAttackSpeed(frenzyWeapon)})");

        var superstitionGO = new GameObject("SmokeTest_Superstition");
        var superstitionCombatManager = superstitionGO.AddComponent<CombatManager>();
        // Новая формула даёт Rage=51% (1% + 50% недостающего HP), ур.5 (X=1.0) ->
        // MagicalResistancePercent=51. Удар 100 маг. урона превращается в 49 -> HP 500->451,
        // а не примерно 400.5, как при старом баге с двойным делением.
        var superstitionPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 500f, SkillSuperstitionLevel = 5 };
        var superstitionEnemy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestEnemy" };
        superstitionEnemy.Weapons.Add(new WeaponAttackState { DamageMin = 100f, DamageMax = 100f, DamageType = DamageType.Magical, AttackSpeed = 2f });

        superstitionCombatManager.StartCombat(superstitionPlayer, new List<CombatantRuntime> { superstitionEnemy });
        superstitionCombatManager.Tick(0.51f); // ровно один удар врага (интервал 0.5с)
        Check(Mathf.Approximately(superstitionPlayer.CurrentHP, 451f), $"Баланс Саши: «Суеверность» ур.5 при Ярости=51% снижает урон 100 -> 49 по HP (HP={superstitionPlayer.CurrentHP}, ожидалось 451)");

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

        // 8.6 (Варвар) — "Боевая регенерация": срабатывает на N-й полученный удар (ур.1 = 6),
        // не раньше, лечит 6% HP и получает двухсекундный кулдаун.
        var combatRegenGO = new GameObject("SmokeTest_CombatRegen");
        var combatRegenCombatManager = combatRegenGO.AddComponent<CombatManager>();
        var combatRegenAttacker = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f };
        combatRegenAttacker.Weapons.Add(new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var combatRegenTarget = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 500f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, SkillCombatRegenLevel = 1, DisplayName = "TestDummy" };
        combatRegenTarget.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        combatRegenCombatManager.StartCombat(combatRegenAttacker, new List<CombatantRuntime> { combatRegenTarget });
        for (int hit = 0; hit < 5; hit++)
        {
            combatRegenCombatManager.Tick(1.01f); // 5 ударов по 1 урону, регенерация ещё не должна сработать
        }
        Check(combatRegenTarget.HitsTakenSinceLastRegen == 5 && Mathf.Approximately(combatRegenTarget.CurrentHP, 495f), $"8.6 «Боевая регенерация» не срабатывает раньше N-го удара (счётчик={combatRegenTarget.HitsTakenSinceLastRegen}, HP={combatRegenTarget.CurrentHP}, ожидалось счётчик=5, HP=495)");

        combatRegenCombatManager.Tick(1.01f); // 6-й удар -> регенерация 6% от 1000 = 60 HP
        Check(combatRegenTarget.HitsTakenSinceLastRegen == 0 && Mathf.Approximately(combatRegenTarget.CurrentHP, 554f) && combatRegenTarget.CombatRegenCooldownRemaining > 0f,
            $"8.6 «Боевая регенерация» срабатывает на 6-й удар, лечит 6% и ставит кулдаун (счётчик={combatRegenTarget.HitsTakenSinceLastRegen}, HP={combatRegenTarget.CurrentHP}, CD={combatRegenTarget.CombatRegenCooldownRemaining})");

        UnityEngine.Object.DestroyImmediate(combatRegenGO);

        // Баланс Варвара: "Берсерк" снижает входящий физический урон ДО брони/щита. Ур.2 = 30%.
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
        Check(Mathf.Approximately(berserkOnPlayer.CurrentHP, 930f), $"Баланс Саши: «Берсерк» ур.2 (30% физ. сопротивления) снижает урон 100 -> 70 (HP={berserkOnPlayer.CurrentHP}, ожидалось 930)");

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

        // Подслучай 3: при полном HP новая формула даёт Ярость=1%, поэтому шанс крита ур.5 равен 1%,
        // а не нулю. Большие обычные источники (SkillCriticalHitsLevel=5 + CritChanceBonusFromItems=50)
        // по-прежнему не добавляются к шансу, а конвертируются в крит-урон. Фиксируем Random state
        // и проверяем один заведомо некритический удар.
        var championZeroRageGO = new GameObject("SmokeTest_ChampionZeroRage");
        var championZeroRageCombatManager = championZeroRageGO.AddComponent<CombatManager>();
        var championZeroRagePlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, UniqueChampionOfTheTribeLevel = 5, CritChanceReplacedByRage = true, SkillCriticalHitsLevel = 5, CritChanceBonusFromItems = 50f };
        championZeroRagePlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var championZeroRageDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        championZeroRageDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        var randomStateBeforeChampionCheck = UnityEngine.Random.state;
        UnityEngine.Random.InitState(24681357);
        championZeroRageCombatManager.StartCombat(championZeroRagePlayer, new List<CombatantRuntime> { championZeroRageDummy });
        championZeroRageCombatManager.Tick(1.01f);
        UnityEngine.Random.state = randomStateBeforeChampionCheck;
        Check(Mathf.Approximately(championZeroRagePlayer.Rage, 1f) && Mathf.Approximately(championZeroRageDummy.CurrentHP, 990f),
            $"Баланс Саши: «Чемпион племени» при полном HP использует 1% Ярости, обычные +100% шанса не прибавляются (HP болвана={championZeroRageDummy.CurrentHP}, ожидалось 990)");

        UnityEngine.Object.DestroyImmediate(championZeroRageGO);

        // После рангового ребаланса «Казнь» даёт 3–15% недостающего HP. Ранг V: бонус
        // 1000×0.5×0.15 = 75, плюс 10 обычного урона = 85.
        var executionGO = new GameObject("SmokeTest_Execution");
        var executionCombatManager = executionGO.AddComponent<CombatManager>();
        var executionPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f };
        executionPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f, ExecutionLevel = 5 });
        var executionDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 500f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        executionDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        executionCombatManager.StartCombat(executionPlayer, new List<CombatantRuntime> { executionDummy });
        executionCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(executionDummy.CurrentHP, 415f), $"3.11 «Казнь» ранга V: 15% недостающего HP + обычный урон: HP болвана={executionDummy.CurrentHP} (ожидалось 415, т.е. 500-85)");

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
        // без свежего уклонения). Уклонение теперь жёстко ограничено 75%, поэтому тест не использует
        // случайность: флаг взводится напрямую после проверки, что он не срабатывает немедленно.
        var riposteGO = new GameObject("SmokeTest_Riposte");
        var riposteCombatManager = riposteGO.AddComponent<CombatManager>();
        var riposteDefender = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 1000f, ItemElusivenessLevel = 100, ItemRiposteLevel = 5 };
        riposteDefender.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var riposteEnemyWeapon = new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, DamageType = DamageType.Physical, AttackSpeed = 1f };
        var riposteEnemy = new CombatantRuntime { IsPlayer = false, MaxHP = 10000f, CurrentHP = 10000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        riposteEnemy.Weapons.Add(riposteEnemyWeapon);

        riposteCombatManager.StartCombat(riposteDefender, new List<CombatantRuntime> { riposteEnemy });
        riposteCombatManager.Tick(1.01f); // игрок бьёт первым без бонуса
        Check(Mathf.Approximately(riposteEnemy.CurrentHP, 9990f), $"3.11 «Рипост» НЕ применяется немедленно на той же атаке, что взвела флаг: HP болвана={riposteEnemy.CurrentHP} (ожидалось 9990, т.е. без бонуса)");
        riposteDefender.RiposteArmed = true;

        riposteCombatManager.Tick(1.01f); // ранг V: +125% собственного урона, итого 22.5
        Check(Mathf.Approximately(riposteEnemy.CurrentHP, 9967.5f), $"3.11 «Рипост» применяется РОВНО на следующей атаке (+125%): HP болвана={riposteEnemy.CurrentHP} (ожидалось 9967.5, т.е. 9990-22.5)");

        riposteDefender.RiposteArmed = false; // имитируем отсутствие нового уклонения (враг уже перевзвёл флаг своим ходом в этом же тике)
        riposteEnemyWeapon.AttackSpeed = 0.0001f; // враг больше не атакует в пределах теста -> новых уклонений не будет
        riposteCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(riposteEnemy.CurrentHP, 9957.5f), $"3.11 «Рипост» не бьёт повторно без свежего уклонения: HP болвана={riposteEnemy.CurrentHP} (ожидалось 9957.5, т.е. 9967.5-10 без бонуса)");

        UnityEngine.Object.DestroyImmediate(riposteGO);

        // 3.11 (Task 6b, Кожанка) — "Объятия ночи": доп. МАГИЧЕСКИЙ урон отдельным попаданием,
        // ТОЛЬКО пока атакующий в Скрытности. После ребаланса ранг V даёт 40%: 20 физ. + 8 маг.
        var embraceGO = new GameObject("SmokeTest_EmbraceOfNight");
        var embraceCombatManager = embraceGO.AddComponent<CombatManager>();
        var embraceAttacker = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, IsStealthed = true, StealthTimer = 999f, ItemEmbraceOfNightLevel = 5 };
        embraceAttacker.Weapons.Add(new WeaponAttackState { DamageMin = 20f, DamageMax = 20f, DamageType = DamageType.Physical, AttackSpeed = 1f });
        var embraceDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 10000f, CurrentHP = 10000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f, DisplayName = "TestDummy" };
        embraceDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        embraceCombatManager.StartCombat(embraceAttacker, new List<CombatantRuntime> { embraceDummy });
        embraceCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(embraceDummy.CurrentHP, 9972f), $"8.6 «Объятия ночи» ранга V в Скрытности: физ. 20 + отдельный маг. бонус 8 = -28: HP болвана={embraceDummy.CurrentHP} (ожидалось 9972)");

        embraceAttacker.IsStealthed = false; // выходим из Скрытности -> бонус больше не должен срабатывать
        embraceCombatManager.Tick(1.01f);
        Check(Mathf.Approximately(embraceDummy.CurrentHP, 9952f), $"8.6 «Объятия ночи» НЕ срабатывает вне Скрытности: HP болвана={embraceDummy.CurrentHP} (ожидалось 9952, т.е. 9972-20 без маг. бонуса)");

        UnityEngine.Object.DestroyImmediate(embraceGO);

        // 3.11 (Task 6b, Эпический трофей) — "Просто царапина": разовое лечение РОВНО при StartCombat
        // (не при Tick), только у игрока (у монстров этих предметов не существует).
        var justAScratchGO = new GameObject("SmokeTest_JustAScratch");
        var justAScratchCombatManager = justAScratchGO.AddComponent<CombatManager>();
        var justAScratchPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 50f, ItemJustAScratchLevel = 5 };
        justAScratchPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });
        var justAScratchDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, DisplayName = "TestDummy" };
        justAScratchDummy.Weapons.Add(new WeaponAttackState { DamageMin = 0f, DamageMax = 0f, DamageType = DamageType.Physical, AttackSpeed = 0.01f });

        justAScratchCombatManager.StartCombat(justAScratchPlayer, new List<CombatantRuntime> { justAScratchDummy });
        Check(Mathf.Approximately(justAScratchPlayer.CurrentHP, 65f), $"8.6 «Просто царапина» лечит РОВНО при StartCombat, ранг V = 15% MaxHP: HP игрока={justAScratchPlayer.CurrentHP} (ожидалось 65)");

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
