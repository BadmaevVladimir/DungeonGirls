using System.Collections.Generic;
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


// Размеры спрайтов на боевой сцене — чистая логика, поэтому проверяется без UI (2026-09-11).
// Старое правило «босс = 518px» опиралось на «бой с боссом всегда 1 на 1»; групповые боссы это
// допущение сломали, и тест фиксирует новое.
public class BossStageLayoutTests
{
    static CombatantRuntime Boss() => new CombatantRuntime
    {
        IsBoss = true,
        BossEncounter = new BossEncounterState(MakeSinglePhaseKit())
    };

    static CombatantRuntime Anchor() => new CombatantRuntime { IsBossAnchor = true };
    static CombatantRuntime Mob() => new CombatantRuntime();

    static BossKitData MakeSinglePhaseKit()
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.phases.Add(new BossPhaseData { phaseName = "Фаза 1", hpThresholdPercent = 100f });
        return kit;
    }

    [Test]
    public void SoloBoss_KeepsTheBigFrame()
    {
        var boss = Boss();
        var stage = new List<CombatantRuntime> { boss };

        Assert.AreEqual(BossStageLayout.SoloBossSize, BossStageLayout.SpriteSize(boss, stage), 0.01f);
    }

    [Test]
    public void TwoBossEntities_BothShrink_SoThePairReadsAsOneEncounter()
    {
        var first = Boss();
        var second = Boss();
        var stage = new List<CombatantRuntime> { first, second };

        Assert.AreEqual(BossStageLayout.PairedBossSize, BossStageLayout.SpriteSize(first, stage), 0.01f);
        Assert.AreEqual(BossStageLayout.PairedBossSize, BossStageLayout.SpriteSize(second, stage), 0.01f);
        Assert.Less(BossStageLayout.PairedBossSize, BossStageLayout.SoloBossSize);
    }

    [Test]
    public void CandleKeeper_StaysNormalBossSize_BecauseAnchorsCarryNoKit()
    {
        // Свечи не носят кита, поэтому Свечник остаётся «одиночным» боссом обычного размера,
        // а место на сцене занимают мелкие якоря — ровно то поведение, которое просили.
        var keeper = Boss();
        var stage = new List<CombatantRuntime> { keeper, Anchor(), Anchor(), Anchor() };

        Assert.AreEqual(BossStageLayout.SoloBossSize, BossStageLayout.SpriteSize(keeper, stage), 0.01f);
        Assert.AreEqual(BossStageLayout.AnchorSize, BossStageLayout.SpriteSize(stage[1], stage), 0.01f);
    }

    [Test]
    public void AnchorIsSmallerThanARegularMonster_SoItNeverStealsAttentionFromTheBoss()
    {
        Assert.Less(BossStageLayout.AnchorSize, BossStageLayout.RegularEnemySize);
    }

    [Test]
    public void OrdinaryMonster_IsUnaffectedByBossRules()
    {
        var mob = Mob();
        var stage = new List<CombatantRuntime> { Boss(), mob };

        Assert.AreEqual(BossStageLayout.RegularEnemySize, BossStageLayout.SpriteSize(mob, stage), 0.01f);
    }

    [Test]
    public void WholeCandleKeeperStage_FitsWithinTheStageWidthBudget()
    {
        // Ширина ряда врагов ограничена окном: если сумма рамок вылезает за разумный предел,
        // спрайты налезут друг на друга. 1200px — консервативная оценка доступной ширины.
        var keeper = Boss();
        var stage = new List<CombatantRuntime> { keeper, Anchor(), Anchor(), Anchor() };

        float total = 0f;
        foreach (var entity in stage) total += BossStageLayout.SpriteSize(entity, stage);

        Assert.LessOrEqual(total, 1200f, $"суммарная ширина сцены Свечника {total}px — свечи налезут на босса");
    }
}
