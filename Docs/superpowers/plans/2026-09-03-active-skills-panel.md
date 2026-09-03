# Active Skills Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-slot, text-button active-skill system with an icon-based, bottom-center HUD panel that supports N skill slots of two types (Cooldown, Toggle), defaults auto-cast to OFF, and makes the cooldown skill ready the instant a room/combat starts.

**Architecture:** `ActiveSkillData` gains a `skillType` (Cooldown/Toggle) and `icon`. `CombatManager` replaces its single set of flat "unique active skill" fields with a `List<ActiveSkillRuntimeState>`, one entry per configured skill, and dispatches activation/auto-cast by `skillType` instead of hardcoding Berserk as a separate code path. `RunFlowController` builds one icon slot per configured skill at combat start (same pattern already used for `enemyListContainer`/`BuildEnemyStageEntries`), wires clicks + Q/W/E/R hotkeys, and updates cooldown/toggle visuals every frame.

**Tech Stack:** Unity (C#), UI Toolkit (UXML/USS), NUnit EditMode tests, PixelLab MCP for icon art.

**Spec:** [Docs/superpowers/specs/2026-09-03-active-skills-panel-design.md](../specs/2026-09-03-active-skills-panel-design.md)

**Project note:** all of `Assets/Scripts` (`Managers/`, `UI/`, `Combat/`, `Data/`, ...) lives in one
assembly (`Assets/Scripts/DungeonGirls.Runtime.asmdef`). A compile error anywhere in it blocks the
Unity Test Runner entirely (not just the affected test), so **every task below must leave the whole
project compiling**, even when a task's "real" deliverable is deep inside `CombatManager` and the
UI hasn't caught up yet — Task 2 includes a minimal, throwaway-quality patch of the UI call sites so
the project keeps compiling; Task 3 replaces that throwaway wiring with the real UI.

## Global Constraints

- Content does not change: every class still configures exactly one skill slot today; only the code becomes list-based.
- Auto-cast defaults to OFF for every newly configured Cooldown skill (previously defaulted ON).
- A Cooldown skill's timer starts at 0 (ready) when configured — never starts in a forced full cooldown.
- Toggle skills (Berserk) go through the same `ActiveSkills` list/dispatch as Cooldown skills; no per-class special-casing in `CombatManager`.
- Manual activation: click on the skill icon, or the hotkey shown on it (Q for slot 0, then W, E, R for slots 1-3).
- No Unity new Input System — this project only uses legacy `UnityEngine.Input`/`KeyCode` (confirmed: `Assets/Scripts/DungeonGirls.Runtime.asmdef` references `Unity.InputSystem` for other reasons, but no combat/skill code anywhere uses it — `RunFlowController` has zero `KeyCode`/new-Input-System usage today).

---

## Task 1: `ActiveSkillData` gains a skill type and an icon

**Files:**
- Modify: `Assets/Scripts/Data/ActiveSkillData.cs`
- Modify: `Assets/ScriptableObjects/Skills/Unique/Skill_Berserk.asset`
- Test: `Assets/Tests/EditMode/ActiveSkillDataTests.cs` (create)

**Interfaces:**
- Produces: `enum ActiveSkillType { Cooldown, Toggle }` (Cooldown = 0, Toggle = 1), `ActiveSkillData.skillType` (defaults to `Cooldown` when absent from a `.asset` file — Unity zero-fills missing serialized ints), `ActiveSkillData.icon` (`Sprite`, nullable).

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/ActiveSkillDataTests.cs`:

```csharp
using NUnit.Framework;
using UnityEditor;

public class ActiveSkillDataTests
{
    [Test]
    public void ThreeQuickStrikes_DefaultsToCooldownType()
    {
        var data = AssetDatabase.LoadAssetAtPath<ActiveSkillData>(
            "Assets/ScriptableObjects/Skills/Unique/Skill_ThreeQuickStrikes.asset");
        Assert.AreEqual(ActiveSkillType.Cooldown, data.skillType);
    }

    [Test]
    public void SmokeBomb_DefaultsToCooldownType()
    {
        var data = AssetDatabase.LoadAssetAtPath<ActiveSkillData>(
            "Assets/ScriptableObjects/Skills/Unique/Skill_SmokeBomb.asset");
        Assert.AreEqual(ActiveSkillType.Cooldown, data.skillType);
    }

    [Test]
    public void Berserk_IsToggleType()
    {
        var data = AssetDatabase.LoadAssetAtPath<ActiveSkillData>(
            "Assets/ScriptableObjects/Skills/Unique/Skill_Berserk.asset");
        Assert.AreEqual(ActiveSkillType.Toggle, data.skillType);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run via Unity Test Runner (EditMode) or `Unity.exe -runTests -testPlatform EditMode -testFilter ActiveSkillDataTests`.
Expected: compile error (`ActiveSkillType` and `skillType`/`icon` don't exist yet) or, once compiling, `Berserk_IsToggleType` FAILs because `skillType` isn't set on the asset yet.

- [ ] **Step 3: Add the enum and fields to `ActiveSkillData`**

Replace the full contents of `Assets/Scripts/Data/ActiveSkillData.cs`:

```csharp
using UnityEngine;

public enum ActiveSkillType
{
    Cooldown,
    Toggle
}

[CreateAssetMenu(fileName = "NewActiveSkill", menuName = "DungeonGirls/Active Skill")]
public class ActiveSkillData : ScriptableObject
{
    public string skillName;
    public SkillId skillId;

    [TextArea(3, 10)]
    public string effectDescription;

    public int maxLevel;
    public float cooldownSeconds; // Toggle-скиллы (см. skillType) это поле игнорируют.
    public ActiveSkillTargetType targetType;

    // Активные-скилы-панель (2026-09-03): Cooldown — уходит в кулдаун и авто-кастуется, если
    // включён авто-режим; Toggle — ручной вкл/выкл без кулдауна (например "Берсерк").
    public ActiveSkillType skillType;
    public Sprite icon;
}
```

- [ ] **Step 4: Set `skillType: 1` (Toggle) on the Berserk asset**

Open `Assets/ScriptableObjects/Skills/Unique/Skill_Berserk.asset` and add a `skillType: 1` line after
`cooldownSeconds: 0` (or wherever the existing scalar fields end, before `targetType`). Leave
`Skill_ThreeQuickStrikes.asset` and `Skill_SmokeBomb.asset` untouched — Unity treats a missing
`skillType` line as `0` (`Cooldown`), which is already correct for both.

- [ ] **Step 5: Run test to verify it passes**

Run: EditMode test filter `ActiveSkillDataTests`
Expected: PASS (all 3 tests)

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Data/ActiveSkillData.cs Assets/ScriptableObjects/Skills/Unique/Skill_Berserk.asset Assets/Tests/EditMode/ActiveSkillDataTests.cs
git commit -m "feat: add ActiveSkillType (Cooldown/Toggle) and icon field to ActiveSkillData"
```

---

## Task 2: `CombatManager` list-based skills — configuration, activation dispatch, auto-cast

**Files:**
- Create: `Assets/Scripts/Combat/ActiveSkillRuntimeState.cs`
- Modify: `Assets/Scripts/Managers/CombatManager.cs:57-235` (flat config fields/methods, `TryActivateUniqueActiveSkill`, `SetBerserkActive`, `StartCombat`, `Tick`)
- Modify: `Assets/Scripts/Combat/CombatantRuntime.cs:59` (remove now-unused field)
- Modify: `Assets/Scripts/UI/RunFlowController.cs:294-296` (OnEnable wiring — throwaway patch, replaced in Task 3)
- Modify: `Assets/Scripts/UI/RunFlowController.Combat.cs:325-344` (config call site), `:602-604` (readiness/cooldown reads — throwaway patch, replaced in Task 3)
- Test: `Assets/Tests/EditMode/CombatManagerTests.cs` (extend)

**Interfaces:**
- Consumes: `ActiveSkillData` from Task 1 (`skillType`, `skillId`, `cooldownSeconds`).
- Produces:
  - `public class ActiveSkillRuntimeState { public ActiveSkillData Data; public int HitCount; public float DamageMultiplierPerHit; public float CooldownTimer; public bool IsToggleActive; public bool AutoMode; }`
  - `public readonly struct ActiveSkillConfigEntry { public ActiveSkillData Data; public int HitCount; public float DamageMultiplierPerHit; public bool AutoMode; public ActiveSkillConfigEntry(ActiveSkillData data, int hitCount, float damageMultiplierPerHit, bool autoMode); }`
  - `CombatManager.ActiveSkills` — `public List<ActiveSkillRuntimeState> ActiveSkills { get; }`
  - `CombatManager.ConfigureActiveSkills(IEnumerable<ActiveSkillConfigEntry> skills)`
  - `CombatManager.IsSkillReady(int slotIndex)` — `bool`
  - `CombatManager.SkillCooldownRemaining(int slotIndex)` — `float`
  - `CombatManager.TryActivateSkill(int slotIndex)` — `bool`
  - `CombatManager.SetSkillAutoMode(int slotIndex, bool autoMode)` — `void`
  - Removed entirely (no longer exist after this task): `ConfigureUniqueActiveSkill`, `ClearUniqueActiveSkillConfiguration`, `TryActivateUniqueActiveSkill`, `SetBerserkActive`, `SetActiveSkillAutoMode`, `IsActiveSkillConfigured`, `IsActiveSkillReady`, `ActiveSkillCooldownRemaining`, `CombatantRuntime.ActiveSkillCooldownTimer`.

- [ ] **Step 1: Write the failing test**

Add to `Assets/Tests/EditMode/CombatManagerTests.cs` (replace the whole file — it currently only has
the 2 `ResolveActiveSkillHitCount` tests):

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class CombatManagerTests
{
    static GameObject NewGo(string name) => new GameObject(name);

    static ActiveSkillData NewSkill(ActiveSkillType type, float cooldownSeconds, SkillId id = SkillId.None)
    {
        var data = ScriptableObject.CreateInstance<ActiveSkillData>();
        data.skillName = "Test Skill";
        data.skillId = id;
        data.cooldownSeconds = cooldownSeconds;
        data.skillType = type;
        return data;
    }

    // Тот же паттерн очистки, что и в BossEncounterTests.cs — CombatManager создаёт реальный
    // GameObject через AddComponent, EditMode-тесты не выгружают сцену между тестами сами.
    [TearDown]
    public void TearDown()
    {
        foreach (var go in Object.FindObjectsByType<CombatManager>(FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(go.gameObject);
        }
    }

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

    [Test]
    public void ConfigureActiveSkills_CooldownSkill_StartsReadyNotOnCooldown()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var cooldownSkill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);

        cm.ConfigureActiveSkills(new[]
        {
            new ActiveSkillConfigEntry(cooldownSkill, hitCount: 3, damageMultiplierPerHit: 1.1f, autoMode: false)
        });

        Assert.AreEqual(1, cm.ActiveSkills.Count);
        Assert.AreEqual(0f, cm.ActiveSkills[0].CooldownTimer);
        Assert.AreEqual(0f, cm.SkillCooldownRemaining(0));
    }

    [Test]
    public void ConfigureActiveSkills_ToggleSkill_StartsInactive()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var toggleSkill = NewSkill(ActiveSkillType.Toggle, cooldownSeconds: 0f, SkillId.Berserk);

        cm.ConfigureActiveSkills(new[]
        {
            new ActiveSkillConfigEntry(toggleSkill, hitCount: 0, damageMultiplierPerHit: 0f, autoMode: false)
        });

        Assert.IsFalse(cm.ActiveSkills[0].IsToggleActive);
    }

    [Test]
    public void ConfigureActiveSkills_ReplacesPreviousConfiguration()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var first = NewSkill(ActiveSkillType.Cooldown, 4f);
        var second = NewSkill(ActiveSkillType.Toggle, 0f, SkillId.Berserk);

        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(first, 3, 1f, false) });
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(second, 0, 0f, false) });

        Assert.AreEqual(1, cm.ActiveSkills.Count);
        Assert.AreEqual(second, cm.ActiveSkills[0].Data);
    }

    [Test]
    public void StartCombat_DoesNotForceActiveSkillIntoCooldown()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var cooldownSkill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(cooldownSkill, 3, 1f, false) });

        var player = new CombatantRuntime { DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f };
        cm.StartCombat(player, new List<CombatantRuntime>());

        Assert.IsTrue(cm.IsSkillReady(0));
        Assert.AreEqual(0f, cm.SkillCooldownRemaining(0));
    }

    [Test]
    public void TryActivateSkill_CooldownSkill_HitsAndStartsCooldown()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 3, damageMultiplierPerHit: 1f, autoMode: false) });

        var player = new CombatantRuntime
        {
            DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f,
            Weapons = new List<WeaponAttackState> { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 1f, DamageType = DamageType.Physical } }
        };
        var enemy = new CombatantRuntime { DisplayName = "Враг", MaxHP = 1000f, CurrentHP = 1000f };
        cm.StartCombat(player, new List<CombatantRuntime> { enemy });

        bool activated = cm.TryActivateSkill(0);

        Assert.IsTrue(activated);
        Assert.IsFalse(cm.IsSkillReady(0));
        Assert.AreEqual(4f, cm.SkillCooldownRemaining(0));
        Assert.Less(enemy.CurrentHP, 1000f); // hit-loop реально бьёт
    }

    [Test]
    public void TryActivateSkill_ToggleSkill_FlipsIsToggleActiveAndPlayerFlag()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Toggle, cooldownSeconds: 0f, SkillId.Berserk);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 0, damageMultiplierPerHit: 0f, autoMode: false) });

        var player = new CombatantRuntime { DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f, UniqueBerserkLevel = 1 };
        cm.StartCombat(player, new List<CombatantRuntime>());

        Assert.IsTrue(cm.TryActivateSkill(0));
        Assert.IsTrue(cm.ActiveSkills[0].IsToggleActive);
        Assert.IsTrue(player.IsBerserkActive);

        Assert.IsTrue(cm.TryActivateSkill(0));
        Assert.IsFalse(cm.ActiveSkills[0].IsToggleActive);
        Assert.IsFalse(player.IsBerserkActive);
    }

    [Test]
    public void TryActivateSkill_ToggleSkill_CannotEnableWithoutLevel()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Toggle, cooldownSeconds: 0f, SkillId.Berserk);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 0, damageMultiplierPerHit: 0f, autoMode: false) });

        var player = new CombatantRuntime { DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f, UniqueBerserkLevel = 0 };
        cm.StartCombat(player, new List<CombatantRuntime>());

        Assert.IsFalse(cm.TryActivateSkill(0));
        Assert.IsFalse(cm.ActiveSkills[0].IsToggleActive);
    }

    [Test]
    public void Tick_AutoModeOff_DoesNotAutoActivateCooldownSkill()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 3, damageMultiplierPerHit: 1f, autoMode: false) });

        var player = new CombatantRuntime
        {
            DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f,
            Weapons = new List<WeaponAttackState> { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f, DamageType = DamageType.Physical } }
        };
        var enemy = new CombatantRuntime { DisplayName = "Враг", MaxHP = 1000f, CurrentHP = 1000f };
        cm.StartCombat(player, new List<CombatantRuntime> { enemy });

        cm.Tick(1f);

        Assert.IsTrue(cm.IsSkillReady(0)); // ready все ещё, авто-режим выключен — никто не потратил его
    }

    [Test]
    public void Tick_AutoModeOn_AutoActivatesReadyCooldownSkill()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Cooldown, cooldownSeconds: 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, hitCount: 3, damageMultiplierPerHit: 1f, autoMode: true) });

        var player = new CombatantRuntime
        {
            DisplayName = "Игрок", IsPlayer = true, MaxHP = 20f, CurrentHP = 20f,
            Weapons = new List<WeaponAttackState> { new WeaponAttackState { DamageMin = 1f, DamageMax = 1f, AttackSpeed = 0.01f, DamageType = DamageType.Physical } }
        };
        var enemy = new CombatantRuntime { DisplayName = "Враг", MaxHP = 1000f, CurrentHP = 1000f };
        cm.StartCombat(player, new List<CombatantRuntime> { enemy });

        cm.Tick(0.01f);

        Assert.IsFalse(cm.IsSkillReady(0)); // авто-режим включён явно — теперь он потратил его
    }

    [Test]
    public void SetSkillAutoMode_UpdatesSlot()
    {
        var cm = NewGo("combat").AddComponent<CombatManager>();
        var skill = NewSkill(ActiveSkillType.Cooldown, 4f);
        cm.ConfigureActiveSkills(new[] { new ActiveSkillConfigEntry(skill, 3, 1f, autoMode: false) });

        cm.SetSkillAutoMode(0, true);

        Assert.IsTrue(cm.ActiveSkills[0].AutoMode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: EditMode test filter `CombatManagerTests`
Expected: compile error — none of `ActiveSkillConfigEntry`/`ConfigureActiveSkills`/`ActiveSkills`/
`IsSkillReady`/`SkillCooldownRemaining`/`TryActivateSkill`/`SetSkillAutoMode` exist yet.

- [ ] **Step 3: Create `ActiveSkillRuntimeState.cs`**

Create `Assets/Scripts/Combat/ActiveSkillRuntimeState.cs`:

```csharp
// Активные-скилы-панель (2026-09-03): рантайм-состояние ОДНОГО сконфигурированного слота на
// панели скиллов. CombatManager.ActiveSkills — список таких состояний (сегодня всегда из 1
// элемента на класс, инфраструктура готова к N). Cooldown-поля (CooldownTimer/AutoMode) и
// Toggle-поле (IsToggleActive) сосуществуют в одном классе — какие из них значимы, определяет
// Data.skillType (см. ActiveSkillData/ActiveSkillType).
public class ActiveSkillRuntimeState
{
    public ActiveSkillData Data;
    public int HitCount;
    public float DamageMultiplierPerHit;
    public float CooldownTimer;
    public bool IsToggleActive;
    public bool AutoMode;
}

// Вход для CombatManager.ConfigureActiveSkills — то, что вызывающая сторона (RunFlowController)
// знает о скилле ДО начала боя (уровень-зависимый множитель урона/hitCount уже посчитаны снаружи,
// CombatManager сам левел-апы не считает).
public readonly struct ActiveSkillConfigEntry
{
    public readonly ActiveSkillData Data;
    public readonly int HitCount;
    public readonly float DamageMultiplierPerHit;
    public readonly bool AutoMode;

    public ActiveSkillConfigEntry(ActiveSkillData data, int hitCount, float damageMultiplierPerHit, bool autoMode)
    {
        Data = data;
        HitCount = hitCount;
        DamageMultiplierPerHit = damageMultiplierPerHit;
        AutoMode = autoMode;
    }
}
```

- [ ] **Step 4: Replace the flat config fields/methods in `CombatManager.cs`**

Delete lines 57-130 entirely (from the `// 4.3: уникальный активный навык...` comment through the
end of `SetBerserkActive`) — this removes: the 6 flat `activeSkill*` fields, `IsActiveSkillConfigured`,
`ActiveSkillCooldownRemaining`, `IsActiveSkillReady`, `ConfigureUniqueActiveSkill`,
`SetActiveSkillAutoMode`, `ClearUniqueActiveSkillConfiguration`, `SetBerserkActive`.

Insert in their place:

```csharp
    // Активные-скилы-панель (2026-09-03): список сконфигурированных на текущий бой слотов —
    // сегодня всегда 1 элемент на класс (инфраструктура готова к N, контент не меняется).
    // Заменяет прежние плоские activeSkill*-поля/ConfigureUniqueActiveSkill.
    public List<ActiveSkillRuntimeState> ActiveSkills { get; } = new List<ActiveSkillRuntimeState>();

    public void ConfigureActiveSkills(IEnumerable<ActiveSkillConfigEntry> skills)
    {
        ActiveSkills.Clear();
        foreach (var entry in skills)
        {
            ActiveSkills.Add(new ActiveSkillRuntimeState
            {
                Data = entry.Data,
                HitCount = entry.HitCount,
                DamageMultiplierPerHit = entry.DamageMultiplierPerHit,
                // Активные-скилы-панель (2026-09-03): скилл готов СРАЗУ, не в полном кулдауне —
                // теперь активация ручная (клик/хоткей), а не автоматическая каждый кадр, так что
                // прежний риск "мгновенно снёс до того как игрок увидел" больше не применим.
                CooldownTimer = 0f,
                IsToggleActive = false,
                AutoMode = entry.AutoMode,
            });
        }
    }

    public bool IsSkillReady(int slotIndex) =>
        Player != null && Player.IsAlive && slotIndex >= 0 && slotIndex < ActiveSkills.Count &&
        ActiveSkills[slotIndex].Data.skillType == ActiveSkillType.Cooldown &&
        ActiveSkills[slotIndex].CooldownTimer <= 0f;

    public float SkillCooldownRemaining(int slotIndex) =>
        slotIndex >= 0 && slotIndex < ActiveSkills.Count ? Mathf.Max(0f, ActiveSkills[slotIndex].CooldownTimer) : 0f;

    public bool TryActivateSkill(int slotIndex)
    {
        if (!IsCombatActive || slotIndex < 0 || slotIndex >= ActiveSkills.Count)
        {
            return false;
        }

        var slot = ActiveSkills[slotIndex];
        return slot.Data.skillType == ActiveSkillType.Toggle ? TryToggleSkill(slot) : TryActivateCooldownSkill(slot);
    }

    public void SetSkillAutoMode(int slotIndex, bool autoMode)
    {
        if (slotIndex < 0 || slotIndex >= ActiveSkills.Count)
        {
            return;
        }

        ActiveSkills[slotIndex].AutoMode = autoMode;
    }

    // 3.11 (Варвар) "Берсерк" — ручной тумблер: нельзя ВКЛЮЧИТЬ без изученного уровня (безопасно
    // ВЫКЛЮЧАТЬ всегда — защитная логика перенесена без изменений из прежнего SetBerserkActive).
    // Диспатч по skillId — как и раньше, единственный toggle-скилл прототипа — Берсерк; если
    // появится другой Toggle-скилл, эффект добавляется сюда отдельной веткой.
    bool TryToggleSkill(ActiveSkillRuntimeState slot)
    {
        if (!IsCombatActive)
        {
            return false;
        }

        bool activate = !slot.IsToggleActive;

        if (slot.Data.skillId == SkillId.Berserk)
        {
            if (activate && Player.UniqueBerserkLevel <= 0)
            {
                return false;
            }

            Player.IsBerserkActive = activate;
        }

        slot.IsToggleActive = activate;
        return true;
    }

    // 4.3: тело перенесено из прежнего TryActivateUniqueActiveSkill без изменений в поведении —
    // Берсерк сюда больше не заходит вовсе (диспатчится в TryToggleSkill по skillType), поэтому
    // прежний защитный бейл-аут на SkillId.Berserk убран как недостижимый.
    bool TryActivateCooldownSkill(ActiveSkillRuntimeState slot)
    {
        if (!IsCombatActive || !IsSkillReady(ActiveSkills.IndexOf(slot)) || Player.Weapons.Count == 0)
        {
            return false;
        }

        ActiveSkillActivated?.Invoke(Player, slot.Data.skillName);

        // 3.11 "Дымовая граната" (уникальная активка Плута): при активации даёт Скрытность и
        // заряжает гарантированные криты на N последующих ОБЫЧНЫХ атак — не бьёт сама.
        if (slot.Data.skillId == SkillId.SmokeBomb)
        {
            GrantOrRefreshStealth(Player);
            Player.SmokeBombGuaranteedCritsRemaining = Player.UniqueSmokeBombLevel;
            Log($"[Combat] «Дымовая граната»: {Player.DisplayName} получает Скрытность и {Player.UniqueSmokeBombLevel} гарантированных крита(ов).");
            slot.CooldownTimer = slot.Data.cooldownSeconds;
            return true;
        }

        var weapon = Player.Weapons[0];
        for (int i = 0; i < slot.HitCount; i++)
        {
            if (!IsCombatActive || !Player.IsAlive)
            {
                break;
            }

            ResolveAttack(Player, weapon, slot.DamageMultiplierPerHit, isRegularAttack: false);
        }

        slot.CooldownTimer = slot.Data.cooldownSeconds;
        return true;
    }
```

- [ ] **Step 5: Remove the forced-cooldown line from `StartCombat`**

In `StartCombat`, delete this block (originally lines 220-224):

```csharp
        // 4.3 (НОВОЕ): активный навык уходит в полный кулдаун сразу при старте боя, а не в 0 —
        // без этого навык (например "3 быстрые атаки") часто срабатывал мгновенно и сносил
        // противника до того, как игрок успевал его увидеть. Обычные атаки оружием (ResetAttackTimers
        // выше) это правило не затрагивает — они по-прежнему начинаются сразу по своей скорости атаки.
        Player.ActiveSkillCooldownTimer = activeSkillCooldownSeconds;
```

`ConfigureActiveSkills` (called before `StartCombat` by the caller) already seeds `CooldownTimer = 0f`,
so nothing needs to happen to skill cooldowns inside `StartCombat` at all.

- [ ] **Step 6: Rewrite the `Tick()` tail for per-slot cooldown + auto-cast**

Replace this block in `Tick()` (originally lines 362-373):

```csharp
        if (IsCombatActive && Player.IsAlive)
        {
            if (Player.ActiveSkillCooldownTimer > 0f)
            {
                Player.ActiveSkillCooldownTimer -= deltaTime;
            }

            if (IsActiveSkillConfigured && activeSkillAutoMode && IsActiveSkillReady)
            {
                TryActivateUniqueActiveSkill();
            }
        }
```

with:

```csharp
        if (IsCombatActive && Player.IsAlive)
        {
            for (int i = 0; i < ActiveSkills.Count; i++)
            {
                var slot = ActiveSkills[i];
                if (slot.Data.skillType != ActiveSkillType.Cooldown)
                {
                    continue;
                }

                if (slot.CooldownTimer > 0f)
                {
                    slot.CooldownTimer -= deltaTime;
                }

                if (slot.AutoMode && slot.CooldownTimer <= 0f)
                {
                    TryActivateSkill(i);
                }
            }
        }
```

- [ ] **Step 7: Remove the now-unused `ActiveSkillCooldownTimer` field from `CombatantRuntime`**

In `Assets/Scripts/Combat/CombatantRuntime.cs`, delete line 59 (`public float ActiveSkillCooldownTimer;`).
Cooldown state now lives entirely in `ActiveSkillRuntimeState.CooldownTimer`.

- [ ] **Step 8: Patch the 3 broken `RunFlowController` call sites so the project compiles**

This is a deliberately minimal, throwaway-quality patch — it keeps the OLD UXML elements
(`AutoModeToggle`/`ActiveSkillButton`/`BerserkToggle`) working against the NEW `CombatManager` API.
Task 3 deletes all of this UI code and replaces it with the real icon panel; don't polish it here.

In `Assets/Scripts/UI/RunFlowController.cs`, replace lines 294-296:

```csharp
        autoModeToggle.RegisterValueChangedCallback(evt => combatManager.SetActiveSkillAutoMode(evt.newValue));
        activeSkillButton.clicked += () => combatManager.TryActivateUniqueActiveSkill();
        berserkToggle.RegisterValueChangedCallback(evt => combatManager.SetBerserkActive(evt.newValue));
```

with:

```csharp
        autoModeToggle.RegisterValueChangedCallback(evt => combatManager.SetSkillAutoMode(0, evt.newValue));
        activeSkillButton.clicked += () => combatManager.TryActivateSkill(0);
        // Временная заглушка (Task 3 удаляет этот Toggle целиком): TryActivateSkill только
        // переключает, а не устанавливает конкретное значение — берсерк-чекбокс временно ведёт
        // себя как кнопка-тумблер вместо чекбокса с явным состоянием.
        berserkToggle.RegisterValueChangedCallback(evt => combatManager.TryActivateSkill(0));
```

In `Assets/Scripts/UI/RunFlowController.Combat.cs`, replace the config call site (originally lines
325-344):

```csharp
        var activeCharacter = characterManager.Progress.Character;
        bool isBarbarian = activeCharacter.characterClass == CharacterClass.Barbarian;

        if (isBarbarian)
        {
            // 3.11 (Варвар) — Берсерк — ручной тумблер, не кулдаун-активка (см. ГДД 3.11, точная
            // цитата: "НЕ работает как обычный активный навык (нет кулдауна, нет авто-режима, нет
            // длительности)"). CombatManager.ConfigureUniqueActiveSkill/TryActivateUniqueActiveSkill
            // не используются для него вовсе — UI использует berserkToggle (см. ниже), не
            // activeSkillButton/autoModeToggle.
            combatManager.SetBerserkActive(false); // сброс на начало боя — тумблер не переносится между боями
            combatManager.ClearUniqueActiveSkillConfiguration(); // см. комментарий в CombatManager — иначе тянется активка предыдущего боя
        }
        else
        {
            int activeLevel = characterManager.Progress.UniqueActiveLevel;
            float activeMultiplier = activeLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f };
            int hitCount = CombatManager.ResolveActiveSkillHitCount(activeCharacter.characterClass);
            combatManager.ConfigureUniqueActiveSkill(hitCount, activeMultiplier, activeCharacter.uniqueActiveSkill.cooldownSeconds, autoModeToggle.value, activeCharacter.uniqueActiveSkill.skillName, activeCharacter.uniqueActiveSkill.skillId);
        }
```

with:

```csharp
        var activeCharacter = characterManager.Progress.Character;
        bool isBarbarian = activeCharacter.characterClass == CharacterClass.Barbarian;

        // Активные-скилы-панель (2026-09-03): Берсерк теперь проходит через ЭТОТ ЖЕ путь как
        // Toggle-скилл (диспатч по ActiveSkillData.skillType в CombatManager.TryActivateSkill) —
        // никакого класс-специфичного if/else в CombatManager больше нет. hitCount/multiplier не
        // имеют смысла для Toggle-скиллов, но передаются нулями для единообразия сигнатуры.
        if (isBarbarian)
        {
            combatManager.ConfigureActiveSkills(new[]
            {
                new ActiveSkillConfigEntry(activeCharacter.uniqueActiveSkill, hitCount: 0, damageMultiplierPerHit: 0f, autoMode: false)
            });
        }
        else
        {
            int activeLevel = characterManager.Progress.UniqueActiveLevel;
            float activeMultiplier = activeLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f };
            int hitCount = CombatManager.ResolveActiveSkillHitCount(activeCharacter.characterClass);
            combatManager.ConfigureActiveSkills(new[]
            {
                new ActiveSkillConfigEntry(activeCharacter.uniqueActiveSkill, hitCount, activeMultiplier, autoMode: autoModeToggle.value)
            });
        }
```

In `UpdateCombatUI` (`RunFlowController.Combat.cs`), replace lines 602-604:

```csharp
            bool ready = combatManager.IsActiveSkillReady;
            activeSkillButton.SetEnabled(!autoModeToggle.value && ready);
            activeSkillButton.text = ready ? "Активный навык (готов)" : $"Активный навык ({combatManager.ActiveSkillCooldownRemaining:F1}с)";
```

with:

```csharp
            bool ready = combatManager.IsSkillReady(0);
            activeSkillButton.SetEnabled(!autoModeToggle.value && ready);
            activeSkillButton.text = ready ? "Активный навык (готов)" : $"Активный навык ({combatManager.SkillCooldownRemaining(0):F1}с)";
```

`berserkToggle.SetValueWithoutNotify(player.IsBerserkActive)` a few lines below stays untouched —
`Player.IsBerserkActive` is still kept in sync by `TryToggleSkill`.

- [ ] **Step 9: Run test to verify it passes, and confirm the whole project compiles**

Run: EditMode test filter `CombatManagerTests`
Expected: PASS (all tests, including the pre-existing `ResolveActiveSkillHitCount_*` ones)

Also run the full EditMode suite once (`BossEncounterTests`, `CursedItemTests`, etc.) to confirm
nothing else in the shared assembly broke:
Run: full EditMode suite
Expected: PASS, 0 failures, 0 compile errors.

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/Combat/ActiveSkillRuntimeState.cs Assets/Scripts/Managers/CombatManager.cs Assets/Scripts/Combat/CombatantRuntime.cs Assets/Scripts/UI/RunFlowController.cs Assets/Scripts/UI/RunFlowController.Combat.cs Assets/Tests/EditMode/CombatManagerTests.cs
git commit -m "refactor: list-based active skills in CombatManager, dispatched by type (default auto-off)"
```

---

## Task 3: Icon-based bottom-center skill panel in `RunFlowController`

**Files:**
- Modify: `Assets/UI/GameRoot.uxml:241-245`
- Modify: `Assets/UI/GameStyles.uss:816-820` (replace `.combat-controls-row` with new skill-panel rules)
- Modify: `Assets/Scripts/UI/RunFlowController.cs:182-184` (fields), `:294-296` (OnEnable wiring — replaces Task 2's throwaway patch), `:433-435` (CacheElements), `:670-676` (tutorial tooltip bindings)
- Modify: `Assets/Scripts/UI/RunFlowController.Combat.cs` (config call site addition, combat loop hotkeys, `UpdateCombatUI` tail — replaces Task 2's throwaway patch)
- Test: `Assets/Tests/EditMode/GameRootUxmlTests.cs` (create)

**Interfaces:**
- Consumes: `CombatManager.ActiveSkills`/`ConfigureActiveSkills`/`TryActivateSkill`/`SetSkillAutoMode`/`IsSkillReady`/`SkillCooldownRemaining` from Task 2; `ActiveSkillData.skillType`/`icon` from Task 1.
- Produces: `SkillPanelContainer` element in `GameRoot.uxml`; no new public C# API (all additions are private to `RunFlowController`).

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/GameRootUxmlTests.cs` (mirrors the loading pattern already used in
`Assets/Tests/EditMode/FloorMapGeneratorTests.cs:243-245`):

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public class GameRootUxmlTests
{
    [Test]
    public void GameRoot_HasSkillPanelContainer_NotOldCombatControlsRow()
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameRoot.uxml");
        var root = asset.CloneTree();

        Assert.IsNotNull(root.Q<VisualElement>("SkillPanelContainer"));
        Assert.IsNull(root.Q<Toggle>("AutoModeToggle"));
        Assert.IsNull(root.Q<Button>("ActiveSkillButton"));
        Assert.IsNull(root.Q<Toggle>("BerserkToggle"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: EditMode test filter `GameRootUxmlTests`
Expected: FAIL — `SkillPanelContainer` doesn't exist yet, old elements still do.

- [ ] **Step 3: Replace `combat-controls-row` in `GameRoot.uxml`**

In `Assets/UI/GameRoot.uxml`, replace lines 241-245:

```xml
                <ui:VisualElement class="combat-controls-row">
                    <ui:Toggle name="AutoModeToggle" label="Авто-режим навыка" value="true" />
                    <ui:Button name="ActiveSkillButton" text="Активный навык" class="button-secondary" />
                    <ui:Toggle name="BerserkToggle" label="Берсерк" value="false" class="hidden" />
                </ui:VisualElement>
```

with:

```xml
                <ui:VisualElement name="SkillPanelContainer" class="skill-panel-container" />
```

- [ ] **Step 4: Replace `.combat-controls-row` styling in `GameStyles.uss`**

In `Assets/UI/GameStyles.uss`, replace lines 816-820:

```css
.combat-controls-row {
    flex-direction: row;
    align-items: center;
    justify-content: space-between;
}
```

with:

```css
.skill-panel-container {
    position: absolute;
    bottom: 12px;
    left: 50%;
    translate: -50% 0;
    flex-direction: row;
    align-items: flex-end;
}

.skill-slot {
    align-items: center;
    margin-left: 8px;
    margin-right: 8px;
}

.skill-icon-frame {
    width: 64px;
    height: 64px;
    border-width: 3px;
    border-color: rgb(90, 84, 72);
    border-radius: 10px;
    background-color: rgb(38, 36, 44);
    overflow: hidden;
    transition-property: border-color;
    transition-duration: 0.2s;
}

.skill-icon {
    width: 100%;
    height: 100%;
    -unity-background-scale-mode: scale-to-fit;
}

.skill-cooldown-overlay {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    background-color: rgba(10, 10, 14, 0.75);
}

.skill-cooldown-text {
    position: absolute;
    left: 0;
    right: 0;
    top: 0;
    bottom: 0;
    -unity-text-align: middle-center;
    font-size: 16px;
    color: rgb(240, 235, 220);
    -unity-font-style: bold;
}

.skill-hotkey-label {
    position: absolute;
    right: 2px;
    bottom: 2px;
    font-size: 11px;
    color: rgb(200, 190, 170);
    background-color: rgba(0, 0, 0, 0.5);
    padding: 1px 3px 1px 3px;
    border-radius: 3px;
}

.skill-icon-ready {
    border-color: rgb(255, 214, 90);
}

.skill-icon-ready-pulse {
    border-color: rgb(255, 245, 200);
}

.skill-icon-toggle-inactive {
    border-color: rgb(90, 84, 72);
    opacity: 0.55;
}

.skill-icon-toggle-active {
    border-color: rgb(255, 70, 35);
    opacity: 1;
}

.skill-auto-toggle {
    width: 20px;
    height: 20px;
    margin-bottom: 4px;
    border-width: 2px;
    border-radius: 4px;
    border-color: rgb(90, 84, 72);
    background-color: rgb(38, 36, 44);
}

.skill-auto-toggle-on {
    border-color: rgb(120, 200, 120);
    background-color: rgb(60, 110, 60);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: EditMode test filter `GameRootUxmlTests`
Expected: PASS

- [ ] **Step 6: Update `RunFlowController.cs` field declarations and caching**

Replace lines 182-184:

```csharp
    Toggle autoModeToggle;
    Button activeSkillButton;
    Toggle berserkToggle;
```

with:

```csharp
    VisualElement skillPanelContainer;
    readonly List<SkillSlotEntry> skillSlotEntries = new List<SkillSlotEntry>();

    // Активные-скилы-панель (2026-09-03): один хоткей на слот по индексу, Q для первого. 4 клавиш
    // с большим запасом сверх сегодняшнего максимума в 1 скилл на класс.
    static readonly KeyCode[] SkillHotkeys = { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R };

    class SkillSlotEntry
    {
        public VisualElement IconFrame;
        public VisualElement CooldownOverlay;
        public Label CooldownText;
        public VisualElement AutoToggle;
    }
```

Add this field near `selectedCharacter`/`selectedMentor` (around line 270):

```csharp
    // Активные-скилы-панель (2026-09-03): единственный сегодня Cooldown-слот на класс — авто-режим
    // персистентен между боями ОДНОГО забега (как и раньше персистился через .value статичного
    // UXML-тумблера), но по умолчанию ВЫКЛЮЧЕН на старте нового забега.
    bool activeSkillAutoModePreference;
```

Replace lines 433-435 (inside `CacheElements`):

```csharp
        autoModeToggle = root.Q<Toggle>("AutoModeToggle");
        activeSkillButton = root.Q<Button>("ActiveSkillButton");
        berserkToggle = root.Q<Toggle>("BerserkToggle");
```

with:

```csharp
        skillPanelContainer = root.Q<VisualElement>("SkillPanelContainer");
```

- [ ] **Step 7: Remove the throwaway wiring from `OnEnable` (added in Task 2 Step 8)**

Delete these 3 lines:

```csharp
        autoModeToggle.RegisterValueChangedCallback(evt => combatManager.SetSkillAutoMode(0, evt.newValue));
        activeSkillButton.clicked += () => combatManager.TryActivateSkill(0);
        berserkToggle.RegisterValueChangedCallback(evt => combatManager.TryActivateSkill(0));
```

(No replacement here — wiring now happens per-slot inside `BuildSkillPanel`, added in Step 9.)

- [ ] **Step 8: Remove the 3 static tutorial-tooltip bindings tied to the removed elements**

In `BindStaticTutorialTooltips` (around line 670), delete:

```csharp
        tutorialManager.BindTooltip(autoModeToggle, "Авто-режим", TutorialContent.TooltipAuto);
        tutorialManager.BindTooltip(activeSkillButton, "Активный навык",
            () => TutorialContent.ActiveSkillTooltip(characterManager?.Character?.characterId));
        tutorialManager.BindTooltip(berserkToggle, "Берсерк",
            () => TutorialContent.BerserkTooltip(combatManager != null && combatManager.Player != null && combatManager.Player.IsBerserkActive
                ? combatManager.Player.PhysicalResistancePercent
                : 0f));
```

(Tooltips move to per-slot binding inside `BuildSkillPanel`, Step 9 — they're rebuilt every combat
since the slots themselves are, unlike the rest of `BindStaticTutorialTooltips` which binds once for
the lifetime of the screen.)

- [ ] **Step 9: Add `BuildSkillPanel`/`UpdateSkillPanel`/`HandleSkillHotkeys`/`TryActivateSkillFromUI` to `RunFlowController.Combat.cs`**

Add these methods (near `BuildEnemyStageEntries`, which they mirror):

```csharp
    // Активные-скилы-панель (2026-09-03): строится ОДИН раз при старте боя, как и
    // BuildEnemyStageEntries — состав слотов не меняется в процессе одного боя.
    void BuildSkillPanel()
    {
        skillPanelContainer.Clear();
        skillSlotEntries.Clear();

        for (int i = 0; i < combatManager.ActiveSkills.Count; i++)
        {
            var data = combatManager.ActiveSkills[i].Data;
            int slotIndex = i;

            var slotRoot = new VisualElement();
            slotRoot.AddToClassList("skill-slot");

            var iconFrame = new VisualElement();
            iconFrame.AddToClassList("skill-icon-frame");
            iconFrame.RegisterCallback<ClickEvent>(_ => TryActivateSkillFromUI(slotIndex));

            var icon = new Image { sprite = data.icon };
            icon.AddToClassList("skill-icon");
            iconFrame.Add(icon);

            var cooldownOverlay = new VisualElement();
            cooldownOverlay.AddToClassList("skill-cooldown-overlay");
            iconFrame.Add(cooldownOverlay);

            var cooldownText = new Label();
            cooldownText.AddToClassList("skill-cooldown-text");
            iconFrame.Add(cooldownText);

            var hotkeyLabel = new Label(slotIndex < SkillHotkeys.Length ? SkillHotkeys[slotIndex].ToString() : string.Empty);
            hotkeyLabel.AddToClassList("skill-hotkey-label");
            iconFrame.Add(hotkeyLabel);

            slotRoot.Add(iconFrame);

            // Пульсация готовности — лёгкий переключатель класса по таймеру, т.к. UI Toolkit USS
            // не поддерживает keyframe-анимации; сам класс skill-icon-ready включается/выключается
            // каждый кадр в UpdateSkillPanel ниже.
            iconFrame.schedule.Execute(() =>
            {
                if (iconFrame.ClassListContains("skill-icon-ready"))
                {
                    iconFrame.ToggleInClassList("skill-icon-ready-pulse");
                }
                else
                {
                    iconFrame.RemoveFromClassList("skill-icon-ready-pulse");
                }
            }).Every(500);

            VisualElement autoToggle = null;
            if (data.skillType == ActiveSkillType.Cooldown)
            {
                autoToggle = new VisualElement();
                autoToggle.AddToClassList("skill-auto-toggle");
                autoToggle.RegisterCallback<ClickEvent>(_ =>
                {
                    activeSkillAutoModePreference = !combatManager.ActiveSkills[slotIndex].AutoMode;
                    combatManager.SetSkillAutoMode(slotIndex, activeSkillAutoModePreference);
                });
                slotRoot.Add(autoToggle);
            }

            tutorialManager?.BindTooltip(iconFrame, data.skillName, () => data.effectDescription);

            skillPanelContainer.Add(slotRoot);
            skillSlotEntries.Add(new SkillSlotEntry
            {
                IconFrame = iconFrame,
                CooldownOverlay = cooldownOverlay,
                CooldownText = cooldownText,
                AutoToggle = autoToggle,
            });
        }
    }

    void TryActivateSkillFromUI(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= combatManager.ActiveSkills.Count)
        {
            return;
        }

        var slot = combatManager.ActiveSkills[slotIndex];
        if (slot.Data.skillType == ActiveSkillType.Cooldown && slot.AutoMode)
        {
            return; // авто-режим сам решает, когда бить — ручной клик/хоткей здесь не имеет смысла
        }

        combatManager.TryActivateSkill(slotIndex);
    }

    void HandleSkillHotkeys()
    {
        for (int i = 0; i < combatManager.ActiveSkills.Count && i < SkillHotkeys.Length; i++)
        {
            if (Input.GetKeyDown(SkillHotkeys[i]))
            {
                TryActivateSkillFromUI(i);
            }
        }
    }

    void UpdateSkillPanel()
    {
        for (int i = 0; i < skillSlotEntries.Count && i < combatManager.ActiveSkills.Count; i++)
        {
            var slot = combatManager.ActiveSkills[i];
            var entry = skillSlotEntries[i];

            if (slot.Data.skillType == ActiveSkillType.Toggle)
            {
                entry.IconFrame.EnableInClassList("skill-icon-toggle-active", slot.IsToggleActive);
                entry.IconFrame.EnableInClassList("skill-icon-toggle-inactive", !slot.IsToggleActive);
                continue;
            }

            bool ready = combatManager.IsSkillReady(i);
            float remaining = combatManager.SkillCooldownRemaining(i);
            float fraction = slot.Data.cooldownSeconds > 0f ? Mathf.Clamp01(remaining / slot.Data.cooldownSeconds) : 0f;
            entry.CooldownOverlay.style.height = new Length(fraction * 100f, LengthUnit.Percent);
            entry.CooldownText.text = ready ? string.Empty : $"{remaining:F1}";
            entry.IconFrame.EnableInClassList("skill-icon-ready", ready);

            if (entry.AutoToggle != null)
            {
                entry.AutoToggle.EnableInClassList("skill-auto-toggle-on", slot.AutoMode);
            }
        }
    }
```

- [ ] **Step 10: Replace the Task-2 throwaway config call site with the real one, and call `BuildSkillPanel`**

In `RunFlowController.Combat.cs`, replace the config block Task 2 wrote:

```csharp
        var activeCharacter = characterManager.Progress.Character;
        bool isBarbarian = activeCharacter.characterClass == CharacterClass.Barbarian;

        // Активные-скилы-панель (2026-09-03): Берсерк теперь проходит через ЭТОТ ЖЕ путь как
        // Toggle-скилл (диспатч по ActiveSkillData.skillType в CombatManager.TryActivateSkill) —
        // никакого класс-специфичного if/else в CombatManager больше нет. hitCount/multiplier не
        // имеют смысла для Toggle-скиллов, но передаются нулями для единообразия сигнатуры.
        if (isBarbarian)
        {
            combatManager.ConfigureActiveSkills(new[]
            {
                new ActiveSkillConfigEntry(activeCharacter.uniqueActiveSkill, hitCount: 0, damageMultiplierPerHit: 0f, autoMode: false)
            });
        }
        else
        {
            int activeLevel = characterManager.Progress.UniqueActiveLevel;
            float activeMultiplier = activeLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f };
            int hitCount = CombatManager.ResolveActiveSkillHitCount(activeCharacter.characterClass);
            combatManager.ConfigureActiveSkills(new[]
            {
                new ActiveSkillConfigEntry(activeCharacter.uniqueActiveSkill, hitCount, activeMultiplier, autoMode: autoModeToggle.value)
            });
        }
```

with (only the last line of each branch changes — `autoModeToggle.value` no longer exists, replaced
by the persisted preference field from Step 6):

```csharp
        var activeCharacter = characterManager.Progress.Character;
        bool isBarbarian = activeCharacter.characterClass == CharacterClass.Barbarian;

        // Активные-скилы-панель (2026-09-03): Берсерк проходит через ЭТОТ ЖЕ путь как Toggle-скилл
        // (диспатч по ActiveSkillData.skillType в CombatManager.TryActivateSkill) — никакого
        // класс-специфичного if/else в CombatManager нет. hitCount/multiplier не имеют смысла для
        // Toggle-скиллов, но передаются нулями для единообразия сигнатуры.
        if (isBarbarian)
        {
            combatManager.ConfigureActiveSkills(new[]
            {
                new ActiveSkillConfigEntry(activeCharacter.uniqueActiveSkill, hitCount: 0, damageMultiplierPerHit: 0f, autoMode: false)
            });
        }
        else
        {
            int activeLevel = characterManager.Progress.UniqueActiveLevel;
            float activeMultiplier = activeLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f };
            int hitCount = CombatManager.ResolveActiveSkillHitCount(activeCharacter.characterClass);
            combatManager.ConfigureActiveSkills(new[]
            {
                new ActiveSkillConfigEntry(activeCharacter.uniqueActiveSkill, hitCount, activeMultiplier, activeSkillAutoModePreference)
            });
        }

        BuildSkillPanel();
```

- [ ] **Step 11: Wire hotkeys into the combat loop**

Replace the combat loop (originally lines 378-382):

```csharp
        while (combatManager.IsCombatActive)
        {
            UpdateCombatUI();
            yield return null;
        }
```

with:

```csharp
        while (combatManager.IsCombatActive)
        {
            HandleSkillHotkeys();
            UpdateCombatUI();
            yield return null;
        }
```

- [ ] **Step 12: Call `UpdateSkillPanel` from `UpdateCombatUI`, remove the Task-2 throwaway text-button block**

In `UpdateCombatUI` (`RunFlowController.Combat.cs`), replace the tail Task 2 left behind:

```csharp
        activeSkillButton.EnableInClassList("hidden", isBarbarianCombat);
        autoModeToggle.EnableInClassList("hidden", isBarbarianCombat);
        berserkToggle.EnableInClassList("hidden", !isBarbarianCombat);

        if (!isBarbarianCombat)
        {
            bool ready = combatManager.IsSkillReady(0);
            activeSkillButton.SetEnabled(!autoModeToggle.value && ready);
            activeSkillButton.text = ready ? "Активный навык (готов)" : $"Активный навык ({combatManager.SkillCooldownRemaining(0):F1}с)";
        }
        else
        {
            berserkToggle.SetValueWithoutNotify(player.IsBerserkActive);
        }
    }
```

with:

```csharp
        UpdateSkillPanel();
    }
```

(`UpdateBerserkAura(isBarbarianCombat && player.IsBerserkActive)`, a few lines above this block at
line 493, already reads `player.IsBerserkActive` — untouched, still correct, since `TryToggleSkill`
keeps that field in sync.)

- [ ] **Step 13: Manual verification in the Editor**

Open the project in Unity, enter Play mode, start a run with each class (Jennifer/Warrior, Rogue,
Barbarian) and confirm for each:
- The skill panel appears bottom-center with one icon.
- The auto-mode toggle (Cooldown classes only) starts OFF.
- The Cooldown icon starts ready (no dark overlay) the instant combat begins.
- Clicking the icon or pressing Q activates the skill; a dark overlay + countdown appears, then
  clears and the icon pulses when ready again.
- For Barbarian: the icon has no auto-toggle, no cooldown overlay ever appears, and clicking it (or
  Q) toggles between a dim/muted look and a bright red-bordered look — visually unambiguous at a
  glance which state it's in.

- [ ] **Step 14: Run the full EditMode suite**

Run: full EditMode suite (Unity Test Runner, or `Unity.exe -runTests -testPlatform EditMode`)
Expected: PASS, 0 failures (in particular `CombatManagerTests`, `ActiveSkillDataTests`,
`GameRootUxmlTests`, and every pre-existing suite touching `CombatManager`/`RunFlowController`
compile-adjacent code — `BossEncounterTests`, `CursedItemTests`, `CombatResourceVisibilityTests`).

- [ ] **Step 15: Commit**

```bash
git add Assets/UI/GameRoot.uxml Assets/UI/GameStyles.uss Assets/Scripts/UI/RunFlowController.cs Assets/Scripts/UI/RunFlowController.Combat.cs Assets/Tests/EditMode/GameRootUxmlTests.cs
git commit -m "feat: icon-based bottom-center skill panel with Q/W/E/R hotkeys and cooldown/toggle visuals"
```

---

## Task 4: Generate and assign skill icons via PixelLab

**Files:**
- Create: `Assets/Sprites/UI/SkillIcons/skill_three_quick_strikes.png`, `skill_smoke_bomb.png`, `skill_berserk.png` (+ `.meta` files, auto-generated by Unity on import)
- Modify: `Assets/ScriptableObjects/Skills/Unique/Skill_ThreeQuickStrikes.asset`, `Skill_SmokeBomb.asset`, `Skill_Berserk.asset` (`icon` field)

**Interfaces:**
- Consumes: `ActiveSkillData.icon` (`Sprite`) from Task 1.
- Produces: nothing new — this is content, not code.

- [ ] **Step 1: Check the PixelLab MCP tools are connected**

Run `ToolSearch` with query `select:mcp__pixellab__create_image_pixflux,mcp__pixellab__create_ui_asset`
(or whichever icon-generation tool the PixelLab server currently exposes — check with
`mcp__pixellab__agent_help` if unsure) to confirm the server is connected in this session before
starting generation.

- [ ] **Step 2: Generate 3 icons, one per skill**

Per the project's established PixelLab workflow (single south-facing/front view only, no
8-directional rotation needed for a UI icon; use a `v3-reference` image if you want the icon to
match an existing sprite's exact palette/style 1:1 — see e.g.
`Assets/Resources/CharacterAnimations/Jennifer/SkillBrightStrike/` for the closest existing visual
reference to "3 быстрые атаки"), generate:

- **Three Quick Strikes** — 3 crossed/streaking blade slashes, warm gold/white highlight, matching
  Jennifer's warrior palette.
- **Smoke Bomb** — a round grey-purple smoke cloud with a small dark canister silhouette, matching
  the Rogue/Violet stealth palette (dark purples).
- **Berserk** — a stylized red/orange rage symbol (clenched fist or flame-wreathed axe), matching
  the Barbarian/Sasha palette (reds).

Target a square canvas (e.g. 64x64) since the icon frame in the UI is square (`.skill-icon-frame`,
64x64 — see Task 3 Step 4). Export each as a flat PNG (not a spritesheet).

- [ ] **Step 3: Import into Unity and set Texture Type to Sprite**

Save the 3 exported PNGs to `Assets/Sprites/UI/SkillIcons/` (create the folder if it doesn't exist).
In the Unity Editor, select each imported PNG in the Project window and in the Inspector set
**Texture Type = Sprite (2D and UI)**, then **Apply**.

- [ ] **Step 4: Assign each sprite to its skill asset's `icon` field**

In the Unity Editor, select each of `Skill_ThreeQuickStrikes.asset`, `Skill_SmokeBomb.asset`,
`Skill_Berserk.asset` under `Assets/ScriptableObjects/Skills/Unique/` and drag the matching sprite
from `Assets/Sprites/UI/SkillIcons/` into the **Icon** field in the Inspector.

- [ ] **Step 5: Verify in Play mode**

Enter Play mode, start a run with each class, and confirm the correct icon (not a blank/missing
sprite) renders inside the skill panel's icon frame for that class.

- [ ] **Step 6: Commit**

```bash
git add Assets/Sprites/UI/SkillIcons/ Assets/ScriptableObjects/Skills/Unique/Skill_ThreeQuickStrikes.asset Assets/ScriptableObjects/Skills/Unique/Skill_SmokeBomb.asset Assets/ScriptableObjects/Skills/Unique/Skill_Berserk.asset
git commit -m "feat: add generated icons for the three active skills"
```

---

## Task 5: Final verification pass

**Files:** none (verification only)

- [ ] **Step 1: Run the full EditMode suite one more time**

Run: full EditMode suite
Expected: PASS, 0 failures.

- [ ] **Step 2: Manual playtest checklist**

In the Editor, play through at least one full room fight per class (Jennifer/Warrior, Rogue,
Barbarian) and confirm every item from the spec's Section 7 holds:
- Skill panel icons visible and distinct per class.
- Cooldown skill ready at combat start (no forced initial wait).
- Auto-mode toggle defaults OFF for a fresh run.
- Manual activation works via click AND via Q (or the slot's assigned hotkey).
- Toggling auto-mode ON causes the skill to fire itself once ready, with no further manual input.
- Toggle-type skill (Berserk) is visually unambiguous between active/inactive at a glance, with no
  cooldown overlay ever shown on it.

- [ ] **Step 3: PlayMode smoke test**

Run the existing `PlayModeSmokeTest` **without** the `-quit` flag, and back up the current save file
first (per project history: running it with `-quit` or without a save backup has previously wiped
save progress — see project memory on this risk). Confirm it completes without new failures.

- [ ] **Step 4: Update project memory**

This plan's completion is exactly the kind of fact worth recording for future sessions. After
verification passes, update `MEMORY.md` and the relevant memory file to mark
"Active Skills Panel (multi-slot + icons)" as DONE, noting the commit range and that PlayMode smoke
test / manual playtest were run.
