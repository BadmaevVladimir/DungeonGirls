using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// Храм ур.5 (G03/R07, решение D03a): перезапуск с начала этажа со снимком состояния перед входом
// в первую комнату.
public class FloorRestartTests
{
    readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (var value in created) if (value != null) Object.DestroyImmediate(value);
        created.Clear();
    }

    T New<T>() where T : ScriptableObject
    {
        var value = ScriptableObject.CreateInstance<T>();
        created.Add(value);
        return value;
    }

    GameObject NewGo(string name)
    {
        var go = new GameObject(name);
        created.Add(go);
        return go;
    }

    CharacterData Character()
    {
        var character = New<CharacterData>();
        character.characterId = "jennifer";
        character.characterName = "Дженифер";
        character.baseHealth = 100;
        character.startingEquipment = new ItemData[0];
        return character;
    }

    [Test]
    public void TempleLevelFive_UnlocksExactlyOneFloorRestart()
    {
        for (int level = 0; level <= 4; level++)
            Assert.AreEqual(0, BuildingCatalog.TempleFloorRestarts(level), $"Уровень {level} перезапуска не даёт.");
        Assert.AreEqual(1, BuildingCatalog.TempleFloorRestarts(5));
    }

    [Test]
    public void FloorMapClone_IsDeepAndSurvivesMutation()
    {
        var map = FloorMapGenerator.Generate(3, 12345);
        foreach (var node in map.Nodes) node.ContentKey = "combat:test";
        var clone = RunStateClone.Clone(map);

        // Отыгрываем этаж на оригинале: посещения и текущий узел меняются.
        map.Nodes[0].Visited = true;
        map.Nodes[0].ContentKey = "испорчено";
        map.CurrentNodeId = map.Nodes[map.Nodes.Count - 1].Id;

        Assert.IsFalse(clone.Nodes[0].Visited, "Клон обязан быть глубоким.");
        Assert.AreEqual("combat:test", clone.Nodes[0].ContentKey);
        Assert.AreNotEqual(map.CurrentNodeId, clone.CurrentNodeId);
        Assert.AreEqual(map.Seed, clone.Seed, "Перезапуск повторяет тот же этаж, а не генерирует новый.");
    }

    [Test]
    public void ProgressClone_CopiesSkillsAndRerollsWithoutSharingState()
    {
        var character = Character();
        var skill = New<PassiveSkillData>();
        var progress = new RunCharacterProgress(character);
        progress.KnownSkillLevels[skill] = 2;
        progress.Level = 7;
        progress.Experience = 40;
        progress.UniqueActiveLevel = 2;
        progress.SetLevelUpRerolls(2);
        progress.MentorUniquePassiveSkillName = "Магнум Опус";

        var clone = RunStateClone.Clone(progress);
        progress.KnownSkillLevels[skill] = 5;
        progress.Level = 9;
        progress.TrySpendLevelUpReroll();

        Assert.AreEqual(7, clone.Level);
        Assert.AreEqual(40, clone.Experience);
        Assert.AreEqual(2, clone.KnownSkillLevels[skill], "Словарь навыков обязан копироваться, а не разделяться.");
        Assert.AreEqual(2, clone.LevelUpRerollsRemaining);
        Assert.AreEqual("Магнум Опус", clone.MentorUniquePassiveSkillName);
    }

    [Test]
    public void ProgressClone_DoesNotRegrantTheSameAutoActiveUpgrade()
    {
        var character = Character();
        character.uniqueActiveSkill = New<ActiveSkillData>();
        character.uniqueActiveSkill.maxLevel = 3;
        var progress = new RunCharacterProgress(character);

        Assert.IsTrue(progress.TryAutoUpgradeUniqueActiveAtLevel(5));
        Assert.AreEqual(2, progress.UniqueActiveLevel);

        var clone = RunStateClone.Clone(progress);
        Assert.IsFalse(clone.TryAutoUpgradeUniqueActiveAtLevel(5),
            "Порог последнего автоулучшения — часть состояния: клон не должен выдавать его повторно.");
        Assert.AreEqual(2, clone.UniqueActiveLevel);
    }

    [Test]
    public void RestoreRunState_ReturnsProgressCurrencyAndHealth()
    {
        var manager = NewGo("character").AddComponent<CharacterManager>();
        manager.BeginRun(Character());
        manager.AddCurrency(300);
        manager.Progress.Level = 6;
        var snapshot = manager.CaptureRunState();

        // «Проходим» этаж и погибаем.
        manager.AddCurrency(500);
        manager.Progress.Level = 8;
        manager.ApplyDirectDamage(manager.Combatant.MaxHP);
        Assert.IsFalse(manager.IsAlive);

        manager.RestoreRunState(snapshot);

        Assert.IsTrue(manager.IsAlive, "Перезапуск обязан вернуть героиню в живое состояние входа на этаж.");
        Assert.AreEqual(300, manager.RunCurrency, "Валюта забега откатывается к состоянию входа на этаж.");
        Assert.AreEqual(6, manager.Progress.Level);
    }

    [Test]
    public void RestoreRunState_ClearsRoomScopedModifiersInsteadOfLeakingThem()
    {
        var manager = NewGo("character").AddComponent<CharacterManager>();
        manager.BeginRun(Character());
        var snapshot = manager.CaptureRunState();

        // Бонус привала действовал на момент смерти.
        manager.RestBonus.Activate(RestBonusCatalog.Find(RestBonusId.Damage), manager.Combatant);
        Assert.AreEqual(15f, manager.Combatant.RestBonusDamagePercent);

        manager.RestoreRunState(snapshot);

        Assert.IsFalse(manager.RestBonus.IsActive, "Временные эффекты сбрасываются при перезапуске.");
        Assert.AreEqual(0f, manager.Combatant.RestBonusDamagePercent,
            "Оставленное в рантайме значение стало бы неистекающим баффом.");
        Assert.AreEqual(0f, manager.Combatant.FoodDamagePercent);
    }

    // Восстановление чистит временные модификаторы ещё раз, хотя снимок и так снимается чистым.
    // Проверяем именно этот защитный слой: снимок, пришедший «грязным», не должен превращаться
    // в неистекающий бафф.
    [Test]
    public void RestoreRunState_SanitizesADirtySnapshot()
    {
        var manager = NewGo("character").AddComponent<CharacterManager>();
        manager.BeginRun(Character());
        var snapshot = manager.CaptureRunState();

        snapshot.Combatant.RestBonusDamagePercent = 15f;
        snapshot.Combatant.FoodAttackSpeedPercent = 30f;

        manager.RestoreRunState(snapshot);

        Assert.AreEqual(0f, manager.Combatant.RestBonusDamagePercent,
            "Владельца этого модификатора после перезапуска не существует — значение обязано быть снято.");
        Assert.AreEqual(0f, manager.Combatant.FoodAttackSpeedPercent);
    }

    [Test]
    public void CaptureRunState_DoesNotCarryActiveBuffsIntoTheSnapshot()
    {
        var manager = NewGo("character").AddComponent<CharacterManager>();
        manager.BeginRun(Character());
        manager.RestBonus.Activate(RestBonusCatalog.Find(RestBonusId.AttackSpeed), manager.Combatant);

        var snapshot = manager.CaptureRunState();

        Assert.AreEqual(0f, snapshot.Combatant.RestBonusAttackSpeedPercent,
            "Снимок не должен консервировать бафф, владельца которого он не восстанавливает.");
    }

    [Test]
    public void RestoreRunState_KeepsSnapshotReusable()
    {
        var manager = NewGo("character").AddComponent<CharacterManager>();
        manager.BeginRun(Character());
        manager.AddCurrency(120);
        manager.Progress.Level = 4;
        var snapshot = manager.CaptureRunState();

        manager.AddCurrency(400);
        manager.Progress.Level = 11;
        manager.RestoreRunState(snapshot);
        Assert.AreEqual(4, manager.Progress.Level);

        // Второй заход по тому же снимку: если восстановление раздаёт ССЫЛКУ на прогресс снимка,
        // мутация после первого восстановления испортит сам снимок.
        manager.AddCurrency(400);
        manager.Progress.Level = 12;
        manager.RestoreRunState(snapshot);

        Assert.AreEqual(120, manager.RunCurrency, "Снимок обязан переживать восстановление неизменным.");
        Assert.AreEqual(4, manager.Progress.Level, "Прогресс обязан клонироваться при восстановлении, а не разделяться.");
    }

    [Test]
    public void CampRations_AreRestoredToFloorEntryAmount()
    {
        var camp = NewGo("camp").AddComponent<CampManager>();
        camp.BeginRun();
        int atFloorEntry = camp.RationsRemaining;

        camp.TrySpendRation();
        camp.TrySpendRation();
        Assert.AreEqual(atFloorEntry - 2, camp.RationsRemaining);

        camp.RestoreRations(atFloorEntry);
        Assert.AreEqual(atFloorEntry, camp.RationsRemaining);
    }

    [Test]
    public void RunLoop_RetriesTheFloorOnlyThroughTheTempleRestart()
    {
        string source = File.ReadAllText("Assets/Scripts/UI/RunFlowController.cs");
        StringAssert.Contains("var floorEntrySnapshot = CaptureFloorRestartSnapshot();", source);
        StringAssert.Contains("if (!floorLost || !CanRestartFloor(floorEntrySnapshot)) break;", source);
        StringAssert.Contains("yield return FloorRestartFlow(floorEntrySnapshot);", source);

        // Снимок обязан сниматься ПОСЛЕ резолва содержимого этажа: иначе перезапуск вернул бы карту
        // без разложенного по узлам контента.
        int resolve = source.IndexOf("ResolveGeneratedFloorMapContent();", System.StringComparison.Ordinal);
        int capture = source.IndexOf("var floorEntrySnapshot = CaptureFloorRestartSnapshot();", System.StringComparison.Ordinal);
        Assert.Greater(capture, resolve);
    }

    [Test]
    public void FloorRestart_PreservesOneShotRunFlags()
    {
        string source = File.ReadAllText("Assets/Scripts/UI/RunFlowController.FloorRestart.cs");
        foreach (string flag in new[]
        {
            "hotSpringsTriggeredThisRun", "violetTrapRoomTriggeredThisRun", "sashaBeerCellarTriggeredThisRun",
            "huntQuestTriggeredThisRun", "swordInStoneSucceededThisRun", "campSceneTriggeredThisRun"
        })
        {
            StringAssert.Contains(flag, source,
                $"Флаг {flag} обязан переживать перезапуск — иначе одноразовая комната открывается повторно (R01).");
        }
    }
}
