# Combat Sprite Floor Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop combat sprites (player, monsters, bosses) from visually floating above the background's floor line by compensating for each character's own transparent PNG padding, on top of the existing background-floor-crop fix — without breaking the vertical motion of jump/lunge animation frames (slime, harpy, etc.).

**Architecture:** A pure pixel-scan function finds, for a given `Texture2D`, how far the lowest non-transparent pixel sits from the bottom edge (as a fraction of height). An Editor-only analyzer tool runs this once per character/monster across ALL of that character's animation frames combined (idle + attack + skills), keeping the **minimum** fraction found — the most "grounded" frame — as a single constant per character, and writes the results to a small JSON lookup table (for `Resources`-loaded animation frames) plus a new field directly on boss phase data (for boss sprites, which aren't `Resources`-loaded). At runtime, `RunFlowController` adds this per-combatant constant, converted to pixels, on top of the existing background-floor-line `marginBottom` it already computes every frame.

**Tech Stack:** Unity (C#), UI Toolkit, NUnit EditMode tests, `JsonUtility` (matches this project's existing JSON usage in `SaveManager`).

**Spec:** [Docs/superpowers/specs/2026-09-03-combat-sprite-floor-alignment-design.md](../specs/2026-09-03-combat-sprite-floor-alignment-design.md)

**Project note:** all of `Assets/Scripts` is one Unity assembly (`DungeonGirls.Runtime.asmdef`); `Assets/Tests/EditMode` (`DungeonGirls.Tests.asmdef`) references ONLY that assembly plus the test-runner packages — it does **not** reference `Assets/Editor` (no `.asmdef` there, so it compiles into Unity's implicit Editor assembly, which the test assembly can't see). This means the analyzer tool's actual asset-scanning/file-writing code (Task 4) is not unit-testable and must live in `Assets/Editor/`; the pure, testable pixel math must live in `Assets/Scripts/` instead, so it compiles into `DungeonGirls.Runtime` where EditMode tests can reach it. Every task must leave the whole project compiling (same single-assembly constraint as the previous plan in this repo).

## Global Constraints

- Compensation is **one constant per character/monster** (the minimum bottom-padding fraction across ALL of that character's frames — idle + attack + skill animations together), never recomputed per individual animation frame — a per-frame value would flatten jump/lunge motion (slime, harpy) by snapping every frame to the floor line.
- Unknown/unrecorded characters default to `0f` offset (today's behavior, no regression) — never throw, never leave a sprite invisible.
- No changes to any existing PNG's Texture import settings (200+ files) — pixel scanning must not require `Read/Write Enabled`.
- Boss sprites (`BossPhaseData.phaseSprite`) are not `Resources`-loaded — they get a dedicated field (`floorPaddingFraction`) directly on `BossPhaseData`, not an entry in the JSON table.

---

## Task 1: Pure pixel-scan function

**Files:**
- Create: `Assets/Scripts/UI/SpriteFloorScan.cs`
- Test: `Assets/Tests/EditMode/SpriteFloorScanTests.cs`

**Interfaces:**
- Produces: `public static class SpriteFloorScan { public static float BottomTransparentFraction(Texture2D texture, float alphaThreshold = 0.05f); }` — returns the fraction (0-1) of the texture's height that is fully transparent below the lowest non-transparent pixel row. Requires the `Texture2D` to already be CPU-readable (any texture created via `new Texture2D(...)` + `SetPixels32`/`LoadImage` in memory satisfies this — the caller, not this function, is responsible for getting pixels into a readable texture; see Task 4 for how the real PNGs get there without touching their import settings).

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/SpriteFloorScanTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class SpriteFloorScanTests
{
    static Texture2D MakeTexture(int width, int height, System.Func<int, int, bool> isOpaqueAt)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool opaque = isOpaqueAt(x, y);
                pixels[y * width + x] = opaque ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    [Test]
    public void FullyOpaque_ReturnsZero()
    {
        var tex = MakeTexture(8, 8, (x, y) => true);
        Assert.AreEqual(0f, SpriteFloorScan.BottomTransparentFraction(tex), 0.001f);
        Object.DestroyImmediate(tex);
    }

    [Test]
    public void FullyTransparent_ReturnsZero()
    {
        // Полностью прозрачная текстура — безопасный дефолт "не смещать", не должно бросать/зависать.
        var tex = MakeTexture(8, 8, (x, y) => false);
        Assert.AreEqual(0f, SpriteFloorScan.BottomTransparentFraction(tex), 0.001f);
        Object.DestroyImmediate(tex);
    }

    [Test]
    public void OpaqueOnlyInTopHalf_ReturnsHalf()
    {
        // 10 строк, непрозрачны только строки 0-4 (сверху), 5-9 прозрачны — снизу 5 прозрачных строк из 10 = 0.5.
        var tex = MakeTexture(4, 10, (x, y) => y < 5);
        Assert.AreEqual(0.5f, SpriteFloorScan.BottomTransparentFraction(tex), 0.001f);
        Object.DestroyImmediate(tex);
    }

    [Test]
    public void SinglePixelAtVeryBottom_ReturnsZero()
    {
        var tex = MakeTexture(4, 10, (x, y) => y == 0 && x == 0);
        Assert.AreEqual(0f, SpriteFloorScan.BottomTransparentFraction(tex), 0.001f);
        Object.DestroyImmediate(tex);
    }

    [Test]
    public void AlphaBelowThreshold_TreatedAsTransparent()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color32[16];
        for (int i = 0; i < 16; i++) pixels[i] = new Color32(255, 255, 255, 0);
        // Одна почти-прозрачная строка (alpha 2%) в самом низу — ниже дефолтного порога 5%, должна игнорироваться.
        pixels[0] = new Color32(255, 255, 255, 5); // y=0 (низ), x=0 — alpha ~2%
        var tex2 = MakeTexture(4, 4, (x, y) => y >= 2); // непрозрачные (100%) строки 2-3, строки 0-1 прозрачные
        Assert.AreEqual(0.5f, SpriteFloorScan.BottomTransparentFraction(tex2), 0.001f);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(tex2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: EditMode test filter `SpriteFloorScanTests`
Expected: compile error — `SpriteFloorScan` doesn't exist yet.

- [ ] **Step 3: Implement `SpriteFloorScan`**

Create `Assets/Scripts/UI/SpriteFloorScan.cs`:

```csharp
using UnityEngine;

// Компенсация "зависания" боевых спрайтов (2026-09-03): каждый PNG-кадр анимации персонажа/монстра
// может иметь свой прозрачный отступ снизу холста — UI Toolkit Image всегда центрирует картинку в
// рамке (ни ScaleToFit, ни ScaleAndCrop не прижимают к нижнему краю), поэтому даже когда сама рамка
// стоит на правильной линии пола (см. RunFlowController.Combat.cs ComputeStageFloorGap), видимые
// "ноги" персонажа внутри неё до линии пола не доходят. Эта функция — чистая математика: сколько
// пустоты снизу текстуры. Используется офлайн-анализатором (Assets/Editor/SpriteFloorAnalyzer.cs),
// сама по себе ничего не читает с диска и не требует Read/Write Enabled на текстуре — вызывающая
// сторона обязана передать уже CPU-читаемую текстуру (см. комментарий в SpriteFloorAnalyzer о том,
// как это делается для реальных PNG-ассетов без изменения их настроек импорта).
public static class SpriteFloorScan
{
    public static float BottomTransparentFraction(Texture2D texture, float alphaThreshold = 0.05f)
    {
        int width = texture.width;
        int height = texture.height;
        var pixels = texture.GetPixels32();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a / 255f > alphaThreshold)
                {
                    return (float)y / height;
                }
            }
        }

        // Полностью прозрачная текстура — не должно встречаться в реальных ассетах, но безопасный
        // дефолт "не смещать" лучше, чем деление на некорректное значение или исключение.
        return 0f;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: EditMode test filter `SpriteFloorScanTests`
Expected: PASS (all 5 tests)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/SpriteFloorScan.cs Assets/Tests/EditMode/SpriteFloorScanTests.cs
git commit -m "feat: add pure pixel-scan function for combat sprite floor alignment"
```

---

## Task 2: Runtime offset lookup table (`SpriteFloorOffsets`)

**Files:**
- Create: `Assets/Scripts/UI/SpriteFloorOffsets.cs`
- Test: `Assets/Tests/EditMode/SpriteFloorOffsetsTests.cs`

**Interfaces:**
- Consumes: nothing new (reads a `TextAsset` from `Resources` at a fixed path, produced by Task 4 — must tolerate that asset not existing yet, since this task runs before Task 4).
- Produces:
  - `public static class SpriteFloorOffsets { public static float GetOffsetFraction(string key); public static Dictionary<string, float> ParseTable(string json); }`
  - JSON envelope shape (parsed by `ParseTable`, matches what Task 4's analyzer must write):
    ```json
    {"entries":[{"key":"Jennifer","value":0.08},{"key":"Monster_Bat","value":0.14}]}
    ```

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/SpriteFloorOffsetsTests.cs`:

```csharp
using NUnit.Framework;

public class SpriteFloorOffsetsTests
{
    [Test]
    public void ParseTable_KnownKey_ReturnsValue()
    {
        string json = "{\"entries\":[{\"key\":\"Jennifer\",\"value\":0.08},{\"key\":\"Monster_Bat\",\"value\":0.14}]}";
        var table = SpriteFloorOffsets.ParseTable(json);

        Assert.AreEqual(0.08f, table["Jennifer"], 0.0001f);
        Assert.AreEqual(0.14f, table["Monster_Bat"], 0.0001f);
    }

    [Test]
    public void ParseTable_EmptyEntries_ReturnsEmptyDictionary()
    {
        var table = SpriteFloorOffsets.ParseTable("{\"entries\":[]}");
        Assert.AreEqual(0, table.Count);
    }

    [Test]
    public void GetOffsetFraction_UnknownKey_ReturnsZero()
    {
        // Реальной Resources-таблицы ещё нет (Task 4 её ещё не сгенерировал) — GetOffsetFraction
        // должен безопасно вернуть 0f, а не бросить исключение, независимо от наличия файла.
        Assert.AreEqual(0f, SpriteFloorOffsets.GetOffsetFraction("НесуществующийКлюч"));
    }

    [Test]
    public void GetOffsetFraction_NullKey_ReturnsZero()
    {
        Assert.AreEqual(0f, SpriteFloorOffsets.GetOffsetFraction(null));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: EditMode test filter `SpriteFloorOffsetsTests`
Expected: compile error — `SpriteFloorOffsets` doesn't exist yet.

- [ ] **Step 3: Implement `SpriteFloorOffsets`**

Create `Assets/Scripts/UI/SpriteFloorOffsets.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

// Компенсация "зависания" боевых спрайтов (2026-09-03) — рантайм-сторона таблицы, сгенерированной
// Assets/Editor/SpriteFloorAnalyzer.cs (см. SpriteFloorScan.cs). Один JSON на все анимированные
// (Resources-загружаемые) кадры персонажей/монстров — боссы используют отдельное поле прямо на
// BossPhaseData.floorPaddingFraction (их спрайты не в Resources/, см. CombatSpriteFloorOffset).
public static class SpriteFloorOffsets
{
    const string ResourcePath = "CharacterAnimations/SpriteFloorOffsets";

    [System.Serializable]
    class Entry
    {
        public string key;
        public float value;
    }

    [System.Serializable]
    class Table
    {
        public List<Entry> entries = new List<Entry>();
    }

    static Dictionary<string, float> cachedTable;

    public static float GetOffsetFraction(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return 0f;
        }

        cachedTable ??= Load();
        return cachedTable.TryGetValue(key, out var value) ? value : 0f;
    }

    static Dictionary<string, float> Load()
    {
        var textAsset = Resources.Load<TextAsset>(ResourcePath);
        // Таблица ещё не сгенерирована анализатором (или ассет не существует) — безопасный дефолт:
        // пустая таблица, GetOffsetFraction вернёт 0f для всех ключей (текущее поведение, без регрессии).
        return textAsset != null ? ParseTable(textAsset.text) : new Dictionary<string, float>();
    }

    public static Dictionary<string, float> ParseTable(string json)
    {
        var table = JsonUtility.FromJson<Table>(json) ?? new Table();
        var result = new Dictionary<string, float>();
        foreach (var entry in table.entries)
        {
            result[entry.key] = entry.value;
        }
        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: EditMode test filter `SpriteFloorOffsetsTests`
Expected: PASS (all 4 tests)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/SpriteFloorOffsets.cs Assets/Tests/EditMode/SpriteFloorOffsetsTests.cs
git commit -m "feat: add runtime lookup table for combat sprite floor offsets"
```

---

## Task 3: Wire the offset into combat sprite positioning

**Files:**
- Modify: `Assets/Scripts/UI/PlayableCharacterAnimations.cs`
- Modify: `Assets/Scripts/UI/MonsterAnimations.cs`
- Modify: `Assets/Scripts/Data/BossKitData.cs` (the nested `BossPhaseData` class)
- Create: `Assets/Scripts/Combat/CombatSpriteFloorOffset.cs`
- Test: `Assets/Tests/EditMode/CombatSpriteFloorOffsetTests.cs`
- Modify: `Assets/Scripts/UI/RunFlowController.Combat.cs:567-572`

**Interfaces:**
- Consumes: `SpriteFloorOffsets.GetOffsetFraction(string)` from Task 2.
- Produces: `public static class CombatSpriteFloorOffset { public static float GetOffsetFraction(CombatantRuntime combatant); public static float GetOffsetFraction(CombatantRuntime combatant, System.Func<string, float> lookup); }` — the two-argument overload exists purely so this can be unit-tested without touching the real `Resources`-backed table.
- `PlayableCharacterAnimations.FolderKey(string displayName)` — `public static string`, mirrors the existing `Idle`/`Attack`/`FastAttackLoop` switch pattern.
- `MonsterAnimations.FolderKey(string monsterAnimationKey)` — `public static string` (new public wrapper around the already-private `Lookup`).
- `BossPhaseData.floorPaddingFraction` — `public float`, defaults to `0f` (existing phases with no value set behave exactly as today).

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/CombatSpriteFloorOffsetTests.cs`:

```csharp
using NUnit.Framework;

public class CombatSpriteFloorOffsetTests
{
    static float FakeLookup(string key) => key switch
    {
        "Jennifer" => 0.08f,
        "Monster_Bat" => 0.14f,
        _ => 0f
    };

    [Test]
    public void Player_UsesFolderKeyFromDisplayName()
    {
        var player = new CombatantRuntime { IsPlayer = true, DisplayName = "Дженифер" };
        Assert.AreEqual(0.08f, CombatSpriteFloorOffset.GetOffsetFraction(player, FakeLookup), 0.0001f);
    }

    [Test]
    public void Player_UnknownDisplayName_ReturnsZero()
    {
        var player = new CombatantRuntime { IsPlayer = true, DisplayName = "Кто-то новый" };
        Assert.AreEqual(0f, CombatSpriteFloorOffset.GetOffsetFraction(player, FakeLookup), 0.0001f);
    }

    [Test]
    public void Monster_UsesMonsterPrefixedFolderKey()
    {
        var monster = new CombatantRuntime { IsPlayer = false, MonsterAnimationKey = "Летучая мышь" };
        Assert.AreEqual(0.14f, CombatSpriteFloorOffset.GetOffsetFraction(monster, FakeLookup), 0.0001f);
    }

    [Test]
    public void Monster_UnknownAnimationKey_ReturnsZero()
    {
        var monster = new CombatantRuntime { IsPlayer = false, MonsterAnimationKey = "Неизвестный монстр" };
        Assert.AreEqual(0f, CombatSpriteFloorOffset.GetOffsetFraction(monster, FakeLookup), 0.0001f);
    }

    [Test]
    public void Boss_UsesCurrentPhaseFloorPaddingDirectly_NotTheLookupTable()
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.phases.Add(new BossPhaseData { hpThresholdPercent = 100f, floorPaddingFraction = 0.22f });
        var boss = new CombatantRuntime { IsPlayer = false, MonsterAnimationKey = "Не должно использоваться" };
        boss.BossEncounter = new BossEncounterState(kit);

        // FakeLookup не знает "Не должно использоваться" (вернул бы 0) — если результат НЕ 0.22,
        // значит боссовский путь ошибочно ушёл через таблицу вместо floorPaddingFraction фазы.
        Assert.AreEqual(0.22f, CombatSpriteFloorOffset.GetOffsetFraction(boss, FakeLookup), 0.0001f);

        Object.DestroyImmediate(kit);
    }

    [Test]
    public void NullCombatant_ReturnsZero()
    {
        Assert.AreEqual(0f, CombatSpriteFloorOffset.GetOffsetFraction(null, FakeLookup));
    }

    [Test]
    public void SingleArgumentOverload_UsesRealSpriteFloorOffsetsTable()
    {
        // Не проверяем конкретное значение (реальная таблица ещё не сгенерирована на этом этапе
        // плана) — только то, что однопараметрический вызов не бросает и возвращает валидное число
        // (SpriteFloorOffsets.GetOffsetFraction безопасно возвращает 0f для отсутствующей таблицы).
        var player = new CombatantRuntime { IsPlayer = true, DisplayName = "Дженифер" };
        Assert.DoesNotThrow(() => CombatSpriteFloorOffset.GetOffsetFraction(player));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: EditMode test filter `CombatSpriteFloorOffsetTests`
Expected: compile error — `CombatSpriteFloorOffset` doesn't exist yet, `BossPhaseData.floorPaddingFraction` doesn't exist yet, `PlayableCharacterAnimations.FolderKey`/`MonsterAnimations.FolderKey` don't exist yet.

- [ ] **Step 3: Add `floorPaddingFraction` to `BossPhaseData`**

In `Assets/Scripts/Data/BossKitData.cs`, inside the `BossPhaseData` class, add (after `phaseSprite`):

```csharp
    [Tooltip("Компенсация 'зависания' в воздухе (2026-09-03) — доля высоты канваса phaseSprite, " +
        "занятая прозрачным отступом снизу. 0 = спрайт вплотную к низу холста (безопасный дефолт " +
        "для фаз, для которых анализатор ещё не запускался). Заполняется Assets/Editor/" +
        "SpriteFloorAnalyzer.cs, вручную не редактировать.")]
    public float floorPaddingFraction;
```

- [ ] **Step 4: Add `PlayableCharacterAnimations.FolderKey`**

In `Assets/Scripts/UI/PlayableCharacterAnimations.cs`, add this method (mirroring `Idle`/`Attack`):

```csharp
    // Компенсация "зависания" боевых спрайтов (2026-09-03) — ключ ровно совпадает с именем папки
    // под Assets/Resources/CharacterAnimations/, которую уже используют Jennifer/Sasha/Violet
    // AnimationFrames-классы через Resources.Load — см. CombatSpriteFloorOffset.
    public static string FolderKey(string displayName) => displayName switch
    {
        "Дженифер" => "Jennifer",
        "Саша" => "Sasha",
        "Вайолет" => "Violet",
        _ => null
    };
```

- [ ] **Step 5: Add `MonsterAnimations.FolderKey`**

In `Assets/Scripts/UI/MonsterAnimations.cs`, add this public method (the private `Lookup` and `Entry.FolderKey` already exist unchanged):

```csharp
    // Компенсация "зависания" боевых спрайтов (2026-09-03) — публичная обёртка над уже
    // существующим Lookup(...).FolderKey, используемая CombatSpriteFloorOffset. Без префикса
    // "Monster_" — вызывающая сторона сама собирает итоговый ключ таблицы (см.
    // CombatSpriteFloorOffset), т.к. этот же FolderKey используется и MonsterAnimationFrames.Load
    // без префикса для построения пути Resources.Load.
    public static string FolderKey(string monsterAnimationKey) => Lookup(monsterAnimationKey)?.FolderKey;
```

- [ ] **Step 6: Create `CombatSpriteFloorOffset`**

Create `Assets/Scripts/Combat/CombatSpriteFloorOffset.cs`:

```csharp
using System;

// Компенсация "зависания" боевых спрайтов (2026-09-03) — один вызов на комбатанта (игрок/монстр/
// босс), определяет константный отступ снизу для ВСЕХ его кадров анимации сразу (не пересчитывается
// по кадрам — см. SpriteFloorOffsets/SpriteFloorScan, почему это важно для прыжковых анимаций).
// Диспетчеризация: босс — читает floorPaddingFraction ТЕКУЩЕЙ фазы напрямую с BossPhaseData (не
// через таблицу — боссовские спрайты не в Resources/); игрок/обычный монстр — через таблицу
// SpriteFloorOffsets по ключу папки анимации (Jennifer/Sasha/Violet или Monster_<Key>).
public static class CombatSpriteFloorOffset
{
    public static float GetOffsetFraction(CombatantRuntime combatant) =>
        GetOffsetFraction(combatant, SpriteFloorOffsets.GetOffsetFraction);

    public static float GetOffsetFraction(CombatantRuntime combatant, Func<string, float> lookup)
    {
        if (combatant == null)
        {
            return 0f;
        }

        if (combatant.BossEncounter != null)
        {
            return combatant.BossEncounter.CurrentPhase.floorPaddingFraction;
        }

        if (combatant.IsPlayer)
        {
            var key = PlayableCharacterAnimations.FolderKey(combatant.DisplayName);
            return key != null ? lookup(key) : 0f;
        }

        var monsterKey = MonsterAnimations.FolderKey(combatant.MonsterAnimationKey);
        return monsterKey != null ? lookup($"Monster_{monsterKey}") : 0f;
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: EditMode test filter `CombatSpriteFloorOffsetTests`
Expected: PASS (all 7 tests)

- [ ] **Step 8: Wire into `RunFlowController.Combat.cs`'s `UpdateCombatUI`**

Replace (originally lines 567-572):

```csharp
        float stageFloorGap = GetStageFloorGapFromBottom();
        playerStageWrapper.style.marginBottom = stageFloorGap;

        foreach (var entry in enemyStageEntries)
        {
            entry.Wrapper.style.marginBottom = stageFloorGap;
```

with:

```csharp
        float stageFloorGap = GetStageFloorGapFromBottom();
        float playerFloorOffset = CombatSpriteFloorOffset.GetOffsetFraction(player) * playerStageSprite.resolvedStyle.height;
        playerStageWrapper.style.marginBottom = stageFloorGap + playerFloorOffset;

        foreach (var entry in enemyStageEntries)
        {
            float enemyFloorOffset = CombatSpriteFloorOffset.GetOffsetFraction(entry.Combatant) * entry.Sprite.resolvedStyle.height;
            entry.Wrapper.style.marginBottom = stageFloorGap + enemyFloorOffset;
```

(`player` is already in scope at the top of `UpdateCombatUI` — `var player = combatManager.Player;`. `resolvedStyle.height` being `0` on the very first frame after `ShowOnly(combatPanel)` self-corrects next frame, same as `GetStageFloorGapFromBottom` already handles for `combatPanel.resolvedStyle` — no new edge case introduced.)

- [ ] **Step 9: Run the full EditMode suite**

Run: full EditMode suite (Unity Test Runner, or `Unity.exe -batchmode -nographics -projectPath "C:/Unity Projects/DungeonGirls" -runTests -testPlatform EditMode -testResults <path>.xml -logFile <path>.log` — **no `-quit`**, it silently no-ops `-runTests` in this Unity version, see project memory)
Expected: PASS, 0 failures, including every new test file from Tasks 1-3.

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/UI/PlayableCharacterAnimations.cs Assets/Scripts/UI/MonsterAnimations.cs Assets/Scripts/Data/BossKitData.cs Assets/Scripts/Combat/CombatSpriteFloorOffset.cs Assets/Tests/EditMode/CombatSpriteFloorOffsetTests.cs Assets/Scripts/UI/RunFlowController.Combat.cs
git commit -m "feat: apply per-character floor-offset compensation to combat sprite positioning"
```

---

## Task 4: Editor analyzer — generate the real offset data

**Files:**
- Create: `Assets/Editor/SpriteFloorAnalyzer.cs`
- Modify (generated, not hand-written): `Assets/Resources/CharacterAnimations/SpriteFloorOffsets.json`
- Modify (generated, not hand-written): `Assets/ScriptableObjects/Bosses/BossKit_Warden.asset`

**Interfaces:**
- Consumes: `SpriteFloorScan.BottomTransparentFraction` from Task 1 (the analyzer's PNG-reading step must decode each file into an in-memory readable `Texture2D` itself — via `Texture2D.LoadImage`, NOT by touching the asset's import settings — before calling this function).
- Produces: the real `SpriteFloorOffsets.json` data file Task 2's `SpriteFloorOffsets.Load()` reads, and real `floorPaddingFraction` values on `BossKit_Warden.asset`'s phases.

- [ ] **Step 1: Write the analyzer**

Create `Assets/Editor/SpriteFloorAnalyzer.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Компенсация "зависания" боевых спрайтов (2026-09-03) — разовый инструмент (запускается через
// -executeMethod SpriteFloorAnalyzer.Run), но ОСТАЁТСЯ в репозитории (в отличие от прошлых one-off
// скриптов в этом проекте) — таблицу нужно перегенерировать при добавлении новых боевых спрайтов.
// См. Docs/superpowers/specs/2026-09-03-combat-sprite-floor-alignment-design.md.
public static class SpriteFloorAnalyzer
{
    const string CharacterAnimationsRoot = "Assets/Resources/CharacterAnimations";
    const string OutputJsonPath = "Assets/Resources/CharacterAnimations/SpriteFloorOffsets.json";

    [System.Serializable]
    class Entry
    {
        public string key;
        public float value;
    }

    [System.Serializable]
    class Table
    {
        public List<Entry> entries = new List<Entry>();
    }

    public static void Run()
    {
        var table = new Table();

        foreach (var folderKey in TopLevelFolderKeys())
        {
            float min = MinBottomTransparentFractionInFolder(Path.Combine(CharacterAnimationsRoot, folderKey));
            table.entries.Add(new Entry { key = folderKey, value = min });
            Debug.Log($"[SpriteFloorAnalyzer] {folderKey}: {min:F4}");
        }

        string json = JsonUtility.ToJson(table, true);
        File.WriteAllText(OutputJsonPath, json);
        AssetDatabase.ImportAsset(OutputJsonPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[SpriteFloorAnalyzer] Wrote {table.entries.Count} entries to {OutputJsonPath}");

        RunOnBossKits();

        AssetDatabase.SaveAssets();
        Debug.Log("[SpriteFloorAnalyzer] Done.");
    }

    static IEnumerable<string> TopLevelFolderKeys() =>
        Directory.GetDirectories(CharacterAnimationsRoot).Select(Path.GetFileName).OrderBy(k => k);

    static float MinBottomTransparentFractionInFolder(string folderPath)
    {
        var pngPaths = Directory.GetFiles(folderPath, "*.png", SearchOption.AllDirectories);
        float min = 1f;
        foreach (var pngPath in pngPaths)
        {
            float fraction = BottomTransparentFractionOfFile(pngPath);
            if (fraction < min)
            {
                min = fraction;
            }
        }
        // Папка без PNG (не должно происходить для реальных CharacterAnimations-папок) — 0f,
        // безопасный дефолт "не смещать".
        return pngPaths.Length > 0 ? min : 0f;
    }

    // Декодирует PNG-файл В ПАМЯТИ через LoadImage — работает независимо от Read/Write Enabled в
    // импорте ассета (не трогаем настройки импорта у 200+ существующих файлов), не требует
    // RenderTexture/GPU-контекста.
    static float BottomTransparentFractionOfFile(string relativeOrAbsolutePath)
    {
        string absolutePath = Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(Directory.GetCurrentDirectory(), relativeOrAbsolutePath);
        byte[] bytes = File.ReadAllBytes(absolutePath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(bytes);
        float fraction = SpriteFloorScan.BottomTransparentFraction(texture);
        Object.DestroyImmediate(texture);
        return fraction;
    }

    static void RunOnBossKits()
    {
        var guids = AssetDatabase.FindAssets("t:BossKitData");
        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var kit = AssetDatabase.LoadAssetAtPath<BossKitData>(assetPath);
            if (kit == null)
            {
                continue;
            }

            foreach (var phase in kit.phases)
            {
                if (phase.phaseSprite == null)
                {
                    continue;
                }

                string spritePath = AssetDatabase.GetAssetPath(phase.phaseSprite.texture);
                float fraction = BottomTransparentFractionOfFile(spritePath);
                phase.floorPaddingFraction = fraction;
                Debug.Log($"[SpriteFloorAnalyzer] {assetPath} / {phase.phaseName}: {fraction:F4}");
            }

            EditorUtility.SetDirty(kit);
        }
    }
}
```

- [ ] **Step 2: Run it on the real project assets**

Run:
```
"C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -projectPath "C:/Unity Projects/DungeonGirls" -executeMethod SpriteFloorAnalyzer.Run -logFile <some temp path>.log
```
Expected: exit code 0, log contains one `[SpriteFloorAnalyzer] <FolderKey>: <fraction>` line per top-level folder under `Assets/Resources/CharacterAnimations/` (currently: `Jennifer`, `Sasha`, `Violet`, `Monster_Bat`, `Monster_DarkKnight`, `Monster_DarkPriest`, `Monster_GoblinThief`, `Monster_Harpy`, `Monster_PoisonSpiderling`, `Monster_Skeleton`, `Monster_Slime`, `Monster_StoneGuardian`, `Monster_Warlock` — 13 entries), plus one line per boss phase with a `phaseSprite` set, and a final `Wrote 13 entries to ...` / `Done.` line.

- [ ] **Step 3: Verify the generated data**

Read `Assets/Resources/CharacterAnimations/SpriteFloorOffsets.json` back and confirm:
- It has exactly one entry per folder listed above (13 entries).
- Every `value` is between `0` and `1` (a sane fraction, not a NaN, not negative, not `>1`).
- No two characters that clearly have very different art (e.g. `Jennifer` vs `Monster_Slime`) show the exact same value by coincidence-of-a-bug (a suspiciously identical `0` or `1` across everything would indicate the scan isn't actually reading real pixel data — investigate before proceeding rather than committing broken data).

Read `Assets/ScriptableObjects/Bosses/BossKit_Warden.asset` back and confirm both phases now have a non-default (or confirmed-correct-if-genuinely-zero) `floorPaddingFraction` line, distinct from `0` unless the Warden's phase art genuinely touches its canvas bottom.

- [ ] **Step 4: Run the full EditMode suite once more**

Run: full EditMode suite (same command as Task 3 Step 9)
Expected: PASS, 0 failures — in particular, `SpriteFloorOffsetsTests` still passes (it doesn't depend on the real table's contents, only on parsing/lookup mechanics) and nothing regresses.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/SpriteFloorAnalyzer.cs Assets/Resources/CharacterAnimations/SpriteFloorOffsets.json Assets/Resources/CharacterAnimations/SpriteFloorOffsets.json.meta Assets/ScriptableObjects/Bosses/BossKit_Warden.asset
git commit -m "feat: generate combat sprite floor-offset data from real assets"
```

---

## Task 5: Final verification pass

**Files:** none (verification only)

- [ ] **Step 1: Run the full EditMode suite one more time**

Run: full EditMode suite
Expected: PASS, 0 failures.

- [ ] **Step 2: Manual playtest checklist**

In the Editor, play through at least one room fight per class (Jennifer/Warrior, Rogue, Barbarian)
against a few different monster types, plus a Warden boss fight if convenient, and confirm:
- Player and monster sprites visually stand on the floor line, not floating above it.
- Jump/bounce-style animations (Slime idle/attack in particular — the motivating example from this
  plan's design discussion) still visibly move up and down; they do not look flattened or frozen at
  a single height.
- Harpy's flight/attack animation (if it has vertical motion) similarly still reads as airborne
  motion, not floor-snapped every frame.
- The Warden boss (both phases) also stands correctly on the floor.

- [ ] **Step 3: Update project memory**

Record this plan's completion (commit range, what was verified, and that the manual playtest from
this plan AND the still-outstanding manual playtest debt from the previous
`2026-09-03-active-skills-panel` plan should ideally be done together in one sitting, since both are
waiting on the same kind of human verification).
