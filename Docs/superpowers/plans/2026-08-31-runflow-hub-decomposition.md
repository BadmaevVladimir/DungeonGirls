# RunFlowController/HubManager Decomposition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `RunFlowController.cs` (2454 lines) and `HubManager.cs` (630 lines) into `partial class`
files by thematic section — same class, same fields, same behavior, only physical file boundaries —
and extract 4 pure functions hidden inside them into their proper domain classes, each with an
EditMode test.

**Architecture:** `partial class` split, zero behavior change. Method bodies move verbatim
(copy-paste, no rewriting) from the current file into new sibling files in the same folder, all
declaring `public partial class RunFlowController : MonoBehaviour` (or `HubManager`). All fields stay
in the core file (`RunFlowController.cs`/`HubManager.cs`) so Unity's Inspector serialization is
unaffected. 4 pure functions currently embedded in `RunFlowController` move to
`MonsterEncounterBudget`, `CombatManager`, `QuestCatalog`, and a new static helper on
`RunFlowController.Combat.cs` itself.

**Tech Stack:** Unity 6000.5.8f1, C#, UI Toolkit, Unity Test Framework (EditMode).

**Spec:** `Docs/superpowers/specs/2026-08-31-runflow-hub-decomposition-design.md`

## Global Constraints

- Every method body that moves is copied **verbatim** — no logic changes, no "while I'm here" fixes.
  If a genuine bug is spotted during the move, note it but do not fix it in this plan.
- Every task ends with a batchmode compile check (0 `error CS` lines) before moving to the next task.
- Field declarations, `[SerializeField]` attributes, and their order in the core file are never
  touched — Unity Inspector serialization must see the exact same field list on the exact same class
  after every task.
- Final task reruns `PlayModeSmokeTest`/`NarrativeSmokeTest` and expects the same 413/32 OK, 0 errors
  as the previous iteration (2026-08-31 engineering-foundation) — any regression blocks completion.
- Source line numbers below refer to `Assets/Scripts/UI/RunFlowController.cs` and
  `Assets/Scripts/Managers/HubManager.cs` as committed at the start of this plan (commit `8c829a7`).
  Re-fetch current line numbers with `grep -n "^    "` before each task if the file has already been
  edited by an earlier task in this plan, since line numbers shift after each extraction.

---

