using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// Регрессии из аудита 06.09.2026 (Docs/Audit/2026-09-06/Audit.md). Каждый тест назван по своему
// идентификатору находки, чтобы правку было видно по имени упавшего теста.
public class AuditRegressionTests
{
    // ==================== R01 — повторное одноразовое содержимое на этаже ====================

    [Test]
    public void R01_OneShotFloorState_ReservesEachContentOnlyOnce()
    {
        var state = new OneShotRoomFloorState();
        Assert.IsFalse(state.IsReserved(OneShotRoomContentId.PersonalRest));
        Assert.IsTrue(state.TryReserve(OneShotRoomContentId.PersonalRest));
        Assert.IsTrue(state.IsReserved(OneShotRoomContentId.PersonalRest));
        Assert.IsFalse(state.TryReserve(OneShotRoomContentId.PersonalRest),
            "Повторное резервирование того же содержимого должно отклоняться.");
        // Резервирование одного одноразового контента не должно закрывать остальные.
        Assert.IsFalse(state.IsReserved(OneShotRoomContentId.HuntQuest));
        Assert.IsFalse(state.IsReserved(OneShotRoomContentId.SwordInStone));
    }

    // Корень R01: контент всех узлов этажа резолвится ДО первого шага игрока, поэтому одного лишь
    // run-флага (он выставляется при входе в комнату) не хватает — нужен ledger на проход генерации.
    [Test]
    public void R01_PickForFloor_WithFloorReservation_NeverRepeatsOneShotQuest()
    {
        var state = new OneShotRoomFloorState();
        int huntPicks = 0;
        int swordPicks = 0;

        for (int node = 0; node < 200; node++)
        {
            var quest = QuestCatalog.PickForFloor(5,
                huntAlreadyTriggered: state.IsReserved(OneShotRoomContentId.HuntQuest),
                swordAlreadySucceeded: state.IsReserved(OneShotRoomContentId.SwordInStone));
            if (quest == QuestCatalog.Hunt) { huntPicks++; state.TryReserve(OneShotRoomContentId.HuntQuest); }
            else if (quest == QuestCatalog.SwordInStone) { swordPicks++; state.TryReserve(OneShotRoomContentId.SwordInStone); }
        }

        Assert.LessOrEqual(huntPicks, 1, "«Добыча» не должна выпасть дважды на одном этаже.");
        Assert.LessOrEqual(swordPicks, 1, "«Меч в камне» не должен выпасть дважды на одном этаже.");
    }

    [Test]
    public void R01_ExhaustedPersonalRest_FallsBackInsteadOfThrowing()
    {
        string source = File.ReadAllText("Assets/Scripts/UI/RunFlowController.Rooms.cs");
        StringAssert.DoesNotContain("Personal rest content for node", source,
            "Брошенное из корутины исключение вешало забег — нужен безопасный fallback.");
        StringAssert.Contains("QuestRoomFlow(PickFallbackQuest())", source);
    }

    [Test]
    public void R01_FallbackQuest_IsNeverAOneShotReward()
    {
        for (int floor = 1; floor <= 10; floor++)
        {
            // Ровно те аргументы, которые подставляет PickFallbackQuest.
            var quest = QuestCatalog.PickForFloor(floor, huntAlreadyTriggered: true, swordAlreadySucceeded: true);
            Assert.AreNotEqual(QuestCatalog.Hunt, quest);
            Assert.AreNotEqual(QuestCatalog.SwordInStone, quest);
        }
    }

    // ==================== R02 — опыт за успешную ловушку/событие ====================

    [Test]
    public void R02_SuccessfulEventOrTrap_IsActuallyGranted()
    {
        string mapContent = File.ReadAllText("Assets/Scripts/UI/RunFlowController.MapContent.cs");
        StringAssert.Contains("ExperienceSource.SuccessfulEventOrTrap", mapContent,
            "Формула опыта существовала, но выдачи не было ни в одном исходе комнаты.");
        StringAssert.Contains("pendingSuccessfulEventOrTrapXp", mapContent);

        string rooms = File.ReadAllText("Assets/Scripts/UI/RunFlowController.Rooms.cs");
        StringAssert.Contains("pendingSuccessfulEventOrTrapXp = true", rooms);
    }

