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

    [Test]
    public void NoAbility_DeclaresPercentDamageAboveTheSingleHitCap()
    {
        foreach (var kit in LoadAllKits())
        {
            foreach (var phase in kit.phases)
            {
                foreach (var ability in phase.abilities)
                {
                    Assert.LessOrEqual(ability.damagePercentOfTargetMaxHp, CombatManager.MaxBossSingleHitPercentOfMaxHp,
                        $"{kit.name} / «{phase.phaseName}» / «{ability.displayName}»: заявлено " +
                        $"{ability.damagePercentOfTargetMaxHp}% от макс. HP при потолке " +
                        $"{CombatManager.MaxBossSingleHitPercentOfMaxHp}%. Потолок всё равно срежет удар — " +
                        "значит ассет врёт о своей силе, и подкрутить его числом уже нельзя");
                }
            }
        }
    }

    // Правило честности №4: броню босса нельзя обнулять, максимум −50 %. Множители входа в фазу
    // задаются в YAML, а пять старых китов (Тюремщик, Свечник, Титан, Близнецы, Страж) написаны
    // ДО появления этих полей и не содержат их вовсе. Тест заодно фиксирует, что отсутствующий
    // ключ YAML оставляет инициализатор поля (1f), а не превращается в 0 — иначе вход во вторую
    // фазу молча обнулял бы броню и выключал Дженифер.
    [Test]
    public void EveryPhaseEntryMultiplier_NeitherZeroesArmorNorStopsTheBoss()
    {
        foreach (var kit in LoadAllKits())
        {
            for (int i = 0; i < kit.phases.Count; i++)
            {
                var phase = kit.phases[i];
                Assert.GreaterOrEqual(phase.enterArmorMultiplier, 0.5f,
                    $"{kit.name}, фаза {i} ({phase.phaseName}): enterArmorMultiplier " +
                    $"{phase.enterArmorMultiplier} режет броню больше чем вдвое — правило честности №4.");
                Assert.LessOrEqual(phase.enterArmorMultiplier, 1f,
                    $"{kit.name}, фаза {i} ({phase.phaseName}): enterArmorMultiplier " +
                    $"{phase.enterArmorMultiplier} — фаза не должна НАРАЩИВАТЬ броню.");
                Assert.Greater(phase.enterAttackSpeedMultiplier, 0f,
                    $"{kit.name}, фаза {i} ({phase.phaseName}): enterAttackSpeedMultiplier " +
                    $"{phase.enterAttackSpeedMultiplier} останавливает атаки босса.");
            }
        }
    }

    // Настроенное поле способности читает ровно один effectKind. Если поле заполнено, а effectKind
    // указывает на другой эффект — способность молча не делает НИЧЕГО: switch уходит в чужую ветку,
    // а та своих полей не находит. Так в роспиcи жили 25 мёртвых способностей у семи боссов
    // (лечение Инквизитора и Матери Спор не резалось, кровотечение Мясника не вешалось, броня
    // Кузнецом не ломалась, щит Ростовщика не регенерировал, Левиафан не погружался). Ошибка не
    // видна ни в редакторе, ни в бою — только по тому, что «ничего не происходит».
    [Test]
    public void EveryConfiguredField_MatchesTheAbilityEffectKind()
    {
        foreach (var kit in LoadAllKits())
        {
            for (int p = 0; p < kit.phases.Count; p++)
            {
                foreach (var a in kit.phases[p].abilities)
                {
                    string where = $"{kit.name}, фаза {p}, «{a.displayName}» (effectKind {a.effectKind})";
                    // disruptSeconds — единственное поле-признак с НЕнулевым инициализатором (5f),
                    // поэтому «настроено» для него значит «и не 0, и не 5»: ноль пишут новые киты,
                    // где ключ выставлен явно, а пятёрку получают старые, где ключа в YAML нет
                    // вовсе (BossKit_Warden). Ни то, ни другое не является осознанной настройкой.
                    Check(a.disruptSeconds != 0f && a.disruptSeconds != 5f, a, where,
                        "disruptSeconds", BossAbilityEffectKind.DisruptSkills);
                    Check(a.damageTakenBonusPercent != 0f, a, where, "damageTakenBonusPercent", BossAbilityEffectKind.DamageTakenBuff);
                    Check(a.freezeStacks != 0, a, where, "freezeStacks", BossAbilityEffectKind.ApplyFreeze);
                    Check(a.attackSpeedMultiplier != 1f, a, where, "attackSpeedMultiplier", BossAbilityEffectKind.AttackSpeedDebuff);
                    Check(a.selfDamagePercentOfMaxHp != 0f, a, where, "selfDamagePercentOfMaxHp", BossAbilityEffectKind.SelfDamage);
                    Check(a.healCutPercent != 0f, a, where, "healCutPercent", BossAbilityEffectKind.HealCut);
                    Check(a.armorDebuffPercent != 0f, a, where, "armorDebuffPercent", BossAbilityEffectKind.StatDebuff);
                    Check(a.roomTickPercentOfMaxHp != 0f, a, where, "roomTickPercentOfMaxHp", BossAbilityEffectKind.RoomTick);
                    Check(a.enrageDamagePercentPerTrigger != 0f, a, where, "enrageDamagePercentPerTrigger", BossAbilityEffectKind.Enrage);
                    Check(a.selfInvulnerableSeconds != 0f, a, where, "selfInvulnerableSeconds", BossAbilityEffectKind.SelfInvulnerable);
                    Check(a.shieldRegenPercentPerSecond != 0f, a, where, "shieldRegenPercentPerSecond", BossAbilityEffectKind.ShieldRegen);
                    Check(a.spawnMonster != null, a, where, "spawnMonster", BossAbilityEffectKind.SpawnMinions);
                    Check(a.healPercentOfMaxHp != 0f, a, where, "healPercentOfMaxHp", BossAbilityEffectKind.ConsumeMinion);
                }
            }
        }
    }

    static void Check(bool configured, BossAbilityConfig a, string where, string field,
        params BossAbilityEffectKind[] readers)
    {
        if (!configured) return;
        foreach (var r in readers)
        {
            if (a.effectKind == r) return;
        }

        Assert.Fail($"{where}: поле {field} заполнено, но его читает только " +
            $"{string.Join("/", readers)} — способность не сделает ничего.");
    }

    // Ни один effectKind не должен выходить за пределы enum: значение вне диапазона просто не
    // попадает ни в один case закрытого switch, и способность тоже становится мёртвой.
    [Test]
    public void NoAbility_UsesAnEffectKindOutsideTheEnum()
    {
        var known = System.Enum.GetValues(typeof(BossAbilityEffectKind));
        foreach (var kit in LoadAllKits())
        {
            for (int p = 0; p < kit.phases.Count; p++)
            {
                foreach (var a in kit.phases[p].abilities)
                {
                    Assert.IsTrue(System.Enum.IsDefined(typeof(BossAbilityEffectKind), a.effectKind),
                        $"{kit.name}, фаза {p}, «{a.displayName}»: effectKind {(int)a.effectKind} вне enum " +
                        $"(в нём {known.Length} значений).");
                }
            }
        }
    }

    // План 8: потолок одновременно живых миньонов — условие проходимости, а не настройка
    // сложности. Каждый лишний атакующий на сцене это лишний бросок против уклонения Вайолет,
    // а её 15 HP базы этого не прощают. Ноль здесь означал бы «без потолка».
    [Test]
    public void EverySpawnAbility_DeclaresAHardAliveCap()
    {
        foreach (var kit in LoadAllKits())
        {
            for (int p = 0; p < kit.phases.Count; p++)
            {
                foreach (var a in kit.phases[p].abilities)
                {
                    if (a.effectKind != BossAbilityEffectKind.SpawnMinions) continue;

                    string where = $"{kit.name}, фаза {p}, «{a.displayName}»";
                    Assert.IsNotNull(a.spawnMonster, $"{where}: SpawnMinions без spawnMonster ничего не ставит.");
                    Assert.GreaterOrEqual(a.spawnAliveCap, 1, $"{where}: spawnAliveCap {a.spawnAliveCap} — ноль это «без потолка».");
                    Assert.LessOrEqual(a.spawnAliveCap, 3,
                        $"{where}: spawnAliveCap {a.spawnAliveCap} — больше трёх добавок Вайолет не переживёт.");
                    Assert.GreaterOrEqual(a.spawnCount, 1, $"{where}: spawnCount {a.spawnCount}.");
                }
            }
        }
    }
}
