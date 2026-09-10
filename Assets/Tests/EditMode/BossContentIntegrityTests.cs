using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Правила честности роспиcи боссов (Docs/Design/2026-09-10-boss-concepts-roster.md, раздел 0.4)
// проверяются здесь один раз для ВСЕХ китов сразу, а не переписываются под каждого нового босса:
// киты собираются руками в YAML, и самая вероятная ошибка — не опечатка в коде, а число в ассете.
// Каждый следующий босс роспиcи попадает под эти проверки автоматически, ничего дописывать не надо.
public class BossContentIntegrityTests
{
    static List<BossKitData> LoadAllKits()
    {
        var kits = new List<BossKitData>();
        foreach (var guid in AssetDatabase.FindAssets("t:BossKitData"))
        {
            var kit = AssetDatabase.LoadAssetAtPath<BossKitData>(AssetDatabase.GUIDToAssetPath(guid));
            if (kit != null) kits.Add(kit);
        }

        return kits;
    }

    [Test]
    public void EveryKit_HasAtLeastOnePhase_AndFirstPhaseStartsAtFullHp()
    {
        var kits = LoadAllKits();
        Assert.IsNotEmpty(kits, "в проекте не найдено ни одного BossKitData — проверка бессмысленна");

        foreach (var kit in kits)
        {
            Assert.IsNotEmpty(kit.phases, $"{kit.name}: кит без фаз");
            Assert.AreEqual(100f, kit.phases[0].hpThresholdPercent, 0.01f,
                $"{kit.name}: первая фаза обязана быть активна с начала боя (порог 100)");
        }
    }

    [Test]
    public void EveryKit_PhaseThresholdsDescend()
    {
        foreach (var kit in LoadAllKits())
        {
            for (int i = 1; i < kit.phases.Count; i++)
            {
                Assert.Less(kit.phases[i].hpThresholdPercent, kit.phases[i - 1].hpThresholdPercent,
                    $"{kit.name}: порог фазы «{kit.phases[i].phaseName}» не ниже предыдущей — " +
                    "BossEncounterState переходит по фазам монотонно, такая фаза недостижима");
            }
        }
    }

    [Test]
    public void EveryDangerousHeavyAttack_IsTelegraphedAtLeastOneAndAHalfSeconds()
    {
        foreach (var kit in LoadAllKits())
        {
            foreach (var phase in kit.phases)
            {
                foreach (var ability in phase.abilities)
                {
                    if (ability.effectKind != BossAbilityEffectKind.HeavyAttack) continue;
                    if (ability.damageMultiplier < 1.5f) continue;

                    Assert.GreaterOrEqual(ability.telegraphSeconds, 1.5f,
                        $"{kit.name} / «{phase.phaseName}» / «{ability.displayName}»: удар с множителем " +
                        $"{ability.damageMultiplier} обязан телеграфироваться минимум 1.5 с — игрок в автобое " +
                        "не может ни уклониться, ни перестать атаковать, телеграф здесь единственное, " +
                        "что делает урон понятным");
                }
            }
        }
    }

    [Test]
    public void EverySkillDisrupt_ObeysCapAndIsTelegraphedAtLeastThreeSeconds()
    {
        foreach (var kit in LoadAllKits())
        {
            foreach (var phase in kit.phases)
            {
                foreach (var ability in phase.abilities)
                {
                    if (ability.effectKind != BossAbilityEffectKind.DisruptSkills) continue;

                    Assert.LessOrEqual(ability.disruptSeconds, CombatManager.MaxBossSkillDisruptSeconds,
                        $"{kit.name} / «{phase.phaseName}» / «{ability.displayName}»: блокировка активного " +
                        "навыка длиннее потолка. Активный навык — единственный рычаг игрока в автобое");
                    Assert.GreaterOrEqual(ability.telegraphSeconds, 3f,
                        $"{kit.name} / «{phase.phaseName}» / «{ability.displayName}»: блокировка навыка " +
                        "обязана телеграфироваться минимум 3 с — это окно, в которое игрок успевает " +
                        "потратить навык до блокировки, и в нём весь смысл механики");
                }
            }
        }
    }

