using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class DamageCalculatorTests
{
    [Test]
    public void ApplyPhysicalDamage_DamageBelowDefense_IsFullyBlockedAndWearsArmor()
    {
        var target = new CombatantRuntime { PhysicalDefenseCurrent = 50f, CurrentHP = 100f };

        var result = DamageCalculator.ApplyPhysicalDamage(target, 10f);

        Assert.IsTrue(result.WasBlocked);
        Assert.AreEqual(0f, result.DamageToHP);
        Assert.AreEqual(49f, target.PhysicalDefenseCurrent); // max(1, floor(10/20)) = 1
        Assert.AreEqual(100f, target.CurrentHP);
    }

    [Test]
    public void ApplyPhysicalDamage_DamageAboveDefense_DealsRemainderToHP()
    {
        var target = new CombatantRuntime { PhysicalDefenseCurrent = 10f, CurrentHP = 100f };

        var result = DamageCalculator.ApplyPhysicalDamage(target, 30f);

        Assert.IsFalse(result.WasBlocked);
        Assert.AreEqual(20f, result.DamageToHP);
        Assert.AreEqual(80f, target.CurrentHP);
    }

    [Test]
    public void ApplyMagicalDamage_DamageExceedsShield_DealsRemainderToHP()
    {
        var target = new CombatantRuntime { MagicShieldCurrent = 15f, CurrentHP = 50f };

        var result = DamageCalculator.ApplyMagicalDamage(target, 20f);

        Assert.IsFalse(result.WasBlocked);
        Assert.AreEqual(5f, result.DamageToHP);
        Assert.AreEqual(0f, target.MagicShieldCurrent);
        Assert.AreEqual(45f, target.CurrentHP);
    }

    [Test]
    public void ApplyDamage_WithResistance_ReducesDamageBeforeDefense()
    {
        var target = new CombatantRuntime { PhysicalDefenseCurrent = 0f, CurrentHP = 100f, PhysicalResistancePercent = 50f };

        var result = DamageCalculator.ApplyDamage(target, 40f, DamageType.Physical);

        Assert.AreEqual(20f, result.DamageToHP); // 40 * (1 - 0.5) = 20
        Assert.AreEqual(80f, target.CurrentHP);
    }

    [Test]
    public void ComputeDamageRange_ReturnsFloorAndCeilOfPlusMinus20Percent()
    {
        DamageCalculator.ComputeDamageRange(10f, out float min, out float max);

        Assert.AreEqual(8f, min);
        Assert.AreEqual(12f, max);
    }
}

public class SeptemberBalanceInteractionTests
{
    readonly List<Object> created = new List<Object>();
    sealed class FixedRandom : ICombatRandom
    {
        public float Value01() => 0f;
        public float Range(float min, float max) => min;
    }
    T Asset<T>() where T : ScriptableObject
    {
        var asset = ScriptableObject.CreateInstance<T>(); created.Add(asset); return asset;
    }
    static CombatantRuntime Fighter(bool player = false, float damage = 10f) => new CombatantRuntime
    {
        IsPlayer = player, MaxHP = 100f, CurrentHP = 100f,
        Weapons = new List<WeaponAttackState> { new WeaponAttackState { DamageMin = damage, DamageMax = damage, AttackSpeed = 0f } }
    };
    CombatManager Combat(CombatantRuntime player, params CombatantRuntime[] enemies)
    {
        var go = new GameObject("Balance regression"); created.Add(go);
        var cm = go.AddComponent<CombatManager>(); cm.SetHeadlessSimulationMode(true);
        cm.SetRandomSource(new FixedRandom()); cm.StartCombat(player, new List<CombatantRuntime>(enemies)); return cm;
    }
    [TearDown] public void Cleanup()
    {
        foreach (var item in created) if (item != null) Object.DestroyImmediate(item);
        created.Clear();
    }

    [TestCase(0.49f, 0f)] [TestCase(0.5f, 1f)] [TestCase(1.5f, 2f)]
    public void DamageRoundsToNearestWholePoint(float incoming, float expected)
    {
        var target = Fighter();
        var result = DamageCalculator.ApplyDamage(target, incoming, DamageType.Physical);
        Assert.AreEqual(expected, result.DamageToHP);
        Assert.AreEqual(100f - expected, target.CurrentHP);
    }

