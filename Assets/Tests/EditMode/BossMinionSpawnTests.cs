using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// План 8 (Docs/superpowers/plans/2026-09-11-boss-08-minion-spawn.md): спавн миньонов по ходу боя —
// Пожирающий Амальгам и Паучиха-Прародительница. Проверяется через живой CombatManager.Tick, потому
// что весь смысл механики в изменении СОСТАВА сцены, а состав живёт в CombatManager.Enemies.
public class BossMinionSpawnTests
{
    [TearDown]
    public void TearDown()
    {
        foreach (var go in Object.FindObjectsByType<CombatManager>(FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(go.gameObject);
        }
    }

    static CombatManager CreateCombatManager() =>
        new GameObject("TestCombatManager").AddComponent<CombatManager>();

    static CombatantRuntime MakePlayer(float hp = 1000f, float attackSpeed = 0.01f) => new CombatantRuntime
    {
        DisplayName = "Тест-игрок",
        IsPlayer = true,
        MaxHP = hp,
        CurrentHP = hp,
        Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = attackSpeed, DamageType = DamageType.Physical } }
    };

    static MonsterData MakeMinionData(string name = "Паучонок")
    {
        var data = ScriptableObject.CreateInstance<MonsterData>();
        data.monsterName = name;
        data.hp = 10f;
        data.damageMin = 1f;
        data.damageMax = 1f;
        data.attackSpeed = 1f;
        return data;
    }

    static BossKitData MakeKit(params BossPhaseData[] phases)
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.phases.AddRange(phases);
        return kit;
    }

    static BossPhaseData MakePhase(string name, float threshold, params BossAbilityConfig[] abilities)
    {
        var phase = new BossPhaseData { phaseName = name, hpThresholdPercent = threshold };
        phase.abilities.AddRange(abilities);
        return phase;
    }

    static CombatantRuntime MakeBoss(BossKitData kit, float hp = 100f)
    {
        return new CombatantRuntime
        {
            DisplayName = "Тест-босс",
            IsBoss = true,
            MaxHP = hp,
            CurrentHP = hp,
            BossEncounter = new BossEncounterState(kit),
            Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f } }
        };
    }

    static BossAbilityConfig SpawnAbility(MonsterData minion, int count, int cap, float cooldown = 100f)
        => new BossAbilityConfig
        {
            displayName = "Кладка",
            effectKind = BossAbilityEffectKind.SpawnMinions,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = cooldown,
            initialDelaySeconds = 0f,
            telegraphSeconds = 0f,
            spawnMonster = minion,
            spawnCount = count,
            spawnAliveCap = cap
        };

    static int MinionsAlive(CombatManager cm) =>
        cm.Enemies.Count(e => e != null && e.IsBossMinion && e.IsAlive);

    // ---- потолок ----

    [Test]
    public void SpawnMinions_PutsRequestedCountOnTheStage()
    {
        var ability = SpawnAbility(MakeMinionData(), count: 2, cap: 3);
        var boss = MakeBoss(MakeKit(MakePhase("Фаза 1", 100f, ability)));
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new List<CombatantRuntime> { boss });

        Assert.AreEqual(0, MinionsAlive(cm));
        cm.Tick(0.016f);
        Assert.AreEqual(2, MinionsAlive(cm), "за одно срабатывание должно встать ровно spawnCount");
    }

    [Test]
    public void SpawnMinions_NeverExceedsAliveCap_EvenAcrossManyTriggers()
    {
        // кулдаун короткий: за прогон способность срабатывает много раз подряд
        var ability = SpawnAbility(MakeMinionData(), count: 2, cap: 3, cooldown: 0.05f);
        var boss = MakeBoss(MakeKit(MakePhase("Фаза 1", 100f, ability)));
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new List<CombatantRuntime> { boss });

        for (int i = 0; i < 200; i++) cm.Tick(0.016f);

        Assert.AreEqual(3, MinionsAlive(cm),
            "потолок живых миньонов — условие проходимости Вайолет, его нельзя перешагнуть");
    }

    [Test]
    public void SpawnMinions_CapFreesUpWhenAMinionDies()
    {
        var ability = SpawnAbility(MakeMinionData(), count: 1, cap: 2, cooldown: 0.05f);
        var boss = MakeBoss(MakeKit(MakePhase("Фаза 1", 100f, ability)));
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new List<CombatantRuntime> { boss });

        for (int i = 0; i < 50; i++) cm.Tick(0.016f);
        Assert.AreEqual(2, MinionsAlive(cm));

        cm.Enemies.First(e => e.IsBossMinion && e.IsAlive).CurrentHP = 0f;
        for (int i = 0; i < 50; i++) cm.Tick(0.016f);

        Assert.AreEqual(2, MinionsAlive(cm), "освободившееся место должно снова заполняться");
    }

    // ---- поглощение ----

    [Test]
    public void ConsumeMinion_EatsOneAndHealsBossByPercentOfItsOwnMaxHp()
    {
        var minion = MakeMinionData("Слизень");
        var spawn = SpawnAbility(minion, count: 2, cap: 3);
        var eat = new BossAbilityConfig
        {
            displayName = "Поглощение",
            effectKind = BossAbilityEffectKind.ConsumeMinion,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0.5f,
            healPercentOfMaxHp = 10f
        };
        var boss = MakeBoss(MakeKit(MakePhase("Фаза 1", 100f, spawn, eat)), hp: 200f);
        boss.CurrentHP = 100f;
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new List<CombatantRuntime> { boss });

        cm.Tick(0.016f);
        Assert.AreEqual(2, MinionsAlive(cm));

        for (int i = 0; i < 40; i++) cm.Tick(0.016f); // добираемся до initialDelay поглощения

        Assert.AreEqual(1, MinionsAlive(cm), "поглощение должно съедать ровно одного миньона");
        Assert.AreEqual(120f, boss.CurrentHP, 0.01f, "10% от 200 максимума = 20 HP");
    }

    [Test]
    public void ConsumeMinion_WithoutMinions_HealsNothing()
    {
        var eat = new BossAbilityConfig
        {
            displayName = "Поглощение",
            effectKind = BossAbilityEffectKind.ConsumeMinion,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            healPercentOfMaxHp = 10f
        };
        var boss = MakeBoss(MakeKit(MakePhase("Фаза 1", 100f, eat)), hp: 200f);
        boss.CurrentHP = 100f;
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new List<CombatantRuntime> { boss });

        for (int i = 0; i < 10; i++) cm.Tick(0.016f);

        Assert.AreEqual(100f, boss.CurrentHP, 0.01f,
            "без живых миньонов способность не лечит — именно это делает переключение цели осмысленным");
    }

    // ---- уборка ----

    [Test]
    public void BossDeath_TakesItsMinionsWithIt()
    {
        var ability = SpawnAbility(MakeMinionData(), count: 3, cap: 3);
        var boss = MakeBoss(MakeKit(MakePhase("Фаза 1", 100f, ability)));
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new List<CombatantRuntime> { boss });

        cm.Tick(0.016f);
        Assert.AreEqual(3, MinionsAlive(cm));

        boss.CurrentHP = 0f;
        cm.Tick(0.016f);

        Assert.AreEqual(0, MinionsAlive(cm),
            "иначе добитый босс оставляет живую мелочь и победа перестаёт совпадать со смертью босса");
    }

    // ---- «Кокон» ----

    [Test]
    public void Cocoon_ReducesBossDamageTakenWhileAMinionLives_AndOnlyTheBoss()
    {
        var kit = MakeKit(MakePhase("Фаза 1", 100f, SpawnAbility(MakeMinionData(), count: 1, cap: 3)));
        kit.groupDamageReductionPercent = 25f;
        var boss = MakeBoss(kit);
        boss.InBossGroup = true;
        boss.PendingGroupDamageReductionPercent = 25f;

        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new List<CombatantRuntime> { boss });
        cm.Tick(0.016f);

        var minion = cm.Enemies.First(e => e.IsBossMinion);
        Assert.AreEqual(25f, boss.BossGroupDamageReductionPercent, 0.01f,
            "пока жив паучонок, босс получает меньше урона");
        Assert.AreEqual(0f, minion.BossGroupDamageReductionPercent, 0.01f,
            "живучесть паучатам не положена — «Кокон» это свойство босса");

        minion.CurrentHP = 0f;
        cm.Tick(0.016f);

        Assert.AreEqual(0f, boss.BossGroupDamageReductionPercent, 0.01f,
            "последний паучонок умер — «Кокон» спадает");
        Assert.AreEqual(0f, boss.BossSoloDamageBonusPercent, 0.01f,
            "у Паучихи солобонуса нет: смерть миньонов только снимает Кокон, а не усиливает её");
        Assert.AreEqual(0f, boss.BossSoloAttackSpeedBonusPercent, 0.01f);
    }

    [Test]
    public void SpawnedMinion_ScalesToTheSameFloorAsItsBoss()
    {
        var ability = SpawnAbility(MakeMinionData(), count: 1, cap: 3);
        var boss = MakeBoss(MakeKit(MakePhase("Фаза 1", 100f, ability)));
        boss.SourceFloorNumber = 7;
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new List<CombatantRuntime> { boss });

        cm.Tick(0.016f);

        var minion = cm.Enemies.First(e => e.IsBossMinion);
        Assert.AreEqual(7, minion.SourceFloorNumber,
            "миньон на 7 этаже не должен быть собран по первому этажу");
    }

    [Test]
    public void SoloBonus_DoesNotLatchBeforeAnyMinionHasEverExisted()
    {
        // Группа Паучихи на старте состоит из неё одной: союзники приходят спавном. Без защиты
        // «остался один» выдавалось бы на первом же тике, до первой кладки.
        var kit = MakeKit(MakePhase("Фаза 1", 100f,
            SpawnAbility(MakeMinionData(), count: 1, cap: 3, cooldown: 100f)));
        var boss = MakeBoss(kit);
        boss.InBossGroup = true;
        boss.PendingGroupDamageReductionPercent = 25f;
        boss.PendingSoloDamageBonusPercent = 40f;   // намеренно ненулевой: проверяем именно защёлку

        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new List<CombatantRuntime> { boss });

        // первый тик: кладка ещё не отработала на момент проверки группы
        cm.Tick(0.016f);
        Assert.AreEqual(0f, boss.BossSoloDamageBonusPercent, 0.01f,
            "усиление одиночки не может сработать раньше, чем у группы вообще был союзник");
    }
}
