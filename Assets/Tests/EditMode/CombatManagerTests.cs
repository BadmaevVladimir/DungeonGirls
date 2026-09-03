using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class CombatManagerTests
{
    static GameObject NewGo(string name) => new GameObject(name);

    static ActiveSkillData NewSkill(ActiveSkillType type, float cooldownSeconds, SkillId id = SkillId.None)
    {
        var data = ScriptableObject.CreateInstance<ActiveSkillData>();
        data.skillName = "Test Skill";
        data.skillId = id;
        data.cooldownSeconds = cooldownSeconds;
        data.skillType = type;
        return data;
    }

    // Тот же паттерн очистки, что и в BossEncounterTests.cs — CombatManager создаёт реальный
    // GameObject через AddComponent, EditMode-тесты не выгружают сцену между тестами сами.
    [TearDown]
    public void TearDown()
    {
        foreach (var go in Object.FindObjectsByType<CombatManager>(FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(go.gameObject);
        }
    }

    [Test]
    public void ResolveActiveSkillHitCount_Rogue_ReturnsZero()
    {
        Assert.AreEqual(0, CombatManager.ResolveActiveSkillHitCount(CharacterClass.Rogue));
    }

    [Test]
    public void ResolveActiveSkillHitCount_NonRogue_ReturnsThree()
    {
        Assert.AreEqual(3, CombatManager.ResolveActiveSkillHitCount(CharacterClass.Warrior));
        Assert.AreEqual(3, CombatManager.ResolveActiveSkillHitCount(CharacterClass.Barbarian));
    }

    [Test]
    public void ConfigureActiveSkills_CooldownSkill_StartsReadyNotOnCooldown()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var cooldownSkill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);

        cm.ConfigureActiveSkills(new[]
        {
            new ActiveSkillConfigEntry(cooldownSkill, hitCount: 3, damageMultiplierPerHit: 1.1f, autoMode: false)
        });

        Assert.AreEqual(1, cm.ActiveSkills.Count);
        Assert.AreEqual(0f, cm.ActiveSkills[0].CooldownTimer);
        Assert.AreEqual(0f, cm.SkillCooldownRemaining(0));
    }

    [Test]
    public void ConfigureActiveSkills_ToggleSkill_StartsInactive()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var toggleSkill = NewSkill(ActiveSkillType.Toggle, cooldownSeconds: 0f, SkillId.Berserk);

        cm.ConfigureActiveSkills(new[]
        {
            new ActiveSkillConfigEntry(toggleSkill, hitCount: 0, damageMultiplierPerHit: 0f, autoMode: false)
        });

        Assert.IsFalse(cm.ActiveSkills[0].IsToggleActive);
    }

    [Test]
    public void ConfigureActiveSkills_ReplacesPreviousConfiguration()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var first = NewSkill(ActiveSkillType.Cooldown, 4f);
        var second = NewSkill(ActiveSkillType.Toggle, 0f, SkillId.Berserk);

        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(first, 3, 1f, false) });
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(second, 0, 0f, false) });

        Assert.AreEqual(1, cm.ActiveSkills.Count);
        Assert.AreEqual(second, cm.ActiveSkills[0].Data);
    }

    [Test]
    public void StartCombat_DoesNotForceActiveSkillIntoCooldown()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var cooldownSkill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(cooldownSkill, 3, 1f, false) });

        var player = new CombatantRuntime { DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f };
        cm.StartCombat(player, new List<CombatantRuntime>());

        Assert.IsTrue(cm.IsSkillReady(0));
        Assert.AreEqual(0f, cm.SkillCooldownRemaining(0));
    }

    [Test]
    public void TryActivateSkill_CooldownSkill_HitsAndStartsCooldown()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 3, damageMultiplierPerHit: 1f, autoMode: false) });

        var player = new CombatantRuntime
        {
            DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f,
            Weapons = new List<WeaponAttackState> { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 1f, DamageType = DamageType.Physical } }
        };
        var enemy = new CombatantRuntime { DisplayName = "Враг", MaxHP = 1000f, CurrentHP = 1000f };
        cm.StartCombat(player, new List<CombatantRuntime> { enemy });

        bool activated = cm.TryActivateSkill(0);

        Assert.IsTrue(activated);
        Assert.IsFalse(cm.IsSkillReady(0));
        Assert.AreEqual(4f, cm.SkillCooldownRemaining(0));
        Assert.Less(enemy.CurrentHP, 1000f); // hit-loop реально бьёт
    }

    [Test]
    public void TryActivateSkill_ToggleSkill_FlipsIsToggleActiveAndPlayerFlag()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Toggle, cooldownSeconds: 0f, SkillId.Berserk);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 0, damageMultiplierPerHit: 0f, autoMode: false) });

        var player = new CombatantRuntime { DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f, UniqueBerserkLevel = 1 };
        cm.StartCombat(player, new List<CombatantRuntime>());

        Assert.IsTrue(cm.TryActivateSkill(0));
        Assert.IsTrue(cm.ActiveSkills[0].IsToggleActive);
        Assert.IsTrue(player.IsBerserkActive);

        Assert.IsTrue(cm.TryActivateSkill(0));
        Assert.IsFalse(cm.ActiveSkills[0].IsToggleActive);
        Assert.IsFalse(player.IsBerserkActive);
    }

    [Test]
    public void TryActivateSkill_ToggleSkill_CannotEnableWithoutLevel()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Toggle, cooldownSeconds: 0f, SkillId.Berserk);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 0, damageMultiplierPerHit: 0f, autoMode: false) });

        var player = new CombatantRuntime { DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f, UniqueBerserkLevel = 0 };
        cm.StartCombat(player, new List<CombatantRuntime>());

        Assert.IsFalse(cm.TryActivateSkill(0));
        Assert.IsFalse(cm.ActiveSkills[0].IsToggleActive);
    }

    [Test]
    public void Tick_AutoModeOff_DoesNotAutoActivateCooldownSkill()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 3, damageMultiplierPerHit: 1f, autoMode: false) });

        var player = new CombatantRuntime
        {
            DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f,
            Weapons = new List<WeaponAttackState> { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f, DamageType = DamageType.Physical } }
        };
        var enemy = new CombatantRuntime { DisplayName = "Враг", MaxHP = 1000f, CurrentHP = 1000f };
        cm.StartCombat(player, new List<CombatantRuntime> { enemy });

        cm.Tick(1f);

        Assert.IsTrue(cm.IsSkillReady(0)); // ready все ещё, авто-режим выключен — никто не потратил его
    }

    [Test]
    public void Tick_AutoModeOn_AutoActivatesReadyCooldownSkill()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 3, damageMultiplierPerHit: 1f, autoMode: true) });

        var player = new CombatantRuntime
        {
            DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f,
            Weapons = new List<WeaponAttackState> { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f, DamageType = DamageType.Physical } }
        };
        var enemy = new CombatantRuntime { DisplayName = "Враг", MaxHP = 1000f, CurrentHP = 1000f };
        cm.StartCombat(player, new List<CombatantRuntime> { enemy });

        cm.Tick(0.01f);

        Assert.IsFalse(cm.IsSkillReady(0)); // авто-режим включён явно — теперь он потратил его
    }

    [Test]
    public void SetSkillAutoMode_UpdatesSlot()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Cooldown, 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, 3, 1f, autoMode: false) });

        cm.SetSkillAutoMode(0, true);

        Assert.IsTrue(cm.ActiveSkills[0].AutoMode);
    }
}