## Task 1: Extract `RunFlowController.Combat.cs`

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs` (remove extracted members, add `partial` modifier)
- Create: `Assets/Scripts/UI/RunFlowController.Combat.cs`

**Members to move** (verbatim, in this order), currently at these line ranges:
- `class EnemyStageEntry` (99-105) — **do not move**, stays in core (referenced by the
  `enemyStageEntries` field which stays in core). Instead, only its *usage* sites move; the nested
  class declaration itself stays with the field in `RunFlowController.cs`.
- `int RollMonsterCount(int level)` (866-878) — becomes a **thin wrapper**:
  `int RollMonsterCount(int level) => MonsterEncounterBudget.RollMonsterCount(level);` (Task 6 creates
  the real implementation there first — do Task 6 before this task, or leave the full body here
  temporarily and thin it out when Task 6 runs; **this plan executes Task 6 before Task 1** to avoid
  a two-step edit of the same method).
- `IEnumerator CombatRoomFlow(bool isBoss)` (885-1046)
- `void UnsubscribeCombatEvents()` (1047-1053)
- `void OnCombatLog(string message)` (1054-1061)
- `void UpdateCombatUI()` (1079-1191)
- `void BuildEnemyStageEntries(List<CombatantRuntime> enemies)` (1192-1229)
- `void PopulateStatusContainer(VisualElement container, CombatantRuntime combatant, bool hideStealth = false)` (1230-1248)
- `void UpdateStatusLabel(Label label, CombatantRuntime combatant)` (1249-1261)
- `VisualElement FindStageWrapper(CombatantRuntime combatant)` (1262-1281)
- `void OnHitResolved(...)` (1282-1301)
- `IEnumerator SpawnFloatingCombatText(...)` (1302-1323)
- `IEnumerator FloatAndFadeOut(VisualElement label)` (1324-1346)
- `void OnActiveSkillActivated(CombatantRuntime user, string skillName)` (1347-1355)
- `IEnumerator ShowSkillBanner(string skillName)` (1356-1387)
- `float GetStageFloorGapFromBottom()` (1402-1436) — becomes a thin wrapper calling the new
  `ComputeStageFloorGap` static (Task 9 creates it; **this plan executes Task 9 before Task 1** for
  the same reason as `RollMonsterCount` above).
- The `public static int ResolveActiveSkillHitCount(CharacterClass characterClass)` method
  (879-884) is **deleted entirely** from `RunFlowController` (not moved as a wrapper — it's `public
  static`, so its only call site, in `CombatRoomFlow`, is updated to call
  `CombatManager.ResolveActiveSkillHitCount(...)` directly; Task 7 creates that method — **executed
  before this task**).

**Interfaces:**
- Consumes: `MonsterEncounterBudget.RollMonsterCount` (Task 6), `CombatManager.ResolveActiveSkillHitCount`
  (Task 7), `RunFlowController.ComputeStageFloorGap` (Task 9, same partial file).
- Produces: nothing new — purely a file-location change of existing private/internal members.

- [ ] **Step 1 (prerequisite): Confirm Tasks 6, 7, 9 are already done**

This task assumes `MonsterEncounterBudget.RollMonsterCount`, `CombatManager.ResolveActiveSkillHitCount`,
and the pure `ComputeStageFloorGap` split already exist (see the execution order note in Task 6/7/9 —
do those three tasks first, in numeric order, before this task).

- [ ] **Step 2: Add `partial` to the class declaration**

In `Assets/Scripts/UI/RunFlowController.cs`, change:
```csharp
public class RunFlowController : MonoBehaviour
```
to:
```csharp
public partial class RunFlowController : MonoBehaviour
```

- [ ] **Step 3: Create `RunFlowController.Combat.cs` with the moved members**

```csharp
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Бой (раздел 4, 7.2) ====================

    int RollMonsterCount(int level) => MonsterEncounterBudget.RollMonsterCount(level);

    IEnumerator CombatRoomFlow(bool isBoss)
    {
        // ... (verbatim body from current lines 885-1046, with ONE change: the line
        // `int hitCount = ResolveActiveSkillHitCount(activeCharacter.characterClass);`
        // becomes `int hitCount = CombatManager.ResolveActiveSkillHitCount(activeCharacter.characterClass);`)
    }

    void UnsubscribeCombatEvents() { /* verbatim, lines 1047-1053 */ }
    void OnCombatLog(string message) { /* verbatim, lines 1054-1061 */ }
    void UpdateCombatUI() { /* verbatim, lines 1079-1191 */ }
    void BuildEnemyStageEntries(List<CombatantRuntime> enemies) { /* verbatim, lines 1192-1229 */ }
    void PopulateStatusContainer(VisualElement container, CombatantRuntime combatant, bool hideStealth = false) { /* verbatim, lines 1230-1248 */ }
    void UpdateStatusLabel(Label label, CombatantRuntime combatant) { /* verbatim, lines 1249-1261 */ }
    VisualElement FindStageWrapper(CombatantRuntime combatant) { /* verbatim, lines 1262-1281 */ }
    void OnHitResolved(CombatantRuntime target, float damageToHP, bool isCrit, bool wasBlocked) { /* verbatim, lines 1282-1301 */ }
    IEnumerator SpawnFloatingCombatText(VisualElement wrapper, string text, bool isCrit, bool isBlock) { /* verbatim, lines 1302-1323 */ }
    IEnumerator FloatAndFadeOut(VisualElement label) { /* verbatim, lines 1324-1346 */ }
    void OnActiveSkillActivated(CombatantRuntime user, string skillName) { /* verbatim, lines 1347-1355 */ }
    IEnumerator ShowSkillBanner(string skillName) { /* verbatim, lines 1356-1387 */ }

    const float combatBackgroundImageWidth = 1536f;
    const float combatBackgroundImageHeight = 1024f;
    const float combatBackgroundFloorRowFromTop = 797f;

    float GetStageFloorGapFromBottom()
    {
        float boxWidth = combatPanel.resolvedStyle.width;
        float boxHeight = combatPanel.resolvedStyle.height;
        if (boxWidth <= 0f || boxHeight <= 0f) return 0f;
        return ComputeStageFloorGap(boxWidth, boxHeight);
    }

    // ComputeStageFloorGap itself is added here by Task 9, before this task runs.
}
```

Use Read on the current `Assets/Scripts/UI/RunFlowController.cs` at each cited line range to copy the
exact current body — the placeholders above (`/* verbatim, lines N-M */`) mark copy-paste boundaries,
they are not code to write literally. Every comment inside the moved bodies (the extensive Russian
design-rationale comments) moves along with its method — do not drop them.

- [ ] **Step 4: Delete the moved members from `RunFlowController.cs`**

Remove each moved member's full body from the core file, plus the `ResolveActiveSkillHitCount` method
entirely (not moved, deleted — its single caller in `CombatRoomFlow` now calls
`CombatManager.ResolveActiveSkillHitCount` directly, per Step 3 above).

- [ ] **Step 5: Compile-check**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "C:/Unity Projects/DungeonGirls" -logFile "C:/Unity Projects/DungeonGirls/unity_task1.log"
grep "error CS" "C:/Unity Projects/DungeonGirls/unity_task1.log"
```
Expected: no `error CS` lines.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/UI/RunFlowController.cs" "Assets/Scripts/UI/RunFlowController.Combat.cs" "Assets/Scripts/UI/RunFlowController.Combat.cs.meta"
git commit -m "refactor: extract RunFlowController.Combat.cs from RunFlowController"
```

---

## Task 2: Extract `RunFlowController.Rooms.cs`

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Create: `Assets/Scripts/UI/RunFlowController.Rooms.cs`

**Members to move** (re-fetch line numbers with `grep -n "^    " Assets/Scripts/UI/RunFlowController.cs`
first — Task 1 shifted everything after line 866):
- `IEnumerator TrapRoomFlow()`
- `IEnumerator ShowChancePopupAndWait(...)`
- `QuestDefinition PickQuestForFloor(int floor)` — becomes a thin wrapper:
  ```csharp
  QuestDefinition PickQuestForFloor(int floor)
  {
      var quest = QuestCatalog.PickForFloor(floor, huntQuestTriggeredThisRun, swordInStoneSucceededThisRun);
      if (quest == QuestCatalog.Hunt) huntQuestTriggeredThisRun = true;
      return quest;
  }
  ```
  (Task 8 must run before this task — creates `QuestCatalog.PickForFloor`.)
- `IEnumerator EventRoomFlow()`
- `bool TryReservePersonalRestRoom()`
- `IEnumerator PersonalRestRoomFlow()`
- `IEnumerator MerchantRoomFlow()`
- `static void SetRarityClass(VisualElement element, ItemTier tier)`

- [ ] **Step 1 (prerequisite): Confirm Task 8 is already done** (see Task 8 for
  `QuestCatalog.PickForFloor`).

- [ ] **Step 2: Create `RunFlowController.Rooms.cs`**

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Ловушка (5.5) и квесты TryOrSkip (5.4) — общий попап ====================

    IEnumerator TrapRoomFlow() { /* verbatim */ }
    IEnumerator ShowChancePopupAndWait(string description, int level, string successText, string failText, string attemptLabel, string skipLabel, string skipOutcome = null) { /* verbatim */ }

    // ==================== Особая комната / квест (5.3-5.4) ====================

    QuestDefinition PickQuestForFloor(int floor)
    {
        var quest = QuestCatalog.PickForFloor(floor, huntQuestTriggeredThisRun, swordInStoneSucceededThisRun);
        if (quest == QuestCatalog.Hunt) huntQuestTriggeredThisRun = true;
        return quest;
    }

    IEnumerator EventRoomFlow() { /* verbatim */ }
    bool TryReservePersonalRestRoom() { /* verbatim */ }
    IEnumerator PersonalRestRoomFlow() { /* verbatim */ }

    // ==================== Торговец (5.2) ====================

    IEnumerator MerchantRoomFlow() { /* verbatim */ }

    static void SetRarityClass(VisualElement element, ItemTier tier) { /* verbatim */ }
}
```

