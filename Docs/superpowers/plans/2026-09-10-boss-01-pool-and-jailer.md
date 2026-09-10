# План 1: Пул боссов по этажам + Тюремщик

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Сделать так, чтобы на этаже мог появиться один из нескольких боссов, и добавить первого нового босса роспиcи — Тюремщика, который блокирует активный навык игрока.

**Architecture:** Два независимых куска. (1) `bossData` (одно поле) заменяется на `BossPoolData` — ScriptableObject со списком «босс + диапазон этажей»; выбор вынесен в чистый статический `BossPoolSelector`, тестируемый без сцены (тот же паттерн, что `MonsterEncounterBudget`/`FloorDirectorEncounterPolicy`). (2) В закрытый `switch` `CombatManager.ExecuteBossAbility` добавляется одно значение `BossAbilityEffectKind.DisruptSkills`, дёргающее уже написанный `CombatManager.DisruptPlayerActiveSkills`. Нового боевого кода нет — только проводка и данные.

**Tech Stack:** Unity 6000.5.8f1, C#, NUnit (EditMode), ScriptableObject-ассеты в YAML.

**Spec:** `Docs/Design/2026-09-10-boss-concepts-roster.md` — раздел 0 (ограничения автобоя), раздел 2 концепт №8 «Тюремщик».
**Дорожная карта:** `Docs/superpowers/plans/2026-09-10-boss-roster-roadmap.md`.

## Global Constraints

- **Бой автоматический.** Игрок может: выбрать цель кликом, нажать один активный навык, включить/выключить его авто-каст. Всё. Механики «не бей», «уклонись», «придержи атаку» нереализуемы — не изобретать их заново.
- **У Дженифер нет защитной активной кнопки** («3 быстрые атаки» — атакующий навык). Ни один босс не строится на «ответь на телеграф, чтобы выжить».
- **Тяжёлый удар считается в % от макс. HP цели, потолок 50 %.** Плоские числа убивают Вайолет (15 HP базы) и не замечаются Сашей (45).
- **Блокировка активного навыка — не длиннее 8 секунд и всегда с телеграфом ≥3 с.** Константа `CombatManager.MaxBossSkillDisruptSeconds = 8f`, клампится в коде, а не «по договорённости в ассете».
- **Новые значения в `enum` дописываются ТОЛЬКО в конец** — иначе сдвигаются числовые значения в уже сериализованных ассетах (`BossKit_Warden.asset` хранит `effectKind: 1`).
- **Комментарии в коде — на русском**, как во всём боевом коде проекта.
- **Unity-тесты запускаются БЕЗ `-quit`** и всегда с `-testResults <абсолютный путь>` — иначе `-runTests` молча ничего не делает (см. память проекта).

## Предусловия (выполнить до Task 1)

- [ ] **Разобраться с некоммитнутым WIP.** В рабочей копии лежат чужие незавершённые правки по аудиту баланса: `CombatManager.cs` (+139 строк), `DamageCalculator.cs`, `CombatantRuntime.cs`, `CombatantFactory.cs`, `MonsterModifierCatalog.cs`, `DamageCalculatorTests.cs` (+203 строки) и ряд ассетов. Закоммитить их отдельным коммитом или отложить (`git stash`) — работа по боссам должна ветвиться от чистого состояния.
- [ ] **Baseline красный, это не твоя работа.** На текущем состоянии EditMode даёт **399/401**, падают два теста, оба на округлении (`RoundPoints` из WIP):
  - `CursedItemTests.Paranoia_NonDodgedHitUsesStacksThenClearsThem` — ожидает 988.5, получает 988.0
  - `RestBonusTests.AttackSpeedAndHealing_ReachTheirConsumptionPoints` — ожидает 12.5, получает 13.0

  Эти два теста чинятся вместе с WIP, до старта плана. **Начинать боссов на красном baseline нельзя** — иначе непонятно, чьё падение чьё.
- [ ] **Создать ветку:** `git checkout -b feat/boss-pool-and-jailer`

