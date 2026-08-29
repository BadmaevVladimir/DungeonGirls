using System.Collections.Generic;
using UnityEngine;

// Ограничивает опасность обычной комнаты независимо от числа противников.
// Число существ по-прежнему задаётся текущими порогами персонажа; бюджет не
// даёт нескольким поздним элитам появиться в одной комнате.
public static class MonsterEncounterBudget
{
    public static int GetThreatBudget(int floorNumber) => Mathf.Clamp(floorNumber, 1, DungeonManager.TotalFloors);

    public static int GetThreatCost(MonsterData monster)
    {
        int minFloor = monster != null ? Mathf.Max(monster.minFloorTier, 1) : 1;
        if (minFloor >= 10) return 5;
        if (minFloor >= 7) return 4;
        if (minFloor >= 4) return 3;
        if (minFloor >= 2) return 2;
        return 1;
    }

    public static MonsterData RollAffordableMonster(List<MonsterData> eligibleMonsters, int remainingBudget)
    {
        var affordable = eligibleMonsters.FindAll(monster => monster != null && GetThreatCost(monster) <= remainingBudget);
        return affordable.Count > 0 ? affordable[Random.Range(0, affordable.Count)] : null;
    }
}