    [Test] public void ExactArmorEqualityDoesNotApplyOnHpEffects()
    {
        var p = Fighter(true); p.SkillBleedLevel = 5; p.SkillFreezeLevel = 5; p.SkillPoisonedBladeLevel = 5;
        var e = Fighter(); e.PhysicalDefenseCurrent = 10f;
        var cm = Combat(p, e); cm.ResolveScriptedAttack(p, p.Weapons[0]);
        Assert.AreEqual(100f, e.CurrentHP); Assert.IsFalse(e.HasBleed);
        Assert.AreEqual(0, e.FreezeStacks); Assert.AreEqual(0, e.RoguePoisonStacksOnTarget);
    }

    [Test] public void FractionalLegacyHealthCannotSurviveAsDisplayedZero()
    {
        var e = Fighter(); e.CurrentHP = .4f;
        DamageCalculator.ApplyDirectDamage(e, 0f);
        Assert.AreEqual(0f, e.CurrentHP); Assert.IsFalse(e.IsAlive);
    }

    [Test] public void PhysicalDamageRoundsAfterFractionalArmorMitigation()
    {
        var e = Fighter(); e.PhysicalDefenseCurrent = .6f;
        var result = DamageCalculator.ApplyDamage(e, 10.4f, DamageType.Physical);
        Assert.AreEqual(10, result.DamageToHP);
    }

    [TestCase(50f, 62f)] [TestCase(-50f, 54f)]
    public void VampirismUsesActualHpDamageAndHealingModifiers(float bonus, float expectedHp)
    {
        var p = Fighter(true, 20f); p.CurrentHP = 50f; p.SkillCriticalHitsLevel = 5;
        p.Weapons[0].VampirismLevel = 5; p.FoodReceivedHealingPercent = bonus;
        var e = Fighter(); e.PhysicalDefenseCurrent = 10f;
        var cm = Combat(p, e); cm.ResolveScriptedAttack(p, p.Weapons[0]);
        Assert.AreEqual(80f, e.CurrentHP); Assert.AreEqual(expectedHp, p.CurrentHP);
    }

    [Test] public void BlockedCriticalHitDoesNotHeal()
    {
        var p = Fighter(true); p.CurrentHP = 50; p.SkillCriticalHitsLevel = 5; p.Weapons[0].VampirismLevel = 5;
        var e = Fighter(); e.PhysicalDefenseCurrent = 100;
        var cm = Combat(p, e); cm.ResolveScriptedAttack(p, p.Weapons[0]);
        Assert.AreEqual(50, p.CurrentHP);
    }

    [TestCase(50f, 57f)] [TestCase(-50f, 51f)]
    public void RegenerationUsesHealingModifiers(float bonus, float expectedHp)
    {
        var p = Fighter(true); p.CurrentHP = 50; p.SkillCombatRegenLevel = 5; p.RunReceivedHealingPercent = bonus;
        var e = Fighter(false, 1);
        var cm = Combat(p, e);
        cm.ResolveScriptedAttack(e, e.Weapons[0]); cm.ResolveScriptedAttack(e, e.Weapons[0]);
        Assert.AreEqual(expectedHp, p.CurrentHP);
    }

    [Test] public void BlockedAttacksDoNotChargeRegeneration()
    {
        var p = Fighter(true); p.CurrentHP = 50; p.SkillCombatRegenLevel = 5; p.PhysicalDefenseCurrent = 100;
        var e = Fighter(false, 1); var cm = Combat(p, e);
        cm.ResolveScriptedAttack(e, e.Weapons[0]); cm.ResolveScriptedAttack(e, e.Weapons[0]);
        Assert.AreEqual(50, p.CurrentHP); Assert.AreEqual(0, p.HitsTakenSinceLastRegen);
    }

    [Test] public void FoodAndRestCritBonusesConvertForRageCharacter()
    {
        var p = Fighter(true, 10); p.CritChanceReplacedByRage = true; p.UniqueChampionOfTheTribeLevel = 1;
        p.FoodCritChancePoints = 10; p.RestBonusCritChancePoints = 8;
        var e = Fighter(); var cm = Combat(p, e); cm.ResolveScriptedAttack(p, p.Weapons[0]);
        Assert.AreEqual(36f, CombatCriticalRules.ConvertedCritDamageBonus(p));
        Assert.AreEqual(81f, e.CurrentHP, "10 * (150+36)% rounds to 19");
    }

