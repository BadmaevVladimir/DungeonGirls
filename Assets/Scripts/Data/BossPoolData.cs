using System.Collections.Generic;
using UnityEngine;

// Один вариант босса и диапазон этажей, на которых он может выпасть. Диапазон задаётся здесь, а не
// в MonsterData.minFloorTier: minFloorTier описывает «доступен с этого этажа и выше» для обычных
// монстров, а боссу нужен именно отрезок (Тюремщик — 4–6, а не «с 4-го и до конца игры»).
[System.Serializable]
public class BossPoolEntry
{
    public MonsterData boss;

    [Tooltip("Первый этаж, на котором босс может выпасть (включительно).")]
    public int minFloor = 1;

    [Tooltip("Последний этаж, на котором босс может выпасть (включительно).")]
    public int maxFloor = 10;
}

// Пул боссов забега: какой босс может встретиться на каком этаже. Заменяет единственное поле
// RunFlowController.bossData — см. Docs/superpowers/plans/2026-09-10-boss-roster-roadmap.md.
[CreateAssetMenu(fileName = "NewBossPool", menuName = "DungeonGirls/Boss Pool")]
public class BossPoolData : ScriptableObject
{
    public List<BossPoolEntry> entries = new List<BossPoolEntry>();
}