**Команда прогона тестов** (используется в каждом Task; путь к результатам — абсолютный, каталог должен существовать):

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -projectPath "C:/Unity Projects/DungeonGirls" -runTests -testPlatform EditMode -testResults "C:/Unity Projects/DungeonGirls/Logs/editmode.xml" -logFile "C:/Unity Projects/DungeonGirls/Logs/editmode.log"
```

Код возврата 0 = все прошли, 2 = есть падения. Имена упавших тестов:

```bash
python -c "import xml.etree.ElementTree as ET;[print(tc.get('fullname')) for tc in ET.parse('C:/Unity Projects/DungeonGirls/Logs/editmode.xml').iter('test-case') if tc.get('result')!='Passed']"
```

## File Structure

| Файл | Ответственность |
|---|---|
| `Assets/Scripts/Data/BossPoolData.cs` (создать) | ScriptableObject: список «босс + диапазон этажей». Только данные, без логики |
| `Assets/Scripts/Combat/BossPoolSelector.cs` (создать) | Чистая функция выбора босса по этажу. Без Unity-сцены, без `Random` внутри |
| `Assets/Scripts/Data/BossKitData.cs` (править) | +1 значение в `BossAbilityEffectKind`, +1 поле в `BossAbilityConfig` |
| `Assets/Scripts/Managers/CombatManager.cs` (править) | +1 `case` в `ExecuteBossAbility`, +1 константа-потолок |
| `Assets/Scripts/UI/RunFlowController.cs` (править, `:27`) | Поле `bossPool` рядом с существующим `bossData` |
| `Assets/Scripts/UI/RunFlowController.Combat.cs` (править, `:299`) | Спавн босса через селектор |
| `Assets/Tests/EditMode/BossPoolTests.cs` (создать) | Тесты селектора |
| `Assets/Tests/EditMode/BossEncounterTests.cs` (править) | Тесты `DisruptSkills` |
| `Assets/ScriptableObjects/Bosses/BossPool_Main.asset` (создать) | Пул: Страж + Тюремщик |
| `Assets/ScriptableObjects/Bosses/BossKit_Jailer.asset` (создать) | Кит Тюремщика |
| `Assets/ScriptableObjects/Monsters/Monster_Jailer.asset` (создать) | Статы Тюремщика |
| `Assets/Art/Enemies/Bosses/Jailer.png` (создать) | Спрайт, отзеркаленный влево |

---

### Task 1: Чистый селектор босса по этажу

**Files:**
- Create: `Assets/Scripts/Data/BossPoolData.cs`
- Create: `Assets/Scripts/Combat/BossPoolSelector.cs`
- Test: `Assets/Tests/EditMode/BossPoolTests.cs`

**Interfaces:**
- Consumes: `MonsterData` (существует).
- Produces: `BossPoolData` c полем `List<BossPoolEntry> entries`; `BossPoolEntry { MonsterData boss; int minFloor; int maxFloor; }`; `BossPoolSelector.Select(BossPoolData pool, int floorNumber, MonsterData fallback, System.Func<int,int> pickIndex = null) → MonsterData`. Task 2 вызывает `Select`.

- [ ] **Step 1: Написать падающий тест**

Создать `Assets/Tests/EditMode/BossPoolTests.cs`:

```csharp
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
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Запустить команду прогона из шапки.
Ожидаемо: компиляция падает с `error CS0246: The type or namespace name 'BossPoolEntry' could not be found`. Проверить текст в `Logs/editmode.log`:

```bash
grep -c "error CS0246" "C:/Unity Projects/DungeonGirls/Logs/editmode.log"
```

- [ ] **Step 3: Написать минимальную реализацию**

Создать `Assets/Scripts/Data/BossPoolData.cs`:

```csharp
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
```

Создать `Assets/Scripts/Combat/BossPoolSelector.cs`:

```csharp
using System.Collections.Generic;

// Выбор босса этажа из пула. Чистая статика без UnityEngine.Random внутри: источник случайности
// приходит параметром (как ICombatRandom в CombatManager), поэтому логика тестируется без сцены и
// без плеймода. Всегда возвращает не-null, если fallback не-null, — вызывающая сторона не обязана
// проверять результат.
public static class BossPoolSelector
{
    // pickIndex получает КОЛИЧЕСТВО подходящих кандидатов и обязан вернуть индекс в [0, count).
    // null = обычный UnityEngine.Random.Range(0, count).
    public static MonsterData Select(BossPoolData pool, int floorNumber, MonsterData fallback,
        System.Func<int, int> pickIndex = null)
    {
        if (pool == null || pool.entries == null || pool.entries.Count == 0)
        {
            return fallback;
        }

        var candidates = new List<MonsterData>();
        foreach (var entry in pool.entries)
        {
            // Пустая строка в списке (boss == null) — обычная ситуация при ручном редактировании
            // ассета в инспекторе, это не ошибка данных: просто пропускаем.
            if (entry == null || entry.boss == null) continue;
            if (floorNumber < entry.minFloor || floorNumber > entry.maxFloor) continue;
            candidates.Add(entry.boss);
        }

        if (candidates.Count == 0)
        {
            return fallback;
        }

        int index = pickIndex != null
            ? pickIndex(candidates.Count)
            : UnityEngine.Random.Range(0, candidates.Count);
        index = UnityEngine.Mathf.Clamp(index, 0, candidates.Count - 1);
        return candidates[index];
    }
}
```

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Запустить команду прогона.
Ожидаемо: exit code 0, **406 passed** (401 baseline + 5 новых). Если падают те же два теста из предусловий — предусловия не выполнены, вернуться к ним.

- [ ] **Step 5: Коммит**

```bash
git add Assets/Scripts/Data/BossPoolData.cs Assets/Scripts/Data/BossPoolData.cs.meta Assets/Scripts/Combat/BossPoolSelector.cs Assets/Scripts/Combat/BossPoolSelector.cs.meta Assets/Tests/EditMode/BossPoolTests.cs Assets/Tests/EditMode/BossPoolTests.cs.meta
git commit -m "feat: пул боссов по этажам — чистый селектор и данные

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Проводка пула в спавн боевой комнаты

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs:27`
- Modify: `Assets/Scripts/UI/RunFlowController.Combat.cs:294-300`

**Interfaces:**
- Consumes: `BossPoolSelector.Select` из Task 1.
- Produces: сериализованное поле `bossPool` на `RunFlowController`; Task 5 заполняет его ассетом.

- [ ] **Step 1: Добавить поле пула рядом с существующим `bossData`**

В `Assets/Scripts/UI/RunFlowController.cs` после строки 27 (`[SerializeField] MonsterData bossData;`):

```csharp
    [SerializeField] MonsterData bossData;
    // Пул боссов этажа (2026-09-10). bossData остаётся как запасной вариант: если пул не заполнен
    // или на этом этаже ни один вариант не подходит, спавнится он — так сцена, собранная до
    // появления пула, продолжает работать без изменений.
    [SerializeField] BossPoolData bossPool;
```

- [ ] **Step 2: Заменить спавн босса на выбор из пула**

В `Assets/Scripts/UI/RunFlowController.Combat.cs` заменить строку 299:

```csharp
            enemies.Add(CombatantFactory.CreateMonsterCombatant(bossData, dungeonManager.CurrentFloorNumber));
```

на:

```csharp
            var bossForFloor = BossPoolSelector.Select(bossPool, dungeonManager.CurrentFloorNumber, bossData);
            enemies.Add(CombatantFactory.CreateMonsterCombatant(bossForFloor, dungeonManager.CurrentFloorNumber));
```

- [ ] **Step 3: Запустить тесты — регрессия не должна появиться**

Запустить команду прогона.
Ожидаемо: exit code 0, 406 passed. Пул ещё пуст, поэтому селектор возвращает `bossData` и поведение боя байт-в-байт прежнее.

- [ ] **Step 4: Коммит**