- [ ] **Step 3: Delete the moved members from `RunFlowController.cs`**

- [ ] **Step 4: Compile-check** (same command pattern as Task 1 Step 5, log file `unity_task2.log`)

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/UI/RunFlowController.cs" "Assets/Scripts/UI/RunFlowController.Rooms.cs" "Assets/Scripts/UI/RunFlowController.Rooms.cs.meta"
git commit -m "refactor: extract RunFlowController.Rooms.cs from RunFlowController"
```

---

## Task 3: Extract `RunFlowController.Progression.cs`

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Create: `Assets/Scripts/UI/RunFlowController.Progression.cs`

**Members to move** (re-fetch current line numbers first):
- `IEnumerator LevelUpFlow(string activeUpgradeNotice = null)`
- `IEnumerator CampOfferAndPhaseCoroutine()`
- `void SetCampOfferButtonsVisible(bool visible)`
- `IEnumerator CampPhaseCoroutine(float healMultiplierOverride = -1f)`
- `IEnumerator TryPlayCampSceneAfterRation()`

- [ ] **Step 1: Create `RunFlowController.Progression.cs`**

```csharp
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Левел-ап (3.5) ====================

    IEnumerator LevelUpFlow(string activeUpgradeNotice = null) { /* verbatim */ }

    // ==================== Привал (раздел 6) ====================

    IEnumerator CampOfferAndPhaseCoroutine() { /* verbatim */ }
    void SetCampOfferButtonsVisible(bool visible) { /* verbatim */ }
    IEnumerator CampPhaseCoroutine(float healMultiplierOverride = -1f) { /* verbatim */ }
    IEnumerator TryPlayCampSceneAfterRation() { /* verbatim */ }
}
```

- [ ] **Step 2: Delete the moved members from `RunFlowController.cs`**

- [ ] **Step 3: Compile-check** (log file `unity_task3.log`)

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/UI/RunFlowController.cs" "Assets/Scripts/UI/RunFlowController.Progression.cs" "Assets/Scripts/UI/RunFlowController.Progression.cs.meta"
git commit -m "refactor: extract RunFlowController.Progression.cs from RunFlowController"
```

---

## Task 4: Extract `RunFlowController.Reward.cs`

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Create: `Assets/Scripts/UI/RunFlowController.Reward.cs`

**Members to move** (re-fetch current line numbers first):
- `IEnumerator ShowRewardChestFlow(int floorNumber, bool isBoss)`
- `IEnumerator ShowRewardOverlay()`
- `IEnumerator HideRewardOverlay()`
- `IEnumerator ChestRevealFlow(ChestReward reward)`
- `static string ChestReelBgClassFor(ItemTier tier)`
- `static string ItemComparisonSummary(ItemData item)`
- `IEnumerator ItemCompareFlow(ItemData newItem)`

- [ ] **Step 1: Create `RunFlowController.Reward.cs`**

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    // ==================== Награда / сундук (8.2, только текстовый результат) ====================

    IEnumerator ShowRewardChestFlow(int floorNumber, bool isBoss) { /* verbatim */ }
    IEnumerator ShowRewardOverlay() { /* verbatim */ }
    IEnumerator HideRewardOverlay() { /* verbatim */ }
    IEnumerator ChestRevealFlow(ChestReward reward) { /* verbatim */ }
    static string ChestReelBgClassFor(ItemTier tier) { /* verbatim */ }

    // ==================== Сравнение предмета (3.4, "Без инвентаря") ====================

    static string ItemComparisonSummary(ItemData item) { /* verbatim */ }
    IEnumerator ItemCompareFlow(ItemData newItem) { /* verbatim */ }
}
```

- [ ] **Step 2: Delete the moved members from `RunFlowController.cs`**

- [ ] **Step 3: Compile-check** (log file `unity_task4.log`)

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/UI/RunFlowController.cs" "Assets/Scripts/UI/RunFlowController.Reward.cs" "Assets/Scripts/UI/RunFlowController.Reward.cs.meta"
git commit -m "refactor: extract RunFlowController.Reward.cs from RunFlowController"
```

