# Scaling & Progression Formulas Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sync all balance-formula constants with the 2026-08-25 GDD update: 10 floors (was 3), monster armor scaling ×1.15/floor (was ×1.8), room-bag composition 8/1/2/1, character level cap 15 (was 10), floor-scaled XP sources, chest rarity table 62/35/3, and a reworked defeat meta-currency formula.

**Architecture:** Pure constant/formula edits to existing managers — no new classes except one small per-floor room counter on `CharacterManager` needed for the defeat-currency formula. Each change is verified by extending the project's existing smoke-test convention (`Assets/Editor/PlayModeSmokeTest.cs`, run via `-batchmode -executeMethod PlayModeSmokeTest.Run`) rather than a separate NUnit suite — this matches how every prior phase in this codebase was verified (see `RunPureLogicChecks`/`RunPlayModeChecks`).

**Tech Stack:** Unity 6000.5.8f1, C#, UI Toolkit. No new packages.

**Spec:** ГДД Данжнгерлс (рабочая версия) — Notion page `3c10227a-2824-81bb-a9c0-c2f212bddbfb`, sections 2.1, 2.6, 3.6, 8.2, 8.4, 8.5. The user-provided sync prompt (session 2026-08-25) items 1, 2, 9, 10, 11.

## Global Constraints

- Room-bag composition must stay 12 rooms/floor (8 combat / 1 merchant / 2 trap / 1 special), identical on all 10 floors (GDD 8.4 — "РЕШЕНО").
- Monster armor multiplier is now the SAME per-floor multiplier as damage: ×1.15/floor, compounding (GDD 2.6).
- Character level cap: 15 (was 10). XP-per-level formula (`level × 25`) is UNCHANGED — only the cap moves and the curve is now defined through level 15.
- Chest/merchant rarity table: Common 62% / Rare 35% / Epic 3% (was 60/30/10). Boss chest still guarantees minimum Rare.
- Defeat meta-currency formula: `50 × (floorsFullyCleared) + 5 × (roomsClearedOnDeathFloor)`, uncapped. `floorsFullyCleared = CurrentFloorNumber − 1` (confirmed with the user — floor only advances after a full clear, so this equals "floors completed before death"). Gacha-currency-on-defeat formula is UNCHANGED (`2 × totalRoomsClearedThisRun`, cap 14).
- Victory rewards are UNCHANGED: 80 meta / 15 gacha flat.

---

### Task 1: Dungeon length — 3 floors → 10

**Files:**
- Modify: `Assets/Scripts/Managers/DungeonManager.cs:6`
- Modify: `Assets/UI/GameRoot.uxml:63` (hardcoded "Этаж 1/3" placeholder text — cosmetic only, `UpdateTopBar()` in RunFlowController already reads `DungeonManager.TotalFloors` dynamically)
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Produces: `DungeonManager.TotalFloors` (const int) — consumed by `RunFlowController.UpdateTopBar()` and `DungeonManager.AdvanceToNextFloor()`, both already generic over this constant, no other changes needed there.

- [ ] **Step 1: Change the constant**

```csharp
// Assets/Scripts/Managers/DungeonManager.cs
// 2.1: 10 этажей, фиксировано для прототипа (ОБНОВЛЕНО 2026-08-25 — было 3, расширено для
// более плавной кривой сложности; см. ГДД 2.1 для дизайн-обоснования).
public const int TotalFloors = 10;
```

- [ ] **Step 2: Update the UXML placeholder label**

In `Assets/UI/GameRoot.uxml`, change:
```xml
<ui:Label name="FloorLabel" text="Этаж 1/3" class="top-bar-label" />
```
to:
```xml
<ui:Label name="FloorLabel" text="Этаж 1/10" class="top-bar-label" />
```
(This is purely the pre-play placeholder shown before `UpdateTopBar()` first runs — no code depends on the literal string.)

- [ ] **Step 3: Add a smoke-test check**

In `Assets/Editor/PlayModeSmokeTest.cs`, inside `RunPureLogicChecks()`, add:

```csharp
Check(DungeonManager.TotalFloors == 10, $"2.1 этажей в подземелье: {DungeonManager.TotalFloors} (ожидалось 10)");
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Managers/DungeonManager.cs Assets/UI/GameRoot.uxml Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Dungeon length 3 -> 10 floors (GDD 2.1)"
```

---

### Task 2: Room-bag composition — 7/1/3/1 → 8/1/2/1

**Files:**
- Modify: `Assets/Scripts/Managers/FloorManager.cs:6-12`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Produces: `FloorManager.RoomBag`, `FloorManager.TotalRoomsOnFloor` — unchanged shape, only the constants feeding `GenerateRoomBag()` change.

- [ ] **Step 1: Update the constants and comment**

```csharp
// Assets/Scripts/Managers/FloorManager.cs
// 8.4 [РЕШЕНО, ОБНОВЛЕНО 2026-08-25]: состав мешка комнат — 8 боевых / 1 торговец / 2 ловушки /
// 1 особая = 12 комнат. Одинаковый на ВСЕХ 10 этажах (не растёт с глубиной) — сложность растёт
// только через масштабирование монстров (2.6/2.7/2.8), не через число/состав комнат.
const int CombatRooms = 8;
const int MerchantRooms = 1;
const int TrapRooms = 2;
const int SpecialRooms = 1;
```

- [ ] **Step 2: Add a smoke-test check**

