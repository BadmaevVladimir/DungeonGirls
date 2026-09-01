using NUnit.Framework;
using UnityEngine;

// Boss framework (минимальный слайс, см. Docs/Design/2026-09-01-floor-boss-system-design.md) —
// покрывает BossEncounterState напрямую (без CombatManager, дешевле/детерминированнее) там, где
// достаточно чистой логики фазы/кулдауна/телеграфа, и через живой CombatManager.Tick там, где нужно
// проверить интеграцию (исполнение способности, смена спрайта, отсутствие регрессий для обычных
// врагов/боссов без bossKit). CombatManager — MonoBehaviour, но Tick(float) явно спроектирован для
// вызова из EditMode-тестов без сцены/плеймода (см. комментарий над CombatManager.Tick).
public class BossEncounterTests
{
    static BossKitData MakeKit(params BossPhaseData[] phases)
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.phases.AddRange(phases);
        return kit;
    }

    static BossPhaseData MakePhase(string name, float hpThresholdPercent, params BossAbilityConfig[] abilities)
    {
        var phase = new BossPhaseData { phaseName = name, hpThresholdPercent = hpThresholdPercent };
        phase.abilities.AddRange(abilities);
        return phase;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var go in Object.FindObjectsByType<CombatManager>(FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(go.gameObject);
        }
    }

    // ---- BossEncounterState (чистая логика, без CombatManager) ----

    [Test]
    public void TryEnterNextPhase_HpAtOrBelowThreshold_TransitionsOnce()
    {
        var kit = MakeKit(MakePhase("Фаза 1", 100f), MakePhase("Фаза 2", 50f));
        var state = new BossEncounterState(kit);

        Assert.AreEqual(0, state.CurrentPhaseIndex);
        Assert.IsFalse(state.TryEnterNextPhase(60f, out _)); // выше порога — фаза не меняется
        Assert.AreEqual(0, state.CurrentPhaseIndex);

        Assert.IsTrue(state.TryEnterNextPhase(50f, out var newPhase));
        Assert.AreEqual(1, state.CurrentPhaseIndex);
        Assert.AreEqual("Фаза 2", newPhase.phaseName);
    }

    [Test]
    public void TryEnterNextPhase_AlreadyInLastPhase_NeverRetriggers()
    {
        var kit = MakeKit(MakePhase("Фаза 1", 100f), MakePhase("Фаза 2", 50f));
        var state = new BossEncounterState(kit);

        Assert.IsTrue(state.TryEnterNextPhase(50f, out _));
        Assert.AreEqual(1, state.CurrentPhaseIndex);

        // HP продолжает падать (или даже "восстанавливается" выше старого порога) — не откатывается
        // и не срабатывает повторно, индекс фазы монотонно растёт.
        Assert.IsFalse(state.TryEnterNextPhase(10f, out _));
        Assert.IsFalse(state.TryEnterNextPhase(90f, out _));
        Assert.AreEqual(1, state.CurrentPhaseIndex);
    }

    [Test]
    public void Tick_TelegraphedAbility_ReportsPendingBeforeExecuting()
    {
        var ability = new BossAbilityConfig
        {
            displayName = "Тестовый замах",
            effectKind = BossAbilityEffectKind.HeavyAttack,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            telegraphSeconds = 2f
        };
        var kit = MakeKit(MakePhase("Фаза 1", 100f, ability));
        var state = new BossEncounterState(kit);

        // Первый Tick тратит свой deltaTime на истечение кулдауна (initialDelaySeconds=0) и ЗАПУСКАЕТ
        // pending-телеграф с полным telegraphSeconds — сам этот deltaTime телеграф ещё не тратит
        // (см. BossEncounterState.BeginOrExecute), поэтому 1с + 1с ниже НЕ равно 2с внутри телеграфа.
        state.Tick(1f, out var executed1);
        Assert.IsNull(executed1, "способность с телеграфом не должна резолвиться мгновенно");
        Assert.IsTrue(state.PendingTelegraph.HasValue);
        Assert.AreEqual("Тестовый замах", state.PendingTelegraph.Value.DisplayName);
        Assert.AreEqual(2f, state.PendingTelegraph.Value.RemainingSeconds, 0.001f);

        state.Tick(1f, out var executedTooEarly);
        Assert.IsNull(executedTooEarly, "1с из 2с телеграфа — ещё рано");
        Assert.AreEqual(1f, state.PendingTelegraph.Value.RemainingSeconds, 0.001f);

        state.Tick(1f, out var executed2);
        Assert.AreSame(ability, executed2, "по истечении telegraphSeconds способность должна резолвиться ровно один раз");
        Assert.IsFalse(state.PendingTelegraph.HasValue, "телеграф должен исчезнуть сразу после резолва");
    }

    // ---- Shield pool (DamageCalculator) ----

    [Test]
    public void ApplyDamage_ShieldPoolAbsorbsBeforeHP_ThenOverflowsToHPOnceDepleted()
    {
        var target = new CombatantRuntime { CurrentHP = 100f, PhysicalDefenseCurrent = 0f, ShieldPoolMax = 30f, ShieldPoolCurrent = 30f };

        var firstHit = DamageCalculator.ApplyDamage(target, 20f, DamageType.Physical);
        Assert.AreEqual(20f, firstHit.ShieldPoolDamageAbsorbed);
        Assert.AreEqual(10f, target.ShieldPoolCurrent);
        Assert.AreEqual(0f, firstHit.DamageToHP, "щит полностью поглотил первый удар — HP не тронут");
        Assert.AreEqual(100f, target.CurrentHP);

        var secondHit = DamageCalculator.ApplyDamage(target, 15f, DamageType.Physical);
        Assert.AreEqual(10f, secondHit.ShieldPoolDamageAbsorbed, "щит поглощает остаток (10), затем истощается");
        Assert.AreEqual(0f, target.ShieldPoolCurrent);
        Assert.AreEqual(5f, secondHit.DamageToHP, "оставшиеся 5 урона идут по HP, как только щит выбит");
        Assert.AreEqual(95f, target.CurrentHP);
    }

    [Test]
    public void ApplyDamage_NoShieldPool_BehavesExactlyAsBeforeShieldFeature()
    {
        var target = new CombatantRuntime { CurrentHP = 100f, PhysicalDefenseCurrent = 0f };

        var result = DamageCalculator.ApplyDamage(target, 40f, DamageType.Physical);

        Assert.AreEqual(0f, result.ShieldPoolDamageAbsorbed);
        Assert.AreEqual(40f, result.DamageToHP);
        Assert.AreEqual(60f, target.CurrentHP);
    }

    // ---- Интеграция через живой CombatManager ----

    static CombatManager CreateCombatManager() => new GameObject("TestCombatManager").AddComponent<CombatManager>();

    // attackSpeed по умолчанию намеренно "почти никогда не бьёт" (интервал 100с) — большинству
    // тестов ниже игрок нужен только как валидная цель/сторона боя, а не как источник урона; тесты,
    // которым нужен реальный урон от игрока, передают свой attackSpeed явно.
    static CombatantRuntime MakePlayer(float hp = 1000f, float weaponDamage = 1f, float attackSpeed = 0.01f) => new CombatantRuntime
    {
        DisplayName = "Тест-игрок",
        IsPlayer = true,
        MaxHP = hp,
        CurrentHP = hp,
        Weapons = { new WeaponAttackState { DamageMin = weaponDamage, DamageMax = weaponDamage, AttackSpeed = attackSpeed, DamageType = DamageType.Physical } }
    };

    [Test]
    public void TickBossEncounters_PhaseTransition_SwapsSpriteAndFiresOnce()
    {
        var spriteA = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
        var spriteB = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
        var kit = MakeKit(
            MakePhase("Фаза 1", 100f),
            MakePhase("Фаза 2", 50f));
        kit.phases[1].phaseSprite = spriteB;

        var boss = new CombatantRuntime
        {
            DisplayName = "Тест-босс",
            IsBoss = true,
            MaxHP = 100f,
            CurrentHP = 100f,
            Sprite = spriteA,
            BossEncounter = new BossEncounterState(kit),
            Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f } }
        };

        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss });

        Assert.AreEqual(spriteA, boss.Sprite);
        boss.CurrentHP = 40f; // ниже порога фазы 2 (50%)
        cm.Tick(0.016f);

        Assert.AreEqual(1, boss.BossEncounter.CurrentPhaseIndex);
        Assert.AreEqual(spriteB, boss.Sprite, "спрайт должен смениться на спрайт новой фазы");

        // Дальнейшие тики не должны откатывать/повторно триггерить переход.
        cm.Tick(0.016f);
        Assert.AreEqual(1, boss.BossEncounter.CurrentPhaseIndex);
    }

    [Test]
    public void TickBossEncounters_HeavyAttackAbility_ExecutesAfterTelegraphAndDamagesPlayer()
    {
        var ability = new BossAbilityConfig
        {
            displayName = "Тестовая тяжёлая атака",
            effectKind = BossAbilityEffectKind.HeavyAttack,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            telegraphSeconds = 1f,
            damageMultiplier = 1f
        };
        var kit = MakeKit(MakePhase("Фаза 1", 100f, ability));
        var boss = new CombatantRuntime
        {
            DisplayName = "Тест-босс",
            IsBoss = true,
            MaxHP = 100f,
            CurrentHP = 100f,
            BossEncounter = new BossEncounterState(kit),
            Weapons = { new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, AttackSpeed = 0.001f, DamageType = DamageType.Physical } }
        };

        var player = MakePlayer(hp: 1000f);
        var cm = CreateCombatManager();
        cm.StartCombat(player, new System.Collections.Generic.List<CombatantRuntime> { boss });

        // Первый Tick тратит свой deltaTime на истечение кулдауна и ЗАПУСКАЕТ pending-телеграф с
        // полным telegraphSeconds(1f) — этот же deltaTime телеграф ещё не тратит (см.
        // BossEncounterState.BeginOrExecute), поэтому резолв ждём отдельным следующим Tick(>=1f).
        cm.Tick(0.5f);
        Assert.IsTrue(boss.BossEncounter.PendingTelegraph.HasValue, "должен быть виден телеграф ДО удара");
        float hpBeforeResolve = player.CurrentHP;

        cm.Tick(1.1f); // >= telegraphSeconds(1f) — способность должна резолвиться в этом Tick
        Assert.IsFalse(boss.BossEncounter.PendingTelegraph.HasValue, "телеграф снят после резолва");
        Assert.Less(player.CurrentHP, hpBeforeResolve, "тяжёлая атака должна была нанести урон игроку");
    }

    [Test]
    public void TickBossEncounters_RegularEnemyWithoutBossEncounter_IsUnaffected()
    {
        var enemy = new CombatantRuntime
        {
            DisplayName = "Обычный враг",
            MaxHP = 50f,
            CurrentHP = 50f,
            Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 1f } }
        };
        Assert.IsNull(enemy.BossEncounter);

        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { enemy });

        Assert.DoesNotThrow(() =>
        {
            for (int i = 0; i < 10; i++) cm.Tick(0.1f);
        });
        Assert.AreEqual(50f, enemy.MaxHP);
    }

    [Test]
    public void CreateMonsterCombatant_BossWithoutBossKit_LeavesBossEncounterNullAndCombatStillWorks()
    {
        var monster = ScriptableObject.CreateInstance<MonsterData>();
        monster.monsterName = "Легаси-босс";
        monster.isBoss = true;
        monster.hp = 100f;
        monster.damageMin = 5f;
        monster.damageMax = 5f;
        monster.attackSpeed = 1f;
        // monster.bossKit сознательно не назначен (null).

        var runtime = CombatantFactory.CreateMonsterCombatant(monster, floorNumber: 1);

        Assert.IsTrue(runtime.IsBoss);
        Assert.IsNull(runtime.BossEncounter);

        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { runtime });

        Assert.DoesNotThrow(() => cm.Tick(0.1f));
        Assert.Greater(runtime.BossHeavyAttackTimer, 0f, "легаси-путь (TickBossHeavyAttacks) должен по-прежнему работать без bossKit");
    }

    [Test]
    public void TickBossEncounters_BossDefeated_EndsCombatNormally()
    {
        var kit = MakeKit(MakePhase("Фаза 1", 100f));
        var boss = new CombatantRuntime
        {
            DisplayName = "Тест-босс",
            IsBoss = true,
            MaxHP = 10f,
            CurrentHP = 10f,
            BossEncounter = new BossEncounterState(kit),
            Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.001f } }
        };
        var player = MakePlayer(hp: 1000f, weaponDamage: 50f, attackSpeed: 10f); // interval 0.1с — быстро добивает 10 HP босса

        var cm = CreateCombatManager();
        cm.StartCombat(player, new System.Collections.Generic.List<CombatantRuntime> { boss });

        for (int i = 0; i < 20 && cm.IsCombatActive; i++)
        {
            cm.Tick(0.1f);
        }

        Assert.IsFalse(cm.IsCombatActive);
        Assert.IsFalse(boss.IsAlive);
        Assert.IsTrue(player.IsAlive);
    }
}