---

## Task 5: Extract `RunFlowController.Results.cs`

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Create: `Assets/Scripts/UI/RunFlowController.Results.cs`

**Members to move** (re-fetch current line numbers first):
- `IEnumerator ShowResultsFlow(bool victory)`
- `string BuildResultsText(...)`
- `VeteranCharacter BuildVeteranSnapshot(int floorsCleared)`
- `void ApplySelectedMentorInheritance()`
- `PassiveSkillData FindPassiveSkill(string skillName)`

- [ ] **Step 1: Create `RunFlowController.Results.cs`**

```csharp
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class RunFlowController
{
    // ==================== Результаты забега (1 п.7-8, 7.2 п.6) ====================

    IEnumerator ShowResultsFlow(bool victory) { /* verbatim */ }
    string BuildResultsText(bool victory, RunCompletionReward completion, string clearBonus, string relationshipReward) { /* verbatim */ }
    VeteranCharacter BuildVeteranSnapshot(int floorsCleared) { /* verbatim */ }
    void ApplySelectedMentorInheritance() { /* verbatim */ }
    PassiveSkillData FindPassiveSkill(string skillName) { /* verbatim */ }
}
```

- [ ] **Step 2: Delete the moved members from `RunFlowController.cs`**

- [ ] **Step 3: Verify the core file's remaining size**

```bash
wc -l Assets/Scripts/UI/RunFlowController.cs Assets/Scripts/UI/RunFlowController.*.cs
```
Expected: core file well under 900 lines (fields + OnEnable/OnDisable/Update + character/mentor
select + RunLoop/ResolveRoom + common UI helpers), each partial file under ~500 lines.

- [ ] **Step 4: Compile-check** (log file `unity_task5.log`)

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/UI/RunFlowController.cs" "Assets/Scripts/UI/RunFlowController.Results.cs" "Assets/Scripts/UI/RunFlowController.Results.cs.meta"
git commit -m "refactor: extract RunFlowController.Results.cs from RunFlowController"
```

---

## Task 6: Extract `MonsterEncounterBudget.RollMonsterCount`

**Files:**
- Modify: `Assets/Scripts/Progression/MonsterEncounterBudget.cs`
- Test: `Assets/Tests/EditMode/MonsterEncounterBudgetTests.cs`

**Run this task before Task 1.**

**Interfaces:**
- Produces: `MonsterEncounterBudget.RollMonsterCount(int level) : int`

- [ ] **Step 1: Write the failing test**

Add to `Assets/Tests/EditMode/MonsterEncounterBudgetTests.cs`:
```csharp
    [Test]
    public void RollMonsterCount_Level2OrBelow_AlwaysReturnsOne()
    {
        Assert.AreEqual(1, MonsterEncounterBudget.RollMonsterCount(1));
        Assert.AreEqual(1, MonsterEncounterBudget.RollMonsterCount(2));
    }

    [Test]
    public void RollMonsterCount_Level6OrAbove_ReturnsBetweenOneAndThree()
    {
        for (int i = 0; i < 20; i++)
        {
            int count = MonsterEncounterBudget.RollMonsterCount(6);
            Assert.GreaterOrEqual(count, 1);
            Assert.LessOrEqual(count, 3);
        }
    }
```

- [ ] **Step 2: Run it to verify it fails to compile**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testFilter "MonsterEncounterBudgetTests" -testResults "C:/Unity Projects/DungeonGirls/test_results_task6_pre.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_task6_pre.log"
```
Expected: compile error, `RollMonsterCount` doesn't exist on `MonsterEncounterBudget` yet.

- [ ] **Step 3: Add the method to `MonsterEncounterBudget.cs`**

Read current `Assets/Scripts/UI/RunFlowController.cs` lines 866-878 (re-fetch exact range with grep
first — this is the current, un-shifted file since Task 6 runs before Task 1) to copy the exact body.
Add to `Assets/Scripts/Progression/MonsterEncounterBudget.cs`, before the closing `}`:
```csharp
    // 4.1 [ОБНОВЛЕНО после третьего плейтеста]: пороги количества монстров в обычной боевой
    // комнате снижены — старый порог в 7 уровня для 3 монстров был слишком поздним.
    public static int RollMonsterCount(int level)
    {
        if (level <= 2) return 1;
        if (level <= 5) return Random.Range(1, 3); // 1-2
        return Random.Range(1, 4); // 1-3 (уровень 6+)
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Same command as Step 2, output to `test_results_task6.xml`. Expected: pass.

- [ ] **Step 5: Update the call site in `RunFlowController.cs`**

Change (current line ~903, inside `CombatRoomFlow`):
```csharp
            int count = RollMonsterCount(characterManager.Level);
```
This line does not need to change yet — `RollMonsterCount` on `RunFlowController` becomes a thin
wrapper in Task 1, not deleted here. Leave the call site as-is in this task; Task 1 handles the
wrapper.

Also update `RunFlowController.RollMonsterCount` itself (current lines 866-878) to delegate instead
of duplicating the body — replace:
```csharp
    int RollMonsterCount(int level)
    {
        if (level <= 2) return 1;
        if (level <= 5) return Random.Range(1, 3); // 1-2
        return Random.Range(1, 4); // 1-3 (уровень 6+)
    }
