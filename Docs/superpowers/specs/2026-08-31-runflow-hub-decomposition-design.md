# Декомпозиция RunFlowController/HubManager — Design Spec

**Дата:** 2026-08-31
**Контекст:** Вторая итерация по устранению проблем из архитектурного ревью 2026-08-31 (первая — asmdef/
`SkillId`/тесты, см. [2026-08-31-engineering-foundation-design.md](2026-08-31-engineering-foundation-design.md)).
Целевые файлы: `RunFlowController.cs` (2454 строки на момент написания) и `HubManager.cs` (630 строк) —
оба god-object'ы, смешивающие UI Toolkit-оркестрацию с бизнес-правилами.

## Цель этой итерации

1. Разбить оба файла на физически меньшие, тематически цельные куски — без изменения поведения.
2. Заодно вынести несколько чистых вычислительных функций, спрятанных внутри UI-оркестрации, в те
   static-классы, где им место по домену — и покрыть тестами (тот же принцип, что и в первой итерации:
   "любая новая/перемещённая чистая логика получает EditMode-тест").

## Подход: механическое разбиение, не реструктуризация

Выбран `partial class` — тот же класс, те же приватные поля, то же поведение, только физическое
разбиение на несколько файлов по тематическим секциям (уже промаркированным в коде комментариями
`// ==================== X ====================`). Это **не** введение слоя presenter-классов
со своим состоянием и интерфейсами — такая более глубокая декомпозиция обсуждалась и отклонена в пользу
низкого риска: текущий геймплей должен продолжать работать идентично, без риска сломать хрупкие
coroutine-цепочки в процессе рефакторинга.

**Не в скоупе:** изменение сигнатур публичных методов, порядка выполнения, UXML/USS, введение
интерфейсов/DI, разбиение самого состояния (полей) на отдельные объекты.

## Компонент 1: `RunFlowController` → 6 файлов

Все файлы — `public partial class RunFlowController : MonoBehaviour`, физически в
`Assets/Scripts/UI/`.

- **`RunFlowController.cs`** (ядро) — все текущие field-декларации (без изменений), `OnEnable`,
  `OnDisable`, `Update`, `IsRunInProgress`, `TryPlayRunVNScene`/`OnRunVNSceneCompleted`, `PauseRun`/
  `ResumeRun`/`AbandonRunFromPause`/`QuitGame`/`RefreshPauseInfo`/`AddPauseLine`, экраны выбора
  персонажа/наставника (`OpenCharacterSelect` … `ReturnToMainMenu`), `CacheElements`, `RunLoop`/
  `ResolveRoom` (главный цикл), общие UI-хелперы (`LogEvent`/`RefreshRunLog`, `ShowOnly`,
  `WaitForClick`/`WaitForAnyClick`, `UpdateTopBar`, `BindStaticTutorialTooltips`) — используются
  всеми остальными partial-файлами, поэтому остаются в ядре.
- **`RunFlowController.Combat.cs`** — вложенный класс `EnemyStageEntry`, `RollMonsterCount`
  (тонкая обёртка над перенесённым `MonsterEncounterBudget.RollMonsterCount`, см. Компонент 3),
  `ResolveActiveSkillHitCount`-вызов (обёртка над `CombatManager.ResolveActiveSkillHitCount`),
  `CombatRoomFlow`, `UnsubscribeCombatEvents`, `OnCombatLog`, `UpdateCombatUI`, `BuildEnemyStageEntries`,
  `PopulateStatusContainer`, `UpdateStatusLabel`, `FindStageWrapper`, `OnHitResolved`,
  `SpawnFloatingCombatText`, `FloatAndFadeOut`, `OnActiveSkillActivated`, `ShowSkillBanner`,
  `GetStageFloorGapFromBottom` (тонкая обёртка над перенесённым `ComputeStageFloorGap`, см.
  Компонент 3).
- **`RunFlowController.Rooms.cs`** — `TrapRoomFlow`, `ShowChancePopupAndWait`, `PickQuestForFloor`
  (тонкая обёртка над перенесённым `QuestCatalog.PickForFloor`), `EventRoomFlow`,
  `TryReservePersonalRestRoom`, `PersonalRestRoomFlow`, `MerchantRoomFlow`, `SetRarityClass`.
- **`RunFlowController.Progression.cs`** — `LevelUpFlow`, `CampOfferAndPhaseCoroutine`,
  `SetCampOfferButtonsVisible`, `CampPhaseCoroutine`, `TryPlayCampSceneAfterRation`.
- **`RunFlowController.Reward.cs`** — `ShowRewardChestFlow`, `ShowRewardOverlay`, `HideRewardOverlay`,
  `ChestRevealFlow`, `ChestReelBgClassFor`, `ItemComparisonSummary`, `ItemCompareFlow`.
- **`RunFlowController.Results.cs`** — `ShowResultsFlow`, `BuildResultsText`, `BuildVeteranSnapshot`,
  `ApplySelectedMentorInheritance`, `FindPassiveSkill`.