    [Test]
    public void JailerAsset_IsWiredEndToEnd()
    {
        var jailer = AssetDatabase.LoadAssetAtPath<MonsterData>(
            "Assets/ScriptableObjects/Monsters/Monster_Jailer.asset");

        Assert.IsNotNull(jailer, "Monster_Jailer.asset не найден");
        Assert.IsTrue(jailer.isBoss);
        Assert.IsNotNull(jailer.sprite, "у Тюремщика должен быть спрайт");
        Assert.IsNotNull(jailer.bossKit, "к Тюремщику должен быть привязан BossKit_Jailer");

        bool foundDisrupt = false;
        foreach (var phase in jailer.bossKit.phases)
        {
            foreach (var ability in phase.abilities)
            {
                if (ability.effectKind == BossAbilityEffectKind.DisruptSkills) foundDisrupt = true;
            }
        }

        Assert.IsTrue(foundDisrupt, "у Тюремщика должна быть хотя бы одна способность DisruptSkills — " +
            "без неё это просто ещё один босс с тяжёлым ударом");
    }

    [Test]
    public void MainBossPool_CoversEveryFloorAndHasNoBrokenEntries()
    {
        var pool = AssetDatabase.LoadAssetAtPath<BossPoolData>(
            "Assets/ScriptableObjects/Bosses/BossPool_Main.asset");

        Assert.IsNotNull(pool, "BossPool_Main.asset не найден");
        Assert.IsNotEmpty(pool.entries);

        foreach (var entry in pool.entries)
        {
            Assert.IsNotNull(entry.boss, "в пуле не должно быть пустых строк");
            Assert.IsTrue(entry.boss.isBoss, $"{entry.boss.monsterName} лежит в пуле боссов, но isBoss=false");
            Assert.IsNotNull(entry.boss.sprite, $"{entry.boss.monsterName}: босс без спрайта");
            Assert.LessOrEqual(entry.minFloor, entry.maxFloor,
                $"{entry.boss.monsterName}: диапазон этажей вывернут наизнанку");
        }

        // Ключевая проверка: на каждом этаже забега обязан быть хотя бы один кандидат, иначе игрок
        // молча получит запасного bossData и весь пул окажется бесполезен.
        for (int floor = 1; floor <= DungeonManager.TotalFloors; floor++)
        {
            var picked = BossPoolSelector.Select(pool, floor, fallback: null, pickIndex: _ => 0);
            Assert.IsNotNull(picked, $"на этаже {floor} ни один босс из пула не подходит");
        }
    }

    [Test]
    public void MainBossPool_ContainsJailerOnItsSpecFloors()
    {
        var pool = AssetDatabase.LoadAssetAtPath<BossPoolData>(
            "Assets/ScriptableObjects/Bosses/BossPool_Main.asset");
        var jailer = AssetDatabase.LoadAssetAtPath<MonsterData>(
            "Assets/ScriptableObjects/Monsters/Monster_Jailer.asset");

        Assert.IsNotNull(pool);
        Assert.IsNotNull(jailer);

        foreach (var entry in pool.entries)
        {
            if (entry.boss != jailer) continue;

            // Спека, концепт №8: Тюремщик — этажи 4–6.
            Assert.AreEqual(4, entry.minFloor);
            Assert.AreEqual(6, entry.maxFloor);
            return;
        }

        Assert.Fail("Тюремщика нет в BossPool_Main — он не сможет выпасть ни на одном этаже");
    }

    [Test]
    public void CyclicPhases_HaveAtLeastTwoSteps_AndNoOnCombatStartAbilities()
    {
        foreach (var kit in LoadAllKits())
        {
            foreach (var phase in kit.phases)
            {
                if (!phase.cycleAbilities) continue;

                Assert.GreaterOrEqual(phase.abilities.Count, 2,
                    $"{kit.name} / «{phase.phaseName}»: цикл из одного шага — это обычная способность " +
                    "на кулдауне, а не паттерн, который игрок заучивает");

                foreach (var ability in phase.abilities)
                {
                    Assert.AreNotEqual(BossAbilityTriggerKind.OnCombatStart, ability.triggerKind,
                        $"{kit.name} / «{phase.phaseName}» / «{ability.displayName}»: в циклической фазе " +
                        "triggerKind игнорируется — OnCombatStart здесь вводит в заблуждение при авторинге");
                }
            }
        }
    }