```
with:
```csharp
    int RollMonsterCount(int level) => MonsterEncounterBudget.RollMonsterCount(level);
```
(Doing this now, ahead of Task 1, avoids touching the same lines twice.)

- [ ] **Step 6: Compile-check** (log file `unity_task6.log`)

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Progression/MonsterEncounterBudget.cs" "Assets/Scripts/UI/RunFlowController.cs" "Assets/Tests/EditMode/MonsterEncounterBudgetTests.cs"
git commit -m "refactor: move RollMonsterCount to MonsterEncounterBudget"
```

---

## Task 7: Move `ResolveActiveSkillHitCount` to `CombatManager`

**Files:**
- Modify: `Assets/Scripts/Managers/CombatManager.cs`
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Test: `Assets/Tests/EditMode/CombatManagerTests.cs` (new file)

**Run this task before Task 1.**

- [ ] **Step 1: Write the failing test**

`Assets/Tests/EditMode/CombatManagerTests.cs`:
```csharp
using NUnit.Framework;

public class CombatManagerTests
{
    [Test]
    public void ResolveActiveSkillHitCount_Rogue_ReturnsZero()
    {
        Assert.AreEqual(0, CombatManager.ResolveActiveSkillHitCount(CharacterClass.Rogue));
    }

    [Test]
    public void ResolveActiveSkillHitCount_NonRogue_ReturnsThree()
    {
        Assert.AreEqual(3, CombatManager.ResolveActiveSkillHitCount(CharacterClass.Warrior));
        Assert.AreEqual(3, CombatManager.ResolveActiveSkillHitCount(CharacterClass.Barbarian));
    }
}
```

- [ ] **Step 2: Run it to verify it fails to compile**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testFilter "CombatManagerTests" -testResults "C:/Unity Projects/DungeonGirls/test_results_task7_pre.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_task7_pre.log"
```

- [ ] **Step 3: Move the method to `CombatManager.cs`**

Read `Assets/Scripts/UI/RunFlowController.cs` current lines 881-891 (the full comment block plus the
method) to copy verbatim. Add to `Assets/Scripts/Managers/CombatManager.cs`, near the other
skill-configuration static helpers (e.g. right after `RageSkillMultiplier`/`StubbornnessThreshold`):
```csharp
    // Codex P1 (ФИКС, 2026-08-27): раньше CombatRoomFlow всегда передавал hitCount=3 и конфиг из
    // jenniferCharacter.uniqueActiveSkill — Плут получал бы конфигурацию Дженифер (неверный
    // hitCount/имя навыка), а Варвар вообще не имеет кулдаун-активки (Берсерк — ручной тумблер, см.
    // ниже). Единственный текущий кейс с hitCount != 3 — Дымовая граната Плута (не бьёт сама, см.
    // TryActivateUniqueActiveSkill, которое жёстко возвращает до hit-loop для неё независимо от
    // переданного числа) — hitCount=0 здесь просто отражает намерение корректно.
    public static int ResolveActiveSkillHitCount(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Rogue => 0, // Дымовая граната — не бьёт сама
        _ => 3 // "3 быстрые атаки" (Дженифер/Воин) — единственный hit-loop навык прототипа кроме Дымовой гранаты
    };
```

- [ ] **Step 4: Delete the method from `RunFlowController.cs` and update its one call site**

Remove the `public static int ResolveActiveSkillHitCount(...)` method entirely from
`RunFlowController.cs`. In `CombatRoomFlow` (current line ~991), change:
```csharp
            int hitCount = ResolveActiveSkillHitCount(activeCharacter.characterClass);
```
to:
```csharp
            int hitCount = CombatManager.ResolveActiveSkillHitCount(activeCharacter.characterClass);
```

- [ ] **Step 5: Run the test to verify it passes**

Same command as Step 2, output to `test_results_task7.xml`. Expected: pass.

- [ ] **Step 6: Compile-check** (log file `unity_task7.log`)

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Managers/CombatManager.cs" "Assets/Scripts/UI/RunFlowController.cs" "Assets/Tests/EditMode/CombatManagerTests.cs" "Assets/Tests/EditMode/CombatManagerTests.cs.meta"
git commit -m "refactor: move ResolveActiveSkillHitCount to CombatManager"
```

---

## Task 8: Extract `QuestCatalog.PickForFloor`

**Files:**
- Modify: `Assets/Scripts/Rooms/QuestCatalog.cs`
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Test: `Assets/Tests/EditMode/QuestCatalogTests.cs` (new file)

**Run this task before Task 2.**

- [ ] **Step 1: Read `QuestCatalog.cs` to confirm its current static field names**

```bash
grep -n "public static QuestDefinition\|class QuestCatalog" "Assets/Scripts/Rooms/QuestCatalog.cs"
```
Confirm the exact field names `Hunt`, `Sphinx`, `FairyRing`, `SwordInStone` exist as referenced below
(these are used already in `RunFlowController.PickQuestForFloor`/`EventRoomFlow` today, so they must
already exist — this step is a sanity check, not a discovery step).

- [ ] **Step 2: Write the failing test**