    [Test]
    public void R02_RoomXp_IsResolvedInASingleTransaction()
    {
        string source = File.ReadAllText("Assets/Scripts/UI/RunFlowController.MapContent.cs");
        // Флаг сбрасывается в ResetPendingRoomRewards, а выдача идёт в общий levelsGained: провал
        // с последующим боем не должен породить два независимых прохода левел-апов.
        StringAssert.Contains("pendingSuccessfulEventOrTrapXp = false", source);
        Assert.AreEqual(1, CountOccurrences(source, "ExperienceSource.SuccessfulEventOrTrap"),
            "Опыт за событие должен выдаваться ровно из одного места.");
    }

    [Test]
    public void R02_ExperienceTable_GrowsWithFloor()
    {
        var manager = new GameObject("reward").AddComponent<RewardManager>();
        try
        {
            Assert.AreEqual(5, manager.GetExperienceReward(ExperienceSource.SuccessfulEventOrTrap, 1));
            Assert.AreEqual(9, manager.GetExperienceReward(ExperienceSource.SuccessfulEventOrTrap, 5));
        }
        finally { UnityEngine.Object.DestroyImmediate(manager.gameObject); }
    }

    // ==================== R03 — сервисы после сброса прогресса ====================

    [Test]
    public void R03_TavernService_FollowsReplacedSaveData()
    {
        var current = new SaveData();
        current.unlockedTavernRecipes.Add("recipe_stew");
        current.resources.Add(new KeyCountEntry { key = PersistentResourceIds.RawMeat, count = 9 });

        var service = new TavernService(() => current, access: new CatalogUnlockPolicy(false));
        Assert.AreEqual(9, service.GetIngredientAmount(PersistentResourceIds.RawMeat));

        // Ровно то, что делает SaveManager.ResetProgress: объект SaveData подменяется целиком.
        current = new SaveData();
        Assert.AreEqual(0, service.GetIngredientAmount(PersistentResourceIds.RawMeat),
            "После сброса сервис не должен видеть запасы старого объекта данных.");
    }

    [Test]
    public void R03_ForgeService_FollowsReplacedSaveData()
    {
        var current = new SaveData();
        current.researchedItemPrototypes.Add("prototype_sword");

        var service = new ForgeService(() => current);
        Assert.IsTrue(service.IsPrototypeResearched("prototype_sword"));

        current = new SaveData();
        Assert.IsFalse(service.IsPrototypeResearched("prototype_sword"),
            "После сброса исследованные прототипы старого объекта данных не должны учитываться.");
    }

    [Test]
    public void R03_SaveManagerFactories_PassProviderNotSnapshot()
    {
        string source = File.ReadAllText("Assets/Scripts/Managers/SaveManager.cs");
        StringAssert.Contains("new TavernService(() => Data", source);
        StringAssert.Contains("new ForgeService(() => Data", source);
    }

    // ==================== R05 — блокировка атак принадлежит боевой модели ====================

    [Test]
    public void R05_AttackLock_IsOwnedByCombatModelNotUi()
    {
        string ui = File.ReadAllText("Assets/Scripts/UI/RunFlowController.Combat.cs");
        StringAssert.DoesNotContain("Player.AttackLocked", ui,
            "UI больше не владеет механической блокировкой атак.");
        StringAssert.DoesNotContain("AttackLockRemaining", ui);

        string combat = File.ReadAllText("Assets/Scripts/Managers/CombatManager.cs");
        StringAssert.Contains("Player.AttackLockRemaining = slot.AttackLockSeconds", combat);
    }

    [Test]
    public void R05_SimulationReceivesTheSameAttackLockAsLiveCombat()
    {
        // Расхождение было именно здесь: у симуляции нет UI, который ставил блокировку.
        Assert.AreEqual(CombatManager.ThreeQuickStrikesRecoverySeconds,
            CombatManager.ResolveActiveSkillAttackLockSeconds(CharacterClass.Warrior));
        Assert.AreEqual(0f, CombatManager.ResolveActiveSkillAttackLockSeconds(CharacterClass.Rogue),
            "«Дымовая граната» замаха не имеет и блокировки давать не должна.");

        string sim = File.ReadAllText("Assets/Scripts/Combat/CombatSimulationEngine.cs");
        StringAssert.Contains("snapshot.attackLockSeconds", sim);
    }

