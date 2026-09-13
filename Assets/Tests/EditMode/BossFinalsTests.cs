using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class BossFinalsTests
{
    [TearDown]
    public void TearDown()
    {
        foreach (var manager in Object.FindObjectsByType<CombatManager>(FindObjectsSortMode.None))
            Object.DestroyImmediate(manager.gameObject);
    }

    static CombatantRuntime Player() => new CombatantRuntime
    {
        IsPlayer = true, MaxHP = 1000f, CurrentHP = 1000f,
        Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f } }
    };

    static BossKitData Kit(params BossAbilityConfig[] abilities)
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        var phase = new BossPhaseData { phaseName = "Фаза", hpThresholdPercent = 100f };
        phase.abilities.AddRange(abilities);
        kit.phases.Add(phase);
        return kit;
    }

    static CombatantRuntime Boss(BossKitData kit) => new CombatantRuntime
    {
        IsBoss = true, MaxHP = 1000f, CurrentHP = 1000f, SourceFloorNumber = 1,
        BossEncounter = new BossEncounterState(kit),
        Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f } }
    };

    static MonsterData DefeatedBoss()
    {
        var boss = ScriptableObject.CreateInstance<MonsterData>();
        boss.monsterName = "Побеждённый";
        boss.isBoss = true;
        boss.hp = 100f;
        boss.damageMin = boss.damageMax = 10f;
        boss.attackSpeed = 0.01f;
        return boss;
    }

    [Test]
    public void SpawnDefeatedBoss_UsesRunHistoryAndCreatesWeakenedNonBossMinion()
    {
        var summon = new BossAbilityConfig
        {
            displayName = "Призыв органов", effectKind = BossAbilityEffectKind.SpawnDefeatedBoss,
            triggerKind = BossAbilityTriggerKind.Periodic, initialDelaySeconds = 0f,
            cooldownSeconds = 100f, spawnAliveCap = 3, minionDeathRoomTickSlowPercent = 15f
        };
        var manager = new GameObject("Combat").AddComponent<CombatManager>();
        manager.SetDefeatedBossesThisRun(new List<MonsterData> { DefeatedBoss() });
        manager.StartCombat(Player(), new List<CombatantRuntime> { Boss(Kit(summon)) });

        manager.Tick(0.016f);

        var minion = manager.Enemies.Single(enemy => enemy.IsBossMinion);
        Assert.IsFalse(minion.IsBoss, "орган не запускает собственный боссовый цикл");
        Assert.IsNull(minion.BossEncounter);
        Assert.AreEqual(45f, minion.MaxHP, 0.01f, "орган получает 45% HP исходного босса");
        Assert.AreEqual(5.5f, minion.Weapons[0].DamageMax, 0.01f, "орган получает 55% урона исходного босса");
    }

    [Test]
    public void DestroyedOrgan_SlowsFutureRoomTicksImmediately()
    {
        var beat = new BossAbilityConfig
        {
            displayName = "Биение", effectKind = BossAbilityEffectKind.RoomTick,
            triggerKind = BossAbilityTriggerKind.Periodic, initialDelaySeconds = 4f, cooldownSeconds = 4f,
            roomTickPercentOfMaxHp = 1f
        };
        var state = new BossEncounterState(Kit(beat));
        state.SlowRoomTicks(15f);

        state.Tick(4.6f, out var tooEarly);
        Assert.IsNull(tooEarly, "после замедления первое Биение не должно остаться на исходных 4 секундах");
        state.Tick(0.11f, out var fired);
        Assert.AreSame(beat, fired);
    }

    [Test]
    public void RoomTick_GrowsByConfiguredPercentOnEachBeat()
    {
        var beat = new BossAbilityConfig
        {
            effectKind = BossAbilityEffectKind.RoomTick, roomTickPercentOfMaxHp = 7f,
            roomTickGrowthPercentPerTrigger = 10f
        };
        var state = new BossEncounterState(Kit(beat));

        Assert.AreEqual(7f, state.ConsumeRoomTickPercent(beat), 0.001f);
        Assert.AreEqual(7.7f, state.ConsumeRoomTickPercent(beat), 0.001f);
        Assert.AreEqual(8.47f, state.ConsumeRoomTickPercent(beat), 0.001f);
    }

    [Test]
    public void DefeatedBossHistory_IsRunScopedUniqueAndCopiedForFloorRestart()
    {
        var character = ScriptableObject.CreateInstance<CharacterData>();
        var first = DefeatedBoss();
        var second = DefeatedBoss();
        second.monsterName = "Второй";
        var progress = new RunCharacterProgress(character);
        progress.RecordDefeatedBoss(first);
        progress.RecordDefeatedBoss(first);
        var copy = RunStateClone.Clone(progress);
        progress.RecordDefeatedBoss(second);

        Assert.AreEqual(1, copy.DefeatedBosses.Count, "снимок этажа не должен получить победы следующей попытки");
        Assert.AreSame(first, copy.DefeatedBosses[0]);
        Assert.AreEqual(2, progress.DefeatedBosses.Count, "история забега хранит разных боссов");
    }
}