```csharp
// In RunPureLogicChecks() or a new floor-manager check block:
var floorManagerGO = new GameObject("SmokeTest_FloorManager");
var floorManager = floorManagerGO.AddComponent<FloorManager>();
floorManager.GenerateRoomBag();
int combatCount = floorManager.RoomBag.FindAll(r => r == RoomType.Combat).Count;
int merchantCount = floorManager.RoomBag.FindAll(r => r == RoomType.Merchant).Count;
int trapCount = floorManager.RoomBag.FindAll(r => r == RoomType.Trap).Count;
int specialCount = floorManager.RoomBag.FindAll(r => r == RoomType.Special).Count;
Check(combatCount == 8 && merchantCount == 1 && trapCount == 2 && specialCount == 1 && floorManager.RoomBag.Count == 12,
    $"8.4 состав мешка: combat={combatCount}, merchant={merchantCount}, trap={trapCount}, special={specialCount}, total={floorManager.RoomBag.Count} (ожидалось 8/1/2/1/12)");
UnityEngine.Object.DestroyImmediate(floorManagerGO);
```
Place this in `RunPlayModeChecks()` (it needs a live `GameObject`/`MonoBehaviour`, so it can't run in `RunPureLogicChecks()` which executes outside Play Mode / without instantiating MonoBehaviours — follow the existing split in the file).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Managers/FloorManager.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Room bag composition 8 combat/1 merchant/2 trap/1 special (GDD 8.4)"
```

---

### Task 3: Monster armor scaling ×1.8/floor → ×1.15/floor

**Files:**
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs:66-90`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `StatScaling.ApplyLevelBonus(float, int)` (unchanged, from Task elsewhere).
- Produces: `CombatantFactory.CreateMonsterCombatant(MonsterData, int floorNumber, int monsterLevel = 1)` — signature unchanged, only the internal armor multiplier changes.

- [ ] **Step 1: Change the multiplier and update the comment block**

```csharp
// Assets/Scripts/Combat/CombatantFactory.cs, replace the comment above CreateMonsterCombatant and the multiplier line:

// 2.6 [ОБНОВЛЕНО 2026-08-25]: HP x1.25/этаж, урон x1.15/этаж, физ. защита ТЕПЕРЬ ТОЖЕ x1.15/этаж
// (было x1.8/этаж — на этаже 10 это давало бы ~x101 от базы, нереалистично; новое значение даёт
// x1.15^9 ≈ x3.52 на этаже 10). Все три множителя накапливаются степенью независимо друг от
// друга (этаж 1 = база). Скорость атаки и маг. защита не масштабируются.
public static CombatantRuntime CreateMonsterCombatant(MonsterData monster, int floorNumber, int monsterLevel = 1)
{
    int floorIndex = Mathf.Max(floorNumber, 1);
    int level = Mathf.Max(monsterLevel, 1);
    float hpMultiplier = FloorScalingMultiplier(1.25f, floorIndex);
    float damageMultiplier = FloorScalingMultiplier(1.15f, floorIndex);
    float armorMultiplier = FloorScalingMultiplier(1.15f, floorIndex); // было 1.8f
    ...
```

- [ ] **Step 2: Add a smoke-test check for the floor-10 armor value**

```csharp
// RunPureLogicChecks(): Skeleton base armor is 8 (per GDD 2.4, unchanged this session).
var skeletonBase = ScriptableObject.CreateInstance<MonsterData>();
skeletonBase.physicalDefense = 8f;
skeletonBase.hp = 40f; skeletonBase.damageMin = 10f; skeletonBase.damageMax = 15f;
var floor10Skeleton = CombatantFactory.CreateMonsterCombatant(skeletonBase, 10);
// x1.15^9 ≈ 3.5179; StatScaling.ApplyLevelBonus at level 1 is a no-op (level-1=0), so this is
// just 8 * 3.5179 ≈ 28.14, matching the GDD's "≈28" example.
Check(floor10Skeleton.PhysicalDefenseMax > 27f && floor10Skeleton.PhysicalDefenseMax < 29f,
    $"2.6 броня Скелета на этаже 10: {floor10Skeleton.PhysicalDefenseMax:F2} (ожидалось ~28.1, было бы ~850+ со старым x1.8)");
UnityEngine.Object.DestroyImmediate(skeletonBase);
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Combat/CombatantFactory.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Monster armor scaling x1.8/floor -> x1.15/floor (GDD 2.6)"
```

---

### Task 4: Character level cap 10 → 15

**Files:**
- Modify: `Assets/Scripts/Progression/RunCharacterProgress.cs:7`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Produces: `RunCharacterProgress.MaxCharacterLevel` (const int), `RunCharacterProgress.ExperienceRequiredForLevel(int)` (unchanged formula, `level * 25`), `RunCharacterProgress.AddExperience(int)` (unchanged logic, now loops up to the new cap).

- [ ] **Step 1: Change the constant and comment**

```csharp
// Assets/Scripts/Progression/RunCharacterProgress.cs
// 3.6 [ОБНОВЛЕНО 2026-08-25]: потолок уровня поднят 10 -> 15 (расширение до 10 этажей требует
// более длинной кривой прокачки). Формула опыта до след. уровня (level x 25) НЕ меняется —
// кривая просто продолжена естественным образом до 15 (сумма на 15 ур. = 2625).
public const int MaxCharacterLevel = 15;
```

- [ ] **Step 2: Add smoke-test checks**

```csharp
// RunPureLogicChecks():
Check(RunCharacterProgress.MaxCharacterLevel == 15, $"3.6 потолок уровня: {RunCharacterProgress.MaxCharacterLevel} (ожидалось 15)");

int totalXpTo15 = 0;
for (int lvl = 1; lvl < 15; lvl++) totalXpTo15 += RunCharacterProgress.ExperienceRequiredForLevel(lvl);
Check(totalXpTo15 == 2625, $"3.6 суммарный опыт до 15 ур.: {totalXpTo15} (ожидалось 2625)");

var jennifer = ScriptableObject.CreateInstance<CharacterData>();
var progress = new RunCharacterProgress(jennifer);
progress.AddExperience(100000); // огромный оверфлоу — не должен пробить потолок 15
Check(progress.Level == 15, $"3.6 AddExperience не пробивает потолок: {progress.Level} (ожидалось 15)");
UnityEngine.Object.DestroyImmediate(jennifer);
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Progression/RunCharacterProgress.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Character level cap 10 -> 15 (GDD 3.6)"
```

---

### Task 5: Floor-scaled XP sources

**Files:**
- Modify: `Assets/Scripts/Managers/RewardManager.cs:138-158`
- Modify: `Assets/Scripts/Managers/CharacterManager.cs:131-134` (`GrantExperience` needs a `floorNumber` parameter now)
- Modify: `Assets/Scripts/UI/RunFlowController.cs:402` (the one call site)
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `RewardManager.GetExperienceReward(ExperienceSource source, int floorNumber)` — signature CHANGES (adds `floorNumber`). `CharacterManager.GrantExperience(RewardManager, ExperienceSource, int floorNumber)` — signature CHANGES. `RewardManager.GrantExperience(RunCharacterProgress, ExperienceSource, int floorNumber)` — signature CHANGES.

- [ ] **Step 1: Update `RewardManager.GetExperienceReward` and `GrantExperience`**

```csharp
// Assets/Scripts/Managers/RewardManager.cs
// 3.6 [ОБНОВЛЕНО 2026-08-25]: источники опыта растут вместе с этажом, чтобы прокачка успевала
// за расширением до 10 этажей. Босс остаётся флэт 50 (тот же переиспользуемый босс на всех этажах).
public int GetExperienceReward(ExperienceSource source, int floorNumber)
{
    int floorIndex = Mathf.Max(floorNumber, 1);
    switch (source)
    {
        case ExperienceSource.CombatRoom: return 10 + 3 * (floorIndex - 1);
        case ExperienceSource.SuccessfulEventOrTrap: return 5 + 1 * (floorIndex - 1);
        case ExperienceSource.Boss: return 50;
        default: return 0;
    }
}

public List<int> GrantExperience(RunCharacterProgress progress, ExperienceSource source, int floorNumber)
{
    int amount = GetExperienceReward(source, floorNumber);
    var levelsGained = progress.AddExperience(amount);

    Debug.Log($"[Reward] +{amount} опыта ({source}, этаж {floorNumber}). Текущий уровень: {progress.Level}, опыт: {progress.Experience}.");

    return levelsGained;
}
```

- [ ] **Step 2: Update `CharacterManager.GrantExperience`**

```csharp
// Assets/Scripts/Managers/CharacterManager.cs
public List<int> GrantExperience(RewardManager rewardManager, ExperienceSource source, int floorNumber)
{
    return rewardManager.GrantExperience(Progress, source, floorNumber);
}
```

- [ ] **Step 3: Update the call site in `RunFlowController.CombatRoomFlow`**

```csharp
// Assets/Scripts/UI/RunFlowController.cs:402, was:
// var levelsGained = characterManager.GrantExperience(rewardManager, isBoss ? ExperienceSource.Boss : ExperienceSource.CombatRoom);
var levelsGained = characterManager.GrantExperience(rewardManager, isBoss ? ExperienceSource.Boss : ExperienceSource.CombatRoom, dungeonManager.CurrentFloorNumber);
```

Note: this plan does not add a call site for `ExperienceSource.SuccessfulEventOrTrap` — check whether one already exists elsewhere (grep confirmed no current call site grants trap/quest XP; that gap is pre-existing and out of scope for this plan, which only fixes the formula for existing call sites). Flag this to the user as a pre-existing gap if it wasn't already known.

- [ ] **Step 4: Add smoke-test checks**

```csharp
// RunPureLogicChecks():
var rewardManagerGO = new GameObject("SmokeTest_RewardManager");
var rewardManager = rewardManagerGO.AddComponent<RewardManager>();
Check(rewardManager.GetExperienceReward(ExperienceSource.CombatRoom, 1) == 10, "3.6 XP боевая комната этаж 1 = 10");
Check(rewardManager.GetExperienceReward(ExperienceSource.CombatRoom, 10) == 37, "3.6 XP боевая комната этаж 10 = 37");
Check(rewardManager.GetExperienceReward(ExperienceSource.SuccessfulEventOrTrap, 1) == 5, "3.6 XP ловушка/квест этаж 1 = 5");
Check(rewardManager.GetExperienceReward(ExperienceSource.SuccessfulEventOrTrap, 10) == 14, "3.6 XP ловушка/квест этаж 10 = 14");
Check(rewardManager.GetExperienceReward(ExperienceSource.Boss, 10) == 50, "3.6 XP босс всегда 50 флэт");
UnityEngine.Object.DestroyImmediate(rewardManagerGO);
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Managers/RewardManager.cs Assets/Scripts/Managers/CharacterManager.cs Assets/Scripts/UI/RunFlowController.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "XP rewards scale with floor number (GDD 3.6)"
```

---

### Task 6: Chest/merchant rarity table 60/30/10 → 62/35/3

**Files:**
- Modify: `Assets/Scripts/Managers/RewardManager.cs:46-59`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Produces: `RewardManager.RollItemRarity(bool isBoss)` — unchanged signature, new thresholds. This is also the table the merchant (Plan 3) will reuse.

- [ ] **Step 1: Update the thresholds**

```csharp
// Assets/Scripts/Managers/RewardManager.cs
// 8.2 [ОБНОВЛЕНО 2026-08-25]: доля Эпического снижена ещё раз — Обычный 62% / Редкий 35% /
// Эпический 3% (было 60/30/10). Сундук босса гарантированно даёт минимум Редкий предмет.
public ItemTier RollItemRarity(bool isBoss)
{
    float roll = Random.value * 100f;
    ItemTier rarity = roll < 62f ? ItemTier.Common : roll < 97f ? ItemTier.Rare : ItemTier.Epic;

    if (isBoss && rarity == ItemTier.Common)
    {
        rarity = ItemTier.Rare;
    }

    return rarity;
}
```

- [ ] **Step 2: Add a smoke-test statistical check**

```csharp
// RunPureLogicChecks(): rough distribution check over a large sample (deterministic bounds, not exact).
var rewardManagerGO2 = new GameObject("SmokeTest_RewardManager2");
var rewardManager2 = rewardManagerGO2.AddComponent<RewardManager>();
int commonCount = 0, rareCount = 0, epicCount = 0;
const int sampleSize = 20000;
for (int i = 0; i < sampleSize; i++)
{
    switch (rewardManager2.RollItemRarity(false))
    {
        case ItemTier.Common: commonCount++; break;
        case ItemTier.Rare: rareCount++; break;
        case ItemTier.Epic: epicCount++; break;
    }
}
float commonPct = commonCount * 100f / sampleSize;
float epicPct = epicCount * 100f / sampleSize;
Check(commonPct > 59f && commonPct < 65f, $"8.2 доля Обычных ~62%: {commonPct:F1}%");
Check(epicPct > 1.5f && epicPct < 4.5f, $"8.2 доля Эпических ~3%: {epicPct:F1}%");
UnityEngine.Object.DestroyImmediate(rewardManagerGO2);
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Managers/RewardManager.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Chest/merchant item rarity table 60/30/10 -> 62/35/3 (GDD 8.2)"
```

---

### Task 7: Per-floor survived-room counter (prerequisite for Task 8)

**Files:**
- Modify: `Assets/Scripts/Managers/CharacterManager.cs`
- Modify: `Assets/Scripts/UI/RunFlowController.cs` (reset the new counter on floor start)
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Produces: `CharacterManager.RoomsClearedOnCurrentFloor` (int, get), `CharacterManager.BeginFloor()` (resets it to 0), extends the existing `MarkRoomCleared()` to also increment it.

- [ ] **Step 1: Add the counter and reset method**

```csharp
// Assets/Scripts/Managers/CharacterManager.cs — add alongside RoomsClearedThisRun:

// 8.5: комнат пройдено (персонаж выжил) НА ТЕКУЩЕМ этаже — используется формулой мета-валюты
// за поражение, которая теперь считает "этаж смерти" отдельно от общего числа комнат за забег.
// Сбрасывается на старте каждого нового этажа (см. BeginFloor, вызывается из RunFlowController).
public int RoomsClearedOnCurrentFloor { get; private set; }

public void BeginFloor()
{
    RoomsClearedOnCurrentFloor = 0;
}
```

Update `MarkRoomCleared()`:
```csharp
public void MarkRoomCleared()
{
    RoomsClearedThisRun++;
    RoomsClearedOnCurrentFloor++;
}
```

Also reset it in `BeginRun()` (it's already implicitly 0 via auto-property but the floor-start reset in Step 2 covers the first floor since `RunLoop` calls it before the first room):

- [ ] **Step 2: Call `BeginFloor()` at the top of each floor loop in `RunFlowController`**

In `RunFlowController.RunLoop()`, inside the `while (true)` floor loop, right after `floorManager.GenerateRoomBag();`:

```csharp
floorManager.SetFloorState(FloorState.FloorStart);
floorManager.GenerateRoomBag();
characterManager.BeginFloor(); // 8.5: сброс счётчика пройденных комнат этого этажа
totalRoomsThisFloorCached = floorManager.TotalRoomsOnFloor;
UpdateTopBar();
```

- [ ] **Step 3: Add a smoke-test check**

```csharp
// RunPlayModeChecks() — needs a live CharacterManager, reuse the one already found in the file:
characterManager.MarkRoomCleared();
characterManager.MarkRoomCleared();
Check(characterManager.RoomsClearedOnCurrentFloor == 2, $"8.5 счётчик комнат этажа: {characterManager.RoomsClearedOnCurrentFloor} (ожидалось 2)");
characterManager.BeginFloor();
Check(characterManager.RoomsClearedOnCurrentFloor == 0, "8.5 BeginFloor() сбрасывает счётчик комнат этажа");
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Managers/CharacterManager.cs Assets/Scripts/UI/RunFlowController.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Track rooms cleared on current floor (prerequisite for GDD 8.5 defeat formula)"
```

---

### Task 8: Defeat meta-currency formula rework

**Files:**
- Modify: `Assets/Scripts/Managers/RewardManager.cs:107-136`
- Modify: `Assets/Scripts/UI/RunFlowController.cs:900` (call site)
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `CharacterManager.RoomsClearedThisRun` (existing), `CharacterManager.RoomsClearedOnCurrentFloor` (Task 7), `DungeonManager.CurrentFloorNumber` (existing).
- Produces: `RewardManager.CalculateRunCompletionReward(bool victory, int totalRoomsCleared, int currentFloorNumber, int roomsClearedOnDeathFloor)` — signature CHANGES (adds two params).

- [ ] **Step 1: Rewrite `CalculateRunCompletionReward`**

```csharp
// Assets/Scripts/Managers/RewardManager.cs
// 8.5 [ОБНОВЛЕНО 2026-08-25, под цель "20-30 поражений на полный макс всех 3 зданий"]:
// победа = 80 мета/15 гача (без изменений). Поражение: мета-валюта переработана —
// 50 x (число ПОЛНОСТЬЮ пройденных этажей) + 5 x (комнат пройдено НА этаже смерти), потолок
// снят (раньше был 70). "Полностью пройденных этажей" = currentFloorNumber - 1, т.к. этаж
// засчитывается только после победы над его боссом (DungeonManager.AdvanceToNextFloor).
// Умер в первой комнате первого этажа (0 этажей пройдено, 0 комнат на этаже) -> 0 награды,
// как явно требует ГДД. Гача-валюта за поражение НЕ меняется: 2 за каждую пройденную комнату
// за ВЕСЬ забег (totalRoomsCleared), потолок 14.
public RunCompletionReward CalculateRunCompletionReward(bool victory, int totalRoomsCleared, int currentFloorNumber = 0, int roomsClearedOnDeathFloor = 0)
{
    int metaCurrency;
    int gachaCurrency;

    if (victory)
    {
        metaCurrency = 80;
        gachaCurrency = 15;
    }
    else
    {
        int floorsFullyCleared = Mathf.Max(0, currentFloorNumber - 1);
        metaCurrency = 50 * floorsFullyCleared + 5 * Mathf.Max(0, roomsClearedOnDeathFloor);
        gachaCurrency = Mathf.Min(totalRoomsCleared * 2, 14);
    }

    var reward = new RunCompletionReward
    {
        MetaCurrency = metaCurrency,
        GachaCurrency = gachaCurrency
    };

    Debug.Log($"[Reward] Итог забега: {reward.MetaCurrency} мета-валюты, {reward.GachaCurrency} гача-валюты (этажей пройдено: {Mathf.Max(0, currentFloorNumber - 1)}, комнат на этаже смерти: {roomsClearedOnDeathFloor}, комнат всего: {totalRoomsCleared}).");

    return reward;
}
```

- [ ] **Step 2: Update the call site**

```csharp
// Assets/Scripts/UI/RunFlowController.cs:900, was:
// var completion = rewardManager.CalculateRunCompletionReward(victory, characterManager.RoomsClearedThisRun);
var completion = rewardManager.CalculateRunCompletionReward(
    victory,
    characterManager.RoomsClearedThisRun,
    dungeonManager.CurrentFloorNumber,
    characterManager.RoomsClearedOnCurrentFloor);
```

- [ ] **Step 3: Update/replace the now-stale smoke-test checks**

The existing checks in `RunPlayModeChecks()` (lines ~195-202) assert the OLD formula (`15/6`, `70/14` cap, etc.) — these must be replaced, not left alongside the new ones (they'd fail):

```csharp
// Replace the old defeat/cap/zero/victory block with:
var rewardManagerLive = UnityEngine.Object.FindFirstObjectByType<RewardManager>(); // already found above as `rewardManager`
var earlyDeathReward = rewardManager.CalculateRunCompletionReward(false, totalRoomsCleared: 0, currentFloorNumber: 1, roomsClearedOnDeathFloor: 0);
Check(earlyDeathReward.MetaCurrency == 0 && earlyDeathReward.GachaCurrency == 0,
    $"8.5 смерть в 1-й комнате 1-го этажа = 0 награды: {earlyDeathReward.MetaCurrency}/{earlyDeathReward.GachaCurrency} (ожидалось 0/0)");

var midDeathReward = rewardManager.CalculateRunCompletionReward(false, totalRoomsCleared: 15, currentFloorNumber: 3, roomsClearedOnDeathFloor: 2);
// floorsFullyCleared = 3-1 = 2 -> 50*2 + 5*2 = 110; gacha = min(15*2,14) = 14
Check(midDeathReward.MetaCurrency == 110 && midDeathReward.GachaCurrency == 14,
    $"8.5 смерть на этаже 3 (2 комнаты пройдено на нём, 15 всего): {midDeathReward.MetaCurrency}/{midDeathReward.GachaCurrency} (ожидалось 110/14)");

var uncappedReward = rewardManager.CalculateRunCompletionReward(false, totalRoomsCleared: 5, currentFloorNumber: 10, roomsClearedOnDeathFloor: 11);
// floorsFullyCleared = 9 -> 50*9 + 5*11 = 505 -- must NOT be capped at the old 70.
Check(uncappedReward.MetaCurrency == 505, $"8.5 потолок снят: {uncappedReward.MetaCurrency} (ожидалось 505, старый потолок был 70)");

var victoryReward = rewardManager.CalculateRunCompletionReward(true, 0);
Check(victoryReward.MetaCurrency == 80 && victoryReward.GachaCurrency == 15, $"8.5 победа фиксированная: {victoryReward.MetaCurrency}/{victoryReward.GachaCurrency} (ожидалось 80/15)");
```

- [ ] **Step 4: Run the full batch smoke test and confirm PASS**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile -
```
(Adjust the Unity.exe path if the local install differs — check `ProjectSettings/ProjectVersion.txt`, already confirmed as `6000.5.8f1` for this repo.)
Expected: log ends with `[SmokeTest] RESULT=PASS` and `0 ошибок`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Managers/RewardManager.cs Assets/Scripts/UI/RunFlowController.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Rework defeat meta-currency formula: 50x(floors cleared)+5x(rooms on death floor), uncapped (GDD 8.5)"
```

---

## Self-Review Notes

- **Spec coverage:** items 1 (floors), 2 (armor scaling — level formula 2.7 already correct pre-session, no change needed), 9 (level cap + XP sources + monster-count threshold — threshold already correct pre-session), 10 (rarity table), 11 (defeat currency) are all covered by Tasks 1-8.
- **Pre-existing gap flagged, not silently fixed:** `ExperienceSource.SuccessfulEventOrTrap` has no call site anywhere in `RunFlowController` (traps/quests currently grant no XP at all) — this is unrelated to the GDD sync (the formula fix in Task 5 is correct regardless) but the user should know trap/quest XP was never wired up even before this session.
- **Not touched by this plan:** monster types/modifiers (Plan 2), merchant (Plan 3), armor wear rule (already correct in code — verified, no task needed), gacha copy formula / mentor (Plan 4), UI skill descriptions + text outline (Plan 5).
