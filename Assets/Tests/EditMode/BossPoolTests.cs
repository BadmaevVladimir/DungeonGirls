using NUnit.Framework;
using UnityEngine;

// Выбор босса этажа из пула — чистая логика, без сцены и без CombatManager (тот же паттерн, что
// MonsterEncounterBudgetTests): случайность инжектится как System.Func<int,int>, поэтому тесты
// детерминированы и не зависят от UnityEngine.Random.
public class BossPoolTests
{
    static MonsterData MakeBoss(string name)
    {
        var data = ScriptableObject.CreateInstance<MonsterData>();
        data.monsterName = name;
        data.isBoss = true;
        return data;
    }

    static BossPoolData MakePool(params BossPoolEntry[] entries)
    {
        var pool = ScriptableObject.CreateInstance<BossPoolData>();
        pool.entries.AddRange(entries);
        return pool;
    }

    [Test]
    public void Select_FloorInsideEntryRange_ReturnsThatBoss()
    {
        var jailer = MakeBoss("Тюремщик");
        var pool = MakePool(new BossPoolEntry { boss = jailer, minFloor = 4, maxFloor = 6 });

        Assert.AreSame(jailer, BossPoolSelector.Select(pool, floorNumber: 5, fallback: null, pickIndex: _ => 0));
    }

    [Test]
    public void Select_FloorOutsideEveryRange_ReturnsFallback()
    {
        var jailer = MakeBoss("Тюремщик");
        var fallback = MakeBoss("Страж");
        var pool = MakePool(new BossPoolEntry { boss = jailer, minFloor = 4, maxFloor = 6 });

        Assert.AreSame(fallback, BossPoolSelector.Select(pool, floorNumber: 9, fallback, pickIndex: _ => 0));
    }

    [Test]
    public void Select_SeveralCandidates_PicksByInjectedIndexAndPassesCandidateCount()
    {
        var first = MakeBoss("Первый");
        var second = MakeBoss("Второй");
        var offFloor = MakeBoss("Не тот этаж");
        var pool = MakePool(
            new BossPoolEntry { boss = first, minFloor = 1, maxFloor = 3 },
            new BossPoolEntry { boss = offFloor, minFloor = 7, maxFloor = 10 },
            new BossPoolEntry { boss = second, minFloor = 2, maxFloor = 4 });

        int seenCount = -1;
        var picked = BossPoolSelector.Select(pool, floorNumber: 3, fallback: null, pickIndex: count =>
        {
            seenCount = count;
            return 1;
        });

        Assert.AreEqual(2, seenCount, "в пикер должно уйти число подходящих этажу кандидатов, а не размер всего пула");
        Assert.AreSame(second, picked);
    }

    [Test]
    public void Select_NullPoolOrEmptyEntries_ReturnsFallback()
    {
        var fallback = MakeBoss("Страж");

        Assert.AreSame(fallback, BossPoolSelector.Select(null, floorNumber: 1, fallback, pickIndex: _ => 0));
        Assert.AreSame(fallback, BossPoolSelector.Select(MakePool(), floorNumber: 1, fallback, pickIndex: _ => 0));
    }

    [Test]
    public void Select_EntryWithNullBoss_IsSkippedInsteadOfReturningNull()
    {
        var real = MakeBoss("Настоящий");
        var pool = MakePool(
            new BossPoolEntry { boss = null, minFloor = 1, maxFloor = 10 },
            new BossPoolEntry { boss = real, minFloor = 1, maxFloor = 10 });

        Assert.AreSame(real, BossPoolSelector.Select(pool, floorNumber: 1, fallback: null, pickIndex: _ => 0));
    }
}
