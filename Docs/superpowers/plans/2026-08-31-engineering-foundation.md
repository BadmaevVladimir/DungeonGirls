# Инженерный фундамент: asmdef, тесты, stable SkillId — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Дать проекту asmdef-границы и EditMode-тесты на чистую логику, заменить сравнение боевых
навыков по строковому `skillName` на стабильный `SkillId`-enum, убрать дублирование UI-форматтеров.

**Architecture:** Два новых asmdef (`DungeonGirls.Runtime`, `DungeonGirls.Tests`). Новый `enum SkillId`
в `Data/Enums.cs` + поле `skillId` на `PassiveSkillData`/`ActiveSkillData`, заполняется на 46
существующих `.asset` одноразовым editor-скриптом по текущему `skillName`. Все места сравнения
навыков по строке переводятся на `SkillId` через сигнатуры `RunCharacterProgress.GetSkillLevel`/
`GetEffectiveUniquePassiveLevel`/`GetMentorUniquePassiveLevel`, новое поле
`CombatantRuntime.MonsterPassiveSkillId` (замена `MonsterPassiveName`), и новый параметр
`skillId` в `CombatManager.ConfigureUniqueActiveSkill`. Персистентный `VeteranCharacter.
uniquePassiveSkillName` (строка в `SaveData`) НЕ меняет формат — вместо этого сравнивается через
новый `SkillEffectMap.ResolveId(string)`, чтобы не трогать формат сохранения (см. Non-Goals спеки).

**Tech Stack:** Unity 6000.5.8f1, C#, Unity Test Framework (NUnit, EditMode), JsonUtility save.

**Spec:** `Docs/superpowers/specs/2026-08-31-engineering-foundation-design.md`

## Global Constraints

- Не трогаем `RunFlowController`/`HubManager` декомпозицию — только точечные правки мест сравнения
  навыков и вынос трёх форматтеров.
- Не трогаем strategy-паттерн — switch/if-ветки в `CombatManager`/`CombatantFactory` остаются, меняется
  только тип сравнения (string → `SkillId`).
- Не трогаем формат `SaveData`/persisted `VeteranCharacter.uniquePassiveSkillName` — остаётся строкой.
- `MagnumOpus`/`ThreeQuickStrikes` не получают `SkillId` — по ним нет сравнений в коде.
- После каждой задачи, трогающей `Assets/Scripts/**`, проект должен компилироваться без ошибок в Unity
  Editor (batchmode-проверка, см. Task 9) — но полный batchmode-прогон делаем один раз в конце (Task 9),
  не после каждой задачи, чтобы не тратить время на 10 отдельных запусков Unity.
- Русские комментарии в духе существующего кода — по желанию, не обязательны для новых тестовых
  файлов (это новый паттерн — Unity Test Framework, не `PlayModeSmokeTest`-стиль).

---

## Task 1: Assembly Definitions

**Files:**
- Create: `Assets/Scripts/DungeonGirls.Runtime.asmdef`
- Create: `Assets/Tests/EditMode/DungeonGirls.Tests.asmdef`