`Assets/Tests/EditMode/QuestCatalogTests.cs`:
```csharp
using NUnit.Framework;

public class QuestCatalogTests
{
    [Test]
    public void PickForFloor_Floor1_ReturnsSphinx()
    {
        // Floor 1 is below the hunt-quest eligibility floor (2+), so hunt can never roll here.
        var quest = QuestCatalog.PickForFloor(1, huntAlreadyTriggered: false, swordAlreadySucceeded: false);
        Assert.AreEqual(QuestCatalog.Sphinx, quest);
    }

    [Test]
    public void PickForFloor_Floor2_HuntAlreadyTriggered_NeverReturnsHunt()
    {
        for (int i = 0; i < 20; i++)
        {
            var quest = QuestCatalog.PickForFloor(2, huntAlreadyTriggered: true, swordAlreadySucceeded: false);
            Assert.AreNotEqual(QuestCatalog.Hunt, quest);
        }
    }

    [Test]
    public void PickForFloor_HighFloor_SwordAlreadySucceeded_ReturnsFairyRingNotSword()
    {
        for (int i = 0; i < 20; i++)
        {
            var quest = QuestCatalog.PickForFloor(5, huntAlreadyTriggered: true, swordAlreadySucceeded: true);
            Assert.AreNotEqual(QuestCatalog.SwordInStone, quest);
        }
    }
}
```

- [ ] **Step 3: Run it to verify it fails to compile**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testFilter "QuestCatalogTests" -testResults "C:/Unity Projects/DungeonGirls/test_results_task8_pre.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_task8_pre.log"
```

- [ ] **Step 4: Add the method to `QuestCatalog.cs`**

Read `Assets/Scripts/UI/RunFlowController.cs` current lines 1531-1549 to copy the decision logic
verbatim (adapting parameter names — the current method reads instance fields
`huntQuestTriggeredThisRun`/`swordInStoneSucceededThisRun` directly; the extracted version takes them
as parameters instead). Add to `Assets/Scripts/Rooms/QuestCatalog.cs`, before the closing `}`:
```csharp
    // «Добыча» доступна со 2-го этажа, с шансом 20% среди квестов и максимум один раз за забег
    // (huntAlreadyTriggered управляется вызывающей стороной — этот метод только решает, не мутирует
    // состояние забега). Награда «Меча в камне» может быть успешно получена только один раз за
    // забег — после успеха возвращается другой полноценный квест вместо пустого исхода.
    public static QuestDefinition PickForFloor(int floor, bool huntAlreadyTriggered, bool swordAlreadySucceeded)
    {
        if (floor >= 2 && !huntAlreadyTriggered && Random.value < 0.20f)
        {
            return Hunt;
        }

        switch (floor)
        {
            case 1: return Sphinx;
            case 2: return FairyRing;
            default: return swordAlreadySucceeded ? FairyRing : SwordInStone;
        }
    }
```
Add `using UnityEngine;` at the top of `QuestCatalog.cs` if not already present (needed for
`Random.value`).

- [ ] **Step 5: Replace the method in `RunFlowController.cs`**

Replace (current lines 1531-1549):
```csharp
    QuestDefinition PickQuestForFloor(int floor)
    {
        // «Добыча» доступна со 2-го этажа, с шансом 20% среди квестов и максимум один раз.
        if (floor >= 2 && !huntQuestTriggeredThisRun && Random.value < 0.20f)
        {
            huntQuestTriggeredThisRun = true;
            return QuestCatalog.Hunt;
        }

        switch (floor)
        {
            case 1: return QuestCatalog.Sphinx;
            case 2: return QuestCatalog.FairyRing;
            // Награда «Меча в камне» может быть успешно получена только один раз за забег.
            // После успеха не подменяем квест пустым исходом, а возвращаем другой полноценный
            // квест, чтобы особая комната по-прежнему была содержательна.
            default: return swordInStoneSucceededThisRun ? QuestCatalog.FairyRing : QuestCatalog.SwordInStone;
        }
    }
```
with:
```csharp
    QuestDefinition PickQuestForFloor(int floor)
    {
        var quest = QuestCatalog.PickForFloor(floor, huntQuestTriggeredThisRun, swordInStoneSucceededThisRun);
        if (quest == QuestCatalog.Hunt) huntQuestTriggeredThisRun = true;
        return quest;
    }
```

- [ ] **Step 6: Run the test to verify it passes**

Same command as Step 3, output to `test_results_task8.xml`. Expected: pass.

- [ ] **Step 7: Compile-check** (log file `unity_task8.log`)

- [ ] **Step 8: Commit**

```bash
git add "Assets/Scripts/Rooms/QuestCatalog.cs" "Assets/Scripts/UI/RunFlowController.cs" "Assets/Tests/EditMode/QuestCatalogTests.cs" "Assets/Tests/EditMode/QuestCatalogTests.cs.meta"
git commit -m "refactor: extract quest-for-floor selection to QuestCatalog.PickForFloor"
```

---

## Task 9: Extract `ComputeStageFloorGap`

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Test: `Assets/Tests/EditMode/RunFlowControllerLayoutTests.cs` (new file)

**Run this task before Task 1.**

- [ ] **Step 1: Write the failing test**

`Assets/Tests/EditMode/RunFlowControllerLayoutTests.cs`:
```csharp
using NUnit.Framework;

public class RunFlowControllerLayoutTests
{
    [Test]
    public void ComputeStageFloorGap_WiderContainerThanImage_CropsTopAndBottom()
    {
        // Image is 1536x1024 (aspect 1.5). A 1920x1080 container (aspect ~1.778) is wider than the
        // image, so the image scales to container width and crops top/bottom evenly.
        float gap = RunFlowController.ComputeStageFloorGap(1920f, 1080f);

        Assert.Greater(gap, 0f);
    }