    [Test] public void SpellEaterDoesNotSpendMainDamageOnMagicShield()
    {
        var e = Fighter(); e.MagicShieldCurrent = 30;
        var r = DamageCalculator.ApplySpellEaterPhysicalDamage(e, 10, 0, out float stripped);
        Assert.AreEqual(10, stripped); Assert.AreEqual(20, e.MagicShieldCurrent);
        Assert.AreEqual(90, e.CurrentHP); Assert.AreEqual(10, r.DamageToHP);
    }

    [Test] public void ShatterRespectsResistanceAndUniversalBarrier()
    {
        var p = Fighter(true); p.SkillFreezeLevel = 5;
        var e = Fighter(); e.IsFrozen = true; e.FreezeTimer = 10;
        e.MagicalResistancePercent = 50;
        var cm = Combat(p, e);
        // Barrier appears immediately after the physical hit, before the shatter.
        cm.HitResolved += (target, damage, crit, block) => { if (target == e && damage == 10) e.ShieldPoolCurrent = 3; };
        cm.ResolveScriptedAttack(p, p.Weapons[0]);
        Assert.AreEqual(88, e.CurrentHP, "10 physical + (10*50% - 3) shatter");
        Assert.AreEqual(0, e.ShieldPoolCurrent); Assert.IsFalse(e.IsFrozen);
    }

    [TestCase(SkillId.MonsterDarkHeal)] [TestCase(SkillId.MonsterDoubleStrike)]
    public void FrozenMonsterPausesSpecialTimer(SkillId passive)
    {
        var p = Fighter(true); var e = Fighter(); e.CurrentHP = 50;
        e.MonsterPassiveSkillId = passive; e.MonsterPassiveCooldownTimer = .1f;
        e.IsFrozen = true; e.FreezeTimer = 2;
        var cm = Combat(p, e); cm.Tick(.5f);
        Assert.AreEqual(.1f, e.MonsterPassiveCooldownTimer); Assert.AreEqual(100, p.CurrentHP); Assert.AreEqual(50, e.CurrentHP);
        e.IsFrozen = false; cm.Tick(.2f);
        if (passive == SkillId.MonsterDarkHeal) Assert.AreEqual(60, e.CurrentHP);
        else Assert.AreEqual(85, p.CurrentHP);
    }

    [Test] public void FrozenBossDoesNotExecuteAbility()
    {
        var kit = Asset<BossKitData>();
        kit.phases.Add(new BossPhaseData { abilities = new List<BossAbilityConfig> {
            new BossAbilityConfig { initialDelaySeconds = 0, damageMultiplier = 2 } } });
        var p = Fighter(true); var e = Fighter(); e.IsBoss = true;
        e.BossEncounter = new BossEncounterState(kit); e.IsFrozen = true; e.FreezeTimer = 2;
        var cm = Combat(p, e); cm.Tick(.5f); Assert.AreEqual(100, p.CurrentHP);
        e.IsFrozen = false; cm.Tick(.1f); Assert.AreEqual(80, p.CurrentHP);
    }

    [Test] public void PoisonExcessSurvivesHitsThenDecaysToCurrentCap()
    {
        var p = Fighter(true, 1); p.SkillPoisonedBladeLevel = 5; p.IsStealthed = true; p.StealthTimer = 30;
        var e = Fighter(); var cm = Combat(p, e);
        for (int i = 0; i < 5; i++) cm.ResolveScriptedAttack(p, p.Weapons[0]);
        Assert.AreEqual(10, e.RoguePoisonStacksOnTarget);
        p.IsStealthed = false;
        cm.ResolveScriptedAttack(p, p.Weapons[0]); Assert.AreEqual(10, e.RoguePoisonStacksOnTarget);
        for (int i = 0; i < 5; i++) { cm.Tick(1); cm.ResolveScriptedAttack(p, p.Weapons[0]); }
        Assert.AreEqual(5, e.RoguePoisonStacksOnTarget);
        cm.ResolveScriptedAttack(p, p.Weapons[0]); Assert.AreEqual(5, e.RoguePoisonStacksOnTarget);
    }