## Компонент 2: `HubManager` → 4 файла

Все файлы — `public partial class HubManager : MonoBehaviour`, физически в
`Assets/Scripts/Managers/`.

- **`HubManager.cs`** (ядро) — field-декларации, `Start`, `OnDestroy`, `StartOpeningSequence`,
  `OnVNSceneCompleted`, `CacheElements`.
- **`HubManager.Navigation.cs`** — `OpenCheatMenu`, `CloseCheatMenu`, `SubmitCheatCommand`,
  `QuitGame`, `BindTutorialTooltips`, `ConfirmResetProgress`.
- **`HubManager.Buildings.cs`** — `RefreshBuildingsScreen`, `TryUpgradeBuilding`.
- **`HubManager.Gacha.cs`** — `RefreshGachaScreen`, `TryPullGacha`, `GachaPullFlow`,
  `HasValidGachaCharacterPool`, `ReelBackgroundClass`.

## Компонент 3: Вынос чистой логики (с EditMode-тестами)

- **`MonsterEncounterBudget.RollMonsterCount(int level) : int`** — перенос текущего
  `RunFlowController.RollMonsterCount` тела один в один (пороги 1/2/5 уровня, `Random.Range`).
  `RunFlowController` вызывает `MonsterEncounterBudget.RollMonsterCount(characterManager.Level)`
  вместо собственного метода.
- **`CombatManager.ResolveActiveSkillHitCount(CharacterClass) : int`** — перенос текущего `static`
  метода `RunFlowController.ResolveActiveSkillHitCount` без изменений тела (уже `public static`,
  переносится как есть). `RunFlowController` вызывает `CombatManager.ResolveActiveSkillHitCount(...)`.
- **`QuestCatalog.PickForFloor(int floor, bool huntAlreadyTriggered, bool swordAlreadySucceeded) : QuestDefinition`**
  — перенос решающей таблицы из `RunFlowController.PickQuestForFloor`, включая ветку "Добыча" на
  `Random.value < 0.20f` (побочный эффект `huntQuestTriggeredThisRun = true` остаётся во
  `RunFlowController` — метод только решает, `RunFlowController` управляет флагом по возвращаемому
  значению: если результат — `QuestCatalog.Hunt`, `RunFlowController` сам выставляет флаг).
- **`RunFlowController.ComputeStageFloorGap(float boxWidth, float boxHeight) : float`** — чистая
  часть текущего `GetStageFloorGapFromBottom` (используются константы
  `combatBackgroundImageWidth/Height/FloorRowFromTop`, уже `const` на классе). Остаётся в
  `RunFlowController.Combat.cs` как `static`, не переносится в другой класс (специфична для этого
  экрана) — но становится тестируемой как чистая функция примитивов, без чтения `resolvedStyle`.

## Риски и как проверяем

- **Риск:** `partial class` разбиение технически некорректно (дубли имён, потерянные `using`,
  порядок статических конструкторов/полей). **Митигация:** после каждого файла — компиляция
  batchmode (0 ошибок) прежде чем переходить к следующему.
- **Риск:** перенос чистой логики случайно меняет поведение (например, `RollMonsterCount` копируется
  не дословно). **Митигация:** тело метода переносится буквально (copy-paste), без "улучшений" по
  ходу; после переноса — `PlayModeSmokeTest`/`NarrativeSmokeTest` подтверждают отсутствие регресса
  (те же 413/32 OK, что и в предыдущей итерации).
- **Риск:** разбиение на файлы меняет поведение сериализации `[SerializeField]`-полей в инспекторе
  Unity. **Митигация:** поля не перемещаются из своих текущих деклараций и не переименовываются — они
  все остаются в файле-ядре (`RunFlowController.cs`/`HubManager.cs`), поэтому Unity видит тот же
  список сериализуемых полей на том же классе; проверяется тем же batchmode-компиляционным прогоном
  плюс визуальной проверкой, что `Assets/Scenes/SampleScene.unity` не помечает компонент как "Missing"
  после разбиения (открывается автоматически в рамках `PlayModeSmokeTest`).

## Приёмка итерации

- `RunFlowController.cs` (ядро) и каждый из 5 partial-файлов — не более ~500 строк каждый.
- `HubManager.cs` (ядро) и каждый из 3 partial-файлов — компилируются без ошибок.
- 3 новых чистых функции перенесены (`MonsterEncounterBudget.RollMonsterCount`,
  `CombatManager.ResolveActiveSkillHitCount`, `QuestCatalog.PickForFloor`) + `ComputeStageFloorGap`
  выделена как чистая — все 4 покрыты EditMode-тестами (happy path + 1 граничный случай каждая).
- `PlayModeSmokeTest`/`NarrativeSmokeTest` проходят с тем же результатом (413 OK / 32 OK, 0 ошибок),
  что подтверждает отсутствие поведенческого регресса.
