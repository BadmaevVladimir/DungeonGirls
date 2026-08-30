# Инженерный фундамент: asmdef, тесты, stable skill ID — Design Spec

**Дата:** 2026-08-31
**Контекст:** По итогам архитектурного ревью всего проекта (57 C#-файлов, ~9.6k строк, см. отчёт в чате
2026-08-31) выявлены системные проблемы: отсутствие тестов и границ сборки, навыки идентифицируются
по русским строкам (`skillName`) вместо стабильных ID, дублирование форматтеров между
`RunFlowController`/`HubManager`. Это первая из нескольких итераций по устранению найденных проблем —
берём **безопасный фундамент**, не трогая декомпозицию `RunFlowController`/`HubManager` (2580/637
строк) — это отдельная, более рискованная итерация на будущее.

## Цели этой итерации

1. Дать проекту физическую границу сборки и возможность писать/гонять модульные тесты на чистую
   логику без запуска сцены.
2. Закрыть регрессом уже однажды ломавшийся класс багов — сопоставление сущностей по строке/
   displayName вместо стабильного ключа (см. `SaveManager.MigrateCharacterKey`, где это уже случилось
   с `characterId`).
3. Убрать конкретно то же самое хрупкое место у навыков: `SkillEffectMap`/`MonsterSkillEffectMap`
   сопоставляют боевую логику с `PassiveSkillData.skillName`/`ActiveSkillData.skillName` — обычной
   строкой из инспектора. Переименование ассета в редакторе молча ломает бой без единой ошибки
   компиляции.
4. Убрать точечное дублирование форматтеров UI.

## Не в скоупе (явно)

- Декомпозиция `RunFlowController`/`HubManager` на слои/View-классы.
- Strategy-паттерн (`ISkillEffect`) для эффектов навыков — switch/if-ветки в `CombatManager`/
  `CombatantFactory` остаются, только переключаются со строкового сравнения на `enum SkillId`.
- Миграция `VeteranCharacter.uniquePassiveSkillName`/`MentorUniquePassiveSkillName` (строки,
  персистентные в `SaveData`, унаследованы от системы наставников) — это отдельный, более рискованный
  кусок, трогающий формат сохранения; не путать с `SkillEffectMap`, который это не затрагивает.
- Батчинг записи `SaveManager.SaveGame()` — риск/выгода не оправдан в этой итерации.
- `MagnumOpus`/`ThreeQuickStrikes` — единственные `PassiveSkillData`/`ActiveSkillData`, у которых нет
  ни одного места сравнения по `skillName` в коде (используются только через
  `characterClass`-ветвление) — им `SkillId` не нужен для этой итерации.

## Компонент 1: Assembly Definitions

- `Assets/Scripts/DungeonGirls.Runtime.asmdef` — весь текущий рантайм-код (`Assets/Scripts/**`).
  Без ссылок на `UnityEditor`.
