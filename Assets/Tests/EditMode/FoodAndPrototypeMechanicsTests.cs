using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class FoodAndPrototypeMechanicsTests
{
    readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (var value in created) Object.DestroyImmediate(value);
    }

    FoodRecipeData Food(FoodEffectType type, float primary, float secondary = 0f,
        float chance = 0f, float threshold = 0f)
    {
        var recipe = ScriptableObject.CreateInstance<FoodRecipeData>();
        created.Add(recipe);
        recipe.resultFoodId = "food_test_" + type;
        recipe.durationRooms = 3;
        recipe.effect = new FoodEffectConfig { effectType = type, primaryValue = primary,
            secondaryValue = secondary, procChance = chance, thresholdPercent = threshold };
        return recipe;
    }

    static CombatantRuntime Runtime(float hp = 100f) => new CombatantRuntime
    {
        MaxHP = hp, CurrentHP = hp, PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f,
        Weapons = new List<WeaponAttackState> { new WeaponAttackState { AttackSpeed = 1f } }
    };

    [Test]
    public void FoodLastsExactlyThreeCompletedRooms_AndReplacementCleansOldEffect()
    {
        var runtime = Runtime();
        var buff = new ActiveFoodBuff();
        buff.Activate(Food(FoodEffectType.MaxHp, 8f), runtime);
        Assert.AreEqual(108f, runtime.MaxHP);
        Assert.AreEqual(3, buff.RemainingRooms, "Rest itself must not decrement duration.");
        buff.Activate(Food(FoodEffectType.AttackSpeed, 5f), runtime);
        Assert.AreEqual(100f, runtime.MaxHP);
        Assert.AreEqual(5f, runtime.FoodAttackSpeedPercent);
        buff.CompleteRoom(runtime, null);
        buff.CompleteRoom(runtime, null);
        Assert.IsTrue(buff.IsActive);
        buff.CompleteRoom(runtime, null);
        Assert.IsFalse(buff.IsActive);
        Assert.AreEqual(0f, runtime.FoodAttackSpeedPercent);
    }

    [Test]
    public void KnightPorridgeBarrierPersistsDamageAndIsRemovedAtExpiry()
    {
        var runtime = Runtime();
        var buff = new ActiveFoodBuff();
        buff.Activate(Food(FoodEffectType.BarrierAfterRest, 10f), runtime);
        Assert.AreEqual(10f, runtime.ShieldPoolCurrent);
        DamageCalculator.ApplyDamage(runtime, 6f, DamageType.Magical);
        Assert.AreEqual(4f, runtime.ShieldPoolCurrent);
        buff.CompleteRoom(runtime, null);
        Assert.AreEqual(4f, runtime.ShieldPoolCurrent, "Barrier must not refresh between rooms/combat.");
        buff.CompleteRoom(runtime, null);
        buff.CompleteRoom(runtime, null);
        Assert.AreEqual(0f, runtime.ShieldPoolCurrent);
    }

    [Test]
    public void HerbalBrothHealsAtMostThreeTimes()
    {
        var runtime = Runtime(); runtime.CurrentHP = 50f;
        var buff = new ActiveFoodBuff();
        buff.Activate(Food(FoodEffectType.HealAfterRoom, 3f), runtime);
        buff.CompleteRoom(runtime, null); buff.CompleteRoom(runtime, null); buff.CompleteRoom(runtime, null);
        Assert.AreEqual(59f, runtime.CurrentHP);
        Assert.IsFalse(buff.IsActive, "The third proc also expires the three-room buff.");
    }

    [Test]
    public void HealingCasseroleTriggersOncePerRoomAndResetsNextRoom()
    {
        var runtime = Runtime();
        var buff = new ActiveFoodBuff();
        buff.Activate(Food(FoodEffectType.LowHealthHeal, 8f, threshold: 30f), runtime);
        runtime.CurrentHP = 29f;
        Assert.IsTrue(buff.TryLowHealthHeal(runtime));
        runtime.CurrentHP = 20f;
        Assert.IsFalse(buff.TryLowHealthHeal(runtime));
        buff.BeginRoom();
        Assert.IsTrue(buff.TryLowHealthHeal(runtime));
    }

    [Test]
    public void EtherealSoupBlocksOnlyFirstNegativeStatus()
    {
        var runtime = Runtime();
        var buff = new ActiveFoodBuff();
        buff.Activate(Food(FoodEffectType.BlockFirstNegativeStatus, 0f), runtime);
        Assert.IsTrue(runtime.TryBlockNegativeStatus());
        Assert.IsFalse(runtime.TryBlockNegativeStatus());
        Assert.IsTrue(buff.IsActive);
    }

    [Test]
    public void ExplorerStewMakesAtMostOneRollPerCompletedRoom()
    {
        var runtime = Runtime();
        var buff = new ActiveFoodBuff();
        buff.Activate(Food(FoodEffectType.BonusIngredientAfterRoom, 0f, chance: .25f), runtime);
        Assert.IsTrue(buff.CompleteRoom(runtime, new FixedRewardRandom(0.24f)));
        Assert.AreEqual(2, buff.RemainingRooms);
    }

    [Test]
    public void ResonanceCountsUniqueStatusesAndCapsAtFour()
    {
        var runtime = Runtime();
        runtime.ActiveDebuffs.Add(new ActiveDebuff { Id = "a", IsBuff = true });
        runtime.ActiveDebuffs.Add(new ActiveDebuff { Id = "a", IsBuff = true });
        runtime.ActiveDebuffs.Add(new ActiveDebuff { Id = "b", IsBuff = true });
        runtime.ActiveDebuffs.Add(new ActiveDebuff { Id = "n", IsBuff = false });
        var weapon = new WeaponAttackState { PrototypePrimaryValue = 5f,
            PrototypeSecondaryValue = 5f, PrototypeMaxStacks = 4 };
        Assert.AreEqual(1.10f, PrototypeWeaponRules.ResonanceDamageMultiplier(runtime, weapon), .001f);
        Assert.AreEqual(5f, PrototypeWeaponRules.ResonanceAttackSpeedPercent(runtime, weapon));
        for (int i = 0; i < 6; i++)
        {
            runtime.ActiveDebuffs.Add(new ActiveDebuff { Id = "p" + i, IsBuff = true });
            runtime.ActiveDebuffs.Add(new ActiveDebuff { Id = "d" + i, IsBuff = false });
        }
        Assert.AreEqual(1.20f, PrototypeWeaponRules.ResonanceDamageMultiplier(runtime, weapon), .001f);
        Assert.AreEqual(20f, PrototypeWeaponRules.ResonanceAttackSpeedPercent(runtime, weapon));
    }

    [Test]
    public void PrototypeMathCoversShieldOverkillPendulumLightningAndSplit()
    {
        Assert.AreEqual(3f, PrototypeWeaponRules.ActualShieldRemoved(3f, 0f));
        Assert.AreEqual(100f, PrototypeWeaponRules.PendulumBonusPercent(8f, 20f, 100f));
        var weapon = new WeaponAttackState { PrototypeMaxStacks = 3 };
        Assert.IsFalse(PrototypeWeaponRules.AdvanceLightningCounter(weapon));
        Assert.IsFalse(PrototypeWeaponRules.AdvanceLightningCounter(weapon));
        Assert.IsTrue(PrototypeWeaponRules.AdvanceLightningCounter(weapon));
        Assert.AreEqual(0, weapon.PrototypeCounter);
        var combined = PrototypeWeaponRules.Combine(
            new DamageCalculator.DamageResult { DamageToHP = 4f },
            new DamageCalculator.DamageResult { DamageToHP = 6f });
        Assert.AreEqual(10f, combined.DamageToHP);
    }

    [Test]
    public void LastArgumentConvertsOnlyPositiveSpeedAndLeavesActualSpeedUnchanged()
    {
        var runtime = Runtime();
        runtime.ItemAttackSpeedBonusPercent = 25f;
        runtime.ActiveDebuffs.Add(new ActiveDebuff { Id = "slow", AttackSpeedMultiplier = .8f });
        var weapon = runtime.Weapons[0];
        weapon.PrototypeEffect = WeaponPrototypeEffectId.LastArgumentConversion;
        weapon.PrototypePrimaryValue = 1f;
        Assert.AreEqual(.8f, runtime.GetEffectiveAttackSpeed(weapon), .001f);
        Assert.AreEqual(25f, runtime.GetPositiveAttackSpeedBonusPercent(), .001f);
        Assert.AreEqual(1.25f, PrototypeWeaponRules.LastArgumentDamageMultiplier(runtime, weapon), .001f);
    }

    [Test]
    public void MushroomPoisonPenalizesHealingForExactlyThreeFollowingRooms()
    {
        var config = ScriptableObject.CreateInstance<RareRoomConfig>(); created.Add(config);
        var runtime = Runtime(); runtime.CurrentHP = 50f;
        var debuff = new ActiveRunRoomDebuff();
        debuff.ApplyMushroomPoison(config, runtime);
        Assert.AreEqual(9f, runtime.Heal(10f));
        Assert.AreEqual(3, debuff.RemainingRooms);
        debuff.CompleteRoom(); // applying Mushroom Cave itself
        Assert.AreEqual(3, debuff.RemainingRooms);
        debuff.CompleteRoom(); debuff.CompleteRoom();
        Assert.IsTrue(debuff.IsActive);
        debuff.CompleteRoom();
        Assert.IsFalse(debuff.IsActive);
        runtime.CurrentHP = 50f;
        Assert.AreEqual(10f, runtime.Heal(10f));
    }

    [Test]
    public void CombatCleanupKeepsFoodButResetsPrototypeCombatState()
    {
        var food = Food(FoodEffectType.AttackSpeed, 5f);
        var player = Runtime(); player.IsPlayer = true;
        var buff = new ActiveFoodBuff(); buff.Activate(food, player);
        var enemy = Runtime();
        var host = new GameObject("combat"); created.Add(host);
        var manager = host.AddComponent<CombatManager>();
        manager.StartCombat(player, new List<CombatantRuntime> { enemy });
        player.Weapons[0].PrototypeAccumulatedDamage = 9f;
        player.Weapons[0].PrototypeCounter = 2;
        manager.EndCombat();
        Assert.IsTrue(buff.IsActive);
        Assert.AreEqual(5f, player.FoodAttackSpeedPercent);
        Assert.AreEqual(0f, player.Weapons[0].PrototypeAccumulatedDamage);
        Assert.AreEqual(0, player.Weapons[0].PrototypeCounter);
    }

    [Test]
    public void SpellEaterCountsActualDestroyedMagicShieldAndUsesBonusOnLaterAttack()
    {
        var player = Runtime(); player.IsPlayer = true;
        var weapon = player.Weapons[0];
        weapon.PrototypeEffect = WeaponPrototypeEffectId.SpellEater;
        weapon.DamageType = DamageType.Physical;
        weapon.DamageMin = weapon.DamageMax = 5f;
        var enemy = Runtime();
        enemy.PhysicalDefenseCurrent = enemy.PhysicalDefenseMax = 0f;
        enemy.MagicShieldCurrent = enemy.MagicShieldMax = 3f;
        var manager = NewCombat(player, enemy);
        InvokeAttack(manager, player, weapon);
        Assert.AreEqual(3f, weapon.PrototypeAccumulatedDamage);
        Assert.AreEqual(98f, enemy.CurrentHP);
        InvokeAttack(manager, player, weapon);
        Assert.AreEqual(90f, enemy.CurrentHP);
    }

    [Test]
    public void LightningSpearAddsNonRecursiveMagicHitOnThirdSuccessfulAttack()
    {
        var player = Runtime(); player.IsPlayer = true;
        var weapon = player.Weapons[0];
        weapon.PrototypeEffect = WeaponPrototypeEffectId.LightningSpear;
        weapon.PrototypePrimaryValue = 50f;
        weapon.PrototypeMaxStacks = 3;
        weapon.DamageType = DamageType.Physical;
        weapon.DamageMin = weapon.DamageMax = 10f;
        var enemy = Runtime(); enemy.PhysicalDefenseCurrent = enemy.PhysicalDefenseMax = 0f;
        var manager = NewCombat(player, enemy);
        InvokeAttack(manager, player, weapon);
        InvokeAttack(manager, player, weapon);
        Assert.AreEqual(80f, enemy.CurrentHP);
        InvokeAttack(manager, player, weapon);
        Assert.AreEqual(65f, enemy.CurrentHP);
        Assert.AreEqual(0, weapon.PrototypeCounter);
    }

    [Test]
    public void DayAndNightSplitsOneAttackWithoutDoublingTotalDamage()
    {
        var player = Runtime(); player.IsPlayer = true;
        var weapon = player.Weapons[0];
        weapon.PrototypeEffect = WeaponPrototypeEffectId.DayAndNight;
        weapon.PrototypePrimaryValue = weapon.PrototypeSecondaryValue = 50f;
        weapon.DamageMin = weapon.DamageMax = 10f;
        var enemy = Runtime();
        enemy.PhysicalDefenseCurrent = enemy.PhysicalDefenseMax = 0f;
        enemy.MagicShieldCurrent = enemy.MagicShieldMax = 5f;
        var manager = NewCombat(player, enemy);
        InvokeAttack(manager, player, weapon);
        Assert.AreEqual(95f, enemy.CurrentHP);
        Assert.AreEqual(0f, enemy.MagicShieldCurrent);
    }

    [Test]
    public void PendulumUsesFullSecondsCapsAndResetsAfterAttack()
    {
        var player = Runtime(); player.IsPlayer = true;
        var weapon = player.Weapons[0];
        weapon.PrototypeEffect = WeaponPrototypeEffectId.Pendulum;
        weapon.PrototypePrimaryValue = 20f;
        weapon.PrototypeSecondaryValue = 100f;
        weapon.DamageMin = weapon.DamageMax = 10f;
        weapon.SecondsSinceLastAttack = 2.9f;
        var enemy = Runtime(); enemy.PhysicalDefenseCurrent = enemy.PhysicalDefenseMax = 0f;
        var manager = NewCombat(player, enemy);
        weapon.SecondsSinceLastAttack = 2.9f; // StartCombat resets combat-local state.
        InvokeAttack(manager, player, weapon);
        Assert.AreEqual(86f, enemy.CurrentHP);
        Assert.AreEqual(0f, weapon.SecondsSinceLastAttack);
    }

    CombatManager NewCombat(CombatantRuntime player, CombatantRuntime enemy)
    {
        var host = new GameObject("prototype-combat"); created.Add(host);
        var manager = host.AddComponent<CombatManager>();
        manager.StartCombat(player, new List<CombatantRuntime> { enemy });
        return manager;
    }

    static void InvokeAttack(CombatManager manager, CombatantRuntime player, WeaponAttackState weapon)
    {
        typeof(CombatManager).GetMethod("ResolveAttack", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, new object[] { player, weapon, 1f, true });
    }

    sealed class FixedRewardRandom : IRewardRandom
    {
        readonly float value;
        public FixedRewardRandom(float value) => this.value = value;
        public float Value() => value;
        public int Range(int minInclusive, int maxExclusive) => minInclusive;
    }
}