**Interfaces:**
- Produces: assembly name `DungeonGirls.Runtime` (referenced by Task 7's test asmdef).

- [ ] **Step 1: Create the Runtime asmdef**

`Assets/Scripts/DungeonGirls.Runtime.asmdef`:
```json
{
    "name": "DungeonGirls.Runtime",
    "rootNamespace": "",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`DOTween`/`UnityEngine.InputSystem`/`UnityEngine.UIElements` are engine-provided modules Unity
auto-resolves for any asmdef with `noEngineReferences: false` — no explicit `references` entries
needed for them. If DOTween ships its own asmdef in this project (check
`find "Assets/Plugins" -iname "*.asmdef"` — likely `DOTween.asmdef` or similar under
`Assets/Plugins/Demigiant`), add its assembly name to `references` before the next step; otherwise
leave `references: []`.

- [ ] **Step 2: Create the Tests folder and asmdef**

`Assets/Tests/EditMode/DungeonGirls.Tests.asmdef`:
```json
{
    "name": "DungeonGirls.Tests",
    "rootNamespace": "",
    "references": [
        "DungeonGirls.Runtime",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: Open Unity Editor and let it regenerate the solution / recompile**

Run (from repo root, adjust the Unity path if the Hub installed it elsewhere):
```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "C:/Unity Projects/DungeonGirls" -logFile "C:/Unity Projects/DungeonGirls/unity_asmdef_check.log"
```
Expected: exit code 0. If not, open `unity_asmdef_check.log` and search for `error CS` — most likely
cause is `Assets/Editor/*.cs` (default Editor assembly, not in either new asmdef) losing implicit
visibility into `Assets/Scripts/**` now that those are walled into `DungeonGirls.Runtime` — Unity's
default (asmdef-less) assemblies DO reference all asmdef-defined assemblies automatically, so this
should not happen, but verify by grep for `error CS0246` (type not found) in the log before assuming
success.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/DungeonGirls.Runtime.asmdef" "Assets/Scripts/DungeonGirls.Runtime.asmdef.meta" "Assets/Tests/EditMode/DungeonGirls.Tests.asmdef" "Assets/Tests/EditMode/DungeonGirls.Tests.asmdef.meta"
git commit -m "build: add Runtime and EditMode Tests assembly definitions"
```
(`.meta` files are auto-generated by the Unity Editor step above — verify they exist before adding.)

---

## Task 2: `SkillId` enum + data fields + resolvers

**Files:**
- Modify: `Assets/Scripts/Data/Enums.cs`
- Modify: `Assets/Scripts/Data/PassiveSkillData.cs`
- Modify: `Assets/Scripts/Data/ActiveSkillData.cs`
- Modify: `Assets/Scripts/Combat/SkillEffectMap.cs`
- Modify: `Assets/Scripts/Combat/MonsterSkillEffectMap.cs`
- Test: `Assets/Tests/EditMode/SkillEffectMapTests.cs`

**Interfaces:**
- Produces: `enum SkillId` (42 named values + `None = 0`), `PassiveSkillData.skillId`,
  `ActiveSkillData.skillId`, `SkillEffectMap.ResolveId(string skillName) : SkillId`,
  `MonsterSkillEffectMap.ResolveId(string skillName) : SkillId`.

- [ ] **Step 1: Add `SkillId` to `Enums.cs`**

Append to `Assets/Scripts/Data/Enums.cs` (open the file first to confirm it ends with a closing
brace on its own line and no trailing enum shares a name below):

```csharp
// Стабильный идентификатор навыка — не меняется при переименовании skillName в инспекторе.
// Заменяет строковое сравнение по SkillEffectMap/MonsterSkillEffectMap константам в боевой логике.
public enum SkillId
{
    None = 0,
    FieldRepair, Freeze, Luck, Evasion, Sturdy, CriticalHits, IAmTheWall, Ambidexterity, Thorns,
    Unyielding, Bleed,
    Vampirism, ArmorBreak, Piercing, Repair, Elusiveness, GoldenTouch, ToughSole,
    EyeForAnEye, PoisonedBlade, ByAThread, Elimination, SlipAway,
    Stubbornness, Frenzy, CombatRegen, Intimidation, Superstition,
    Shadow, SmokeBomb, ChampionOfTheTribe, Berserk,
    Riposte, EmbraceOfNight, Execution, GiantSlayer, JustAScratch,
    MonsterSlowCurse, MonsterFluttering, MonsterArmorPiercingBlade, MonsterCorrosion,
    MonsterStunningScream, MonsterDarkHeal, MonsterDoubleStrike
}
```

Monster values are prefixed `Monster*` to keep them visually distinct at call sites (e.g.
`SkillId.MonsterCorrosion`) even though there's no actual name collision with the character-skill
values above.

- [ ] **Step 2: Add `skillId` field to both skill data classes**

`Assets/Scripts/Data/PassiveSkillData.cs` — add after `public string skillName;`:
```csharp
    public SkillId skillId;
```

`Assets/Scripts/Data/ActiveSkillData.cs` — add after `public string skillName;`:
```csharp
    public SkillId skillId;
```

- [ ] **Step 3: Add `ResolveId` to `SkillEffectMap.cs`**

Append inside the class, before the closing `}`:
```csharp
    // Разовый мост string -> SkillId для мест, где имя приходит из персистентных данных
    // (SaveData.VeteranCharacter.uniquePassiveSkillName), а не напрямую из PassiveSkillData.skillId.
    // Не для нового кода в боевой логике — там читайте .skillId с самого ассета.
    public static SkillId ResolveId(string skillName) => skillName switch
    {
        FieldRepair => SkillId.FieldRepair, Freeze => SkillId.Freeze, Luck => SkillId.Luck,
        Evasion => SkillId.Evasion, Sturdy => SkillId.Sturdy, CriticalHits => SkillId.CriticalHits,
        IAmTheWall => SkillId.IAmTheWall, Ambidexterity => SkillId.Ambidexterity, Thorns => SkillId.Thorns,
        Unyielding => SkillId.Unyielding, Bleed => SkillId.Bleed,
        Vampirism => SkillId.Vampirism, ArmorBreak => SkillId.ArmorBreak, Piercing => SkillId.Piercing,
        Repair => SkillId.Repair, Elusiveness => SkillId.Elusiveness, GoldenTouch => SkillId.GoldenTouch,
        ToughSole => SkillId.ToughSole,
        EyeForAnEye => SkillId.EyeForAnEye, PoisonedBlade => SkillId.PoisonedBlade,
        ByAThread => SkillId.ByAThread, Elimination => SkillId.Elimination, SlipAway => SkillId.SlipAway,
        Stubbornness => SkillId.Stubbornness, Frenzy => SkillId.Frenzy, CombatRegen => SkillId.CombatRegen,
        Intimidation => SkillId.Intimidation, Superstition => SkillId.Superstition,
        Shadow => SkillId.Shadow, SmokeBomb => SkillId.SmokeBomb,
        ChampionOfTheTribe => SkillId.ChampionOfTheTribe, Berserk => SkillId.Berserk,
        Riposte => SkillId.Riposte, EmbraceOfNight => SkillId.EmbraceOfNight,
        Execution => SkillId.Execution, GiantSlayer => SkillId.GiantSlayer, JustAScratch => SkillId.JustAScratch,
        _ => SkillId.None
    };
```

- [ ] **Step 4: Add `ResolveId` to `MonsterSkillEffectMap.cs`**

Append inside the class, before the closing `}`:
```csharp
    public static SkillId ResolveId(string skillName) => skillName switch
    {
        SlowCurse => SkillId.MonsterSlowCurse,
        Fluttering => SkillId.MonsterFluttering,
        ArmorPiercingBlade => SkillId.MonsterArmorPiercingBlade,
        Corrosion => SkillId.MonsterCorrosion,
        StunningScream => SkillId.MonsterStunningScream,
        DarkHeal => SkillId.MonsterDarkHeal,
        DoubleStrike => SkillId.MonsterDoubleStrike,
        _ => SkillId.None
    };
```

- [ ] **Step 5: Write the test**

`Assets/Tests/EditMode/SkillEffectMapTests.cs`:
```csharp
using NUnit.Framework;

public class SkillEffectMapTests
{
    [Test]
    public void ResolveId_KnownCharacterSkillName_ReturnsMatchingId()
    {
        Assert.AreEqual(SkillId.Berserk, SkillEffectMap.ResolveId(SkillEffectMap.Berserk));
        Assert.AreEqual(SkillId.Sturdy, SkillEffectMap.ResolveId(SkillEffectMap.Sturdy));
    }

    [Test]
    public void ResolveId_UnknownName_ReturnsNone()
    {
        Assert.AreEqual(SkillId.None, SkillEffectMap.ResolveId("не существует"));
        Assert.AreEqual(SkillId.None, SkillEffectMap.ResolveId(null));
    }

    [Test]
    public void MonsterResolveId_KnownMonsterSkillName_ReturnsMatchingId()
    {
        Assert.AreEqual(SkillId.MonsterCorrosion, MonsterSkillEffectMap.ResolveId(MonsterSkillEffectMap.Corrosion));
    }

    [Test]
    public void MonsterResolveId_UnknownName_ReturnsNone()
    {
        Assert.AreEqual(SkillId.None, MonsterSkillEffectMap.ResolveId("не существует"));
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run (assumes Unity Editor is closed so batchmode can take the project lock):
```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testFilter "SkillEffectMapTests" -testResults "C:/Unity Projects/DungeonGirls/test_results_task2.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_task2.log"
```
Expected: `test_results_task2.xml` shows 4 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Data/Enums.cs" "Assets/Scripts/Data/PassiveSkillData.cs" "Assets/Scripts/Data/ActiveSkillData.cs" "Assets/Scripts/Combat/SkillEffectMap.cs" "Assets/Scripts/Combat/MonsterSkillEffectMap.cs" "Assets/Tests/EditMode/SkillEffectMapTests.cs" "Assets/Tests/EditMode/SkillEffectMapTests.cs.meta"
git commit -m "feat: add SkillId enum and skillId field on skill data"
```

---

## Task 3: Populate `skillId` on existing 46 skill assets

**Files:**
- Create (temporary, deleted at end of this task): `Assets/Editor/AssignSkillIdsFromNames.cs`
- Modify (data only): all `.asset` files under `Assets/ScriptableObjects/Skills/**`

**Interfaces:**
- Consumes: `SkillEffectMap.ResolveId`, `MonsterSkillEffectMap.ResolveId` (Task 2).
- Produces: every `PassiveSkillData`/`ActiveSkillData` asset whose current `skillName` matches a
  constant in either map now has `skillId` set to the matching `SkillId` value on disk.

- [ ] **Step 1: Write the one-shot migration script**

`Assets/Editor/AssignSkillIdsFromNames.cs`:
```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Разовый скрипт (как PlayModeSmokeTest.cs) — заполняет PassiveSkillData.skillId/ActiveSkillData.skillId
// на существующих ассетах по их текущему skillName, сопоставляя с SkillEffectMap/MonsterSkillEffectMap.
// Удаляется после однократного запуска (см. Task 3, Step 4 плана 2026-08-31-engineering-foundation.md).
public static class AssignSkillIdsFromNames
{
    public static void Run()
    {
        var unmatched = new List<string>();
        int passiveUpdated = 0;
        int activeUpdated = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:PassiveSkillData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<PassiveSkillData>(path);
            if (asset == null) continue;

            SkillId id = SkillEffectMap.ResolveId(asset.skillName);
            if (id == SkillId.None) id = MonsterSkillEffectMap.ResolveId(asset.skillName);

            if (id == SkillId.None)
            {
                unmatched.Add($"{path} (skillName=\"{asset.skillName}\")");
                continue;
            }

            asset.skillId = id;
            EditorUtility.SetDirty(asset);
            passiveUpdated++;
        }

        foreach (string guid in AssetDatabase.FindAssets("t:ActiveSkillData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<ActiveSkillData>(path);
            if (asset == null) continue;

            SkillId id = SkillEffectMap.ResolveId(asset.skillName);
            if (id == SkillId.None)
            {
                unmatched.Add($"{path} (skillName=\"{asset.skillName}\")");
                continue;
            }

            asset.skillId = id;
            EditorUtility.SetDirty(asset);
            activeUpdated++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[AssignSkillIdsFromNames] Passive updated: {passiveUpdated}, Active updated: {activeUpdated}, unmatched: {unmatched.Count}");
        foreach (string entry in unmatched)
        {
            Debug.Log($"[AssignSkillIdsFromNames] Unmatched (expected for MagnumOpus/ThreeQuickStrikes): {entry}");
        }
    }
}
```

- [ ] **Step 2: Run it in batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "C:/Unity Projects/DungeonGirls" -executeMethod AssignSkillIdsFromNames.Run -logFile "C:/Unity Projects/DungeonGirls/unity_task3.log"
```

- [ ] **Step 3: Verify the unmatched list**

Search the log for `Unmatched`:
```bash
grep "Unmatched" "C:/Unity Projects/DungeonGirls/unity_task3.log"
```
Expected: exactly the assets carrying `MagnumOpus`/`ThreeQuickStrikes` `skillName`s (2 entries) —
if anything else shows up unmatched, stop and check whether that asset's `skillName` has drifted from
both maps' constants (fix the map constant or the asset before continuing — do not proceed with a
silent gap).

Then verify the count of changed `.asset` files matches expectations:
```bash
cd "C:/Unity Projects/DungeonGirls" && git status --short "Assets/ScriptableObjects/Skills" | wc -l
```
Expected: 44 (46 total assets minus the 2 unmatched).

- [ ] **Step 4: Delete the one-shot script and commit**

```bash
cd "C:/Unity Projects/DungeonGirls" && rm "Assets/Editor/AssignSkillIdsFromNames.cs" "Assets/Editor/AssignSkillIdsFromNames.cs.meta"
git add -A "Assets/ScriptableObjects/Skills" "Assets/Editor/AssignSkillIdsFromNames.cs" "Assets/Editor/AssignSkillIdsFromNames.cs.meta"
git commit -m "data: populate SkillId on existing skill assets from skillName"
```

---

## Task 4: Migrate `RunCharacterProgress` skill lookups to `SkillId`

**Files:**
- Modify: `Assets/Scripts/Progression/RunCharacterProgress.cs`
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs` (21 call sites)
- Modify: `Assets/Scripts/UI/RunFlowController.cs` (2 call sites, lines 1501 and 2000)
- Modify: `Assets/Scripts/Managers/CampManager.cs` (1 call site, line 59)
- Test: `Assets/Tests/EditMode/RunCharacterProgressTests.cs`

**Interfaces:**
- Consumes: `PassiveSkillData.skillId`, `ActiveSkillData.skillId`, `SkillEffectMap.ResolveId` (Task 2).
- Produces: `RunCharacterProgress.GetSkillLevel(SkillId)`, `GetEffectiveUniquePassiveLevel(SkillId)`,
  `GetMentorUniquePassiveLevel(SkillId)` — same return types as before (`int`), new parameter type.

- [ ] **Step 1: Write the failing test**

`Assets/Tests/EditMode/RunCharacterProgressTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;

public class RunCharacterProgressTests
{
    CharacterData character;
    PassiveSkillData sturdySkill;

    [SetUp]
    public void SetUp()
    {
        character = ScriptableObject.CreateInstance<CharacterData>();
        sturdySkill = ScriptableObject.CreateInstance<PassiveSkillData>();
        sturdySkill.skillName = SkillEffectMap.Sturdy;
        sturdySkill.skillId = SkillId.Sturdy;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(character);
        Object.DestroyImmediate(sturdySkill);
    }

    [Test]
    public void GetSkillLevel_KnownSkillId_ReturnsStoredLevel()
    {
        var progress = new RunCharacterProgress(character);
        progress.KnownSkillLevels[sturdySkill] = 3;

        Assert.AreEqual(3, progress.GetSkillLevel(SkillId.Sturdy));
    }

    [Test]
    public void GetSkillLevel_UnknownSkillId_ReturnsZero()
    {
        var progress = new RunCharacterProgress(character);
        Assert.AreEqual(0, progress.GetSkillLevel(SkillId.Berserk));
    }

    [Test]
    public void GetEffectiveUniquePassiveLevel_MentorSkillMatchesByLegacyName_ReturnsMentorLevel()
    {
        var progress = new RunCharacterProgress(character)
        {
            MentorUniquePassiveSkillName = SkillEffectMap.Shadow,
            MentorUniquePassiveLevel = 2
        };

        Assert.AreEqual(2, progress.GetEffectiveUniquePassiveLevel(SkillId.Shadow));
    }
}
```

- [ ] **Step 2: Run it to verify it fails to compile**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testFilter "RunCharacterProgressTests" -testResults "C:/Unity Projects/DungeonGirls/test_results_task4_pre.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_task4_pre.log"
```
Expected: FAIL — compile error, `GetSkillLevel(SkillId)` doesn't exist yet (current signature takes
`string`).

- [ ] **Step 3: Change the three method signatures in `RunCharacterProgress.cs`**

Replace (lines 68-96 in the current file):
```csharp
    public int GetSkillLevel(string skillName)
    {
        foreach (var pair in KnownSkillLevels)
        {
            if (pair.Key.skillName == skillName)
            {
                return pair.Value;
            }
        }

        return 0;
    }

    public int GetEffectiveUniquePassiveLevel(string skillName)
    {
        int ownLevel = Character != null && Character.uniquePassiveSkill != null &&
            string.Equals(Character.uniquePassiveSkill.skillName, skillName, System.StringComparison.OrdinalIgnoreCase)
            ? UniquePassiveLevel
            : 0;
        int mentorLevel = string.Equals(MentorUniquePassiveSkillName, skillName, System.StringComparison.OrdinalIgnoreCase)
            ? MentorUniquePassiveLevel
            : 0;
        return Mathf.Max(ownLevel, mentorLevel);
    }

    public int GetMentorUniquePassiveLevel(string skillName) =>
        string.Equals(MentorUniquePassiveSkillName, skillName, System.StringComparison.OrdinalIgnoreCase)
            ? MentorUniquePassiveLevel
            : 0;
```

With:
```csharp
    public int GetSkillLevel(SkillId skillId)
    {
        foreach (var pair in KnownSkillLevels)
        {
            if (pair.Key.skillId == skillId)
            {
                return pair.Value;
            }
        }

        return 0;
    }

    // MentorUniquePassiveSkillName остаётся строкой (персистентный формат SaveData/VeteranCharacter,
    // не меняем) — сравнивается через SkillEffectMap.ResolveId, а не напрямую по строке.
    public int GetEffectiveUniquePassiveLevel(SkillId skillId)
    {
        int ownLevel = Character != null && Character.uniquePassiveSkill != null &&
            Character.uniquePassiveSkill.skillId == skillId
            ? UniquePassiveLevel
            : 0;
        int mentorLevel = SkillEffectMap.ResolveId(MentorUniquePassiveSkillName) == skillId
            ? MentorUniquePassiveLevel
            : 0;
        return Mathf.Max(ownLevel, mentorLevel);
    }

    public int GetMentorUniquePassiveLevel(SkillId skillId) =>
        SkillEffectMap.ResolveId(MentorUniquePassiveSkillName) == skillId
            ? MentorUniquePassiveLevel
            : 0;
```

- [ ] **Step 4: Update all 21 call sites in `CombatantFactory.cs`**

Every occurrence of `SkillEffectMap.<Name>` passed into `GetSkillLevel(...)` or
`GetMentorUniquePassiveLevel(...)` becomes `SkillId.<Name>` (same `<Name>`, no other change). Concretely,
replace each of these exact substrings (all appear as call-site arguments, `replace_all` per line is
safe since each constant name is unique in this file):

| Line | Old | New |
|---|---|---|
| 29 | `GetSkillLevel(SkillEffectMap.Ambidexterity)` | `GetSkillLevel(SkillId.Ambidexterity)` |
| 64, 207 | `GetSkillLevel(SkillEffectMap.Sturdy)` | `GetSkillLevel(SkillId.Sturdy)` |
| 209 | `GetSkillLevel(SkillEffectMap.IAmTheWall)` | `GetSkillLevel(SkillId.IAmTheWall)` |
| 226 | `GetSkillLevel(SkillEffectMap.Freeze)` | `GetSkillLevel(SkillId.Freeze)` |
| 227 | `GetSkillLevel(SkillEffectMap.Luck)` | `GetSkillLevel(SkillId.Luck)` |
| 228 | `GetSkillLevel(SkillEffectMap.Evasion)` | `GetSkillLevel(SkillId.Evasion)` |
| 230 | `GetSkillLevel(SkillEffectMap.CriticalHits)` | `GetSkillLevel(SkillId.CriticalHits)` |
| 232 | `GetSkillLevel(SkillEffectMap.Ambidexterity)` | `GetSkillLevel(SkillId.Ambidexterity)` |
| 233 | `GetSkillLevel(SkillEffectMap.Thorns)` | `GetSkillLevel(SkillId.Thorns)` |
| 234 | `GetSkillLevel(SkillEffectMap.Unyielding)` | `GetSkillLevel(SkillId.Unyielding)` |
| 235 | `GetSkillLevel(SkillEffectMap.Bleed)` | `GetSkillLevel(SkillId.Bleed)` |
| 248 | `GetSkillLevel(SkillEffectMap.EyeForAnEye)` | `GetSkillLevel(SkillId.EyeForAnEye)` |
| 249 | `GetSkillLevel(SkillEffectMap.PoisonedBlade)` | `GetSkillLevel(SkillId.PoisonedBlade)` |
| 250 | `GetSkillLevel(SkillEffectMap.ByAThread)` | `GetSkillLevel(SkillId.ByAThread)` |
| 251 | `GetSkillLevel(SkillEffectMap.SlipAway)` | `GetSkillLevel(SkillId.SlipAway)` |
| 255 | `GetMentorUniquePassiveLevel(SkillEffectMap.Shadow)` | `GetMentorUniquePassiveLevel(SkillId.Shadow)` |
| 257 | `GetSkillLevel(SkillEffectMap.Elimination)` | `GetSkillLevel(SkillId.Elimination)` |
| 265 | `GetSkillLevel(SkillEffectMap.Stubbornness)` | `GetSkillLevel(SkillId.Stubbornness)` |
| 266 | `GetSkillLevel(SkillEffectMap.Frenzy)` | `GetSkillLevel(SkillId.Frenzy)` |
| 267 | `GetSkillLevel(SkillEffectMap.CombatRegen)` | `GetSkillLevel(SkillId.CombatRegen)` |
| 268 | `GetSkillLevel(SkillEffectMap.Intimidation)` | `GetSkillLevel(SkillId.Intimidation)` |
| 269 | `GetSkillLevel(SkillEffectMap.Superstition)` | `GetSkillLevel(SkillId.Superstition)` |
| 282 | `GetMentorUniquePassiveLevel(SkillEffectMap.ChampionOfTheTribe)` | `GetMentorUniquePassiveLevel(SkillId.ChampionOfTheTribe)` |

Use `Grep` for `SkillEffectMap\.` in this file after editing — every remaining hit must be inside an
item-passive `==` comparison handled by Task 5/6, not a `GetSkillLevel`/`GetMentorUniquePassiveLevel`
call.

- [ ] **Step 5: Update `RunFlowController.cs:1501` and `:2000`**

Both lines are identically:
```csharp
        int luckLevel = characterManager.Progress.GetSkillLevel(SkillEffectMap.Luck);
```
Replace with:
```csharp
        int luckLevel = characterManager.Progress.GetSkillLevel(SkillId.Luck);
```
(`replace_all: true` is safe — both occurrences get the same fix.)

- [ ] **Step 6: Update `CampManager.cs:59`**

```csharp
        int fieldRepairLevel = characterManager.Progress.GetEffectiveUniquePassiveLevel(SkillEffectMap.FieldRepair);
```
becomes:
```csharp
        int fieldRepairLevel = characterManager.Progress.GetEffectiveUniquePassiveLevel(SkillId.FieldRepair);
```

- [ ] **Step 7: Run the test to verify it passes**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testFilter "RunCharacterProgressTests" -testResults "C:/Unity Projects/DungeonGirls/test_results_task4.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_task4.log"
```
Expected: 3 passed, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add "Assets/Scripts/Progression/RunCharacterProgress.cs" "Assets/Scripts/Combat/CombatantFactory.cs" "Assets/Scripts/UI/RunFlowController.cs" "Assets/Scripts/Managers/CampManager.cs" "Assets/Tests/EditMode/RunCharacterProgressTests.cs" "Assets/Tests/EditMode/RunCharacterProgressTests.cs.meta"
git commit -m "refactor: migrate character skill lookups from skillName strings to SkillId"
```

---

## Task 5: Migrate monster passive comparisons to `SkillId`

**Files:**
- Modify: `Assets/Scripts/Combat/CombatantRuntime.cs` (field declaration, line 102)
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs` (lines 154-172)
- Modify: `Assets/Scripts/Managers/CombatManager.cs` (lines 1022-1120 region)

**Interfaces:**
- Consumes: `MonsterSkillEffectMap.ResolveId`, `PassiveSkillData.skillId` (Task 2).
- Produces: `CombatantRuntime.MonsterPassiveSkillId` (`SkillId`, replaces `MonsterPassiveName : string`).

- [ ] **Step 1: Rename and retype the field in `CombatantRuntime.cs`**

Replace:
```csharp
    public string MonsterPassiveName;
```
With:
```csharp
    public SkillId MonsterPassiveSkillId;
```

- [ ] **Step 2: Update the setter and comparisons in `CombatantFactory.cs`**

Replace (around line 154-172):
```csharp
        runtime.MonsterPassiveName = monster.passiveSkill != null ? monster.passiveSkill.skillName : null;

        if (runtime.MonsterPassiveName == MonsterSkillEffectMap.ArmorPiercingBlade)
        {
            // Та же механика, что у клинков Вайолет, но фиксированное значение монстра.
```
With:
```csharp
        runtime.MonsterPassiveSkillId = monster.passiveSkill != null ? monster.passiveSkill.skillId : SkillId.None;

        if (runtime.MonsterPassiveSkillId == SkillId.MonsterArmorPiercingBlade)
        {
            // Та же механика, что у клинков Вайолет, но фиксированное значение монстра.
```
And further down in the same block:
```csharp
        if (runtime.MonsterPassiveName == MonsterSkillEffectMap.Fluttering)
        {
            runtime.MonsterEvasionPercent = 20f;
        }
        else if (runtime.MonsterPassiveName == MonsterSkillEffectMap.DarkHeal)
        {
            runtime.MonsterPassiveCooldownTimer = 8f;
        }
        else if (runtime.MonsterPassiveName == MonsterSkillEffectMap.DoubleStrike)
        {
            runtime.MonsterPassiveCooldownTimer = 6f;
        }
```
becomes:
```csharp
        if (runtime.MonsterPassiveSkillId == SkillId.MonsterFluttering)
        {
            runtime.MonsterEvasionPercent = 20f;
        }
        else if (runtime.MonsterPassiveSkillId == SkillId.MonsterDarkHeal)
        {
            runtime.MonsterPassiveCooldownTimer = 8f;
        }
        else if (runtime.MonsterPassiveSkillId == SkillId.MonsterDoubleStrike)
        {
            runtime.MonsterPassiveCooldownTimer = 6f;
        }
```

- [ ] **Step 3: Update `CombatManager.cs`**

Line 1022:
```csharp
        if (attacker.IsPlayer || attacker.MonsterPassiveName == null)
```
becomes:
```csharp
        if (attacker.IsPlayer || attacker.MonsterPassiveSkillId == SkillId.None)
```

Line 1027-1029 (`switch` statement — only the switched expression and the three case labels this
plan touches change; leave any other `case` labels not listed here untouched):
```csharp
        switch (attacker.MonsterPassiveName)
        {
            case MonsterSkillEffectMap.Corrosion:
```
becomes:
```csharp
        switch (attacker.MonsterPassiveSkillId)
        {
            case SkillId.MonsterCorrosion:
```
Line 1057:
```csharp
            case MonsterSkillEffectMap.StunningScream:
```
becomes:
```csharp
            case SkillId.MonsterStunningScream:
```
Line 1068:
```csharp
            case MonsterSkillEffectMap.SlowCurse:
```
becomes:
```csharp
            case SkillId.MonsterSlowCurse:
```
Line 1095:
```csharp
            if (!enemy.IsAlive || enemy.MonsterPassiveName == null)
```
becomes:
```csharp
            if (!enemy.IsAlive || enemy.MonsterPassiveSkillId == SkillId.None)
```
Line 1100:
```csharp
            if (enemy.MonsterPassiveName == MonsterSkillEffectMap.DarkHeal)
```
becomes:
```csharp
            if (enemy.MonsterPassiveSkillId == SkillId.MonsterDarkHeal)
```
Line 1115:
```csharp
            else if (enemy.MonsterPassiveName == MonsterSkillEffectMap.DoubleStrike)
```
becomes:
```csharp
            else if (enemy.MonsterPassiveSkillId == SkillId.MonsterDoubleStrike)
```

- [ ] **Step 4: Grep-verify no stray references remain**

```bash
cd "C:/Unity Projects/DungeonGirls" && grep -rn "MonsterPassiveName" Assets/Scripts
```
Expected: no output (all renamed to `MonsterPassiveSkillId`).

- [ ] **Step 5: Compile-check via batchmode (no dedicated unit test for this task — covered by
  Task 9's full PlayModeSmokeTest rerun, which already exercises monster combat)**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "C:/Unity Projects/DungeonGirls" -logFile "C:/Unity Projects/DungeonGirls/unity_task5.log"
grep "error CS" "C:/Unity Projects/DungeonGirls/unity_task5.log"
```
Expected: no `error CS` lines.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Combat/CombatantRuntime.cs" "Assets/Scripts/Combat/CombatantFactory.cs" "Assets/Scripts/Managers/CombatManager.cs"
git commit -m "refactor: migrate monster passive comparisons from skillName strings to SkillId"
```

---

## Task 6: Migrate active-skill (Berserk/SmokeBomb) comparisons to `SkillId`

**Files:**
- Modify: `Assets/Scripts/Managers/CombatManager.cs` (`ConfigureUniqueActiveSkill`, lines ~58-84, ~144, ~154)
- Modify: `Assets/Scripts/UI/RunFlowController.cs` (line 992)

**Interfaces:**
- Produces: `CombatManager.ConfigureUniqueActiveSkill(int, float, float, bool, string, SkillId)` — new
  6th parameter `SkillId skillId`, appended (not inserted) so this is the only call site to update.

- [ ] **Step 1: Add the field and update the signature in `CombatManager.cs`**

Replace:
```csharp
    int activeSkillHitCount;
    float activeSkillDamageMultiplierPerHit;
    float activeSkillCooldownSeconds;
    string activeSkillName;
    bool activeSkillAutoMode = true;
```
With:
```csharp
    int activeSkillHitCount;
    float activeSkillDamageMultiplierPerHit;
    float activeSkillCooldownSeconds;
    string activeSkillName;
    SkillId activeSkillId;
    bool activeSkillAutoMode = true;
```

Replace:
```csharp
    public void ConfigureUniqueActiveSkill(int hitCount, float damageMultiplierPerHit, float cooldownSeconds, bool autoMode, string skillName)
    {
        activeSkillHitCount = hitCount;
        activeSkillDamageMultiplierPerHit = damageMultiplierPerHit;
        activeSkillCooldownSeconds = cooldownSeconds;
        activeSkillName = skillName;
        activeSkillAutoMode = autoMode;
        IsActiveSkillConfigured = true;
    }
```
With:
```csharp
    public void ConfigureUniqueActiveSkill(int hitCount, float damageMultiplierPerHit, float cooldownSeconds, bool autoMode, string skillName, SkillId skillId)
    {
        activeSkillHitCount = hitCount;
        activeSkillDamageMultiplierPerHit = damageMultiplierPerHit;
        activeSkillCooldownSeconds = cooldownSeconds;
        activeSkillName = skillName;
        activeSkillId = skillId;
        activeSkillAutoMode = autoMode;
        IsActiveSkillConfigured = true;
    }
```

- [ ] **Step 2: Update the two comparisons**

Line 144:
```csharp
        if (activeSkillName == SkillEffectMap.Berserk)
```
becomes:
```csharp
        if (activeSkillId == SkillId.Berserk)
```
Line 154:
```csharp
        if (activeSkillName == SkillEffectMap.SmokeBomb)
```
becomes:
```csharp
        if (activeSkillId == SkillId.SmokeBomb)
```
(`activeSkillName` stays used at line 149, `ActiveSkillActivated?.Invoke(Player, activeSkillName)` —
unchanged, that's display text for the banner, not a comparison.)

- [ ] **Step 3: Update the call site in `RunFlowController.cs:992`**

```csharp
            combatManager.ConfigureUniqueActiveSkill(hitCount, activeMultiplier, activeCharacter.uniqueActiveSkill.cooldownSeconds, autoModeToggle.value, activeCharacter.uniqueActiveSkill.skillName);
```
becomes:
```csharp
            combatManager.ConfigureUniqueActiveSkill(hitCount, activeMultiplier, activeCharacter.uniqueActiveSkill.cooldownSeconds, autoModeToggle.value, activeCharacter.uniqueActiveSkill.skillName, activeCharacter.uniqueActiveSkill.skillId);
```

- [ ] **Step 4: Compile-check via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "C:/Unity Projects/DungeonGirls" -logFile "C:/Unity Projects/DungeonGirls/unity_task6.log"
grep "error CS" "C:/Unity Projects/DungeonGirls/unity_task6.log"
```
Expected: no `error CS` lines.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Managers/CombatManager.cs" "Assets/Scripts/UI/RunFlowController.cs"
git commit -m "refactor: migrate Berserk/SmokeBomb active-skill checks from skillName strings to SkillId"
```

---

## Task 7: EditMode tests for pure logic

**Files:**
- Test: `Assets/Tests/EditMode/DamageCalculatorTests.cs`
- Test: `Assets/Tests/EditMode/SuccessChanceCalculatorTests.cs`
- Test: `Assets/Tests/EditMode/BalanceClampsTests.cs`
- Test: `Assets/Tests/EditMode/GachaCopyBonusCalculatorTests.cs`
- Test: `Assets/Tests/EditMode/MonsterEncounterBudgetTests.cs`
- Test: `Assets/Tests/EditMode/VeteranSystemTests.cs`
- Test: `Assets/Tests/EditMode/StatScalingTests.cs`
- Test: `Assets/Tests/EditMode/SaveManagerMigrationTests.cs`

No production code changes in this task — these classes are already pure/static.

- [ ] **Step 1: `DamageCalculatorTests.cs`**

```csharp
using NUnit.Framework;

public class DamageCalculatorTests
{
    [Test]
    public void ApplyPhysicalDamage_DamageBelowDefense_IsFullyBlockedAndWearsArmor()
    {
        var target = new CombatantRuntime { PhysicalDefenseCurrent = 50f, CurrentHP = 100f };

        var result = DamageCalculator.ApplyPhysicalDamage(target, 10f);

        Assert.IsTrue(result.WasBlocked);
        Assert.AreEqual(0f, result.DamageToHP);
        Assert.AreEqual(49f, target.PhysicalDefenseCurrent); // max(1, floor(10/20)) = 1
        Assert.AreEqual(100f, target.CurrentHP);
    }

    [Test]
    public void ApplyPhysicalDamage_DamageAboveDefense_DealsRemainderToHP()
    {
        var target = new CombatantRuntime { PhysicalDefenseCurrent = 10f, CurrentHP = 100f };

        var result = DamageCalculator.ApplyPhysicalDamage(target, 30f);

        Assert.IsFalse(result.WasBlocked);
        Assert.AreEqual(20f, result.DamageToHP);
        Assert.AreEqual(90f, target.CurrentHP);
    }

    [Test]
    public void ApplyMagicalDamage_DamageExceedsShield_DealsRemainderToHP()
    {
        var target = new CombatantRuntime { MagicShieldCurrent = 15f, CurrentHP = 50f };

        var result = DamageCalculator.ApplyMagicalDamage(target, 20f);

        Assert.IsFalse(result.WasBlocked);
        Assert.AreEqual(5f, result.DamageToHP);
        Assert.AreEqual(0f, target.MagicShieldCurrent);
        Assert.AreEqual(45f, target.CurrentHP);
    }

    [Test]
    public void ApplyDamage_WithResistance_ReducesDamageBeforeDefense()
    {
        var target = new CombatantRuntime { PhysicalDefenseCurrent = 0f, CurrentHP = 100f, PhysicalResistancePercent = 50f };

        var result = DamageCalculator.ApplyDamage(target, 40f, DamageType.Physical);

        Assert.AreEqual(20f, result.DamageToHP); // 40 * (1 - 0.5) = 20
        Assert.AreEqual(80f, target.CurrentHP);
    }

    [Test]
    public void ComputeDamageRange_ReturnsFloorAndCeilOfPlusMinus20Percent()
    {
        DamageCalculator.ComputeDamageRange(10f, out float min, out float max);

        Assert.AreEqual(8f, min);
        Assert.AreEqual(12f, max);
    }
}
```

- [ ] **Step 2: `SuccessChanceCalculatorTests.cs`**

```csharp
using NUnit.Framework;

public class SuccessChanceCalculatorTests
{
    [Test]
    public void CalculateSuccessChancePercent_EqualLevels_Returns50()
    {
        Assert.AreEqual(50f, SuccessChanceCalculator.CalculateSuccessChancePercent(5, 5));
    }

    [Test]
    public void CalculateSuccessChancePercent_ClampsToMax95()
    {
        Assert.AreEqual(95f, SuccessChanceCalculator.CalculateSuccessChancePercent(20, 1));
    }

    [Test]
    public void CalculateSuccessChancePercent_ClampsToMin5()
    {
        Assert.AreEqual(5f, SuccessChanceCalculator.CalculateSuccessChancePercent(1, 20));
    }

    [Test]
    public void GetLuckBonusPercent_ScalesLinearlyByTen()
    {
        Assert.AreEqual(30f, SuccessChanceCalculator.GetLuckBonusPercent(3));
    }
}
```

- [ ] **Step 3: `BalanceClampsTests.cs`**

```csharp
using NUnit.Framework;

public class BalanceClampsTests
{
    [Test]
    public void ClampCritChancePercent_AboveMax_ClampsTo75()
    {
        Assert.AreEqual(75f, BalanceClamps.ClampCritChancePercent(120f));
    }

    [Test]
    public void ThornsReflectPercent_Level5_ClampsToMax50()
    {
        Assert.AreEqual(50f, BalanceClamps.ThornsReflectPercent(5));
    }

    [Test]
    public void ThornsReflectPercent_Level2_Returns20()
    {
        Assert.AreEqual(20f, BalanceClamps.ThornsReflectPercent(2));
    }

    [Test]
    public void CombatRegenHitsRequired_Level1_Returns6()
    {
        Assert.AreEqual(6, BalanceClamps.CombatRegenHitsRequired(1));
    }

    [Test]
    public void CombatRegenHitsRequired_Level5_Returns2()
    {
        Assert.AreEqual(2, BalanceClamps.CombatRegenHitsRequired(5));
    }
}
```

- [ ] **Step 4: `GachaCopyBonusCalculatorTests.cs`**

```csharp
using NUnit.Framework;

public class GachaCopyBonusCalculatorTests
{
    [Test]
    public void CalculateBonus_FirstCopyOnly_NoBonus()
    {
        var bonus = GachaCopyBonusCalculator.CalculateBonus(1);

        Assert.AreEqual(0, bonus.GearLevelBonus);
        Assert.AreEqual(0, bonus.PassiveLevelBonus);
        Assert.AreEqual(0, bonus.ActiveLevelBonus);
    }

    [Test]
    public void CalculateBonus_FiveCopies_FourExtraCyclesThroughGearPassiveGearActive()
    {
        // extraCopies = 4 -> steps 0,1,2,3 -> Gear, Passive, Gear, Active
        var bonus = GachaCopyBonusCalculator.CalculateBonus(5);

        Assert.AreEqual(2, bonus.GearLevelBonus);
        Assert.AreEqual(1, bonus.PassiveLevelBonus);
        Assert.AreEqual(1, bonus.ActiveLevelBonus);
    }

    [Test]
    public void CalculateBonus_ManyCopies_PassiveBonusClampsToFour()
    {
        var bonus = GachaCopyBonusCalculator.CalculateBonus(1 + 4 * 10); // 10 full cycles of extra copies

        Assert.AreEqual(4, bonus.PassiveLevelBonus);
        Assert.AreEqual(2, bonus.ActiveLevelBonus);
    }
}
```

- [ ] **Step 5: `MonsterEncounterBudgetTests.cs`**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MonsterEncounterBudgetTests
{
    [Test]
    public void GetThreatBudget_ClampsToFloorRange()
    {
        Assert.AreEqual(1, MonsterEncounterBudget.GetThreatBudget(0));
        Assert.AreEqual(DungeonManager.TotalFloors, MonsterEncounterBudget.GetThreatBudget(999));
    }

    [Test]
    public void GetThreatCost_HighTierMonster_ReturnsFive()
    {
        var monster = ScriptableObject.CreateInstance<MonsterData>();
        monster.minFloorTier = 10;

        Assert.AreEqual(5, MonsterEncounterBudget.GetThreatCost(monster));

        Object.DestroyImmediate(monster);
    }

    [Test]
    public void RollAffordableMonster_NoneAffordable_ReturnsNull()
    {
        var monster = ScriptableObject.CreateInstance<MonsterData>();
        monster.minFloorTier = 10; // cost 5

        var result = MonsterEncounterBudget.RollAffordableMonster(new List<MonsterData> { monster }, remainingBudget: 1);

        Assert.IsNull(result);

        Object.DestroyImmediate(monster);
    }

    [Test]
    public void RollAffordableMonster_OneAffordable_ReturnsIt()
    {
        var monster = ScriptableObject.CreateInstance<MonsterData>();
        monster.minFloorTier = 1; // cost 1

        var result = MonsterEncounterBudget.RollAffordableMonster(new List<MonsterData> { monster }, remainingBudget: 1);

        Assert.AreEqual(monster, result);

        Object.DestroyImmediate(monster);
    }
}
```

- [ ] **Step 6: `VeteranSystemTests.cs`**

```csharp
using System;
using NUnit.Framework;

public class VeteranSystemTests
{
    [Test]
    public void GradeForFloors_FullClear_ReturnsSPlus()
    {
        Assert.AreEqual("S+", VeteranSystem.GradeForFloors(DungeonManager.TotalFloors));
    }

    [Test]
    public void GradeForFloors_ZeroFloors_ReturnsCMinus()
    {
        Assert.AreEqual("C-", VeteranSystem.GradeForFloors(0));
    }

    [Test]
    public void IsEligibleMentor_SameCharacterId_ReturnsFalse()
    {
        var veteran = new VeteranCharacter { characterId = "jennifer", floorsCleared = 3, uniquePassiveSkillName = "Полевой ремонт" };

        Assert.IsFalse(VeteranSystem.IsEligibleMentor(veteran, "jennifer"));
    }

    [Test]
    public void IsEligibleMentor_DifferentCharacterIdAndCleared_ReturnsTrue()
    {
        var veteran = new VeteranCharacter { characterId = "jennifer", floorsCleared = 3, uniquePassiveSkillName = "Полевой ремонт" };

        Assert.IsTrue(VeteranSystem.IsEligibleMentor(veteran, "violet"));
    }

    [Test]
    public void RollTransferredSkills_AlwaysIncludesUniquePassiveFirst()
    {
        var veteran = new VeteranCharacter { characterId = "jennifer", floorsCleared = 1, uniquePassiveSkillName = "Полевой ремонт" };

        var result = VeteranSystem.RollTransferredSkills(veteran, new Random(42));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Полевой ремонт", result[0]);
    }
}
```

- [ ] **Step 7: `StatScalingTests.cs`**

```csharp
using NUnit.Framework;

public class StatScalingTests
{
    [Test]
    public void ItemEffectRank_Level1To3_ReturnsRank1()
    {
        Assert.AreEqual(1, StatScaling.ItemEffectRank(1));
        Assert.AreEqual(1, StatScaling.ItemEffectRank(3));
    }

    [Test]
    public void ItemEffectRank_HighLevel_ClampsToRank5()
    {
        Assert.AreEqual(5, StatScaling.ItemEffectRank(999));
    }

    [Test]
    public void ApplyLevelBonus_ZeroBaseStat_StaysZero()
    {
        Assert.AreEqual(0f, StatScaling.ApplyLevelBonus(0f, 10));
    }

    [Test]
    public void ApplyLevelBonus_Level1_ReturnsBaseStatUnchanged()
    {
        Assert.AreEqual(100f, StatScaling.ApplyLevelBonus(100f, 1));
    }

    [Test]
    public void ApplyLevelBonus_MinimumIncrementIsOne()
    {
        // baseStat=5 -> round(5*0.1)=0, but increment is clamped to max(1, ...) = 1 per level
        Assert.AreEqual(7f, StatScaling.ApplyLevelBonus(5f, 3));
    }

    [Test]
    public void ArmorBreakExtraWearChancePercent_ClampsTo100()
    {
        Assert.AreEqual(100f, ItemEffectBalance.ArmorBreakExtraWearChancePercent(5));
    }
}
```

- [ ] **Step 8: `SaveManagerMigrationTests.cs`**

```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class SaveManagerMigrationTests
{
    [Test]
    public void MigrateIfNeeded_NullCollections_BecomeEmptyLists()
    {
        var data = new SaveData
        {
            veteranDeck = null,
            gachaOwnedCharacters = null,
            characterRunCounts = null,
            seenVNScenes = null,
            relationshipPoints = null,
            seenTutorialHints = null
        };

        SaveManager.MigrateIfNeeded(data);

        Assert.IsNotNull(data.veteranDeck);
        Assert.IsNotNull(data.gachaOwnedCharacters);
        Assert.IsNotNull(data.characterRunCounts);
        Assert.IsNotNull(data.seenVNScenes);
        Assert.IsNotNull(data.relationshipPoints);
        Assert.IsNotNull(data.seenTutorialHints);
    }

    [Test]
    public void MigrateIfNeeded_LegacyRogueKey_MergedIntoVioletId()
    {
        var data = new SaveData
        {
            gachaOwnedCharacters = new List<KeyCountEntry>
            {
                new KeyCountEntry { key = "rogue", count = 2 },
                new KeyCountEntry { key = "violet", count = 1 }
            }
        };

        SaveManager.MigrateIfNeeded(data);

        var violetEntries = data.gachaOwnedCharacters.FindAll(e => e.key == "violet");
        Assert.AreEqual(1, violetEntries.Count);
        Assert.AreEqual(3, violetEntries[0].count);
    }

    [Test]
    public void MigrateIfNeeded_AlwaysGrantsAtLeastOneJenniferCopy()
    {
        var data = new SaveData { gachaOwnedCharacters = new List<KeyCountEntry>() };

        SaveManager.MigrateIfNeeded(data);

        var jennifer = data.gachaOwnedCharacters.Find(e => e.key == "jennifer");
        Assert.IsNotNull(jennifer);
        Assert.GreaterOrEqual(jennifer.count, 1);
    }

    [Test]
    public void MigrateIfNeeded_SetsCurrentSaveVersion()
    {
        var data = new SaveData();

        SaveManager.MigrateIfNeeded(data);

        Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
    }
}
```

- [ ] **Step 9: Run all new tests**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testResults "C:/Unity Projects/DungeonGirls/test_results_task7.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_task7.log"
```
Expected: all tests across all EditMode test files (Task 2, 4, 7) pass — check `test_results_task7.xml`
for `result="Passed"` on the root `<test-run>` element and 0 in the `failed` attribute.

- [ ] **Step 10: Commit**

```bash
git add "Assets/Tests/EditMode/DamageCalculatorTests.cs" "Assets/Tests/EditMode/DamageCalculatorTests.cs.meta" \
        "Assets/Tests/EditMode/SuccessChanceCalculatorTests.cs" "Assets/Tests/EditMode/SuccessChanceCalculatorTests.cs.meta" \
        "Assets/Tests/EditMode/BalanceClampsTests.cs" "Assets/Tests/EditMode/BalanceClampsTests.cs.meta" \
        "Assets/Tests/EditMode/GachaCopyBonusCalculatorTests.cs" "Assets/Tests/EditMode/GachaCopyBonusCalculatorTests.cs.meta" \
        "Assets/Tests/EditMode/MonsterEncounterBudgetTests.cs" "Assets/Tests/EditMode/MonsterEncounterBudgetTests.cs.meta" \
        "Assets/Tests/EditMode/VeteranSystemTests.cs" "Assets/Tests/EditMode/VeteranSystemTests.cs.meta" \
        "Assets/Tests/EditMode/StatScalingTests.cs" "Assets/Tests/EditMode/StatScalingTests.cs.meta" \
        "Assets/Tests/EditMode/SaveManagerMigrationTests.cs" "Assets/Tests/EditMode/SaveManagerMigrationTests.cs.meta"
git commit -m "test: add EditMode tests for pure balance/progression/save logic"
```

---

## Task 8: `DisplayFormat` — dedupe UI formatters

**Files:**
- Create: `Assets/Scripts/UI/DisplayFormat.cs`
- Modify: `Assets/Scripts/UI/RunFlowController.cs` (remove 3 private methods, update call sites)
- Modify: `Assets/Scripts/Managers/HubManager.cs` (update call sites, if any duplicate exists there —
  verify with Grep in Step 1 before assuming)

**Interfaces:**
- Produces: `DisplayFormat.CharacterClassDisplayName(CharacterClass) : string`,
  `DisplayFormat.SlotLabel(ItemData) : string`, `DisplayFormat.ItemStatsText(ItemData) : string`.

- [ ] **Step 1: Confirm current locations and read the three methods in full**

```bash
cd "C:/Unity Projects/DungeonGirls" && grep -n "static string CharacterClassDisplayName\|static string SlotLabel\|static string ItemStatsText" Assets/Scripts/UI/RunFlowController.cs
```
Read `Assets/Scripts/UI/RunFlowController.cs` at the three matched line numbers (each method body,
start to closing brace) before proceeding — copy their exact current bodies into `DisplayFormat.cs`
in the next step verbatim, don't paraphrase.

- [ ] **Step 2: Create `Assets/Scripts/UI/DisplayFormat.cs`**

Structure (paste the three method bodies read in Step 1 verbatim into the placeholders below —
do not alter their logic, only the enclosing class and access modifier from `static string` on
`RunFlowController` to `public static string` on the new standalone class):
```csharp
// Общие форматтеры отображения, использовавшиеся дублировано в RunFlowController/HubManager.
public static class DisplayFormat
{
    public static string CharacterClassDisplayName(CharacterClass characterClass) /* ...body from RunFlowController.cs, unchanged... */

    public static string SlotLabel(ItemData item) /* ...body from RunFlowController.cs, unchanged... */

    public static string ItemStatsText(ItemData item) /* ...body from RunFlowController.cs, unchanged... */
}
```

- [ ] **Step 3: Remove the three methods from `RunFlowController.cs` and redirect call sites**

Delete the three `static string CharacterClassDisplayName`/`SlotLabel`/`ItemStatsText` method
definitions from `RunFlowController.cs` entirely. Then find every call site:
```bash
cd "C:/Unity Projects/DungeonGirls" && grep -n "CharacterClassDisplayName(\|SlotLabel(\|ItemStatsText(" Assets/Scripts/UI/RunFlowController.cs
```
For each match, prefix the call with `DisplayFormat.` (e.g. `CharacterClassDisplayName(x)` becomes
`DisplayFormat.CharacterClassDisplayName(x)`).

- [ ] **Step 4: Check `HubManager.cs` for its own copies and redirect if present**

```bash
cd "C:/Unity Projects/DungeonGirls" && grep -n "CharacterClassDisplayName\|SlotLabel\|ItemStatsText" Assets/Scripts/Managers/HubManager.cs
```
If `HubManager.cs` has its own private duplicate implementations of any of the three, delete them and
redirect its call sites to `DisplayFormat.X` the same way as Step 3. If it has no duplicates (only
calls into logic that doesn't need these formatters), no change needed here — record which case it was
in the final report (Task 9 will not re-check this).

- [ ] **Step 5: Compile-check via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "C:/Unity Projects/DungeonGirls" -logFile "C:/Unity Projects/DungeonGirls/unity_task8.log"
grep "error CS" "C:/Unity Projects/DungeonGirls/unity_task8.log"
```
Expected: no `error CS` lines.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/UI/DisplayFormat.cs" "Assets/Scripts/UI/DisplayFormat.cs.meta" "Assets/Scripts/UI/RunFlowController.cs" "Assets/Scripts/Managers/HubManager.cs"
git commit -m "refactor: dedupe display formatters into shared DisplayFormat class"
```

---

## Task 9: Full verification pass

**Files:** none (verification only).

- [ ] **Step 1: Full EditMode test suite**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath "C:/Unity Projects/DungeonGirls" -testPlatform EditMode -testResults "C:/Unity Projects/DungeonGirls/test_results_final.xml" -logFile "C:/Unity Projects/DungeonGirls/unity_final_editmode.log"
```
Expected: 0 failed in `test_results_final.xml`.

- [ ] **Step 2: Existing gameplay smoke tests (unchanged from before this plan — this is the
  regression check that `SkillId` migration didn't break real combat/save/VN flow)**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -executeMethod PlayModeSmokeTest.Run -projectPath "C:/Unity Projects/DungeonGirls" -logFile "C:/Unity Projects/DungeonGirls/unity_final_playmode.log"
```
Expected: log ends with the smoke test's own summary line reporting 0 errors (grep the log for
`Errors.Add` triggers / the test's final `Debug.Log` summary — read the tail of the log to confirm
the exact expected string, since this is Codex's existing script, not one this plan wrote).

```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -nographics -executeMethod NarrativeSmokeTest.Run -projectPath "C:/Unity Projects/DungeonGirls" -logFile "C:/Unity Projects/DungeonGirls/unity_final_narrative.log"
```
(If `NarrativeSmokeTest` exposes a different entry method name, check
`Assets/Editor/NarrativeSmokeTest.cs` for its actual public static `Run`/`Main` method before running.)

- [ ] **Step 3: Clean up log/result artifacts (not part of the repo)**

```bash
cd "C:/Unity Projects/DungeonGirls" && rm -f unity_*.log test_results_*.xml
```

- [ ] **Step 4: No commit for this task** — verification only, nothing to commit.

---

## Task 10: Notion "Engineering Guidelines" page

**Files:** none (Notion page, not a repo file).

- [ ] **Step 1: Find the GDD page in Notion to place the new page as a sibling**

Use the `notion-search` tool for the GDD's page (already known from prior session context — see
`memory` for the page ID, or search for "DungeonGirls GDD"). Confirm its parent page/container before
creating a sibling.

- [ ] **Step 2: Create the page**

Use `notion-create-pages` with title "Engineering Guidelines" under the same parent as the GDD.
Content — five sections, each a short paragraph (2-4 sentences):

1. **Data-driven через ScriptableObject.** Контент (навыки, предметы, монстры, персонажи) — всегда
   `ScriptableObject`-ассеты, не хардкод-таблицы в коде. Уже соблюдается в проекте — фиксируем как
   правило для новых систем.
2. **Stable ID вместо строк/displayName для игровой логики.** Не сравнивать сущности по `skillName`,
   `characterName` или любому другому полю, предназначенному для отображения игроку — оно может
   измениться в любой момент. Два прецедента: `characterId` в `SaveManager` (миграция от
   displayName-ключей) и `SkillId` (2026-08-31, эта итерация) — конкретный живой пример дрейфа: ассет
   `Skill_Pickpocket.asset` фактически хранил `skillName: "Бронебойный клинок"` (не "Карманник") —
   имя файла и данные внутри давно разошлись, а строковое сравнение работало только потому, что
   сравнивался сам `skillName`, не имя файла.
3. **Разделение чистой логики и `MonoBehaviour`-оркестрации.** Расчётная/балансная логика (урон,
   шансы, кламп-формулы, прогрессия) живёт в `static`-классах без `MonoBehaviour` и, где возможно, без
   зависимости от состояния сцены — это единственное, что реально тестируется в этом проекте.
   `MonoBehaviour`-менеджеры — тонкая оркестрация поверх неё.
4. **Обязательный EditMode-тест для новой чистой логики.** Любая новая функция в `static`-классе
   (Combat/Progression/Data) при добавлении получает EditMode-тест в `Assets/Tests/EditMode/` —
   happy path минимум + 1 граничный случай. Не обязательно для `MonoBehaviour`-кода — для него пока
   остаются существующие smoke-тесты (`Assets/Editor/PlayModeSmokeTest.cs`).
5. **Транзакционность `SaveManager`.** Одна логическая мутация `SaveData` — один вызов `SaveGame()`.
   Не расщеплять сложную операцию (например, апгрейд здания) на несколько отдельных
   Try-методов-с-собственным-SaveGame — при сбое между записями это оставляет данные в
   рассогласованном состоянии (см. пример из истории проекта — `TryUpgradeBuilding`).

Add a closing line: "Источники: спека `Docs/superpowers/specs/2026-08-31-engineering-foundation-design.md`
и план `Docs/superpowers/plans/2026-08-31-engineering-foundation.md` в репозитории."

- [ ] **Step 3: Record the created page URL** for the final report (Task 9's report references it).