    [Test]
    public void ClockworkTitanAsset_IsWiredWithCycleAndVulnerabilityWindow()
    {
        var titan = AssetDatabase.LoadAssetAtPath<MonsterData>(
            "Assets/ScriptableObjects/Monsters/Monster_ClockworkTitan.asset");

        Assert.IsNotNull(titan, "Monster_ClockworkTitan.asset не найден");
        Assert.IsNotNull(titan.bossKit);
        Assert.IsNotNull(titan.sprite);

        foreach (var phase in titan.bossKit.phases)
        {
            Assert.IsTrue(phase.cycleAbilities,
                $"«{phase.phaseName}»: у Титана каждая фаза обязана быть циклом — в этом весь босс");

            bool hasWindow = false;
            bool hasFinisher = false;
            foreach (var ability in phase.abilities)
            {
                if (ability.effectKind == BossAbilityEffectKind.DamageTakenBuff) hasWindow = true;
                if (ability.effectKind == BossAbilityEffectKind.HeavyAttack && ability.damageMultiplier >= 2f)
                    hasFinisher = true;
            }

            Assert.IsTrue(hasWindow, $"«{phase.phaseName}»: без окна уязвимости тайминг навыка " +
                "перестаёт что-либо решать, и цикл превращается в декорацию");
            Assert.IsTrue(hasFinisher, $"«{phase.phaseName}»: в цикле должен быть тяжёлый удар, " +
                "ради которого игрок и читает паттерн");
        }
    }

    [Test]
    public void TitanVulnerabilityWindow_OutlastsTheWindUpItIsMeantToCover()
    {
        // Окно «открытая грудь» обязано ещё держаться в момент, когда прилетает тяжёлый удар,
        // иначе игрок физически не может попасть навыком в открытую грудь: пауза перед замахом
        // плюс сам телеграф съедят всё окно.
        var titan = AssetDatabase.LoadAssetAtPath<MonsterData>(
            "Assets/ScriptableObjects/Monsters/Monster_ClockworkTitan.asset");
        Assert.IsNotNull(titan);

        foreach (var phase in titan.bossKit.phases)
        {
            for (int i = 0; i < phase.abilities.Count; i++)
            {
                var window = phase.abilities[i];
                if (window.effectKind != BossAbilityEffectKind.DamageTakenBuff) continue;

                var next = phase.abilities[(i + 1) % phase.abilities.Count];
                float untilNextResolves = window.cooldownSeconds + next.telegraphSeconds;

                Assert.Greater(window.damageTakenBonusSeconds, untilNextResolves,
                    $"«{phase.phaseName}»: окно живёт {window.damageTakenBonusSeconds}с, а следующий шаг " +
                    $"резолвится через {untilNextResolves}с — окно закроется раньше удара");
            }
        }
    }

    [Test]
    public void EveryKitWithCompanions_HasLinkRules_AndNoCompanionWithoutMonster()
    {
        foreach (var kit in LoadAllKits())
        {
            if (kit.companions == null || kit.companions.Count == 0) continue;

            foreach (var spawn in kit.companions)
            {
                Assert.IsNotNull(spawn.monster, $"{kit.name}: строка спутника без монстра");
                Assert.Greater(spawn.count, 0, $"{kit.name}: спутник {spawn.monster.monsterName} с count=0");
            }

            // Правило связки — это либо взаимное усиление/ослабление (Близнецы), либо
            // неуязвимость от якорей (Свечник). Без хотя бы одного из них спутники — просто
            // лишние враги на сцене, и выбор цели ничего не решает.
            Assert.IsTrue(kit.groupDamageReductionPercent > 0f
                || kit.soloDamageBonusPercent > 0f
                || kit.soloAttackSpeedBonusPercent > 0f
                || kit.bossInvulnerableWhileAnchorsAlive,
                $"{kit.name}: спутники есть, но ни одного правила связки");

            Assert.LessOrEqual(kit.groupDamageReductionPercent, 90f,
                $"{kit.name}: снижение урона по связи выше клампа в DamageCalculator");
        }
    }