```bash
git add Assets/Scripts/UI/RunFlowController.cs Assets/Scripts/UI/RunFlowController.Combat.cs
git commit -m "feat: спавн босса этажа через пул с откатом на bossData

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Эффект `DisruptSkills` — блокировка активного навыка игрока

**Files:**
- Modify: `Assets/Scripts/Data/BossKitData.cs` (enum `BossAbilityEffectKind`, класс `BossAbilityConfig`)
- Modify: `Assets/Scripts/Managers/CombatManager.cs` (`ExecuteBossAbility`, новая константа)
- Test: `Assets/Tests/EditMode/BossEncounterTests.cs` (дописать в конец класса)

**Interfaces:**
- Consumes: `CombatManager.DisruptPlayerActiveSkills(float seconds)` — **уже существует**, `CombatManager.cs:144`.
- Produces: `BossAbilityEffectKind.DisruptSkills` (значение 2), поле `BossAbilityConfig.disruptSeconds`, константа `CombatManager.MaxBossSkillDisruptSeconds`. Task 4 использует их в ассете.

- [ ] **Step 1: Написать падающие тесты**

Дописать в конец класса `BossEncounterTests` в `Assets/Tests/EditMode/BossEncounterTests.cs`:

```csharp
    // ---- DisruptSkills (Тюремщик, 2026-09-10) ----

    static ActiveSkillData MakeCooldownSkill(float cooldownSeconds)
    {
        var data = ScriptableObject.CreateInstance<ActiveSkillData>();
        data.skillName = "Тестовый навык";
        data.skillId = SkillId.None; // в SkillId нет отдельного значения для «3 быстрых атак» —
        // Skill_ThreeQuickStrikes.asset тоже хранит skillId: 0. Для этих тестов id не важен: ветки
        // Berserk/SmokeBomb в CombatManager диспатчатся по skillId, а нам нужен нейтральный слот.
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
        cm.Tick(1.2f);   // телеграф истёк — способность резолвится

        Assert.IsFalse(cm.IsSkillReady(0), "после «Кандалов» навык должен быть недоступен");
        Assert.GreaterOrEqual(cm.SkillCooldownRemaining(0), 5f, "блокировка добавляет свои секунды к кулдауну");
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
        // ConfigureActiveSkills намеренно НЕ вызывается: у монстров и у персонажа без изученного
        // активного навыка список пуст, и способность босса не должна на этом падать.

        Assert.DoesNotThrow(() => cm.Tick(0.016f));
    }
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Запустить команду прогона.
Ожидаемо: компиляция падает с `error CS0117: 'BossAbilityEffectKind' does not contain a definition for 'DisruptSkills'`.

- [ ] **Step 3: Добавить значение enum и поле конфига**

В `Assets/Scripts/Data/BossKitData.cs` в `enum BossAbilityEffectKind` дописать **в конец** (значение 2):

```csharp
    // ShieldPool ... (существующий комментарий не трогать)
    ShieldPool,
    // Блокирует активный навык игрока на disruptSeconds (см. CombatManager.DisruptPlayerActiveSkills):
    // Cooldown-навыку добавляются секунды к кулдауну, Toggle гасится и сам возвращается после снятия.
    // Единственная механика роспиcи, целящаяся не в HP игрока, а в его единственный рычаг, поэтому
    // длительность жёстко ограничена сверху — см. CombatManager.MaxBossSkillDisruptSeconds.
    DisruptSkills
```

В том же файле в класс `BossAbilityConfig` дописать поле после `shieldDurationSeconds`:

```csharp
    [Tooltip("DisruptSkills: на сколько секунд блокируется активный навык игрока. Клампится сверху " +
        "потолком CombatManager.MaxBossSkillDisruptSeconds — значение больше потолка не даёт эффекта.")]
    public float disruptSeconds = 5f;
```

- [ ] **Step 4: Добавить константу и `case` в `ExecuteBossAbility`**

В `Assets/Scripts/Managers/CombatManager.cs` рядом с `public const float ThreeQuickStrikesRecoverySeconds` добавить:

```csharp
    // Правило честности роспиcи боссов (Docs/Design/2026-09-10-boss-concepts-roster.md, раздел 0.4):
    // активный навык — единственный рычаг игрока в автобое, поэтому отнимать его дольше чем на 8с
    // нельзя ни одному боссу. Потолок живёт в коде, а не в ассете, чтобы его нельзя было случайно
    // превысить при авторинге контента.
    public const float MaxBossSkillDisruptSeconds = 8f;
```

В `ExecuteBossAbility` добавить `case` после ветки `ShieldPool`:

```csharp
            case BossAbilityEffectKind.DisruptSkills:
                float disruptSeconds = Mathf.Clamp(ability.disruptSeconds, 0f, MaxBossSkillDisruptSeconds);
                // DisruptPlayerActiveSkills сам корректно обрабатывает пустой список слотов и
                // разницу Cooldown/Toggle — здесь только клампим длительность и логируем.
                DisruptPlayerActiveSkills(disruptSeconds);
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName} применяет «{ability.displayName}»: активный навык заблокирован на {disruptSeconds:F0} с.");
                break;
```

- [ ] **Step 5: Запустить тесты и убедиться, что они проходят**

Запустить команду прогона.
Ожидаемо: exit code 0, **409 passed** (406 + 3 новых).

- [ ] **Step 6: Коммит**

