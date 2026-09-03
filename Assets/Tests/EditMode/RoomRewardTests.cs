using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public class RoomRewardTests
{
    sealed class SequenceRandom : IRewardRandom
    {
        readonly Queue<float> values;
        public SequenceRandom(params float[] values) => this.values = new Queue<float>(values);
        public float Value() => values.Count > 0 ? values.Dequeue() : 0.5f;
        public int Range(int minInclusive, int maxExclusive) => minInclusive;
    }

    GameObject gameObject;
    RewardManager manager;

    [SetUp]
    public void SetUp()
    {
        gameObject = new GameObject("reward-tests");
        manager = gameObject.AddComponent<RewardManager>();
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(gameObject);

    [Test]
    public void RegularCombatChestChance_IsExactlyFiftyPercent()
    {
        Assert.AreEqual(0.5f, RewardManager.RegularCombatChestChance);
        Assert.IsTrue(manager.CalculateRoomReward(1, false, 1, random: new SequenceRandom(0.499f, 0.5f)).HasChest);
        Assert.IsFalse(manager.CalculateRoomReward(1, false, 1, random: new SequenceRandom(0.5f, 0.5f)).HasChest);
    }

    [Test]
    public void BossChest_RemainsGuaranteedAndAtLeastRare()
    {
        var result = manager.CalculateRoomReward(1, true, 1, random: new SequenceRandom(0.99f));
        Assert.IsTrue(result.HasChest);
        Assert.GreaterOrEqual(result.Chest.ItemRarity, ItemTier.Rare);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CurrencyAndIngredients_AreGrantedRegardlessOfChest(bool chest)
    {
        var result = new RoomRewardResult(17,
            new[] { new ResourceAmount(PersistentResourceIds.Grain, 2) }, chest,
            RewardRoomContext.Combat, chest ? new ChestReward() : null);
        var grant = new RoomRewardGrant(result);
        int currency = 0;
        var ingredients = new List<ResourceAmount>();

        Assert.IsTrue(grant.TryApply(value => currency += value, ingredients.Add));
        Assert.AreEqual(17, currency);
        Assert.AreEqual(2, ingredients[0].amount);
        Assert.IsFalse(grant.TryApply(value => currency += value, ingredients.Add));
        Assert.AreEqual(17, currency, "Repeated confirmation must not grant twice.");
    }

    [Test]
    public void CalculatedResult_KeepsSameChestPayloadBetweenStates()
    {
        var result = manager.CalculateRoomReward(1, false, 1, random: new SequenceRandom(0.1f, 0.5f));
        var grant = new RoomRewardGrant(result);
        Assert.AreSame(result, grant.Result);
        Assert.AreSame(result.Chest, grant.Result.Chest);
    }

    [TestCase(0.1f, true)]
    [TestCase(0.9f, false)]
    public void ConfiguredIngredient_IsGrantedInBothChestBranches(float chestRoll, bool expectedChest)
    {
        var config = ScriptableObject.CreateInstance<RoomRewardConfig>();
        config.combatIngredientDropChance = 1f;
        config.combatIngredientDrops.Clear();
        config.combatIngredientDrops.Add(new IngredientDropRule
        {
            resourceId = PersistentResourceIds.HealingHerbs,
            weight = 1f,
            minAmount = 2,
            maxAmount = 2
        });
        manager.SetRoomRewardConfig(config);
        var result = manager.CalculateRoomReward(1, false, 1,
            random: new SequenceRandom(chestRoll, 0.5f, 0f, 0f, 1f));
        Assert.AreEqual(expectedChest, result.HasChest);
        Assert.AreEqual(2, result.Ingredients[0].amount);
        Object.DestroyImmediate(config);
    }


    [Test]
    public void ForgeMaterialAndIngredient_AreGrantedBySameOneShotGrant()
    {
        var result = new RoomRewardResult(0,
            new[] { new ResourceAmount(PersistentResourceIds.RawMeat, 1) }, false,
            RewardRoomContext.Combat, null,
            new[] { new ResourceAmount(PersistentResourceIds.MagicCrystal, 1) });
        var resources = new List<ResourceAmount>();
        var grant = new RoomRewardGrant(result);
        Assert.IsTrue(grant.TryApply(null, resources.Add));
        Assert.AreEqual(2, resources.Count);
        Assert.IsFalse(grant.TryApply(null, resources.Add));
        Assert.AreEqual(2, resources.Count);
    }

    [Test]
    public void LootSummary_HandlesNoIngredientsAndMultipleStacks()
    {
        var rows = new VisualElement();
        LootSummaryPresenter.Populate(rows, new RoomRewardResult(10, null, false, RewardRoomContext.Combat, null));
        Assert.AreEqual(1, rows.childCount);

        LootSummaryPresenter.Populate(rows, new RoomRewardResult(10, new[]
        {
            new ResourceAmount(PersistentResourceIds.Grain, 2),
            new ResourceAmount(PersistentResourceIds.RawMeat, 1),
            new ResourceAmount("", 4),
            new ResourceAmount(PersistentResourceIds.Dairy, 0)
        }, true, RewardRoomContext.Combat, new ChestReward(), new[]
        {
            new ResourceAmount(PersistentResourceIds.TemperedSteel, 1)
        }));
        Assert.AreEqual(5, rows.childCount); // currency + two ingredients + material + chest
    }

    [Test]
    public void ContextTablesUseApprovedTrapAndSpecialWeights()
    {
        var config = ScriptableObject.CreateInstance<RoomRewardConfig>();
        manager.SetRoomRewardConfig(config);
        Assert.AreEqual(PersistentResourceIds.HealingHerbs,
            manager.RollIngredient(RewardRoomContext.Trap, new SequenceRandom(0f)).Value.resourceId);
        Assert.AreEqual(PersistentResourceIds.EtherealSpice,
            manager.RollIngredient(RewardRoomContext.Special, new SequenceRandom(.999f)).Value.resourceId);
        Object.DestroyImmediate(config);
    }

    [Test]
    public void RareRoomHooksKeepApprovedQuantitiesAndConsequences()
    {
        var config = ScriptableObject.CreateInstance<RareRoomConfig>();
        var safe = RareRoomRewardHooks.ResolveMushroomCave(false, config, new SequenceRandom(.99f));
        Assert.AreEqual(2, safe.Mushrooms.amount);
        Assert.IsFalse(safe.ApplyNegativeConsequence);
        var risky = RareRoomRewardHooks.ResolveMushroomCave(true, config, new SequenceRandom(.2f));
        Assert.AreEqual(4, risky.Mushrooms.amount);
        Assert.IsTrue(risky.ApplyNegativeConsequence);
        Assert.AreEqual(1, RareRoomRewardHooks.ResolveHarpyNestFailureCombatVictory(config).amount);
        Object.DestroyImmediate(config);
    }

    [Test]
    public void ApprovedRewardChancesAndWeightsAreConfigured()
    {
        var config = ScriptableObject.CreateInstance<RoomRewardConfig>();
        Assert.AreEqual(.30f, config.combatIngredientDropChance);
        Assert.AreEqual(.45f, config.successfulTrapIngredientDropChance);
        Assert.AreEqual(.60f, config.supportedSpecialIngredientDropChance);
        Assert.AreEqual(100f, RewardManager.TotalWeight(config.combatIngredientDrops));
        Assert.AreEqual(100f, RewardManager.TotalWeight(config.trapIngredientDrops));
        Assert.AreEqual(100f, RewardManager.TotalWeight(config.specialIngredientDrops));
        Assert.AreEqual(100f, RewardManager.TotalWeight(config.bossIngredientDrops));
        Assert.AreEqual(100f, RewardManager.TotalWeight(config.combatForgeMaterialDrops));
        Assert.AreEqual(100f, RewardManager.TotalWeight(config.trapForgeMaterialDrops));
        Assert.AreEqual(100f, RewardManager.TotalWeight(config.bossForgeMaterialDrops));
        Object.DestroyImmediate(config);
    }

    [Test]
    public void ContextIngredientChanceUsesDeterministicRoll()
    {
        var config = ScriptableObject.CreateInstance<RoomRewardConfig>();
        manager.SetRoomRewardConfig(config);
        Assert.IsTrue(manager.RollIngredientReward(RewardRoomContext.Combat,
            new SequenceRandom(.299f, 0f)).HasValue);
        Assert.IsFalse(manager.RollIngredientReward(RewardRoomContext.Combat,
            new SequenceRandom(.30f)).HasValue);
        Assert.IsTrue(manager.RollIngredientReward(RewardRoomContext.Trap,
            new SequenceRandom(.449f, 0f)).HasValue);
        Assert.IsTrue(manager.RollIngredientReward(RewardRoomContext.Special,
            new SequenceRandom(.599f, 0f)).HasValue);
        var seededA = manager.RollIngredientReward(RewardRoomContext.Boss, new SeededRewardRandom(42));
        var seededB = manager.RollIngredientReward(RewardRoomContext.Boss, new SeededRewardRandom(42));
        Assert.AreEqual(seededA.Value.resourceId, seededB.Value.resourceId);
        Object.DestroyImmediate(config);
    }

    [Test]
    public void BossAlwaysProvidesOneIngredientAndOneForgeMaterial()
    {
        var config = ScriptableObject.CreateInstance<RoomRewardConfig>();
        manager.SetRoomRewardConfig(config);
        var result = manager.CalculateRoomReward(2, true, 1, random: new SequenceRandom(.5f));
        Assert.AreEqual(1, result.Ingredients.Count);
        Assert.AreEqual(1, result.Ingredients[0].amount);
        Assert.AreEqual(1, result.ForgeMaterials.Count);
        Assert.AreEqual(1, result.ForgeMaterials[0].amount);
        Object.DestroyImmediate(config);
    }

    [Test]
    public void BossGuaranteesDoNotDependOnChanceBoundary()
    {
        var config = ScriptableObject.CreateInstance<RoomRewardConfig>();
        manager.SetRoomRewardConfig(config);
        Assert.IsTrue(manager.RollIngredientReward(RewardRoomContext.Boss,
            new SequenceRandom(1f)).HasValue);
        Assert.IsTrue(manager.RollForgeMaterial(RewardRoomContext.Boss,
            new SequenceRandom(1f)).HasValue);
        Object.DestroyImmediate(config);
    }

    [Test]
    public void RareContentHonoursFloorMinimumAndPerFloorLimit()
    {
        var config = ScriptableObject.CreateInstance<RareRoomConfig>();
        var state = new RareRoomFloorState();
        Assert.AreEqual(RareRoomContentId.None, RareRoomContentResolver.Resolve(
            RoomType.Special, 1, config, state, new SequenceRandom(0f)));
        Assert.AreEqual(RareRoomContentId.MushroomCave, RareRoomContentResolver.Resolve(
            RoomType.Special, 2, config, state, new SequenceRandom(0f)));
        Assert.AreEqual(RareRoomContentId.None, RareRoomContentResolver.Resolve(
            RoomType.Special, 2, config, state, new SequenceRandom(0f)));

        var harpyState = new RareRoomFloorState();
        Assert.AreEqual(RareRoomContentId.None, RareRoomContentResolver.Resolve(
            RoomType.Trap, 1, config, harpyState, new SequenceRandom(0f)));
        Assert.AreEqual(RareRoomContentId.HarpyNest, RareRoomContentResolver.Resolve(
            RoomType.Trap, 2, config, harpyState, new SequenceRandom(0f)));
        Assert.AreEqual(RareRoomContentId.None, RareRoomContentResolver.Resolve(
            RoomType.Trap, 2, config, harpyState, new SequenceRandom(0f)));

        var forgeState = new RareRoomFloorState();
        var mapNode = new FloorMapNode { RoomType = RoomType.Special };
        Assert.AreEqual(RareRoomContentId.None, RareRoomContentResolver.Resolve(
            mapNode.RoomType, 2, config, forgeState, new SequenceRandom(.11f)));
        Assert.AreEqual(RareRoomContentId.AbandonedForge, RareRoomContentResolver.Resolve(
            mapNode.RoomType, 3, config, forgeState, new SequenceRandom(.11f)));
        Assert.AreEqual(RoomType.Special, mapNode.RoomType, "Rare content must not alter map room type.");
        Object.DestroyImmediate(config);
    }
}