    [Test]
    public void ComputeStageFloorGap_NeverReturnsNegative()
    {
        // A container much taller than wide (narrower than image aspect) exercises the other branch.
        float gap = RunFlowController.ComputeStageFloorGap(400f, 2000f);

        Assert.GreaterOrEqual(gap, 0f);
    }
}
```

- [ ] **Step 2: Run it to verify it fails to compile**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testFilter "RunFlowControllerLayoutTests" -testResults "C:/Unity Projects/DungeonGirls/test_results_task9_pre.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_task9_pre.log"
```

- [ ] **Step 3: Split `GetStageFloorGapFromBottom` in `RunFlowController.cs`**

Replace (current lines 1398-1436):
```csharp
    const float combatBackgroundImageWidth = 1536f;
    const float combatBackgroundImageHeight = 1024f;
    const float combatBackgroundFloorRowFromTop = 797f;

    float GetStageFloorGapFromBottom()
    {
        float boxWidth = combatPanel.resolvedStyle.width;
        float boxHeight = combatPanel.resolvedStyle.height;
        if (boxWidth <= 0f || boxHeight <= 0f)
        {
            // Первый кадр после ShowOnly(combatPanel) — Yoga-layout ещё не посчитан
            // (resolvedStyle временно 0x0). Само-корректируется на следующем кадре.
            return 0f;
        }

        float imageAspect = combatBackgroundImageWidth / combatBackgroundImageHeight;
        float boxAspect = boxWidth / boxHeight;

        float scale;
        float cropTop;
        if (boxAspect > imageAspect)
        {
            // Контейнер шире фона (типичный случай 16:9-21:9 против 3:2) — фон растягивается по
            // ширине контейнера, высота обрезается сверху и снизу поровну (центр-кроп).
            scale = boxWidth / combatBackgroundImageWidth;
            float scaledHeight = combatBackgroundImageHeight * scale;
            cropTop = (scaledHeight - boxHeight) / 2f;
        }
        else
        {
            // Контейнер уже фона (не целевой диапазон платформы, но не должен ломаться) — кроп по
            // высоте, вертикального кропа нет вовсе.
            scale = boxHeight / combatBackgroundImageHeight;
            cropTop = 0f;
        }

        float floorFromTop = combatBackgroundFloorRowFromTop * scale - cropTop;
        return Mathf.Max(0f, boxHeight - floorFromTop);
    }
```
with:
```csharp
    const float combatBackgroundImageWidth = 1536f;
    const float combatBackgroundImageHeight = 1024f;
    const float combatBackgroundFloorRowFromTop = 797f;

    float GetStageFloorGapFromBottom()
    {
        float boxWidth = combatPanel.resolvedStyle.width;
        float boxHeight = combatPanel.resolvedStyle.height;
        if (boxWidth <= 0f || boxHeight <= 0f)
        {
            // Первый кадр после ShowOnly(combatPanel) — Yoga-layout ещё не посчитан
            // (resolvedStyle временно 0x0). Само-корректируется на следующем кадре.
            return 0f;
        }

        return ComputeStageFloorGap(boxWidth, boxHeight);
    }

    // Чистая часть формулы — вынесена из GetStageFloorGapFromBottom, чтобы быть тестируемой без
    // живого UIDocument/resolvedStyle. Баг (2026-08-26): фон боя (Dungeon.png, 1536x1024)
    // рендерится через ScaleAndCrop — на экранах шире исходного соотношения (16:9-21:9 против 3:2
    // фона) кроп идёт по центру, и линия пола на фоне смещается относительно нижнего края
    // контейнера тем сильнее, чем шире экран. Пересчитывается по формуле "cover"-кропа.
    public static float ComputeStageFloorGap(float boxWidth, float boxHeight)
    {
        float imageAspect = combatBackgroundImageWidth / combatBackgroundImageHeight;
        float boxAspect = boxWidth / boxHeight;

        float scale;
        float cropTop;
        if (boxAspect > imageAspect)
        {
            scale = boxWidth / combatBackgroundImageWidth;
            float scaledHeight = combatBackgroundImageHeight * scale;
            cropTop = (scaledHeight - boxHeight) / 2f;
        }
        else
        {
            scale = boxHeight / combatBackgroundImageHeight;
            cropTop = 0f;
        }

        float floorFromTop = combatBackgroundFloorRowFromTop * scale - cropTop;
        return Mathf.Max(0f, boxHeight - floorFromTop);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Same command as Step 2, output to `test_results_task9.xml`. Expected: pass.

- [ ] **Step 5: Compile-check** (log file `unity_task9.log`)

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/UI/RunFlowController.cs" "Assets/Tests/EditMode/RunFlowControllerLayoutTests.cs" "Assets/Tests/EditMode/RunFlowControllerLayoutTests.cs.meta"
git commit -m "refactor: extract pure ComputeStageFloorGap from GetStageFloorGapFromBottom"
```

---

## Task 10: Split `HubManager` into 4 partial files

**Files:**
- Modify: `Assets/Scripts/Managers/HubManager.cs`
- Create: `Assets/Scripts/Managers/HubManager.Navigation.cs`
- Create: `Assets/Scripts/Managers/HubManager.Buildings.cs`
- Create: `Assets/Scripts/Managers/HubManager.Gacha.cs`