    [Test]
    public void R05_AttackLock_ExpiresOnCombatTime()
    {
        var runtime = new CombatantRuntime { AttackLockRemaining = CombatManager.ThreeQuickStrikesRecoverySeconds };
        Assert.IsTrue(runtime.IsAttackLocked);
        runtime.AttackLockRemaining = Mathf.Max(0f, runtime.AttackLockRemaining - 1f);
        Assert.IsFalse(runtime.IsAttackLocked, "Блокировка обязана истекать сама, без участия UI.");
    }

    // ==================== R06 — резервная копия и повреждённое сохранение ====================

    string tempDirectory;
    string SavePath => Path.Combine(tempDirectory, "save.json");

    [SetUp]
    public void SetUp()
    {
        // Живой сейв игрока (Application.persistentDataPath) тесты не трогают — только temp.
        tempDirectory = Path.Combine(Path.GetTempPath(), "dg_save_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (!string.IsNullOrEmpty(tempDirectory) && Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, true);
    }

    static string Json(int metaCurrency) => JsonUtility.ToJson(new SaveData { metaCurrency = metaCurrency }, true);

    [Test]
    public void R06_SecondWrite_LeavesPreviousVersionAsBackup()
    {
        SaveFileStore.Write(SavePath, Json(100));
        Assert.IsFalse(File.Exists(SaveFileStore.BackupPathFor(SavePath)),
            "Первой записи резервировать ещё нечего.");

        SaveFileStore.Write(SavePath, Json(200));
        Assert.IsTrue(File.Exists(SaveFileStore.BackupPathFor(SavePath)));
        Assert.IsTrue(SaveFileStore.TryRead(SaveFileStore.BackupPathFor(SavePath), out var backup));
        Assert.AreEqual(100, backup.metaCurrency, "В .bak должна лежать предыдущая успешная версия.");
    }

    [Test]
    public void R06_CorruptSave_RecoversFromBackupAndKeepsCorruptFile()
    {
        SaveFileStore.Write(SavePath, Json(100));
        SaveFileStore.Write(SavePath, Json(200));
        File.WriteAllText(SavePath, "{ это не json");

        var outcome = SaveFileStore.Load(SavePath);

        Assert.IsFalse(outcome.Failed);
        Assert.IsTrue(outcome.RestoredFromBackup);
        Assert.AreEqual(100, outcome.Data.metaCurrency, "Прогресс должен подниматься из резервной копии.");
        Assert.IsTrue(File.Exists(SaveFileStore.CorruptPathFor(SavePath)),
            "Повреждённый файл обязан сохраняться, а не молча затираться следующей записью.");
        Assert.IsFalse(File.Exists(SavePath), "Повреждённый файл унесён из-под будущей записи.");
    }

    [Test]
    public void R06_CorruptSaveWithoutBackup_ReportsFailureInsteadOfSilentReset()
    {
        File.WriteAllText(SavePath, "{ это не json");

        var outcome = SaveFileStore.Load(SavePath);

        Assert.IsTrue(outcome.Failed, "Молчаливый чистый прогресс — это и есть потеря сессии из R06.");
        Assert.IsNotNull(outcome.Message);
        Assert.IsTrue(File.Exists(SaveFileStore.CorruptPathFor(SavePath)));
    }

    [Test]
    public void R06_MissingMainSave_RecoversFromBackup()
    {
        SaveFileStore.Write(SavePath, Json(100));
        SaveFileStore.Write(SavePath, Json(200));
        File.Delete(SavePath);

        var outcome = SaveFileStore.Load(SavePath);

        Assert.IsFalse(outcome.Failed);
        Assert.IsTrue(outcome.RestoredFromBackup);
        Assert.AreEqual(100, outcome.Data.metaCurrency);
    }

    [Test]
    public void R06_HealthySave_LoadsWithoutTouchingBackupOrCorruptPaths()
    {
        SaveFileStore.Write(SavePath, Json(150));

        var outcome = SaveFileStore.Load(SavePath);

        Assert.IsFalse(outcome.Failed);
        Assert.IsFalse(outcome.RestoredFromBackup);
        Assert.IsNull(outcome.Message);
        Assert.AreEqual(150, outcome.Data.metaCurrency);
        Assert.IsFalse(File.Exists(SaveFileStore.CorruptPathFor(SavePath)));
    }

    static int CountOccurrences(string source, string value)
    {
        int count = 0;
        for (int i = source.IndexOf(value, StringComparison.Ordinal); i >= 0;
             i = source.IndexOf(value, i + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
