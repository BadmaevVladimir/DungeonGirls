using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// Таверна ур.5 (G04/R07, решение D03b): случайный бонус после привала на 3 комнаты в ОТДЕЛЬНОМ
// слоте, складывающийся с эффектом блюда.
public class RestBonusTests
{
    sealed class FixedRandom : IRewardRandom
    {
        readonly int index;
        public FixedRandom(int index) => this.index = index;
        public float Value() => 0f;
        public int Range(int minInclusive, int maxExclusive) => minInclusive + index;
    }

    static CombatantRuntime Runtime() => new CombatantRuntime
    {
        IsPlayer = true,
        MaxHP = 100f,
        CurrentHP = 100f,
        Weapons = new List<WeaponAttackState>
        {
            new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, AttackSpeed = 1f, DamageType = DamageType.Physical }
        }
    };

    [Test]
    public void TavernLevelFive_UnlocksRestBonus()
    {
        for (int level = 0; level <= 4; level++)
            Assert.IsFalse(BuildingCatalog.TavernGrantsRestBonus(level), $"Уровень {level} бонуса привала не даёт.");
        Assert.IsTrue(BuildingCatalog.TavernGrantsRestBonus(5));
    }

    [Test]
    public void EveryPooledBonus_AppliesItsOwnChannel()
    {
        for (int i = 0; i < RestBonusCatalog.All.Length; i++)
        {
            var runtime = Runtime();
            var bonus = RestBonusCatalog.Roll(new FixedRandom(i));
            var slot = new ActiveRestBonus();
            slot.Activate(bonus, runtime);

            float applied = bonus.Id switch
            {
                RestBonusId.Damage => runtime.RestBonusDamagePercent,
                RestBonusId.AttackSpeed => runtime.RestBonusAttackSpeedPercent,
                RestBonusId.ReceivedHealing => runtime.RestBonusReceivedHealingPercent,
                RestBonusId.CriticalChance => runtime.RestBonusCritChancePoints,
                _ => float.NaN
            };
            Assert.AreEqual(bonus.Value, applied, $"Бонус {bonus.Id} не применился.");
        }
    }

    // Главное требование D03b: бонус привала и блюдо не вытесняют друг друга.
    [Test]
    public void RestBonus_StacksWithFoodAndSurvivesFoodExpiry()
    {
        var runtime = Runtime();
        var slot = new ActiveRestBonus();
        slot.Activate(RestBonusCatalog.Find(RestBonusId.Damage), runtime);
        runtime.FoodDamagePercent = 20f; // как если бы блюдо выставило свой канал

        Assert.AreEqual(35f, runtime.TotalDamageBonusPercent, "Блюдо и бонус привала обязаны складываться.");

        runtime.FoodDamagePercent = 0f; // блюдо истекло и обнулило СВОЁ поле
        Assert.AreEqual(15f, runtime.TotalDamageBonusPercent,
            "Истечение блюда не должно стирать бонус привала — у них разные слоты.");
    }

    [Test]
    public void ExpiringRestBonus_LeavesFoodUntouched()
    {
        var runtime = Runtime();
        runtime.FoodDamagePercent = 20f;
        var slot = new ActiveRestBonus();
        slot.Activate(RestBonusCatalog.Find(RestBonusId.Damage), runtime);

        for (int room = 0; room < RestBonusCatalog.DurationRooms; room++) slot.CompleteRoom();

        Assert.IsFalse(slot.IsActive);
        Assert.AreEqual(0f, runtime.RestBonusDamagePercent);
        Assert.AreEqual(20f, runtime.FoodDamagePercent, "Бонус привала не владеет полем блюда.");
    }

    [Test]
    public void RestBonus_LastsExactlyThreeRooms()
    {
        var runtime = Runtime();
        var slot = new ActiveRestBonus();
        slot.Activate(RestBonusCatalog.Find(RestBonusId.CriticalChance), runtime);

        slot.CompleteRoom();
        slot.CompleteRoom();
        Assert.IsTrue(slot.IsActive, "После двух комнат бонус ещё держится.");
        Assert.AreEqual(8f, runtime.RestBonusCritChancePoints);

        slot.CompleteRoom();
        Assert.IsFalse(slot.IsActive);
        Assert.AreEqual(0f, runtime.RestBonusCritChancePoints);
    }

    [Test]
    public void NewRestBonus_ReplacesPreviousOneWithoutLeakingIt()
    {
        var runtime = Runtime();
        var slot = new ActiveRestBonus();
        slot.Activate(RestBonusCatalog.Find(RestBonusId.Damage), runtime);
        slot.Activate(RestBonusCatalog.Find(RestBonusId.AttackSpeed), runtime);

        Assert.AreEqual(0f, runtime.RestBonusDamagePercent, "Прежний бонус обязан сняться, а не накопиться.");
        Assert.AreEqual(15f, runtime.RestBonusAttackSpeedPercent);
        Assert.AreEqual(RestBonusCatalog.DurationRooms, slot.RemainingRooms, "Новый привал обновляет длительность.");
    }

    [Test]
    public void Rebinding_AfterStatsRebuild_DoesNotDoubleApply()
    {
        // RefreshCombatStats пересобирает CombatantRuntime и переподвязывает модификаторы.
        var first = Runtime();
        var slot = new ActiveRestBonus();
        slot.Activate(RestBonusCatalog.Find(RestBonusId.Damage), first);

        var rebuilt = Runtime();
        slot.Bind(rebuilt);

        Assert.AreEqual(15f, rebuilt.RestBonusDamagePercent);
        Assert.AreEqual(0f, first.RestBonusDamagePercent, "Старый runtime обязан быть отвязан.");
    }

    [Test]
    public void AttackSpeedAndHealing_ReachTheirConsumptionPoints()
    {
        var runtime = Runtime();
        float baseInterval = runtime.GetEffectiveAttackInterval(runtime.Weapons[0]);
        var slot = new ActiveRestBonus();
        slot.Activate(RestBonusCatalog.Find(RestBonusId.AttackSpeed), runtime);
        Assert.Less(runtime.GetEffectiveAttackInterval(runtime.Weapons[0]), baseInterval,
            "Бонус скорости атаки обязан доходить до интервала атаки.");

        var healed = Runtime();
        healed.CurrentHP = 50f;
        var healSlot = new ActiveRestBonus();
        healSlot.Activate(RestBonusCatalog.Find(RestBonusId.ReceivedHealing), healed);
        Assert.AreEqual(12.5f, healed.Heal(10f), 0.01f, "Получаемое лечение обязано учитывать бонус привала.");
    }

    [Test]
    public void AttestationSnapshot_IgnoresRestBonus()
    {
        var runtime = Runtime();
        var slot = new ActiveRestBonus();
        slot.Activate(RestBonusCatalog.Find(RestBonusId.Damage), runtime);

        var fresh = VeteranBuildSnapshot.CaptureTransient("test", runtime).CreateFreshRuntime();

        Assert.AreEqual(0f, fresh.RestBonusDamagePercent,
            "Аттестация оценивает сборку, а не временный бонус привала.");
    }
}
