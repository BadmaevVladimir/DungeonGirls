using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MonsterEncounterBudgetTests
{
    [Test]
    public void GetThreatBudget_ClampsToFloorRange()
    {
        Assert.AreEqual(1, MonsterEncounterBudget.GetThreatBudget(0));
        Assert.AreEqual(DungeonManager.TotalFloors, MonsterEncounterBudget.GetThreatBudget(999));
    }

    [Test]
    public void GetThreatCost_HighTierMonster_ReturnsFive()
    {
        var monster = ScriptableObject.CreateInstance<MonsterData>();
        monster.minFloorTier = 10;

        Assert.AreEqual(5, MonsterEncounterBudget.GetThreatCost(monster));

        Object.DestroyImmediate(monster);
    }

    [Test]
    public void RollAffordableMonster_NoneAffordable_ReturnsNull()
    {
        var monster = ScriptableObject.CreateInstance<MonsterData>();
        monster.minFloorTier = 10; // cost 5

        var result = MonsterEncounterBudget.RollAffordableMonster(new List<MonsterData> { monster }, remainingBudget: 1);

        Assert.IsNull(result);

        Object.DestroyImmediate(monster);
    }

    [Test]
    public void RollAffordableMonster_OneAffordable_ReturnsIt()
    {
        var monster = ScriptableObject.CreateInstance<MonsterData>();
        monster.minFloorTier = 1; // cost 1

        var result = MonsterEncounterBudget.RollAffordableMonster(new List<MonsterData> { monster }, remainingBudget: 1);

        Assert.AreEqual(monster, result);

        Object.DestroyImmediate(monster);
    }

    [Test]
    public void RollMonsterCount_Level2OrBelow_AlwaysReturnsOne()
    {
        Assert.AreEqual(1, MonsterEncounterBudget.RollMonsterCount(1));
        Assert.AreEqual(1, MonsterEncounterBudget.RollMonsterCount(2));
    }

    [Test]
    public void RollMonsterCount_Level6OrAbove_ReturnsBetweenOneAndThree()
    {
        for (int i = 0; i < 20; i++)
        {
            int count = MonsterEncounterBudget.RollMonsterCount(6);
            Assert.GreaterOrEqual(count, 1);
            Assert.LessOrEqual(count, 3);
        }
    }
}
