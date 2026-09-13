using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class BossSlotDisableTests
{
    readonly List<Object> created = new List<Object>();

    T NewObject<T>() where T : ScriptableObject
    {
        var value = ScriptableObject.CreateInstance<T>();
        created.Add(value);
        return value;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var value in created) Object.DestroyImmediate(value);
        foreach (var manager in Object.FindObjectsByType<CombatManager>(FindObjectsSortMode.None))
            Object.DestroyImmediate(manager.gameObject);
    }

    ItemData Item(EquipmentSlot slot, float hpBonus = 0f)
    {
        var item = NewObject<ItemData>();
        item.itemName = slot.ToString();
        item.slot = slot;
        if (slot == EquipmentSlot.Weapon)
        {
            item.weaponSubtype = WeaponSubtype.Sword;
            item.baseDamage = 10f;
            item.attackSpeed = 1f;
        }
        if (hpBonus > 0f)
            item.bonusStat = new BonusStat { type = BonusStatType.FlatHP, baseValue = hpBonus };
        return item;
    }

    CombatantRuntime Player(params ItemData[] equipment)
    {
        var character = NewObject<CharacterData>();
        character.characterName = "Тест";
        character.baseHealth = 100;
        character.startingEquipment = equipment;
        return CombatantFactory.CreatePlayerCombatant(character, 1, equipment: equipment);
    }

    CombatantRuntime Boss(BossAbilityConfig ability)
    {
        var kit = NewObject<BossKitData>();
        var phase = new BossPhaseData { hpThresholdPercent = 100f };
        phase.abilities.Add(ability);
        kit.phases.Add(phase);
        return new CombatantRuntime
        {
            DisplayName = "Пожиратель",
            IsBoss = true,
            MaxHP = 1000f,
            CurrentHP = 1000f,
            BossEncounter = new BossEncounterState(kit),
            Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.001f } }
        };
    }

    static BossAbilityConfig Ability(float seconds = 10f) => new BossAbilityConfig
    {
        displayName = "Заглатывание",
        effectKind = BossAbilityEffectKind.SlotDisable,
        triggerKind = BossAbilityTriggerKind.Periodic,
        initialDelaySeconds = 0f,
        cooldownSeconds = 100f,
        slotDisableSeconds = seconds,
        damageTakenBonusPercent = 20f,
        slotDisableOrder = new List<EquipmentSlot>
        {
            EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Helmet,
            EquipmentSlot.Boots, EquipmentSlot.Ring, EquipmentSlot.Accessory
        }
    };

    [Test]
    public void SlotDisable_NeverAllowsWeaponOrArmor()
    {
        Assert.IsFalse(CombatManager.IsSlotDisableAllowed(EquipmentSlot.Weapon));
        Assert.IsFalse(CombatManager.IsSlotDisableAllowed(EquipmentSlot.Armor));
        Assert.IsTrue(CombatManager.IsSlotDisableAllowed(EquipmentSlot.Helmet));
    }

    [Test]
    public void SlotDisable_SkipsForbiddenAndEmptySlots_ThenDisablesFirstOccupiedSafeSlot()
    {
        var player = Player(Item(EquipmentSlot.Weapon), Item(EquipmentSlot.Armor), Item(EquipmentSlot.Boots));
        var boss = Boss(Ability());
        var manager = new GameObject("Combat").AddComponent<CombatManager>();
        manager.StartCombat(player, new List<CombatantRuntime> { boss });

        manager.Tick(0.016f);

        CollectionAssert.AreEquivalent(new[] { EquipmentSlot.Boots }, player.DisabledEquipmentSlots.Keys);
        Assert.AreEqual(20f, boss.DamageTakenBonusPercent, 0.01f);
    }

    [Test]
    public void SlotDisable_WithNoOccupiedSafeSlot_ExpiresCooldownWithoutChangingStats()
    {
        var player = Player(Item(EquipmentSlot.Weapon), Item(EquipmentSlot.Armor));
        float maxHp = player.MaxHP;
        var manager = new GameObject("Combat").AddComponent<CombatManager>();
        manager.StartCombat(player, new List<CombatantRuntime> { Boss(Ability()) });

        Assert.DoesNotThrow(() => manager.Tick(0.016f));
        Assert.IsEmpty(player.DisabledEquipmentSlots);
        Assert.AreEqual(maxHp, player.MaxHP);
    }

    [Test]
    public void SlotDisable_RecalculatesDerivedStatsWithoutResettingLiveCombatState_AndRestoresOnTimer()
    {
        var player = Player(Item(EquipmentSlot.Weapon), Item(EquipmentSlot.Helmet, 40f));
        player.CurrentHP = 70f;
        player.Weapons[0].AttackTimer = 0.42f;
        player.ActiveDebuffs.Add(new ActiveDebuff { Id = "keep", RemainingTime = 30f });
        float fullMaxHp = player.MaxHP;
        var manager = new GameObject("Combat").AddComponent<CombatManager>();
        manager.StartCombat(player, new List<CombatantRuntime> { Boss(Ability(1f)) });
        player.AttackLockRemaining = 4f;
        player.Weapons[0].AttackTimer = 0.42f;

        manager.Tick(0.016f);

        Assert.AreEqual(fullMaxHp - 40f, player.MaxHP, 0.01f);
        Assert.AreEqual(70f, player.CurrentHP, 0.01f, "отключение HP-предмета не лечит и не пересчитывает HP пропорционально");
        Assert.AreEqual(1, player.ActiveDebuffs.Count);
        Assert.Greater(player.AttackLockRemaining, 0f);
        Assert.AreEqual(0.42f, player.Weapons[0].AttackTimer, 0.02f);

        manager.Tick(1.1f);

        Assert.IsEmpty(player.DisabledEquipmentSlots);
        Assert.AreEqual(fullMaxHp, player.MaxHP, 0.01f);
        Assert.AreEqual(70f, player.CurrentHP, 0.01f, "возврат слота не должен лечить");
        Assert.AreEqual(1, player.ActiveDebuffs.Count);
    }

    [Test]
    public void EndCombat_AlwaysRestoresDisabledSlots()
    {
        var player = Player(Item(EquipmentSlot.Weapon), Item(EquipmentSlot.Helmet, 40f));
        float fullMaxHp = player.MaxHP;
        var manager = new GameObject("Combat").AddComponent<CombatManager>();
        manager.StartCombat(player, new List<CombatantRuntime> { Boss(Ability(100f)) });
        manager.Tick(0.016f);
        Assert.Less(player.MaxHP, fullMaxHp);

        manager.EndCombat();

        Assert.IsEmpty(player.DisabledEquipmentSlots);
        Assert.AreEqual(fullMaxHp, player.MaxHP, 0.01f);
    }
}
