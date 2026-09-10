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

    // ---- DisruptSkills (Тюремщик, 2026-09-10) ----

    static ActiveSkillData MakeCooldownSkill(float cooldownSeconds)
    {
        var data = ScriptableObject.CreateInstance<ActiveSkillData>();
        data.skillName = "Тестовый навык";
        // В SkillId нет отдельного значения для «3 быстрых атак» — Skill_ThreeQuickStrikes.asset тоже
        // хранит skillId: 0. Для этих тестов id не важен: ветки Berserk/SmokeBomb в CombatManager
        // диспатчатся по skillId, а нам нужен нейтральный слот.
        data.skillId = SkillId.None;
        data.skillType = ActiveSkillType.Cooldown;
        data.cooldownSeconds = cooldownSeconds;
        return data;
    }

    static CombatantRuntime MakeDisruptBoss(BossAbilityConfig ability) => new CombatantRuntime
    {
        DisplayName = "Тест-Тюремщик",
        IsBoss = true,
        MaxHP = 100f,
        CurrentHP = 100f,
        BossEncounter = new BossEncounterState(MakeKit(MakePhase("Фаза 1", 100f, ability))),
        Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.001f, DamageType = DamageType.Physical } }
    };

    [Test]
    public void DisruptSkills_AfterTelegraph_PutsPlayerCooldownSkillOutOfReach()
    {
        var ability = new BossAbilityConfig
        {
            displayName = "Кандалы",
            effectKind = BossAbilityEffectKind.DisruptSkills,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            telegraphSeconds = 1f,
            disruptSeconds = 5f
        };
        var boss = MakeDisruptBoss(ability);
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss });
        cm.ConfigureActiveSkills(new[]
        {
            new ActiveSkillConfigEntry(MakeCooldownSkill(4f), hitCount: 3, damageMultiplierPerHit: 1f, autoMode: false)
        });

        Assert.IsTrue(cm.IsSkillReady(0), "до срабатывания способности навык доступен");

        cm.Tick(0.016f); // запускает pending-телеграф
        Assert.IsTrue(cm.IsSkillReady(0), "пока идёт телеграф, навык ещё доступен — это и есть окно,"+
            " в которое игрок должен успеть его потратить");

        cm.Tick(1.2f);   // телеграф истёк — способность резолвится

        Assert.IsFalse(cm.IsSkillReady(0), "после «Кандалов» навык должен быть недоступен");
        // Блокировка добавляет disruptSeconds к кулдауну, но тик ТОГО ЖЕ кадра списывает свою
        // дельту, поэтому остаток — 5 − 1.2, а не ровно 5. Проверяем точное значение, чтобы тест
        // ломался при изменении этой семантики, а не молча проходил на любом положительном числе.
        Assert.AreEqual(3.8f, cm.SkillCooldownRemaining(0), 0.05f,
            "блокировка добавляет свои секунды к кулдауну навыка");
    }

    [Test]
    public void DisruptSkills_LongerThanCap_IsClampedToMaxBossSkillDisruptSeconds()
    {
        var ability = new BossAbilityConfig
        {
            displayName = "Кандалы навсегда",
            effectKind = BossAbilityEffectKind.DisruptSkills,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            telegraphSeconds = 0f,
            disruptSeconds = 999f
        };
        var boss = MakeDisruptBoss(ability);
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss });
        cm.ConfigureActiveSkills(new[]
        {
            new ActiveSkillConfigEntry(MakeCooldownSkill(0f), hitCount: 3, damageMultiplierPerHit: 1f, autoMode: false)
        });

        cm.Tick(0.016f);

        Assert.LessOrEqual(cm.SkillCooldownRemaining(0), CombatManager.MaxBossSkillDisruptSeconds,
            "правило честности: блокировка навыка не длиннее потолка, даже если в ассете указано больше");
    }

    [Test]
    public void DisruptSkills_WithNoConfiguredSkills_DoesNotThrow()
    {
        var ability = new BossAbilityConfig
        {
            displayName = "Кандалы",
            effectKind = BossAbilityEffectKind.DisruptSkills,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            telegraphSeconds = 0f,
            disruptSeconds = 5f
        };
        var boss = MakeDisruptBoss(ability);
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss });
        // ConfigureActiveSkills намеренно НЕ вызывается: у персонажа без изученного активного навыка
        // список пуст, и способность босса не должна на этом падать.

        Assert.DoesNotThrow(() => cm.Tick(0.016f));
    }

    // ---- Циклические способности и окно уязвимости (Часовой Титан, 2026-09-10) ----

    static BossAbilityConfig MakeCycleStep(string name, float cooldownSeconds)
    {
        return new BossAbilityConfig
        {
            displayName = name,
            effectKind = BossAbilityEffectKind.HeavyAttack,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = cooldownSeconds,
            initialDelaySeconds = 0f,
            telegraphSeconds = 0f,
            damageMultiplier = 1f
        };
    }

    static BossPhaseData MakeCyclePhase(params BossAbilityConfig[] abilities)
    {
        var phase = MakePhase("Цикл", 100f, abilities);
        phase.cycleAbilities = true;
        return phase;
    }

    [Test]
    public void CyclicPhase_FiresAbilitiesInStrictOrderAndWrapsAround()
    {
        var first = MakeCycleStep("Первая шестерня", 1f);
        var second = MakeCycleStep("Вторая шестерня", 1f);
        var third = MakeCycleStep("Третья шестерня", 1f);
        var state = new BossEncounterState(MakeKit(MakeCyclePhase(first, second, third)));

        var fired = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 400 && fired.Count < 5; i++)
        {
            state.Tick(0.1f, out var executed);
            if (executed != null) fired.Add(executed.displayName);
        }

        // Порядок жёсткий и повторяется — именно на это игрок и опирается, заучивая паттерн.
        CollectionAssert.AreEqual(
            new[] { "Первая шестерня", "Вторая шестерня", "Третья шестерня", "Первая шестерня", "Вторая шестерня" },
            fired);
    }

    [Test]
    public void CyclicPhase_CooldownOfExecutedStepIsTheGapBeforeTheNextStep()
    {
        // У первого шага пауза 2с, у второго 5с. После первого шага следующий обязан ждать 2с
        // (кулдаун СРАБОТАВШЕГО), а не 5с (свой собственный) — иначе цикл читался бы задом наперёд.
        var first = MakeCycleStep("Первая", 2f);
        var second = MakeCycleStep("Вторая", 5f);
        var state = new BossEncounterState(MakeKit(MakeCyclePhase(first, second)));

        state.Tick(0.1f, out var opening);
        Assert.AreEqual("Первая", opening.displayName);

        float waited = 0f;
        BossAbilityConfig next = null;
        for (int i = 0; i < 200 && next == null; i++)
        {
            state.Tick(0.1f, out next);
            waited += 0.1f;
        }

        Assert.IsNotNull(next);
        Assert.AreEqual("Вторая", next.displayName);
        Assert.AreEqual(2f, waited, 0.15f, "пауза перед вторым шагом = cooldownSeconds первого");
    }

    [Test]
    public void CyclicPhase_ExposesCurrentStepForUi_AndOrdinaryPhaseDoesNot()
    {
        var cyclic = new BossEncounterState(MakeKit(MakeCyclePhase(MakeCycleStep("A", 1f), MakeCycleStep("B", 1f))));
        Assert.AreEqual(0, cyclic.CurrentCycleIndex, "в начале боя стрелка на первом шаге");

        cyclic.Tick(0.1f, out _);
        Assert.AreEqual(1, cyclic.CurrentCycleIndex, "после исполнения стрелка сдвигается");

        var ordinary = new BossEncounterState(MakeKit(MakePhase("Обычная", 100f, MakeCycleStep("A", 1f))));
        Assert.AreEqual(-1, ordinary.CurrentCycleIndex, "у нецикличной фазы шага цикла нет");
    }

    [Test]
    public void DamageTakenBuff_RaisesIncomingDamage_ThenExpiresOnItsOwn()
    {
        var ability = new BossAbilityConfig
        {
            displayName = "Открытая грудь",
            effectKind = BossAbilityEffectKind.DamageTakenBuff,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            telegraphSeconds = 0f,
            damageTakenBonusPercent = 50f,
            damageTakenBonusSeconds = 2f
        };
        var boss = new CombatantRuntime
        {
            DisplayName = "Тест-Титан",
            IsBoss = true,
            MaxHP = 1000f,
            CurrentHP = 1000f,
            BossEncounter = new BossEncounterState(MakeKit(MakePhase("Фаза 1", 100f, ability))),
            Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.001f, DamageType = DamageType.Physical } }
        };

        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss });

        cm.Tick(0.016f); // способность резолвится сразу, телеграфа нет
        Assert.AreEqual(50f, boss.DamageTakenBonusPercent, 0.01f);

        float hpBefore = boss.CurrentHP;
        DamageCalculator.ApplyDamage(boss, 100f, DamageType.Physical);
        float takenInsideWindow = hpBefore - boss.CurrentHP;

        // Окно истекает само по боевому времени и обнуляет процент, а не только таймер.
        cm.Tick(2.5f);
        Assert.AreEqual(0f, boss.DamageTakenBonusPercent, 0.01f);
        Assert.AreEqual(0f, boss.DamageTakenBonusTimer, 0.01f);

        hpBefore = boss.CurrentHP;
        DamageCalculator.ApplyDamage(boss, 100f, DamageType.Physical);
        float takenAfterWindow = hpBefore - boss.CurrentHP;

        Assert.Greater(takenInsideWindow, takenAfterWindow,
            "в окне уязвимости тот же удар обязан снимать больше HP");
    }

    // ---- Групповой босс-бой: «Связь» и «Скорбь» (Тени-Близнецы, 2026-09-11) ----

    static CombatantRuntime MakeGroupMember(string name, float hp, float reductionPercent,
        float soloDamagePercent, float soloSpeedPercent)
    {
        return new CombatantRuntime
        {
            DisplayName = name,
            IsBoss = true,
            MaxHP = hp,
            CurrentHP = hp,
            InBossGroup = true,
            PendingGroupDamageReductionPercent = reductionPercent,
            PendingSoloDamageBonusPercent = soloDamagePercent,
            PendingSoloAttackSpeedBonusPercent = soloSpeedPercent,
            SoloTransitionName = "Скорбь",
            Weapons = { new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, AttackSpeed = 0.001f, DamageType = DamageType.Physical } }
        };
    }

    [Test]
    public void BossGroup_WhileAllyAlive_MembersTakeReducedDamage()
    {
        var first = MakeGroupMember("Первая тень", 500f, 30f, 50f, 30f);
        var second = MakeGroupMember("Вторая тень", 500f, 30f, 50f, 30f);
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { first, second });

        cm.Tick(0.016f);
        Assert.AreEqual(30f, first.BossGroupDamageReductionPercent, 0.01f);

        float before = first.CurrentHP;
        DamageCalculator.ApplyDamage(first, 100f, DamageType.Physical);
        float takenLinked = before - first.CurrentHP;

        Assert.AreEqual(70f, takenLinked, 0.5f, "при связи -30% удар на 100 обязан снимать 70");
    }

    [Test]
    public void BossGroup_WhenAllyDies_SurvivorLosesLinkAndGainsSoloBonusExactlyOnce()
    {
        var survivor = MakeGroupMember("Выжившая", 500f, 30f, 50f, 30f);
        var doomed = MakeGroupMember("Обречённая", 500f, 30f, 50f, 30f);
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { survivor, doomed });

        cm.Tick(0.016f);
        Assert.AreEqual(30f, survivor.BossGroupDamageReductionPercent, 0.01f);
        Assert.AreEqual(0f, survivor.BossSoloDamageBonusPercent, 0.01f, "пока союзник жив, усиления нет");

        doomed.CurrentHP = 0f;
        cm.Tick(0.016f);

        Assert.AreEqual(0f, survivor.BossGroupDamageReductionPercent, 0.01f, "связь разорвана — снижение урона спадает");
        Assert.AreEqual(50f, survivor.BossSoloDamageBonusPercent, 0.01f);
        Assert.AreEqual(30f, survivor.BossSoloAttackSpeedBonusPercent, 0.01f);
        Assert.IsTrue(survivor.BossSoloBonusApplied);

        // Дальнейшие тики не должны накручивать бонус повторно — иначе выживший разгонялся бы
        // бесконечно, просто потому что бой продолжается.
        survivor.BossSoloDamageBonusPercent = 50f;
        cm.Tick(1f);
        cm.Tick(1f);
        Assert.AreEqual(50f, survivor.BossSoloDamageBonusPercent, 0.01f);
    }

    [Test]
    public void BossGroup_SoloSpeedBonus_ActuallySpeedsUpAttacks()
    {
        var survivor = MakeGroupMember("Выжившая", 500f, 0f, 0f, 100f);
        var weapon = survivor.Weapons[0];
        weapon.AttackSpeed = 1f;

        float baseSpeed = survivor.GetEffectiveAttackSpeed(weapon);
        survivor.BossSoloAttackSpeedBonusPercent = 100f;
        float boosted = survivor.GetEffectiveAttackSpeed(weapon);

        Assert.AreEqual(baseSpeed * 2f, boosted, 0.01f, "+100% скорости обязаны удваивать эффективную скорость");
    }

    [Test]
    public void CreateBossCompanions_SelfReferencingKit_SpawnsOnePartnerAndMarksBothWithoutRecursion()
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.phases.Add(MakePhase("Фаза 1", 100f));
        kit.groupDamageReductionPercent = 30f;
        kit.soloDamageBonusPercent = 50f;
        kit.soloAttackSpeedBonusPercent = 30f;
        kit.soloTransitionName = "Скорбь";

        var monster = ScriptableObject.CreateInstance<MonsterData>();
        monster.monsterName = "Тень";
        monster.isBoss = true;
        monster.hp = 100f;
        monster.damageMin = 1f;
        monster.damageMax = 1f;
        monster.attackSpeed = 1f;
        monster.bossKit = kit;
        // Кит ссылается на СВОЕГО ЖЕ монстра — так делаются симметричные связки.
        kit.companions.Add(new BossCompanionSpawn { monster = monster, count = 1 });

        var boss = CombatantFactory.CreateMonsterCombatant(monster, floorNumber: 1);
        var companions = CombatantFactory.CreateBossCompanions(monster, 1, boss);

        Assert.AreEqual(1, companions.Count, "самоссылающийся кит обязан дать РОВНО одного спутника, а не уйти в рекурсию");
        Assert.IsTrue(boss.InBossGroup);
        Assert.IsTrue(companions[0].InBossGroup);
        Assert.AreEqual(30f, companions[0].PendingGroupDamageReductionPercent, 0.01f);
        Assert.AreEqual(50f, boss.PendingSoloDamageBonusPercent, 0.01f);
        Assert.AreEqual("Скорбь", boss.SoloTransitionName);
    }

    [Test]
    public void CreateBossCompanions_KitWithoutCompanions_LeavesBossOutsideAnyGroup()
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.phases.Add(MakePhase("Фаза 1", 100f));

        var monster = ScriptableObject.CreateInstance<MonsterData>();
        monster.monsterName = "Одиночка";
        monster.isBoss = true;
        monster.hp = 100f;
        monster.attackSpeed = 1f;
        monster.bossKit = kit;

        var boss = CombatantFactory.CreateMonsterCombatant(monster, floorNumber: 1);
        var companions = CombatantFactory.CreateBossCompanions(monster, 1, boss);

        Assert.IsEmpty(companions);
        Assert.IsFalse(boss.InBossGroup, "обычный одиночный босс не должен попадать в групповые правила");
    }

    // ---- Якоря и неуязвимость (Свечник, 2026-09-11) ----

    static CombatantRuntime MakeAnchor(string name, float hp) => new CombatantRuntime
    {
        DisplayName = name,
        MaxHP = hp,
        CurrentHP = hp,
        InBossGroup = true,
        IsBossAnchor = true
        // Оружия намеренно нет: якорь — цель, а не атакующий.
    };

    static CombatantRuntime MakeAnchoredBoss(params BossAbilityConfig[] abilities) => new CombatantRuntime
    {
        DisplayName = "Тест-Свечник",
        IsBoss = true,
        MaxHP = 500f,
        CurrentHP = 500f,
        InBossGroup = true,
        PendingInvulnerableWhileAnchorsAlive = true,
        BossEncounter = new BossEncounterState(MakeKit(MakePhase("Фаза 1", 100f, abilities))),
        Weapons = { new WeaponAttackState { DamageMin = 5f, DamageMax = 5f, AttackSpeed = 0.001f, DamageType = DamageType.Physical } }
    };

    [Test]
    public void AnchoredBoss_IsInvulnerableWhileAnchorAlive_AndKillableAfterAllAnchorsDie()
    {
        var boss = MakeAnchoredBoss();
        var candle = MakeAnchor("Свеча", 10f);
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss, candle });

        cm.Tick(0.016f);
        Assert.IsTrue(boss.IsInvulnerable, "пока горит свеча, босс неуязвим");

        float hpBefore = boss.CurrentHP;
        DamageCalculator.ApplyDamage(boss, 250f, DamageType.Physical);
        Assert.AreEqual(hpBefore, boss.CurrentHP, 0.01f, "урон по неуязвимому боссу обязан пропадать полностью");

        candle.CurrentHP = 0f;
        cm.Tick(0.016f);

        Assert.IsFalse(boss.IsInvulnerable, "свеча потушена — открылось окно урона");
        DamageCalculator.ApplyDamage(boss, 100f, DamageType.Physical);
        Assert.Less(boss.CurrentHP, hpBefore, "в окне босс обязан получать урон");
    }

    [Test]
    public void AnchoredBoss_InvulnerabilityDoesNotWearArmorOrShields()
    {
        var boss = MakeAnchoredBoss();
        boss.PhysicalDefenseMax = 50f;
        boss.PhysicalDefenseCurrent = 50f;
        boss.ShieldPoolMax = 40f;
        boss.ShieldPoolCurrent = 40f;
        var candle = MakeAnchor("Свеча", 10f);
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss, candle });
        cm.Tick(0.016f);

        DamageCalculator.ApplyDamage(boss, 500f, DamageType.Physical);

        // Иначе игрок «продавливал» бы неуязвимость, снашивая броню сквозь неё, и окно урона
        // переставало быть единственным способом навредить.
        Assert.AreEqual(50f, boss.PhysicalDefenseCurrent, 0.01f, "броня не изнашивается сквозь неуязвимость");
        Assert.AreEqual(40f, boss.ShieldPoolCurrent, 0.01f, "щит не тратится сквозь неуязвимость");
    }

    [Test]
    public void ReviveAnchor_BringsBackOneDeadAnchorAtFullHp_AndRestoresInvulnerability()
    {
        var relight = new BossAbilityConfig
        {
            displayName = "Зажечь",
            effectKind = BossAbilityEffectKind.ReviveAnchor,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            telegraphSeconds = 0f
        };
        var boss = MakeAnchoredBoss(relight);
        var candle = MakeAnchor("Свеча", 10f);
        candle.CurrentHP = 0f;

        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss, candle });

        cm.Tick(0.016f);

        Assert.AreEqual(10f, candle.CurrentHP, 0.01f, "свеча зажигается на полное HP");
        Assert.IsTrue(candle.IsAlive);

        cm.Tick(0.016f);
        Assert.IsTrue(boss.IsInvulnerable, "зажжённая свеча снова закрывает босса");
    }

    [Test]
    public void ReviveAnchor_WithNoDeadAnchors_DoesNothingAndDoesNotThrow()
    {
        var relight = new BossAbilityConfig
        {
            displayName = "Зажечь",
            effectKind = BossAbilityEffectKind.ReviveAnchor,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            telegraphSeconds = 0f
        };
        var boss = MakeAnchoredBoss(relight);
        var candle = MakeAnchor("Свеча", 10f);
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss, candle });

        Assert.DoesNotThrow(() => cm.Tick(0.016f));
        Assert.AreEqual(10f, candle.CurrentHP, 0.01f);
    }

    [Test]
    public void NonAttackingMonster_GetsNoWeaponAndNeverDamagesThePlayer()
    {
        var candleData = ScriptableObject.CreateInstance<MonsterData>();
        candleData.monsterName = "Свеча";
        candleData.hp = 10f;
        candleData.damageMin = 99f;
        candleData.damageMax = 99f;
        candleData.attackSpeed = 10f;
        candleData.doesNotAttack = true;

        var candle = CombatantFactory.CreateMonsterCombatant(candleData, floorNumber: 1);
        Assert.IsEmpty(candle.Weapons, "сущность с doesNotAttack не получает оружия вовсе");

        var player = MakePlayer(hp: 100f);
        var cm = CreateCombatManager();
        cm.StartCombat(player, new System.Collections.Generic.List<CombatantRuntime> { candle });

        for (int i = 0; i < 300; i++) cm.Tick(0.05f);

        Assert.AreEqual(100f, player.CurrentHP, 0.01f,
            "свеча не должна снять ни одного HP: каждый лишний атакующий — лишний бросок против уклонения Вайолет");
    }

    // ---- Фильтры билда: заморозка, замедление, самоурон (план 4, 2026-09-11) ----

    static CombatantRuntime MakeSimpleBoss(params BossAbilityConfig[] abilities) => new CombatantRuntime
    {
        DisplayName = "Тест-босс",
        IsBoss = true,
        MaxHP = 200f,
        CurrentHP = 200f,
        BossEncounter = new BossEncounterState(MakeKit(MakePhase("Фаза 1", 100f, abilities))),
        Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.001f, DamageType = DamageType.Physical } }
    };

    static BossAbilityConfig Instant(string name, BossAbilityEffectKind kind) => new BossAbilityConfig
    {
        displayName = name,
        effectKind = kind,
        triggerKind = BossAbilityTriggerKind.Periodic,
        cooldownSeconds = 100f,
        initialDelaySeconds = 0f,
        telegraphSeconds = 0f
    };

    [Test]
    public void ApplyFreeze_GrantsSeveralStacksAtOnce_AndFreezesAtTen()
    {
        var ability = Instant("Плач", BossAbilityEffectKind.ApplyFreeze);
        ability.freezeStacks = 10;
        var boss = MakeSimpleBoss(ability);
        var player = MakePlayer();
        var cm = CreateCombatManager();
        cm.StartCombat(player, new System.Collections.Generic.List<CombatantRuntime> { boss });

        cm.Tick(0.016f);

        Assert.AreEqual(10, player.FreezeStacks, "способность выдаёт заряды пачкой, а не по одному");
        Assert.IsTrue(player.IsFrozen, "на десяти зарядах игрок замораживается");
    }

    [Test]
    public void ApplyFreeze_RespectsFreezeImmunity_SoChainsAreImpossible()
    {
        var ability = Instant("Плач", BossAbilityEffectKind.ApplyFreeze);
        ability.freezeStacks = 10;
        var boss = MakeSimpleBoss(ability);
        var player = MakePlayer();
        player.FreezeImmune = true;
        player.FreezeImmuneTimer = 5f;
        var cm = CreateCombatManager();
        cm.StartCombat(player, new System.Collections.Generic.List<CombatantRuntime> { boss });

        cm.Tick(0.016f);

        Assert.AreEqual(0, player.FreezeStacks, "иммунитет после разморозки не даёт зацепить игрока снова");
        Assert.IsFalse(player.IsFrozen);
    }

    [Test]
    public void AttackSpeedDebuff_SlowsThePlayer_AndExpiresOnItsOwn()
    {
        var ability = Instant("Иней", BossAbilityEffectKind.AttackSpeedDebuff);
        ability.attackSpeedMultiplier = 0.8f;
        ability.debuffSeconds = 5f;
        var boss = MakeSimpleBoss(ability);
        var player = MakePlayer();
        var weapon = player.Weapons[0];
        weapon.AttackSpeed = 1f;
        float baseSpeed = player.GetEffectiveAttackSpeed(weapon);

        var cm = CreateCombatManager();
        cm.StartCombat(player, new System.Collections.Generic.List<CombatantRuntime> { boss });
        cm.Tick(0.016f);

        Assert.AreEqual(baseSpeed * 0.8f, player.GetEffectiveAttackSpeed(weapon), 0.01f);

        for (int i = 0; i < 12; i++) cm.Tick(0.5f);

        Assert.AreEqual(baseSpeed, player.GetEffectiveAttackSpeed(weapon), 0.01f, "дебафф обязан истечь сам");
    }

    [Test]
    public void SelfDamage_HurtsTheBossByShareOfItsOwnMaxHp()
    {
        var ability = Instant("Пошатнулся", BossAbilityEffectKind.SelfDamage);
        ability.selfDamagePercentOfMaxHp = 8f;
        var boss = MakeSimpleBoss(ability);
        // Броня не должна спасать босса от собственной неуклюжести.
        boss.PhysicalDefenseMax = 100f;
        boss.PhysicalDefenseCurrent = 100f;
        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss });

        cm.Tick(0.016f);

        Assert.AreEqual(184f, boss.CurrentHP, 0.5f, "8% от 200 максимального HP");
        Assert.AreEqual(100f, boss.PhysicalDefenseCurrent, 0.01f, "самоурон идёт мимо брони");
    }

    [Test]
    public void PhaseEntry_AppliesStatMultipliersOnce_ArmorDownAndAttacksMoreOften()
    {
        var kit = MakeKit(MakePhase("Целый", 100f), MakePhase("Треснувший", 45f));
        kit.phases[1].enterArmorMultiplier = 0.5f;
        kit.phases[1].enterAttackSpeedMultiplier = 2f;

        var boss = new CombatantRuntime
        {
            DisplayName = "Тест-Идол",
            IsBoss = true,
            MaxHP = 100f,
            CurrentHP = 100f,
            PhysicalDefenseMax = 40f,
            PhysicalDefenseCurrent = 40f,
            BossEncounter = new BossEncounterState(kit),
            Weapons = { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.5f, DamageType = DamageType.Physical } }
        };

        var cm = CreateCombatManager();
        cm.StartCombat(MakePlayer(), new System.Collections.Generic.List<CombatantRuntime> { boss });

        boss.CurrentHP = 40f; // ниже порога второй фазы
        cm.Tick(0.016f);

        Assert.AreEqual(20f, boss.PhysicalDefenseCurrent, 0.01f, "броня падает вдвое при входе в фазу");
        Assert.AreEqual(20f, boss.PhysicalDefenseMax, 0.01f);
        // AttackSpeed — частота, поэтому «вдвое чаще» это ×2, а не ÷2.
        Assert.AreEqual(1f, boss.Weapons[0].AttackSpeed, 0.01f);

        cm.Tick(0.016f);
        cm.Tick(0.016f);
        Assert.AreEqual(20f, boss.PhysicalDefenseCurrent, 0.01f, "множитель не должен применяться повторно каждый тик");
        Assert.AreEqual(1f, boss.Weapons[0].AttackSpeed, 0.01f);
    }

    // ---- Процентный урон и потолок одиночного удара (2026-09-11) ----

    static CombatantRuntime MakeHeavyBoss(float multiplier, float percentOfMaxHp, float weaponDamage)
    {
        var ability = new BossAbilityConfig
        {
            displayName = "Удар",
            effectKind = BossAbilityEffectKind.HeavyAttack,
            triggerKind = BossAbilityTriggerKind.Periodic,
            cooldownSeconds = 100f,
            initialDelaySeconds = 0f,
            telegraphSeconds = 0f,
            damageMultiplier = multiplier,
            damagePercentOfTargetMaxHp = percentOfMaxHp
        };
        return new CombatantRuntime
        {
            DisplayName = "Тест-босс",
            IsBoss = true,
            MaxHP = 500f,
            CurrentHP = 500f,
            BossEncounter = new BossEncounterState(MakeKit(MakePhase("Фаза 1", 100f, ability))),
            Weapons = { new WeaponAttackState { DamageMin = weaponDamage, DamageMax = weaponDamage, AttackSpeed = 0.001f, DamageType = DamageType.Physical } }
        };
    }

    [Test]
    public void HeavyAttack_WithPercentDamage_ScalesWithTargetMaxHpNotWeapon()
    {
        // Один и тот же босс против «толстой» и «тонкой» цели снимает одну и ту же ДОЛЮ здоровья —
        // ради этого процентный урон и вводился.
        var fat = MakePlayer(hp: 1000f);
        var bossA = MakeHeavyBoss(multiplier: 1f, percentOfMaxHp: 20f, weaponDamage: 5f);
        var cmA = CreateCombatManager();
        cmA.StartCombat(fat, new System.Collections.Generic.List<CombatantRuntime> { bossA });
        cmA.Tick(0.016f);
        float fatLost = 1000f - fat.CurrentHP;

        var thin = MakePlayer(hp: 100f);
        var bossB = MakeHeavyBoss(multiplier: 1f, percentOfMaxHp: 20f, weaponDamage: 5f);
        var cmB = CreateCombatManager();
        cmB.StartCombat(thin, new System.Collections.Generic.List<CombatantRuntime> { bossB });
        cmB.Tick(0.016f);
        float thinLost = 100f - thin.CurrentHP;

        Assert.AreEqual(200f, fatLost, 1f, "20% от 1000");
        Assert.AreEqual(20f, thinLost, 1f, "20% от 100");
    }

    [Test]
    public void HeavyAttack_NeverTakesMoreThanHalfOfTargetMaxHp_EvenWithAbsurdNumbers()
    {
        // Правило честности №3 живёт в коде: даже кривой ассет с множителем ×50 не снимает больше
        // половины полоски. Без этого Вайолет с её 15 HP базы умирала бы с одного удара там, где
        // Саша не замечает урона.
        var player = MakePlayer(hp: 200f);
        var boss = MakeHeavyBoss(multiplier: 50f, percentOfMaxHp: 0f, weaponDamage: 100f);
        var cm = CreateCombatManager();
        cm.StartCombat(player, new System.Collections.Generic.List<CombatantRuntime> { boss });

        cm.Tick(0.016f);

        float lost = 200f - player.CurrentHP;
        Assert.LessOrEqual(lost, 100f + 0.5f,
            $"одиночный удар снял {lost} из 200 максимального HP — потолок {CombatManager.MaxBossSingleHitPercentOfMaxHp}% пробит");
        Assert.Greater(lost, 0f, "потолок не должен превращать удар в ноль");
    }

    [Test]
    public void HeavyAttack_PercentDamage_StillGoesThroughArmor()
    {
        // Процентный урон не должен быть «чистым»: броня обязана продолжать работать, иначе
        // вложение Дженифер в защиту обесценивается против всей роспиcи разом.
        var armored = MakePlayer(hp: 1000f);
        armored.PhysicalDefenseMax = 500f;
        armored.PhysicalDefenseCurrent = 500f;
        var boss = MakeHeavyBoss(multiplier: 1f, percentOfMaxHp: 20f, weaponDamage: 5f);
        var cm = CreateCombatManager();
        cm.StartCombat(armored, new System.Collections.Generic.List<CombatantRuntime> { boss });

        cm.Tick(0.016f);

        Assert.AreEqual(1000f, armored.CurrentHP, 0.01f, "броня обязана поглотить процентный удар");
        Assert.Less(armored.PhysicalDefenseCurrent, 500f, "и при этом износиться");
    }
}
