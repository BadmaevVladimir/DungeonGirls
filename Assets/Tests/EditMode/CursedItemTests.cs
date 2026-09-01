using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class CursedItemTests
{
    readonly List<Object> created = new List<Object>();

    T NewObject<T>() where T : ScriptableObject
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

    ItemData Weapon(CursedEffectId effect, int rank = 1, WeaponSubtype subtype = WeaponSubtype.Sword, bool pair = false)
    {
        var item = NewObject<ItemData>();
        item.itemName = effect.ToString();
        item.slot = EquipmentSlot.Weapon;
        item.weaponSubtype = subtype;
        item.tier = ItemTier.Cursed;
        item.cursedEffect = effect;
        item.itemRank = rank;
        item.baseDamage = subtype == WeaponSubtype.Blade ? 4f : 10f;
        item.attackSpeed = 1f;
        item.isPairedWeapon = pair;
        item.isTwoHanded = pair || subtype == WeaponSubtype.TwoHandedAxe;
        return item;
    }

    static CombatantRuntime Fighter(bool player, float hp = 1000f) => new CombatantRuntime
    {
        DisplayName = player ? "Игрок" : "Цель", IsPlayer = player, MaxHP = hp, CurrentHP = hp
    };

    static WeaponAttackState RuntimeWeapon(CursedEffectId effect, int rank = 1, float damage = 10f, float speed = 1f) => new WeaponAttackState
    {
        CursedEffect = effect, ItemRank = rank, DamageMin = damage, DamageMax = damage,
        DamageType = DamageType.Physical, AttackSpeed = speed
    };

    [TearDown]
    public void TearDown()
    {
        foreach (var value in created) Object.DestroyImmediate(value);
        created.Clear();
    }

    [Test] public void RarityTable_SumsToOneHundred() => Assert.AreEqual(100f, ItemRarityTable.TotalPercent);

    [Test]
    public void RarityTable_UsesSixtyThirtyFiveThreeTwoAndBossKeepsCursed()
    {
        Assert.AreEqual(ItemTier.Common, ItemRarityTable.Roll(59.999f, false));
        Assert.AreEqual(ItemTier.Rare, ItemRarityTable.Roll(60f, false));
        Assert.AreEqual(ItemTier.Epic, ItemRarityTable.Roll(95f, false));
        Assert.AreEqual(ItemTier.Cursed, ItemRarityTable.Roll(98f, false));
        Assert.AreEqual(ItemTier.Rare, ItemRarityTable.Roll(10f, true));
        Assert.AreEqual(ItemTier.Cursed, ItemRarityTable.Roll(99f, true));
    }

    [Test]
    public void Catalog_ClassFiltering_WorksForCursed()
    {
        var rogue = Weapon(CursedEffectId.ParanoiaBlades, subtype: WeaponSubtype.Blade, pair: true);
        rogue.allowedClasses = new[] { CharacterClass.Rogue };
        var catalog = NewObject<ItemCatalogData>();
        catalog.items = new[] { rogue };
        Assert.IsTrue(catalog.TryGetGuaranteedCursedItem(CursedEffectId.ParanoiaBlades, CharacterClass.Rogue, out var found));
        Assert.AreSame(rogue, found);
        Assert.IsFalse(catalog.TryGetGuaranteedCursedItem(CursedEffectId.ParanoiaBlades, CharacterClass.Warrior, out _));
    }

    [Test]
    public void CursedMainStat_UsesTierMultiplierAndLevelIncrementFromUnscaledBase()
    {
        var item = Weapon(CursedEffectId.Executioner);
        item.baseDamage = 10f;
        item.itemLevel = 3;
        Assert.AreEqual(24f, item.EffectiveDamage); // 10*2.2 + round(10%)*2
    }

    [Test]
    public void EquipmentCurse_IsRealDebuffSeenByUnyielding_AndStubbornnessCanPreventIt()
    {
        var owner = Fighter(true);
        owner.Weapons.Add(RuntimeWeapon(CursedEffectId.Executioner));
        Assert.IsTrue(CursedItemRules.TryApplyEquipmentCurse(owner, CursedEffectId.Executioner));
        Assert.IsTrue(owner.HasActiveDebuff);
        owner.SkillUnyieldingLevel = 1;
        Assert.IsTrue(owner.HasActiveDebuff);

        var stubborn = Fighter(true, 100f);
        stubborn.CurrentHP = 10f;
        stubborn.SkillStubbornnessLevel = 1;
        Assert.IsFalse(CursedItemRules.TryApplyEquipmentCurse(stubborn, CursedEffectId.Executioner));
        Assert.IsFalse(stubborn.HasActiveDebuff);
    }

    [Test]
    public void ReplacingCursedWeapon_RemovesEquipmentCurse()
    {
        var cursed = Weapon(CursedEffectId.Executioner);
        var normal = NewObject<ItemData>(); normal.slot = EquipmentSlot.Weapon; normal.weaponSubtype = WeaponSubtype.Sword; normal.baseDamage = 6; normal.attackSpeed = 1;
        var character = NewObject<CharacterData>(); character.characterName = "Воин"; character.baseHealth = 100; character.startingEquipment = new[] { cursed };
        var manager = NewGo("character").AddComponent<CharacterManager>(); manager.BeginRun(character);
        Assert.IsTrue(manager.Combatant.HasActiveDebuff);
        manager.EquipItem(normal, cursed);
        Assert.IsFalse(manager.Combatant.HasActiveDebuff);
    }

    [Test]
    public void Oathbreaker_CritAddsThirty_NonCritDoesNot()
    {
        int currency = 0;
        var player = Fighter(true);
        player.AddRunCurrency = value => currency += value;
        player.SmokeBombGuaranteedCritsRemaining = 1;
        player.Weapons.Add(RuntimeWeapon(CursedEffectId.Oathbreaker, damage: 10f));
        var enemy = Fighter(false);
        enemy.Weapons.Add(RuntimeWeapon(CursedEffectId.None, speed: 0.01f));
        var cm = NewGo("combat").AddComponent<CombatManager>();
        cm.StartCombat(player, new List<CombatantRuntime> { enemy });
        cm.Tick(1.01f);
        Assert.AreEqual(30, currency);
        cm.Tick(1.01f);
        Assert.AreEqual(30, currency);
    }

    [Test]
    public void Oathbreaker_NormalCritBacklash_DoesNotTouchArmor()
    {
        var owner = Fighter(true, 100f);
        owner.PhysicalDefenseCurrent = 50f;
        var weapon = RuntimeWeapon(CursedEffectId.Oathbreaker, damage: 20f);
        owner.Weapons.Add(weapon);
        CursedItemRules.TryApplyEquipmentCurse(owner, CursedEffectId.Oathbreaker);
        float damage = CursedItemRules.CalculateNormalCritDamage(owner, weapon);
        owner.CurrentHP -= damage;
        Assert.AreEqual(30f, damage);
        Assert.AreEqual(70f, owner.CurrentHP);
        Assert.AreEqual(50f, owner.PhysicalDefenseCurrent);
    }

    [TestCase(25f, 2f)] [TestCase(25.01f, 1f)] [TestCase(74.99f, 1f)] [TestCase(75f, 0.75f)]
    public void Executioner_Boundaries(float hp, float expected) => Assert.AreEqual(expected, CursedItemRules.ExecutionerDamageMultiplier(hp, 100f));

    [Test]
    public void Berserker_StacksCapAndMultiplicativeResistanceProducesNinety()
    {
        var target = Fighter(true);
        var weapon = RuntimeWeapon(CursedEffectId.BerserkerAxe, 5);
        weapon.CursedStacks = 5;
        target.Weapons.Add(weapon);
        target.PhysicalResistancePercent = 40f;
        CursedItemRules.TryApplyEquipmentCurse(target, CursedEffectId.BerserkerAxe);
        var result = DamageCalculator.ApplyDamage(target, 100f, DamageType.Physical);
        Assert.AreEqual(90f, result.DamageToHP, 0.001f); // 100*1.5*0.6
        Assert.AreEqual(50f, CursedItemRules.StackBonusPercent(5, 99));
    }

    [Test]
    public void RecklessCharge_CapsDefensePenaltyAtThirtyPercent()
    {
        Assert.AreEqual(0.97f, CursedItemRules.RecklessDefenseMultiplier(1));
        Assert.AreEqual(0.70f, CursedItemRules.RecklessDefenseMultiplier(10));
        Assert.AreEqual(0.70f, CursedItemRules.RecklessDefenseMultiplier(99));
    }

    [Test]
    public void RecklessCharge_AttackAddsStack_AndThreeSecondsWithoutAttacksResetsIt()
    {
        var player = Fighter(true); player.Weapons.Add(RuntimeWeapon(CursedEffectId.RecklessCharge));
        CursedItemRules.TryApplyEquipmentCurse(player, CursedEffectId.RecklessCharge);
        var enemy = Fighter(false); enemy.Weapons.Add(RuntimeWeapon(CursedEffectId.None, speed: 0.01f));
        var cm = NewGo("combat").AddComponent<CombatManager>(); cm.StartCombat(player, new List<CombatantRuntime> { enemy });
        cm.Tick(1.01f);
        Assert.AreEqual(1, player.CursedRecklessStacks);
        player.AttackLocked = true;
        cm.Tick(CursedItemRules.RecklessStackDecaySeconds + 0.01f);
        Assert.AreEqual(0, player.CursedRecklessStacks);
    }

    [TestCase(1, 2f)] [TestCase(2, 3f)] [TestCase(3, 4f)] [TestCase(4, 5f)] [TestCase(5, 6f)]
    public void LastArgument_ScalesFromRankNotItemLevel(int rank, float expected) =>
        Assert.AreEqual(expected, CursedItemRules.LastArgumentBonusDamage(100f, rank));

    [Test]
    public void LastArgument_BlocksCampArmorRecovery_ButChestReplacementRefillsArmor()
    {
        var armor = NewObject<ItemData>(); armor.slot = EquipmentSlot.Armor; armor.physicalDefense = 50f;
        var hammer = Weapon(CursedEffectId.LastArgument, subtype: WeaponSubtype.Hammer);
        var repair = NewObject<PassiveSkillData>(); repair.skillId = SkillId.Repair; hammer.passiveSkill = repair;
        var character = NewObject<CharacterData>(); character.characterName = "Воин"; character.baseHealth = 100; character.startingEquipment = new[] { armor, hammer };
        var manager = NewGo("character").AddComponent<CharacterManager>(); manager.BeginRun(character);
        manager.Combatant.PhysicalDefenseCurrent = 10f;
        var camp = NewGo("camp").AddComponent<CampManager>(); camp.BeginRun();
        var result = camp.RestoreAtCamp(manager);
        Assert.AreEqual(0f, result.ArmorRestored);
        Assert.AreEqual(10f, manager.Combatant.PhysicalDefenseCurrent);

        var newArmor = NewObject<ItemData>(); newArmor.slot = EquipmentSlot.Armor; newArmor.physicalDefense = 25f;
        manager.EquipItem(newArmor, armor);
        Assert.AreEqual(manager.Combatant.PhysicalDefenseMax, manager.Combatant.PhysicalDefenseCurrent);
    }

    [Test]
    public void PairWeapon_CreatesTwoIndependentAttackSourcesAndOccupiesBothHands()
    {
        var pair = Weapon(CursedEffectId.BetrayerAndAccomplice, 3, WeaponSubtype.Blade, true);
        var character = NewObject<CharacterData>();
        character.characterName = "Плут"; character.baseHealth = 100; character.startingEquipment = new ItemData[0];
        var runtime = CombatantFactory.CreatePlayerCombatant(character, 1, null, new[] { pair });
        Assert.AreEqual(2, runtime.Weapons.Count);
        Assert.AreNotSame(runtime.Weapons[0], runtime.Weapons[1]);
        Assert.AreEqual(runtime.Weapons[0].DamageMin, runtime.Weapons[1].DamageMin);

        var manager = NewGo("character").AddComponent<CharacterManager>();
        manager.BeginRun(character);
        manager.EquipItem(pair, null);
        Assert.AreEqual(1, manager.EquippedItems.Count);
        Assert.AreEqual(1, manager.GetComparisonCandidates(Weapon(CursedEffectId.Oathbreaker)).Count);
    }

    [Test]
    public void Betrayer_StealthDamageScalesByRank_AndTimerClamps()
    {
        Assert.AreEqual(20f, CursedItemRules.StealthDamageBonusPercent(1));
        Assert.AreEqual(40f, CursedItemRules.StealthDamageBonusPercent(5));
        Assert.AreEqual(0f, Mathf.Max(0f, 0.1f - 0.25f));
    }

    [Test]
    public void Paranoia_IncomingDamageScalesAndStacksCanBeRebuilt()
    {
        Assert.AreEqual(1f, CursedItemRules.ParanoiaIncomingMultiplier(0));
        Assert.AreEqual(1.25f, CursedItemRules.ParanoiaIncomingMultiplier(5));
        int stacks = 5;
        stacks = 0;
        stacks = Mathf.Min(CursedItemRules.MaxStacks, stacks + 1);
        Assert.AreEqual(1, stacks);
        Assert.AreEqual(75f, BalanceClamps.MaxEvasionChancePercent);
    }

    [Test]
    public void Paranoia_NonDodgedHitUsesStacksThenClearsThem()
    {
        var player = Fighter(true); player.Weapons.Add(RuntimeWeapon(CursedEffectId.ParanoiaBlades, speed: 0.01f));
        player.CursedParanoiaStacks = 3; CursedItemRules.TryApplyEquipmentCurse(player, CursedEffectId.ParanoiaBlades);
        var enemy = Fighter(false); enemy.Weapons.Add(RuntimeWeapon(CursedEffectId.None, damage: 10f));
        var cm = NewGo("combat").AddComponent<CombatManager>(); cm.StartCombat(player, new List<CombatantRuntime> { enemy });
        cm.Tick(1.01f);
        Assert.AreEqual(988.5f, player.CurrentHP, 0.001f);
        Assert.AreEqual(0, player.CursedParanoiaStacks);
    }

    [Test]
    public void ThornAxe_RankFiveBleedIsInfinite_AndStubbornnessCanPreventEventDebuff()
    {
        Assert.AreEqual(5, Mathf.Clamp(5, 1, 5));
        Assert.IsTrue(float.IsPositiveInfinity(BleedRules.DurationForLevel(5)));
        Assert.AreEqual(3f, BleedRules.DurationForLevel(4));
        var stubborn = Fighter(true, 100f);
        stubborn.CurrentHP = 10f; stubborn.SkillStubbornnessLevel = 1;
        Assert.IsTrue(CursedItemRules.IgnoresNewDebuffs(stubborn));
    }

    [Test]
    public void ThornAxe_CritAppliesRankFiveBleed_AndBleedEnablesSpeedBonus()
    {
        var player = Fighter(true); var thorn = RuntimeWeapon(CursedEffectId.ThornAxe, 5); player.Weapons.Add(thorn);
        player.SmokeBombGuaranteedCritsRemaining = 1;
        var enemy = Fighter(false); enemy.Weapons.Add(RuntimeWeapon(CursedEffectId.None, speed: 0.01f));
        var cm = NewGo("combat").AddComponent<CombatManager>(); cm.StartCombat(player, new List<CombatantRuntime> { enemy });
        cm.Tick(1.01f);
        Assert.IsTrue(player.HasBleed);
        Assert.AreEqual(5, player.BleedLevel);
        Assert.IsTrue(float.IsPositiveInfinity(player.BleedTimer));
        Assert.AreEqual(1.4f, player.GetEffectiveAttackSpeed(thorn), 0.001f);
    }

    [Test]
    public void ThornAxe_StubbornnessPreventsSelfBleed()
    {
        var player = Fighter(true, 100f); player.CurrentHP = 10f; player.SkillStubbornnessLevel = 1;
        player.Weapons.Add(RuntimeWeapon(CursedEffectId.ThornAxe, 5)); player.SmokeBombGuaranteedCritsRemaining = 1;
        var enemy = Fighter(false); enemy.Weapons.Add(RuntimeWeapon(CursedEffectId.None, speed: 0.01f));
        var cm = NewGo("combat").AddComponent<CombatManager>(); cm.StartCombat(player, new List<CombatantRuntime> { enemy });
        cm.Tick(1.01f);
        Assert.IsFalse(player.HasBleed);
    }
}