    [TestCase(0f, 6f)] [TestCase(10f, 2f)]
    public void HarpyOnlyDelaysActiveSkillWhenMagicScreamDamagesHp(float shield, float cooldown)
    {
        var p = Fighter(true); p.PhysicalDefenseCurrent = 100; p.MagicShieldCurrent = shield;
        var e = Fighter(); e.MonsterPassiveSkillId = SkillId.MonsterStunningScream;
        var cm = Combat(p, e); var skill = Asset<ActiveSkillData>(); skill.skillType = ActiveSkillType.Cooldown;
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, 1, 1, false) });
        cm.ActiveSkills[0].CooldownTimer = 2;
        cm.ResolveScriptedAttack(e, e.Weapons[0]); Assert.AreEqual(cooldown, cm.SkillCooldownRemaining(0));
    }

    [Test] public void HarpyTemporarilyDisablesBerserkAndPreventsEarlyReactivation()
    {
        var p = Fighter(true); p.UniqueBerserkLevel = 3; var cm = Combat(p, Fighter());
        var skill = Asset<ActiveSkillData>(); skill.skillId = SkillId.Berserk; skill.skillType = ActiveSkillType.Toggle;
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, 0, 0, false) });
        cm.TryActivateSkill(0); cm.DisruptPlayerActiveSkills(4);
        Assert.IsFalse(p.IsBerserkActive); Assert.IsFalse(cm.TryActivateSkill(0));
        cm.Tick(3); Assert.IsFalse(p.IsBerserkActive);
        cm.Tick(1); Assert.IsTrue(p.IsBerserkActive);
    }

    [Test] public void FrontlineGuardianIsFirstWithoutReorderingOtherEnemies()
    {
        var p = Fighter(true); var a = Fighter(); var guardian = Fighter(); guardian.FrontlinePriority = true; var b = Fighter();
        var cm = Combat(p, a, guardian, b);
        CollectionAssert.AreEqual(new[] { guardian, a, b }, cm.Enemies); Assert.AreSame(guardian, p.Target);
    }

    [Test] public void MonsterShieldsScaleAndZeroShieldStaysAbsent()
    {
        var m = Asset<MonsterData>(); m.hp = 50; m.magicDefense = 10; m.universalShield = 20;
        var early = CombatantFactory.CreateMonsterCombatant(m, 1, 1, true);
        var late = CombatantFactory.CreateMonsterCombatant(m, 4, 4, true);
        Assert.Greater(late.MagicShieldMax, early.MagicShieldMax); Assert.Greater(late.ShieldPoolMax, early.ShieldPoolMax);
        m.magicDefense = 0; Assert.AreEqual(0, CombatantFactory.CreateMonsterCombatant(m, 10, 4, true).MagicShieldMax);
    }

    [Test] public void EachArmorBreakRankImprovesChance()
    {
        for (int rank = 1; rank <= 5; rank++) Assert.AreEqual(rank * 20f, ItemEffectBalance.ArmorBreakExtraWearChancePercent(rank));
    }

    [TestCase(50f, 65f)] [TestCase(-50f, 55f)] [TestCase(-200f, 50f)]
    public void SharedHealingRespectsPositiveAndNegativeBonuses(float bonus, float expected)
    {
        var p = Fighter(); p.CurrentHP = 50; p.RunReceivedHealingPercent = bonus;
        p.Heal(10); Assert.AreEqual(expected, p.CurrentHP);
    }

    [Test] public void CritConversionIncludesEquipmentFoodAndRest()
    {
        var p = Fighter(); p.SkillCriticalHitsLevel = 2; p.CritChanceBonusFromItems = 3;
        p.FoodCritChancePoints = 10; p.RestBonusCritChancePoints = 8;
        Assert.AreEqual(82, CombatCriticalRules.ConvertedCritDamageBonus(p));
    }

    [Test] public void ApprovedRogueSkillRanksUseDistinctGrowth()
    {
        for (int rank = 1; rank <= 5; rank++)
        {
            Assert.AreEqual(rank * 3f, CombatCriticalRules.EyeForAnEyeBonus(rank));
            var rogue = Fighter(); rogue.SkillSlipAwayLevel = rank;
            Assert.AreEqual(rank * 2f, CombatEvasionRules.CalculateChancePercent(rogue));
        }
    }

    [Test] public void RoguePoisonScalesFromAverageWeaponDamageWithoutDualWieldDoubling()
    {
        var source = Fighter(true, 10);
        source.Weapons.Add(new WeaponAttackState { DamageMin = 30, DamageMax = 30 });
        Assert.AreEqual(1.4f, CombatManager.RoguePoisonDamagePerStack(source), 0.001f);

        source.SkillPoisonedBladeLevel = 5;
        source.IsStealthed = true;
        var target = Fighter();
        target.RoguePoisonSource = source;
        target.RoguePoisonStacksOnTarget = 10;
        target.RoguePoisonTimer = 3;
        Combat(source, target).Tick(1f);
        Assert.AreEqual(86, target.CurrentHP, "10 зарядов × 1,4 должны округляться один раз до 14 урона.");
    }

    [Test] public void EveryEquipmentItemHasVisibleTagsAndEveryTagHasTooltip()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemData", new[] { "Assets" });
        Assert.Greater(guids.Length, 0);
        foreach (string guid in guids)
        {
            var item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            var tags = ItemMechanicTags.Get(item);
            Assert.That(tags.Count, Is.InRange(1, 5), item != null ? item.itemName : guid);
            foreach (var tag in tags)
            {
                Assert.IsNotEmpty(tag.Label);
                Assert.IsNotEmpty(tag.Tooltip, $"У тега {tag.Label} предмета {item.itemName} нет пояснения.");
            }
        }
    }

    [Test] public void DayAndNightUsesBothHandsAndExactlyOneAttackSource()
    {
        var item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ScriptableObjects/Items/ForgePrototypes/Item_Prototype_DayAndNight.asset");
        Assert.IsNotNull(item); Assert.IsTrue(item.isTwoHanded); Assert.IsFalse(item.isPairedWeapon);
        var character = Asset<CharacterData>(); character.baseHealth = 100;
        var runtime = CombatantFactory.CreatePlayerCombatant(character, 1, equipment: new[] { item });
        Assert.AreEqual(1, runtime.Weapons.Count);
        foreach (string file in new[] { "Item_Cursed_ParanoiaBlades", "Item_Cursed_BetrayerAndAccomplice" })
        {
            var paired = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ScriptableObjects/Items/CursedWeapons/" + file + ".asset");
            Assert.IsTrue(paired.isTwoHanded); Assert.IsTrue(paired.isPairedWeapon);
            Assert.AreEqual(2, CombatantFactory.CreatePlayerCombatant(character, 1, equipment: new[] { paired }).Weapons.Count);
        }
    }

    [Test] public void OddSplitDamageIsNotRoundedUpTwice()
    {
        var p = Fighter(true, 11); p.Weapons[0].PrototypeEffect = WeaponPrototypeEffectId.DayAndNight;
        p.Weapons[0].PrototypePrimaryValue = p.Weapons[0].PrototypeSecondaryValue = 50;
        var e = Fighter(); var cm = Combat(p, e); cm.ResolveScriptedAttack(p, p.Weapons[0]);
        Assert.AreEqual(89, e.CurrentHP);
    }

    [Test] public void MonsterModifiersGetStrongerWithLevel()
    {
        foreach (var modifier in new[] { MonsterModifierType.Fast, MonsterModifierType.Big, MonsterModifierType.Armored, MonsterModifierType.Fierce, MonsterModifierType.ArmorPiercing })
        {
            var low = Fighter(); var high = Fighter(); low.Weapons[0].AttackSpeed = high.Weapons[0].AttackSpeed = 1;
            MonsterModifierCatalog.ApplyToRuntime(low, modifier, 10, 1);
            MonsterModifierCatalog.ApplyToRuntime(high, modifier, 10, 4);
            Assert.Greater(high.MaxHP + high.PhysicalDefenseMax + high.MonsterGuaranteedArmorDamage + high.Weapons[0].DamageMin + high.Weapons[0].AttackSpeed,
                low.MaxHP + low.PhysicalDefenseMax + low.MonsterGuaranteedArmorDamage + low.Weapons[0].DamageMin + low.Weapons[0].AttackSpeed);
        }
    }

    [Test] public void InvulnerableTargetIgnoresDirectAndSpellEaterDamage()
    {
        var e = Fighter(); e.IsInvulnerable = true; e.MagicShieldCurrent = 20;
        Assert.AreEqual(0, DamageCalculator.ApplyDirectDamage(e, 10));
        DamageCalculator.ApplySpellEaterPhysicalDamage(e, 10, 0, out float removed);
        Assert.AreEqual(0, removed); Assert.AreEqual(100, e.CurrentHP); Assert.AreEqual(20, e.MagicShieldCurrent);
    }
}