- `Assets/Tests/DungeonGirls.Tests.asmdef` — новая папка `Assets/Tests/EditMode/`, ссылки:
  `DungeonGirls.Runtime`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner` (testable only),
  define constraints `UNITY_INCLUDE_TESTS`. EditMode-only (без Play Mode assembly) — тестируем чистую
  логику, не полный геймплей-цикл.
- Существующий `Assets/Editor/*.cs` (`PlayModeSmokeTest.cs`, `NarrativeSmokeTest.cs`) остаётся как
  есть, вне asmdef-графа (компилируется в default Editor assembly) — не переносим, не меняем: они уже
  работают через `-executeMethod` и есть договорённость держать их как "разовые диагностические
  скрипты" (см. их собственные комментарии).

**Проверка:** проект должен компилироваться без ошибок в Unity Editor после добавления обоих asmdef
(`Assets/Editor` скрипты не входят ни в один asmdef и по умолчанию ссылаются на все asmdef без
`"Editor"`-платформы, значит `PlayModeSmokeTest.cs` продолжит видеть `DungeonGirls.Runtime` — если это
не так, `Assets/Editor` тоже получит свой `.asmdef` со ссылкой на `DungeonGirls.Runtime`).

## Компонент 2: Stable `SkillId` вместо строкового `skillName`

- Новый `enum SkillId` в `Data/Enums.cs`: одно значение на каждую константу из `SkillEffectMap.cs`
  (35 значений) и `MonsterSkillEffectMap.cs` (7 значений: `SlowCurse`, `Fluttering`,
  `ArmorPiercingBlade`, `Corrosion`, `StunningScream`, `DarkHeal`, `DoubleStrike`). Имена констант в
  двух картах не пересекаются (проверено) — единый `enum` без коллизий имён. Плюс `SkillId.None = 0`
  как дефолт для ассетов вне обеих карт (`MagnumOpus`, `ThreeQuickStrikes`, и т.д.).
- Поле `public SkillId skillId;` на `PassiveSkillData` и `ActiveSkillData`.
- `SkillEffectMap`/`MonsterSkillEffectMap` не удаляются (нужны как источник рус. названий для UI/
  тултипов), но перестают быть точкой сравнения в игровой логике — добавляется `SkillId → string`
  функция отображения (или оставляем текущие константы как есть для текста, они не проблема сами по
  себе — проблема была в СРАВНЕНИИ по ним).
- Замена мест сравнения (найдено 5 файлов: `CombatManager.cs`, `CombatantFactory.cs`,
  `RunFlowController.cs`, `CampManager.cs`, `MonsterSkillEffectMap.cs`-потребители) — все
  `== SkillEffectMap.X` / `== MonsterSkillEffectMap.X` на `== SkillId.X`, начиная с чтения
  `passiveSkillData.skillId`/`activeSkillData.skillId` вместо `.skillName`.
- **Заполнение `skillId` на существующих 46 `.asset`-файлах:** редакторский one-shot скрипт
  (`Assets/Editor/AssignSkillIdsFromNames.cs`, temporary — как `PlayModeSmokeTest`, тоже одноразовый,
  документируется как таковой), который по текущему `skillName` каждого ассета находит совпадающую
  константу в `SkillEffectMap`/`MonsterSkillEffectMap`, проставляет `skillId`, логирует ассеты без
  совпадения (ожидаемо: `MagnumOpus`, `ThreeQuickStrikes` — остаются `SkillId.None`), сохраняет через
  `AssetDatabase.SaveAssets()`. Прогоняется один раз через `-batchmode -executeMethod`, результат
  (изменённые `.asset`-файлы) коммитится в git как обычные правки данных.

**Почему не переписывать все 46 ассетов вручную:** правки `.asset`-YAML вручную для 46 файлов —
источник опечаток; скрипт детерминированно сопоставляет с уже существующей картой имён, которая
и так должна быть 1:1 с ассетами (иначе она бы не работала сегодня).

## Компонент 3: EditMode-тесты на уже чистую логику

Классы уже `static`/без `MonoBehaviour`, тестируем как есть, без рефакторинга самого кода:

- `DamageCalculator`
- `SuccessChanceCalculator`
- `BalanceClamps`
- `GachaCopyBonusCalculator`
- `MonsterEncounterBudget`
- `VeteranSystem` (в первую очередь `GradeForFloors`, `IsEligibleMentor`, `RollTransferredSkills`)
- `StatScaling`
- `SaveManager.MigrateIfNeeded`/`MigrateCharacterKey`/`NormalizeCharacterId` (статические методы,
  тестируемые без экземпляра `MonoBehaviour` — `SaveData` создаётся напрямую в тесте)

Каждый файл теста — happy path + 1-2 граничных случая (не exhaustive coverage, это фундамент, не
полное покрытие). Стиль ассертов — обычный NUnit (`Assert.AreEqual` и т.д.), не самодельный `Check()`
как в `PlayModeSmokeTest` — это Unity Test Framework, другой раннер.

## Компонент 4: Дедупликация форматтеров

Новый `static class DisplayFormat` (`Assets/Scripts/UI/DisplayFormat.cs` — единственный новый
non-test файл вне Data/Combat): `CharacterClassDisplayName`, `SlotLabel`, `ItemStatsText`, перенесённые
из `RunFlowController` без изменения логики форматирования. `HubManager` и `RunFlowController` зовут
`DisplayFormat.X` вместо приватных копий/единственной копии в `RunFlowController`.

## Notion: страница Engineering Guidelines

Отдельная страница рядом с GDD в Notion. Разделы: Data-driven через ScriptableObject (никаких
хардкод-таблиц контента в коде — уже соблюдается, фиксируем как принцип), Stable ID вместо
строк/displayName для игровой логики (со ссылкой на оба прецедента — миграция `characterId` в
`SaveManager` и эта итерация с `SkillId`), разделение чистой логики (testable, без `UnityEngine`,
кроме `Mathf`/векторов при необходимости) от `MonoBehaviour`-оркестрации, обязательный EditMode-тест
для новой чистой логики при добавлении, транзакционность `SaveManager` (одна логическая мутация
`Data` → один `SaveGame()`), где искать существующие правила (эта спека +
`Docs/superpowers/plans/*`).

## Риски и как проверяем

- **Риск:** генератор `SkillId` из имён пропускает ассет (опечатка в `skillName` vs константе карты).
  **Митигация:** скрипт логирует каждый ассет без совпадения — до коммита проверяем список руками,
  сверяя с 46 найденными `.asset`-файлами.
- **Риск:** asmdef-границы ломают существующую компиляцию (например, `Assets/Editor` скрипты не
  видят рантайм-типы). **Митигация:** после добавления asmdef — полная перекомпиляция в Unity Editor
  батчмодом (`-batchmode -nographics -quit`), проверка на 0 ошибок компиляции, затем повторный прогон
  `PlayModeSmokeTest`/`NarrativeSmokeTest` (уже существующая проверка сквозного геймплей-флоу).
- **Риск:** замена строкового сравнения на `SkillId` меняет поведение по ошибке (например, забыт один
  из 5 файлов). **Митигация:** `Grep` по `SkillEffectMap\.` и `MonsterSkillEffectMap\.` до и после —
  список мест сравнения должен свестись к нулю вне самих файлов карт и вспомогательных text-функций.

## Приёмка итерации

- Проект компилируется 0 ошибок с новыми asmdef.
- Новые EditMode-тесты (Компонент 3) — все зелёные через `-runTests -testPlatform EditMode`.
- `PlayModeSmokeTest`/`NarrativeSmokeTest` — все проверки по-прежнему проходят (сквозной геймплей не
  сломан миграцией `SkillId`).
- Страница Engineering Guidelines создана в Notion, содержит все 5 принципов из раздела выше.