```bash
git add Assets/Scripts/Data/BossKitData.cs Assets/Scripts/Managers/CombatManager.cs Assets/Tests/EditMode/BossEncounterTests.cs
git commit -m "feat: способность босса DisruptSkills — блокировка активного навыка игрока

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Ассеты Тюремщика

**Files:**
- Create: `Assets/Art/Enemies/Bosses/Jailer.png`
- Create: `Assets/ScriptableObjects/Bosses/BossKit_Jailer.asset`
- Create: `Assets/ScriptableObjects/Monsters/Monster_Jailer.asset`

**Interfaces:**
- Consumes: `BossAbilityEffectKind.DisruptSkills` = **2** (третье значение enum), `disruptSeconds` из Task 3.
- Produces: ассет `Monster_Jailer` — Task 5 добавляет его в пул.

- [ ] **Step 1: Отзеркалить концепт-спрайт и положить в боевую папку**

Концепт сгенерирован лицом вправо, а вражеская сцена в бою — справа, поэтому спрайт должен смотреть **влево** (см. память проекта `feedback_warden_facing`; рантайм-`flipX` в проекте не используется намеренно).

```bash
python -c "from PIL import Image; im=Image.open(r'C:/Unity Projects/DungeonGirls/Assets/Art/Enemies/Bosses/Concepts/Boss_Jailer.png'); im.transpose(Image.FLIP_LEFT_RIGHT).save(r'C:/Unity Projects/DungeonGirls/Assets/Art/Enemies/Bosses/Jailer.png')"
```

- [ ] **Step 2: Импортировать спрайт в Unity и забрать его GUID**

Запустить команду прогона тестов (любой батч-запуск импортирует новые ассеты и создаёт `.meta`), затем:

```bash
grep -o "guid: [a-f0-9]*" "C:/Unity Projects/DungeonGirls/Assets/Art/Enemies/Bosses/Jailer.png.meta" | head -1
```

Записать GUID — он нужен в Step 3. Проверить, что в `.meta` тип текстуры — Sprite (`textureType: 8`); если нет, выставить его в инспекторе так же, как у `Warden_Phase1.png`.

- [ ] **Step 3: Создать кит Тюремщика**

Создать `Assets/ScriptableObjects/Bosses/BossKit_Jailer.asset`. Числа — из спеки, концепт №8: телеграф «Кандалов» 3 с, блокировка 8 с, кд 18 с; во второй фазе кд 12 с и блокировка 6 с. `m_Script` guid — это guid `BossKitData.cs` (`85d8396258c4c6f479339f1b761e13f9`), скопирован из `BossKit_Warden.asset`. Подставить GUID спрайта из Step 2 вместо `ЗАМЕНИТЬ_НА_GUID_СПРАЙТА` (обе фазы используют один спрайт — у Тюремщика нет второго):

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 85d8396258c4c6f479339f1b761e13f9, type: 3}
  m_Name: BossKit_Jailer
  m_EditorClassIdentifier: DungeonGirls.Runtime::BossKitData
  phases:
  - phaseName: "\u0422\u044E\u0440\u0435\u043C\u0449\u0438\u043A"
    hpThresholdPercent: 100
    phaseSprite: {fileID: 21300000, guid: ЗАМЕНИТЬ_НА_GUID_СПРАЙТА, type: 3}
    animationFolderKey:
    floorPaddingFraction: 0
    abilities:
    - displayName: "\u041A\u0430\u043D\u0434\u0430\u043B\u044B"
      effectKind: 2
      triggerKind: 1
      cooldownSeconds: 18
      initialDelaySeconds: 6
      telegraphSeconds: 3
      damageMultiplier: 1
      shieldAmount: 0
      shieldDurationSeconds: 0
      disruptSeconds: 8
    - displayName: "\u0414\u0443\u0431\u0438\u043D\u043A\u0430"
      effectKind: 0
      triggerKind: 1
      cooldownSeconds: 9
      initialDelaySeconds: 3
      telegraphSeconds: 1.5
      damageMultiplier: 1.7
      shieldAmount: 0
      shieldDurationSeconds: 0
      disruptSeconds: 0
  - phaseName: "\u0421\u0442\u0430\u0440\u0448\u0438\u0439 \u043D\u0430\u0434\u0437\u0438\u0440\u0430\u0442\u0435\u043B\u044C"
    hpThresholdPercent: 45
    phaseSprite: {fileID: 21300000, guid: ЗАМЕНИТЬ_НА_GUID_СПРАЙТА, type: 3}
    animationFolderKey:
    floorPaddingFraction: 0
    abilities:
    - displayName: "\u041A\u0430\u043D\u0434\u0430\u043B\u044B"
      effectKind: 2
      triggerKind: 1
      cooldownSeconds: 12
      initialDelaySeconds: 4
      telegraphSeconds: 3
      damageMultiplier: 1
      shieldAmount: 0
      shieldDurationSeconds: 0
      disruptSeconds: 6
    - displayName: "\u0414\u0443\u0431\u0438\u043D\u043A\u0430"
      effectKind: 0
      triggerKind: 1
      cooldownSeconds: 7
      initialDelaySeconds: 2
      telegraphSeconds: 1.5
      damageMultiplier: 1.7
      shieldAmount: 0
      shieldDurationSeconds: 0
      disruptSeconds: 0
```

- [ ] **Step 4: Создать статы Тюремщика**

Забрать GUID кита из Step 3:

```bash
grep -o "guid: [a-f0-9]*" "C:/Unity Projects/DungeonGirls/Assets/ScriptableObjects/Bosses/BossKit_Jailer.asset.meta" | head -1
```

(`.meta` появится после следующего импорта — если файла ещё нет, сначала выполнить любой батч-запуск.)

Создать `Assets/ScriptableObjects/Monsters/Monster_Jailer.asset`. Статы взяты от `Monster_Boss` (Страж, этажи 1–3) и подняты под этажи 4–6: HP 150 → 210, урон 15–20 → 20–26, броня 12 → 14. `m_Script` guid — guid `MonsterData.cs`:

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 37c73bb7b20f0774db1d325b77fd4879, type: 3}
  m_Name: Monster_Jailer
  m_EditorClassIdentifier: Assembly-CSharp::MonsterData
  monsterName: "\u0422\u044E\u0440\u0435\u043C\u0449\u0438\u043A"
  sprite: {fileID: 21300000, guid: ЗАМЕНИТЬ_НА_GUID_СПРАЙТА, type: 3}
  isBoss: 1
  hp: 210
  damageMin: 20
  damageMax: 26
  damageType: 0
  attackSpeed: 1.1
  physicalDefense: 14
  magicDefense: 0
  universalShield: 0
  frontlinePriority: 0
  passiveSkill: {fileID: 0}
  bossKit: {fileID: 11400000, guid: ЗАМЕНИТЬ_НА_GUID_КИТА, type: 2}
  gender: 0
  minFloorTier: 4