    [Test]
    public void TwinShadesAsset_IsSymmetricPairWithLinkAndGrief()
    {
        var twin = AssetDatabase.LoadAssetAtPath<MonsterData>(
            "Assets/ScriptableObjects/Monsters/Monster_TwinShade.asset");

        Assert.IsNotNull(twin, "Monster_TwinShade.asset не найден");
        Assert.IsNotNull(twin.bossKit);
        Assert.IsNotNull(twin.sprite);

        var kit = twin.bossKit;
        Assert.AreEqual(1, kit.companions.Count, "у Близнецов ровно один спутник");
        Assert.AreSame(twin, kit.companions[0].monster,
            "связка симметрична: кит обязан ссылаться на того же монстра, иначе близнецыне одинаковы");
        Assert.AreEqual(1, kit.companions[0].count);

        Assert.Greater(kit.groupDamageReductionPercent, 0f, "без «Связи» нет причины думать о порядке убийства");
        Assert.Greater(kit.soloDamageBonusPercent, 0f, "без «Скорби» убийство первой близняшки ничего не меняет");
    }

    [Test]
    public void TwinShades_PairedHeavyAttack_StaysUnderSingleBossBudget()
    {
        // Обе близняшки бегут ОДИН И ТОТ ЖЕ кит, то есть «Согласие» срабатывает у каждой. Суммарный
        // множитель пары и есть то, что чувствует игрок, — он не должен превышать удар одиночного
        // босса (спека: «суммарно ×1.5»).
        var twin = AssetDatabase.LoadAssetAtPath<MonsterData>(
            "Assets/ScriptableObjects/Monsters/Monster_TwinShade.asset");
        Assert.IsNotNull(twin);

        int membersInPair = 1 + twin.bossKit.companions[0].count;

        foreach (var phase in twin.bossKit.phases)
        {
            foreach (var ability in phase.abilities)
            {
                if (ability.effectKind != BossAbilityEffectKind.HeavyAttack) continue;

                float pairTotal = ability.damageMultiplier * membersInPair;
                Assert.LessOrEqual(pairTotal, 1.5f + 0.001f,
                    $"«{ability.displayName}»: множитель {ability.damageMultiplier} × {membersInPair} участника " +
                    $"= {pairTotal} суммарно — пара бьёт сильнее одиночного босса");
            }
        }
    }

    [Test]
    public void EveryAnchorCompanion_DoesNotAttack_AndItsKitDeclaresInvulnerability()
    {
        foreach (var kit in LoadAllKits())
        {
            if (kit.companions == null) continue;

            foreach (var spawn in kit.companions)
            {
                if (spawn == null || spawn.monster == null || !spawn.isAnchor) continue;

                Assert.IsTrue(kit.bossInvulnerableWhileAnchorsAlive,
                    $"{kit.name}: спутник {spawn.monster.monsterName} помечен якорем, но кит не объявляет " +
                    "неуязвимость — флаг ни на что не влияет");
                Assert.IsTrue(spawn.monster.doesNotAttack,
                    $"{kit.name}: якорь {spawn.monster.monsterName} атакует. Якорь — это цель, а не " +
                    "атакующий: каждый лишний бросок по игроку бьёт по уклонению Вайолет");
            }
        }
    }

    [Test]
    public void EveryKitWithAnchorInvulnerability_CanBeOpened()
    {
        foreach (var kit in LoadAllKits())
        {
            if (!kit.bossInvulnerableWhileAnchorsAlive) continue;

            int anchors = 0;
            foreach (var spawn in kit.companions)
            {
                if (spawn != null && spawn.monster != null && spawn.isAnchor) anchors += spawn.count;
            }

            Assert.Greater(anchors, 0,
                $"{kit.name}: босс неуязвим, пока жив якорь, но якорей в ките нет — бой невыигрываем");

            // Воскрешение якорей обязано быть медленнее, чем игрок способен тушить: иначе окна
            // урона не существует вовсе. Порог намеренно грубый — точное значение подбирается
            // симуляцией (см. Docs/Balance/2026-09-10-boss-test-and-balance-plan.md).
            foreach (var phase in kit.phases)
            {
                foreach (var ability in phase.abilities)
                {
                    if (ability.effectKind != BossAbilityEffectKind.ReviveAnchor) continue;
                    Assert.GreaterOrEqual(ability.cooldownSeconds, 8f,
                        $"{kit.name} / «{phase.phaseName}»: якоря зажигаются каждые " +
                        $"{ability.cooldownSeconds}с — окно урона по боссу может не успеть открыться");
                }
            }
        }
    }
}