**Members to move** (re-fetch current line numbers with
`grep -n "^    " Assets/Scripts/Managers/HubManager.cs` — this file hasn't been touched by earlier
tasks, so line numbers from the earlier survey are still valid: `OpenCheatMenu` 240,
`CloseCheatMenu` 249, `SubmitCheatCommand` 257, `QuitGame` 275, `BindTutorialTooltips` 361,
`ConfirmResetProgress` 622 for Navigation; `RefreshBuildingsScreen` 420, `TryUpgradeBuilding` 446 for
Buildings; `RefreshGachaScreen` 456, `TryPullGacha` 462, `GachaPullFlow` 483,
`HasValidGachaCharacterPool` 602, `ReelBackgroundClass` 613 for Gacha).

- [ ] **Step 1: Add `partial` to the class declaration**

In `Assets/Scripts/Managers/HubManager.cs`, change `public class HubManager : MonoBehaviour` to
`public partial class HubManager : MonoBehaviour`.

- [ ] **Step 2: Create `HubManager.Navigation.cs`**

```csharp
using UnityEngine;
using UnityEngine.UIElements;

public partial class HubManager
{
    // ==================== Навигация (7.1) ====================

    void OpenCheatMenu() { /* verbatim, current lines 240-248 */ }
    void CloseCheatMenu() { /* verbatim, current lines 249-256 */ }
    void SubmitCheatCommand() { /* verbatim, current lines 257-274 */ }
    void QuitGame() { /* verbatim, current lines 275-360 — verify exact end boundary with grep before BindTutorialTooltips at 361 */ }
    void BindTutorialTooltips() { /* verbatim, current lines 361-417 */ }

    // ==================== Сброс прогресса (7.1) ====================

    void ConfirmResetProgress() { /* verbatim, from line 622 to end of file */ }
}
```
Before writing, run `grep -n "^    [a-zA-Z]" Assets/Scripts/Managers/HubManager.cs` to get the exact
boundary between `QuitGame` and `BindTutorialTooltips`, and confirm `ConfirmResetProgress` is the last
method in the file (read its closing brace to find the exact end line).

- [ ] **Step 3: Create `HubManager.Buildings.cs`**

```csharp
using UnityEngine.UIElements;

public partial class HubManager
{
    // ==================== Здания (8.1) ====================

    void RefreshBuildingsScreen() { /* verbatim, current lines 420-445 */ }
    void TryUpgradeBuilding(BuildingType building) { /* verbatim, current lines 446-453 */ }
}
```

- [ ] **Step 4: Create `HubManager.Gacha.cs`**

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class HubManager
{
    // ==================== Гача (8.5/11.1) ====================

    void RefreshGachaScreen() { /* verbatim, current lines 456-461 */ }
    void TryPullGacha() { /* verbatim, current lines 462-482 */ }
    IEnumerator GachaPullFlow(GachaPool.Result result, CharacterData character, int copies) { /* verbatim, current lines 483-601 */ }
    bool HasValidGachaCharacterPool() { /* verbatim, current lines 602-611 */ }
    static string ReelBackgroundClass(ItemTier tier) { /* verbatim, current lines 613-619 */ }
}
```

- [ ] **Step 5: Delete all moved members from `HubManager.cs`**

- [ ] **Step 6: Compile-check** (log file `unity_task10.log`)

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Managers/HubManager.cs" "Assets/Scripts/Managers/HubManager.Navigation.cs" "Assets/Scripts/Managers/HubManager.Navigation.cs.meta" "Assets/Scripts/Managers/HubManager.Buildings.cs" "Assets/Scripts/Managers/HubManager.Buildings.cs.meta" "Assets/Scripts/Managers/HubManager.Gacha.cs" "Assets/Scripts/Managers/HubManager.Gacha.cs.meta"
git commit -m "refactor: split HubManager into partial files by section"
```

---

## Task 11: Full verification pass

**Files:** none (verification only).

- [ ] **Step 1: File size sanity check**

```bash
wc -l Assets/Scripts/UI/RunFlowController*.cs Assets/Scripts/Managers/HubManager*.cs
```
Expected: no single file over ~900 lines (core files), most partials under 500.

- [ ] **Step 2: Full EditMode test suite**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testResults "C:/Unity Projects/DungeonGirls/test_results_final.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_final_editmode.log"
```
Expected: 0 failed (should be 43 from the previous iteration + ~9 new from this plan's Tasks 6-9).

- [ ] **Step 3: `PlayModeSmokeTest` and `NarrativeSmokeTest` regression check**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -executeMethod PlayModeSmokeTest.Run -projectPath "C:/Unity Projects/DungeonGirls" -logFile "C:/Unity Projects/DungeonGirls/unity_final_playmode.log"
grep "ИТОГ" "C:/Unity Projects/DungeonGirls/unity_final_playmode.log"
```
Expected: `ИТОГ: 413 OK, 0 ошибок` (same count as the previous iteration — this plan changes no
behavior, so the count must not change).

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -executeMethod NarrativeSmokeTest.Run -projectPath "C:/Unity Projects/DungeonGirls" -logFile "C:/Unity Projects/DungeonGirls/unity_final_narrative.log"
grep "RESULT=" "C:/Unity Projects/DungeonGirls/unity_final_narrative.log"
```
Expected: `RESULT=PASS (32 OK, 0 failed)`.

- [ ] **Step 4: Clean up log/result artifacts**

```bash
cd "C:/Unity Projects/DungeonGirls" && rm -f unity_*.log test_results_*.xml
```

- [ ] **Step 5: No commit for this task** — verification only.