(Если поля `universalShield`/`frontlinePriority` в текущем `MonsterData` отсутствуют — их строки в YAML безвредны, Unity игнорирует неизвестные ключи при десериализации.)
```

- [ ] **Step 5: Написать тест целостности контента**

Ассеты легко испортить руками (не тот guid, не то значение enum), поэтому они проверяются тестом — в проекте уже есть такой паттерн (`ContentSyncTests.cs`). Дописать в `Assets/Tests/EditMode/BossPoolTests.cs`:

```csharp
    [Test]
    public void JailerAsset_IsWiredAndObeysDisruptCap()
    {
        var jailer = UnityEditor.AssetDatabase.LoadAssetAtPath<MonsterData>(
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
                if (ability.effectKind != BossAbilityEffectKind.DisruptSkills) continue;
                foundDisrupt = true;
                Assert.LessOrEqual(ability.disruptSeconds, CombatManager.MaxBossSkillDisruptSeconds,
                    $"«{ability.displayName}» в фазе «{phase.phaseName}» превышает потолок блокировки навыка");
                Assert.GreaterOrEqual(ability.telegraphSeconds, 3f,
                    $"«{ability.displayName}»: блокировка навыка обязана телеграфироваться минимум 3 секунды");
            }
        }

        Assert.IsTrue(foundDisrupt, "у Тюремщика должна быть хотя бы одна способность DisruptSkills");
    }
```

Файл тестов лежит в EditMode-сборке, поэтому `UnityEditor.AssetDatabase` доступен без дополнительных зависимостей (`DungeonGirls.Tests.asmdef` уже собран под редактор — проверить, что `ContentSyncTests.cs` использует тот же вызов, и повторить его форму).

- [ ] **Step 6: Запустить тесты и убедиться, что они проходят**

Запустить команду прогона.
Ожидаемо: exit code 0, **410 passed**.

- [ ] **Step 7: Коммит**

```bash
git add Assets/Art/Enemies/Bosses/Jailer.png Assets/Art/Enemies/Bosses/Jailer.png.meta Assets/ScriptableObjects/Bosses/BossKit_Jailer.asset Assets/ScriptableObjects/Bosses/BossKit_Jailer.asset.meta Assets/ScriptableObjects/Monsters/Monster_Jailer.asset Assets/ScriptableObjects/Monsters/Monster_Jailer.asset.meta Assets/Tests/EditMode/BossPoolTests.cs
git commit -m "feat: Тюремщик — спрайт, статы и кит с Кандалами

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Пул с двумя боссами и проводка сцены

**Files:**
- Create: `Assets/ScriptableObjects/Bosses/BossPool_Main.asset`
- Modify: сцена (поле `bossPool` на объекте с `RunFlowController`)
- Test: `Assets/Tests/EditMode/BossPoolTests.cs`

**Interfaces:**
- Consumes: `BossPoolData` (Task 1), `Monster_Jailer` (Task 4), существующий `Monster_Boss`.

- [ ] **Step 1: Написать падающий тест на реальный ассет пула**

Дописать в `Assets/Tests/EditMode/BossPoolTests.cs`:

```csharp
    [Test]
    public void MainBossPool_CoversEveryFloorAndHasNoEmptyEntries()
    {
        var pool = UnityEditor.AssetDatabase.LoadAssetAtPath<BossPoolData>(
            "Assets/ScriptableObjects/Bosses/BossPool_Main.asset");

        Assert.IsNotNull(pool, "BossPool_Main.asset не найден");
        Assert.IsNotEmpty(pool.entries);

        foreach (var entry in pool.entries)
        {
            Assert.IsNotNull(entry.boss, "в пуле не должно быть пустых строк");
            Assert.IsTrue(entry.boss.isBoss, $"{entry.boss.monsterName} в пуле боссов, но isBoss=false");
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
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Запустить команду прогона.
Ожидаемо: exit code 2, падает `MainBossPool_CoversEveryFloorAndHasNoEmptyEntries` с «BossPool_Main.asset не найден».

- [ ] **Step 3: Создать ассет пула**

Забрать GUID `Monster_Boss.asset` и `Monster_Jailer.asset`:

```bash
grep -o "guid: [a-f0-9]*" "C:/Unity Projects/DungeonGirls/Assets/ScriptableObjects/Monsters/Monster_Boss.asset.meta" | head -1
grep -o "guid: [a-f0-9]*" "C:/Unity Projects/DungeonGirls/Assets/ScriptableObjects/Monsters/Monster_Jailer.asset.meta" | head -1
```

Забрать GUID `BossPoolData.cs`:

```bash
grep -o "guid: [a-f0-9]*" "C:/Unity Projects/DungeonGirls/Assets/Scripts/Data/BossPoolData.cs.meta" | head -1
```

Создать `Assets/ScriptableObjects/Bosses/BossPool_Main.asset`. Страж покрывает 1–10 (чтобы тест на покрытие этажей проходил уже сейчас — остальные боссы роспиcи сузят его диапазон по мере появления), Тюремщик — 4–6 по спеке:

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: ЗАМЕНИТЬ_НА_GUID_BossPoolData, type: 3}
  m_Name: BossPool_Main
  m_EditorClassIdentifier:
  entries:
  - boss: {fileID: 11400000, guid: ЗАМЕНИТЬ_НА_GUID_Monster_Boss, type: 2}
    minFloor: 1
    maxFloor: 10
  - boss: {fileID: 11400000, guid: ЗАМЕНИТЬ_НА_GUID_Monster_Jailer, type: 2}
    minFloor: 4
    maxFloor: 6
```

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Запустить команду прогона.
Ожидаемо: exit code 0, **411 passed**.

- [ ] **Step 5: Привязать пул в сцене**

Открыть сцену в редакторе, выбрать объект с `RunFlowController`, в поле **Boss Pool** положить `BossPool_Main`. Поле `Boss Data` **не очищать** — оно остаётся запасным вариантом.

Это единственный шаг плана, который нельзя сделать из батчмода. Проверка — Step 6.

- [ ] **Step 6: Прогнать плеймод-смоук**

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -projectPath "C:/Unity Projects/DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile "C:/Unity Projects/DungeonGirls/Logs/smoke.log"
```

**Перед запуском сделать бэкап файла сохранения** — смоук-тест затирает прогресс (см. память проекта `feedback_smoketest_eats_save`).
Ожидаемо в `Logs/smoke.log`: `RESULT=PASS`.

- [ ] **Step 7: Коммит**

```bash
git add Assets/ScriptableObjects/Bosses/BossPool_Main.asset Assets/ScriptableObjects/Bosses/BossPool_Main.asset.meta Assets/Tests/EditMode/BossPoolTests.cs Assets/Scenes
git commit -m "feat: пул боссов с Тюремщиком на этажах 4-6

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Критерии готовности плана

- [ ] EditMode: **411/411** (401 baseline + 10 новых), exit code 0
- [ ] PlayModeSmokeTest: `RESULT=PASS`
- [ ] На этажах 4–6 в боевой комнате босса может выпасть Тюремщик, на остальных — Страж
- [ ] «Кандалы» телеграфируются 3 секунды и блокируют активный навык не дольше 8 секунд у всех трёх персонажей
- [ ] Ручной плейтест: пройти этаж 4–6 каждым из троих и убедиться, что блокировка читается как задача, а не как отобранный геймплей

**Ручной плейтест обязателен и не заменяется тестами** — Тюремщик единственный босс, чьё качество определяется исключительно ощущением, а не числами.
