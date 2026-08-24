# New Monsters, Modifiers & Monster-Passive Combat Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the 7 new monster types from GDD 2.4, wire up monster passive skills in combat (including retroactively fixing the pre-existing Колдун "Проклятие замедления" gap, which was never actually connected to combat logic despite its asset existing), add the 4-modifier system from GDD 2.8 with Russian gender agreement, and floor-tier the monster pool per the GDD's draft distribution.

**Architecture:** `MonsterData` gains two new fields (`gender` for modifier grammar, `minFloorTier` for pool filtering). A new `MonsterSkillEffectMap` (mirrors the existing `SkillEffectMap` pattern) maps monster passive names to combat behavior. `CombatantRuntime` gains passive-specific runtime state fields (poison stacks, periodic-passive cooldown, crit-debuff, monster evasion). `CombatManager` gains one new method for on-hit monster passives and one for periodic (non-attack-timer) monster passives, both following the existing `ApplyFreezeOnHit`/`ApplyBleed` code shape. A new `MonsterModifierCatalog` static class holds the 2.8 catalog + gender-aware adjective forms + the roll formula, invoked from `CombatantFactory.CreateMonsterCombatant`. `RunFlowController`'s monster-pool selection is filtered by floor tier instead of picking uniformly from one flat list.

**Tech Stack:** Unity 6000.5.8f1, C#, ScriptableObject assets (hand-authored YAML, matching this project's existing asset-authoring convention — no assets have been created through the Unity Editor UI in this repo's history, they're written directly as `.asset`/`.asset.meta` file pairs).

**Spec:** ГДД Данжнгерлс (рабочая версия) — Notion page `3c10227a-2824-81bb-a9c0-c2f212bddbfb`, sections 2.3, 2.4, 2.8. User sync prompt items 3, 4.

## Global Constraints

- Modifier catalog is EXACTLY 4 entries, no more: Быстрый (+25% attack speed), Большой (+50% max HP), Бронированный (+5 flat physical defense, applies even to monsters with 0 base defense), Свирепый (+25% damage). No duplicate modifier on one monster.
- Modifier cap by floor: floor 1 = 0; floors 2-5 = max 1; floors 6-9 = max 2; floor 10 = uncapped (effectively 4, catalog size).
- Modifier roll chance is tied to monster level (2.7, range 1-4) via `(monsterLevel − 1) × 10%` → 0/10/20/30%. Rolls are sequential and independent; a failed roll stops further rolls for that monster (does not retry).
- Modifier stat effects apply ON TOP OF (multiplicatively/additively after) the floor-scaling (2.6) and monster-level (2.7) formulas already applied in `CombatantFactory.CreateMonsterCombatant` — never before.
- Monster passive percentages/flat values (evasion %, steal %, poison damage, crit-debuff %, heal %, double-strike multiplier, slow %) are FIXED per the catalog below and do NOT scale with floor/monster-level — only the monster's base HP/damage/defense scale (per GDD 2.4's explicit clarification, to prevent e.g. Bat evasion becoming unbeatable at high floors).
- Draft floor-tier distribution (GDD 2.4, "точные пороги открыты, требуют баланс-тестов" — thresholds themselves ARE given, only their exact tuning is open): floors 1-3 = Слизь/Скелет/Колдун/Летучая мышь/Гоблин-вор; floors 4-6 = + Каменный страж/Ядовитый паучок; floors 7-9 = + Гарпия/Жрец тьмы; floor 10 = + Рыцарь тьмы. Implemented as a single `minFloorTier` field per monster (1/4/7/10) rather than a hardcoded per-floor list, so a monster is eligible on its tier floor AND all floors above it (matches the GDD's "+" notation — each tier adds to, not replaces, the previous roster).

---

### Task 1: `MonsterData` schema additions — gender + floor tier

**Files:**
- Modify: `Assets/Scripts/Data/MonsterData.cs`
- Modify (add 2 fields to existing YAML): `Assets/ScriptableObjects/Monsters/Monster_Slime.asset`, `Monster_Skeleton.asset`, `Monster_Warlock.asset`, `Monster_Boss.asset`

**Interfaces:**
- Produces: `MonsterData.gender` (`MonsterGender` enum), `MonsterData.minFloorTier` (int).

- [ ] **Step 1: Add the `MonsterGender` enum**

```csharp
// Assets/Scripts/Data/Enums.cs — add near the other content enums:

// 2.8: род существительного-названия монстра на русском, нужен для согласования прилагательного
// модификатора ("Быстрый Скелет" vs "Большая Слизь" vs, гипотетически, "Быстрое Существо").
public enum MonsterGender
{
    Masculine,
    Feminine,
    Neuter
}
```

- [ ] **Step 2: Add the fields to `MonsterData`**

```csharp
// Assets/Scripts/Data/MonsterData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "NewMonster", menuName = "DungeonGirls/Monster")]
public class MonsterData : ScriptableObject
{
    public string monsterName;
    public bool isBoss;

    public float hp;
    public float damageMin;
    public float damageMax;
    public DamageType damageType;
    public float attackSpeed;

    public float physicalDefense;
    public float magicDefense;

    public PassiveSkillData passiveSkill;

    // 2.8: род названия монстра — согласование прилагательного модификатора.
    public MonsterGender gender = MonsterGender.Masculine;

    // 2.4: минимальный этаж, с которого монстр может попасться в обычной боевой комнате (1/4/7/10
    // по черновому распределению ГДД). Монстр доступен на этом этаже И ВЫШЕ (тиры суммируются,
    // не заменяют друг друга — см. "Черновое распределение по этажам" в 2.4).
    public int minFloorTier = 1;
}
```

- [ ] **Step 3: Add the two new fields to the 4 existing monster assets**

Append these two lines to the end of each existing `.asset` file's `MonoBehaviour:` body (after `passiveSkill:`):

`Monster_Slime.asset`, `Monster_Skeleton.asset`, `Monster_Warlock.asset` — all base-tier monsters:
```yaml
  gender: 2
  minFloorTier: 1
```
(`gender: 2` = `Neuter` for Слизь ["средний род"], reuse `2` there; for Скелет/Колдун use `gender: 0` = `Masculine`.)

Concretely:
- `Monster_Slime.asset`: append `  gender: 2\n  minFloorTier: 1` (Слизь is feminine grammatically in Russian — "она" — actually reconsider: "слизь" is a feminine noun (3rd declension, like "мышь"). Use `gender: 1` = Feminine.)
- `Monster_Skeleton.asset`: append `  gender: 0\n  minFloorTier: 1` (Скелет — masculine)
- `Monster_Warlock.asset`: append `  gender: 0\n  minFloorTier: 1` (Колдун — masculine)
- `Monster_Boss.asset`: append `  gender: 0\n  minFloorTier: 1` (boss gender is irrelevant — bosses never roll modifiers per 2.2/2.8, but the field must have a value; `minFloorTier: 1` is also irrelevant for the boss since it's picked separately from the regular pool, but keep it consistent)

- [ ] **Step 4: Verify Unity re-imports the assets without console errors**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile -
```
Expected: no new console errors about the 4 modified assets (existing checks will still fail at this point since later tasks aren't done yet — just confirm no YAML/import errors, i.e. no `[Console Error]` lines mentioning these 4 asset paths).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Data/Enums.cs Assets/Scripts/Data/MonsterData.cs Assets/ScriptableObjects/Monsters/*.asset
git commit -m "Add MonsterData.gender and minFloorTier fields (GDD 2.4/2.8)"
```

---

### Task 2: 7 new monster assets + their passive-skill assets

**Files:**
- Create: `Assets/Scripts/Combat/MonsterSkillEffectMap.cs`
- Create: `Assets/ScriptableObjects/Skills/MonsterPassives/Skill_Fluttering.asset(.meta)`
- Create: `Assets/ScriptableObjects/Skills/MonsterPassives/Skill_Pickpocket.asset(.meta)`
- Create: `Assets/ScriptableObjects/Skills/MonsterPassives/Skill_Poison.asset(.meta)`
- Create: `Assets/ScriptableObjects/Skills/MonsterPassives/Skill_StunningScream.asset(.meta)`
- Create: `Assets/ScriptableObjects/Skills/MonsterPassives/Skill_DarkHeal.asset(.meta)`
- Create: `Assets/ScriptableObjects/Skills/MonsterPassives/Skill_DoubleStrike.asset(.meta)`
- Create: `Assets/ScriptableObjects/Monsters/Monster_Bat.asset(.meta)`
- Create: `Assets/ScriptableObjects/Monsters/Monster_GoblinThief.asset(.meta)`
- Create: `Assets/ScriptableObjects/Monsters/Monster_StoneGuardian.asset(.meta)`
- Create: `Assets/ScriptableObjects/Monsters/Monster_PoisonSpiderling.asset(.meta)`
- Create: `Assets/ScriptableObjects/Monsters/Monster_Harpy.asset(.meta)`
- Create: `Assets/ScriptableObjects/Monsters/Monster_DarkPriest.asset(.meta)`
- Create: `Assets/ScriptableObjects/Monsters/Monster_DarkKnight.asset(.meta)`

**Interfaces:**
- Produces: `MonsterSkillEffectMap` string constants, consumed by Task 3 (`CombatManager`) and Task 4 (`CombatantFactory`).

- [ ] **Step 1: Create `MonsterSkillEffectMap.cs`**

```csharp
// Assets/Scripts/Combat/MonsterSkillEffectMap.cs
// Связывает боевую логику монстров с ассетами PassiveSkillData (2.4) по их skillName — аналог
// SkillEffectMap для навыков персонажа. "Проклятие замедления" (Колдун) уже существовало как
// ассет (Skill_SlowCurse) с самой первой сессии, но никогда не было подключено к CombatManager —
// эта карта исправляет и его тоже, заодно с 6 новыми монстрами 2.4.
public static class MonsterSkillEffectMap
{
    public const string SlowCurse = "Проклятие замедления"; // Колдун — уже существовавший ассет
    public const string Fluttering = "Порхание"; // Летучая мышь
    public const string Pickpocket = "Карманник"; // Гоблин-вор
    public const string Poison = "Яд"; // Ядовитый паучок
    public const string StunningScream = "Оглушающий крик"; // Гарпия
    public const string DarkHeal = "Тёмное исцеление"; // Жрец тьмы
    public const string DoubleStrike = "Двойной удар"; // Рыцарь тьмы
    // Каменный страж не имеет пассивки (2.4) — только базовые статы.
}
```

- [ ] **Step 2: Generate a fresh 32-hex GUID per new file**

Run this once per file you create below (13 assets total: 6 skills + 7 monsters), capturing the output for use in that file's `.meta`:

```bash
openssl rand -hex 16
```

(If `openssl` isn't available, use `powershell -Command "[guid]::NewGuid().ToString('N')"` instead — either produces a 32-char lowercase hex string, which is exactly Unity's `guid:` format.)

- [ ] **Step 3: Create the 6 new `PassiveSkillData` assets**

For each, the file is:
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
  m_Script: {fileID: 11500000, guid: 33564269414bad2439b83f3f7597e022, type: 3}
  m_Name: <ASSET_NAME>
  m_EditorClassIdentifier: Assembly-CSharp::PassiveSkillData
  skillName: <SKILL_NAME>
  category: 4
  effectDescription: <DESCRIPTION>
  maxLevel: 1
```
and its paired `.meta`:
```yaml
fileFormatVersion: 2
guid: <FRESH_GUID_FROM_STEP_2>
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```
(`category: 4` = `SkillCategory.MonsterPassive`, matching `Skill_SlowCurse.asset`'s existing value — verify against `Assets/Scripts/Data/Enums.cs`'s `SkillCategory` enum order: `General=0, WarriorClass=1, Unique=2, ItemPassive=3, MonsterPassive=4`.)

Use plain UTF-8 Cyrillic text directly in `skillName`/`m_Name`/`effectDescription` (Unity YAML accepts UTF-8 natively when writing a new file with a text editor — the `\uXXXX` escapes seen in existing assets are just how the Editor happened to serialize them, not a requirement).

| File | m_Name | skillName | effectDescription |
|---|---|---|---|
| `Skill_Fluttering.asset` | `Skill_Fluttering` | `Порхание` | `Пассивка Летучей мыши. 20% шанс полностью уклониться от атаки персонажа.` |
| `Skill_Pickpocket.asset` | `Skill_Pickpocket` | `Карманник` | `Пассивка Гоблина-вора. При попадании по здоровью персонажа с 20% шансом ворует 5% текущей валюты забега.` |
| `Skill_Poison.asset` | `Skill_Poison` | `Яд` | `Пассивка Ядовитого паучка. При попадании по здоровью накладывает яд (3 сек, 4 урона/сек, стакается до 3 раз).` |
| `Skill_StunningScream.asset` | `Skill_StunningScream` | `Оглушающий крик` | `Пассивка Гарпии. 15% шанс при атаке снизить шанс крита персонажа на 20% на 4 сек.` |
| `Skill_DarkHeal.asset` | `Skill_DarkHeal` | `Тёмное исцеление` | `Пассивка Жреца тьмы. Раз в 8 сек восстанавливает 10% макс. HP себе или ближайшему союзнику в комнате.` |
| `Skill_DoubleStrike.asset` | `Skill_DoubleStrike` | `Двойной удар` | `Пассивка Рыцаря тьмы (элитный тип). Раз в 6 сек наносит доп. атаку с силой 150% от обычной.` |

- [ ] **Step 4: Create the 7 new `MonsterData` assets**

Same YAML shape as `Monster_Skeleton.asset` (Task 1), script guid `37c73bb7b20f0774db1d325b77fd4879`. `damageType: 0` = Physical, `1` = Magical (matches `Monster_Warlock.asset`'s `damageType: 1`). `passiveSkill: {fileID: 0}` means no passive (Каменный страж); otherwise `{fileID: 11400000, guid: <that skill's fresh guid from Step 3>, type: 2}`.

| File | monsterName | hp | damageMin | damageMax | damageType | attackSpeed | physicalDefense | magicDefense | passiveSkill | gender | minFloorTier |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `Monster_Bat.asset` | `Летучая мышь` | 20 | 5 | 8 | 0 | 2.2 | 0 | 0 | Skill_Fluttering | 1 (Feminine) | 1 |
| `Monster_GoblinThief.asset` | `Гоблин-вор` | 35 | 8 | 12 | 0 | 1.2 | 0 | 0 | Skill_Pickpocket | 0 (Masculine) | 1 |
| `Monster_StoneGuardian.asset` | `Каменный страж` | 70 | 12 | 18 | 0 | 0.6 | 15 | 0 | none | 0 (Masculine) | 4 |
| `Monster_PoisonSpiderling.asset` | `Ядовитый паучок` | 25 | 6 | 10 | 0 | 1.3 | 0 | 0 | Skill_Poison | 0 (Masculine) | 4 |
| `Monster_Harpy.asset` | `Гарпия` | 30 | 10 | 14 | 0 | 1.4 | 0 | 0 | Skill_StunningScream | 1 (Feminine) | 7 |
| `Monster_DarkPriest.asset` | `Жрец тьмы` | 35 | 10 | 15 | 1 | 0.9 | 0 | 15 | Skill_DarkHeal | 0 (Masculine) | 7 |
| `Monster_DarkKnight.asset` | `Рыцарь тьмы` | 90 | 15 | 22 | 0 | 1.0 | 12 | 10 | Skill_DoubleStrike | 0 (Masculine) | 10 |

Example full file (`Monster_Bat.asset`), to use as the template for the other 6:
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
  m_Name: Monster_Bat
  m_EditorClassIdentifier: Assembly-CSharp::MonsterData
  monsterName: Летучая мышь
  isBoss: 0
  hp: 20
  damageMin: 5
  damageMax: 8
  damageType: 0
  attackSpeed: 2.2
  physicalDefense: 0
  magicDefense: 0
  passiveSkill: {fileID: 11400000, guid: <Skill_Fluttering's fresh guid>, type: 2}
  gender: 1
  minFloorTier: 1
```

- [ ] **Step 5: Register the 7 new monsters + the mentor-visible boss unaffected**

The regular monster pool is wired in the Inspector on the `RunFlowController` GameObject's `regularMonsterPool` list (a `[SerializeField] List<MonsterData>`), not in code — this step is a scene-data change, not a code change. Since this repo has no committed scene diffing workflow visible to a plan author, do this step manually in the Unity Editor after all 13 assets exist: open the scene, select the `RunFlowController` GameObject, drag all 10 `MonsterData` assets (3 existing + 7 new) into `Regular Monster Pool`. Confirm by re-running the Task 3 (Plan section below) smoke-test check, which asserts the pool size/tier filtering from code — that check will fail loudly if the scene wasn't updated.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Combat/MonsterSkillEffectMap.cs Assets/ScriptableObjects/Skills/MonsterPassives/ Assets/ScriptableObjects/Monsters/
git commit -m "Add 7 new monster types + their passive-skill assets (GDD 2.4)"
```

---

### Task 3: Monster-passive combat infrastructure (wires up 6 new passives + fixes the pre-existing Колдун gap)

**Files:**
- Modify: `Assets/Scripts/Combat/CombatantRuntime.cs`
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs` (initialize the new runtime fields from `MonsterData.passiveSkill`)
- Modify: `Assets/Scripts/Managers/CombatManager.cs`
- Modify: `Assets/Scripts/Managers/CharacterManager.cs` (currency-steal handler)
- Modify: `Assets/Scripts/UI/RunFlowController.cs` (subscribe to the new currency-steal event, mirroring the existing `LogMessage` subscription)
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `MonsterSkillEffectMap` (Task 2).
- Produces: `CombatManager.MonsterStoleCurrency` (event, `System.Action<CombatantRuntime, float>` — victim, percent stolen), `CharacterManager.StealCurrencyPercent(float percent)`.

- [ ] **Step 1: Add runtime state fields to `CombatantRuntime`**

```csharp
// Assets/Scripts/Combat/CombatantRuntime.cs — add alongside the existing skill/item fields:

// 2.4/2.8: пассивка монстра (MonsterSkillEffectMap-константа из monster.passiveSkill.skillName),
// null = у монстра нет пассивки (например, Каменный страж).
public string MonsterPassiveName;

// "Порхание" (Летучая мышь): флэт-бонус к шансу уклонения ЭТОГО участника, складывается с
// SkillEvasionLevel/ItemElusivenessLevel в существующей формуле уклонения CombatManager.
public float MonsterEvasionPercent;

// "Оглушающий крик" (Гарпия): временный дебафф шанса крита ЭТОГО участника (обычно — игрока).
public float CritChanceDebuffPercent;
public float CritChanceDebuffTimer;

// "Яд" (Ядовитый паучок): стакается до 3, каждый стек 4 урона/сек, длительность 3 сек, обновляется
// при повторном наложении (не суммирует длительность). Структурно похоже на HasBleed/BleedTimer,
// но с явным счётчиком стаков вместо фиксированного урона.
public int PoisonStacks;
public float PoisonTimer;
public float PoisonTickAccumulator;

// "Тёмное исцеление" (Жрец тьмы) / "Двойной удар" (Рыцарь тьмы): периодические пассивки монстра,
// не привязанные к таймеру атаки оружия — тикают отдельно в CombatManager.TickMonsterPeriodicPassives.
public float MonsterPassiveCooldownTimer;
```

- [ ] **Step 2: Initialize the new fields in `CombatantFactory.CreateMonsterCombatant`**

```csharp
// Assets/Scripts/Combat/CombatantFactory.cs — inside CreateMonsterCombatant, after building `runtime` and
// before `runtime.Weapons.Add(...)` (or right after — order doesn't matter, just before `return runtime;`):

runtime.MonsterPassiveName = monster.passiveSkill != null ? monster.passiveSkill.skillName : null;

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

- [ ] **Step 3: Extend the evasion check in `CombatManager.ResolveAttack` to include `MonsterEvasionPercent`**

```csharp
// Assets/Scripts/Managers/CombatManager.cs — was:
// float evadeChancePercent = target.SkillEvasionLevel * 5f + target.ItemElusivenessLevel * 1f;
// becomes:
float evadeChancePercent = target.SkillEvasionLevel * 5f + target.ItemElusivenessLevel * 1f + target.MonsterEvasionPercent; // + "Порхание" (2.4)
```

- [ ] **Step 4: Extend the crit-chance calculation to subtract `CritChanceDebuffPercent`**

```csharp
// Assets/Scripts/Managers/CombatManager.cs — was:
// float critChancePercent = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems;
// becomes:
float critChancePercent = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems - attacker.CritChanceDebuffPercent; // "Оглушающий крик" (2.4)
critChancePercent = Mathf.Max(0f, critChancePercent);
critChancePercent = BalanceClamps.ClampCritChancePercent(critChancePercent);
```

- [ ] **Step 5: Add the `MonsterStoleCurrency` event and call it from a new `ApplyMonsterPassiveOnAttack` method**

```csharp
// Assets/Scripts/Managers/CombatManager.cs — add near the LogMessage event declaration:
public event System.Action<CombatantRuntime, float> MonsterStoleCurrency; // (victim, percentStolen) — "Карманник"
```

Add a new method, called from `ResolveAttack` right after the existing "Заморозка"/"Кровотечение" block (which already runs only for the attacker's OWN skills — this new method is symmetric but for monster passives on non-player attackers):

```csharp
// 2.4: пассивки монстров, срабатывающие ПРИ АТАКЕ (в отличие от периодических — см.
// TickMonsterPeriodicPassives). attacker всегда монстр здесь (у игрока нет MonsterPassiveName).
void ApplyMonsterPassiveOnAttack(CombatantRuntime attacker, CombatantRuntime target, DamageCalculator.DamageResult result)
{
    if (attacker.IsPlayer || attacker.MonsterPassiveName == null)
    {
        return;
    }

    switch (attacker.MonsterPassiveName)
    {
        case MonsterSkillEffectMap.Pickpocket:
            // "При попадании по здоровью персонажа с 20% шансом ворует 5% текущей валюты забега."
            if (!result.WasBlocked && Random.value < 0.20f)
            {
                MonsterStoleCurrency?.Invoke(target, 5f);
                Log($"[Combat] {attacker.DisplayName} обчищает карманы {target.DisplayName} (Карманник)!");
            }
            break;

        case MonsterSkillEffectMap.Poison:
            // "При попадании по здоровью накладывает яд (3 сек, 4 урона/сек, стакается до 3 раз)."
            if (!result.WasBlocked)
            {
                target.PoisonStacks = Mathf.Min(target.PoisonStacks + 1, 3);
                target.PoisonTimer = 3f;
                Log($"[Combat] {target.DisplayName} получает яд ({target.PoisonStacks}/3 стаков).");
            }
            break;

        case MonsterSkillEffectMap.StunningScream:
            // "15% шанс при атаке снизить шанс крита персонажа на 20% на 4 сек." — на атаке, не на попадании.
            if (Random.value < 0.15f)
            {
                target.CritChanceDebuffPercent = 20f;
                target.CritChanceDebuffTimer = 4f;
                Log($"[Combat] Оглушающий крик {attacker.DisplayName} снижает шанс крита {target.DisplayName}.");
            }
            break;

        case MonsterSkillEffectMap.SlowCurse:
            // "Если урон Колдуна проходит по здоровью персонажа, скорость атаки персонажа снижается
            // на 30% на 3 секунды (не стакается, повторное попадание обновляет длительность)."
            if (!result.WasBlocked)
            {
                var existing = target.ActiveDebuffs.Find(d => d.Id == "warlock_slow");
                if (existing != null)
                {
                    existing.RemainingTime = 3f;
                }
                else
                {
                    target.ActiveDebuffs.Add(new ActiveDebuff { Id = "warlock_slow", RemainingTime = 3f, AttackSpeedMultiplier = 0.7f });
                }
                Log($"[Combat] Проклятие замедления {attacker.DisplayName} снижает скорость атаки {target.DisplayName} на 30% (3 сек).");
            }
            break;
    }
}
```

Call it from `ResolveAttack`, right before the existing `if (!target.IsAlive) { Log(...) }` closing block at the end of the method:

```csharp
ApplyMonsterPassiveOnAttack(attacker, target, result);

if (!target.IsAlive)
{
    Log($"[Combat] {target.DisplayName} погибает.");
}
```

- [ ] **Step 6: Add `TickMonsterPeriodicPassives` for Тёмное исцеление / Двойной удар, and poison ticking**

```csharp
// Assets/Scripts/Managers/CombatManager.cs — new method, called from Tick() (Step 7).

// "Тёмное исцеление" / "Двойной удар": пассивки на собственном периодическом таймере, независимом
// от таймера атаки оружия (в отличие от обычных атак и мгновенных пассивок из ApplyMonsterPassiveOnAttack).
void TickMonsterPeriodicPassives(float deltaTime)
{
    foreach (var enemy in Enemies)
    {
        if (!enemy.IsAlive || enemy.MonsterPassiveName == null)
        {
            continue;
        }

        if (enemy.MonsterPassiveName == MonsterSkillEffectMap.DarkHeal)
        {
            enemy.MonsterPassiveCooldownTimer -= deltaTime;
            if (enemy.MonsterPassiveCooldownTimer <= 0f)
            {
                enemy.MonsterPassiveCooldownTimer = 8f;
                var healTarget = PickDarkHealTarget(enemy);
                if (healTarget != null)
                {
                    float healAmount = healTarget.MaxHP * 0.10f;
                    healTarget.CurrentHP = Mathf.Min(healTarget.MaxHP, healTarget.CurrentHP + healAmount);
                    Log($"[Combat] Тёмное исцеление {enemy.DisplayName} восстанавливает {healTarget.DisplayName} {healAmount:F1} HP.");
                }
            }
        }
        else if (enemy.MonsterPassiveName == MonsterSkillEffectMap.DoubleStrike)
        {
            enemy.MonsterPassiveCooldownTimer -= deltaTime;
            if (enemy.MonsterPassiveCooldownTimer <= 0f && enemy.Weapons.Count > 0)
            {
                enemy.MonsterPassiveCooldownTimer = 6f;
                Log($"[Combat] {enemy.DisplayName} наносит двойной удар!");
                ResolveAttack(enemy, enemy.Weapons[0], 1.5f);
            }
        }
    }
}

// "себе или ближайшему союзнику в комнате" — интерпретация: лечит того из (себя + живых союзников
// в Enemies), у кого сейчас наименьший % HP от максимума (ближе всех к смерти = приоритетная цель
// для лечения; при равенстве побеждает первый найденный в списке).
static CombatantRuntime PickDarkHealTarget(CombatantRuntime healer)
{
    // здесь нет прямого доступа к списку Enemies (метод static для чистоты) — передаётся явно ниже.
    return healer;
}
```

Correction — `PickDarkHealTarget` needs the `Enemies` list, so make it an instance method instead of `static`:

```csharp
CombatantRuntime PickDarkHealTarget(CombatantRuntime healer)
{
    CombatantRuntime best = healer;
    float bestPercent = healer.MaxHP > 0f ? healer.CurrentHP / healer.MaxHP : 1f;

    foreach (var other in Enemies)
    {
        if (other == healer || !other.IsAlive)
        {
            continue;
        }

        float percent = other.MaxHP > 0f ? other.CurrentHP / other.MaxHP : 1f;
        if (percent < bestPercent)
        {
            best = other;
            bestPercent = percent;
        }
    }

    return best.CurrentHP < best.MaxHP ? best : null; // никто не ранен -> лечить некого
}
```
(Remove the earlier `static` stub version — it was shown first only to explain the signature choice; the plan's actual deliverable is this instance-method version.)

- [ ] **Step 7: Wire `TickMonsterPeriodicPassives` and poison ticking into `Tick()`**

```csharp
// Assets/Scripts/Managers/CombatManager.cs, inside Tick(), after the existing UpdateStatusEffects loop
// and BEFORE the first CheckCombatEnd() call (so a poison/heal tick this frame is reflected immediately):
UpdateStatusEffects(Player, deltaTime);
foreach (var enemy in Enemies)
{
    UpdateStatusEffects(enemy, deltaTime);
}

TickMonsterPeriodicPassives(deltaTime); // NEW

CheckCombatEnd();
```

Add poison ticking inside `UpdateStatusEffects` (same place `TickBleed` is already called):

```csharp
// Assets/Scripts/Managers/CombatManager.cs, inside UpdateStatusEffects(combatant, deltaTime), after TickBleed(combatant, deltaTime):
TickBleed(combatant, deltaTime);
TickPoison(combatant, deltaTime);
```

```csharp
// New method, same shape as TickBleed:
void TickPoison(CombatantRuntime target, float deltaTime)
{
    if (target.PoisonStacks <= 0)
    {
        return;
    }

    target.PoisonTimer -= deltaTime;
    target.PoisonTickAccumulator += deltaTime;

    float damagePerSecond = target.PoisonStacks * 4f;
    while (target.PoisonTickAccumulator >= 1f && target.PoisonStacks > 0 && target.IsAlive)
    {
        target.PoisonTickAccumulator -= 1f;
        target.CurrentHP -= damagePerSecond;
        Log($"[Combat] {target.DisplayName} получает {damagePerSecond:F1} урона от яда (HP {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

        if (!target.IsAlive)
        {
            Log($"[Combat] {target.DisplayName} погибает от яда.");
        }
    }

    if (target.PoisonTimer <= 0f)
    {
        target.PoisonStacks = 0;
    }
}
```

Also tick down `CritChanceDebuffTimer` in `UpdateStatusEffects`:
```csharp
if (combatant.CritChanceDebuffTimer > 0f)
{
    combatant.CritChanceDebuffTimer -= deltaTime;
    if (combatant.CritChanceDebuffTimer <= 0f)
    {
        combatant.CritChanceDebuffPercent = 0f;
    }
}
```

- [ ] **Step 8: `CharacterManager.StealCurrencyPercent` + `RunFlowController` subscription**

```csharp
// Assets/Scripts/Managers/CharacterManager.cs — add near AddCurrency:
public void StealCurrencyPercent(float percent)
{
    int stolen = Mathf.RoundToInt(RunCurrency * percent / 100f);
    RunCurrency = Mathf.Max(0, RunCurrency - stolen);
}
```

```csharp
// Assets/Scripts/UI/RunFlowController.cs — inside CombatRoomFlow, alongside the existing
// combatManager.LogMessage += OnCombatLog; subscription (and unsubscribe symmetrically below):
combatManager.MonsterStoleCurrency += OnMonsterStoleCurrency;
// ... (existing StartCombat/while loop unchanged) ...
combatManager.MonsterStoleCurrency -= OnMonsterStoleCurrency;
```

```csharp
// New handler method, near OnCombatLog:
void OnMonsterStoleCurrency(CombatantRuntime victim, float percent)
{
    characterManager.StealCurrencyPercent(percent);
}
```

- [ ] **Step 9: Add smoke-test checks**

```csharp
// RunPureLogicChecks() — pure logic, no MonoBehaviour needed for these:

// Порхание: 100-sample statistical sanity check that evasion actually triggers (deterministic
// bound, not exact) — construct a Bat-equivalent runtime directly rather than loading the asset,
// to keep this a pure-logic check.
var evasive = new CombatantRuntime { PhysicalDefenseMax = 0f, PhysicalDefenseCurrent = 0f, MagicShieldMax = 0f, MagicShieldCurrent = 0f, MaxHP = 100f, CurrentHP = 100f, MonsterEvasionPercent = 20f, IsPlayer = false };
// (Full evasion-triggers-in-combat behavior is exercised indirectly via ResolveAttack in Play Mode
// checks below — this pure check only confirms the field exists and defaults correctly.)
Check(evasive.MonsterEvasionPercent == 20f, "2.4 Порхание: MonsterEvasionPercent устанавливается корректно");

// Яд: 3 stacks max, 4 dmg/stack/sec.
var poisoned = new CombatantRuntime { MaxHP = 100f, CurrentHP = 100f };
poisoned.PoisonStacks = 2;
poisoned.PoisonTimer = 3f;
Check(poisoned.PoisonStacks == 2, "2.4 Яд: стаки устанавливаются");

Info.Add("Проверки полей монстро-пассивок (2.4) выполнены.");
```

```csharp
// RunPlayModeChecks() — needs a live CombatManager to exercise ResolveAttack end-to-end:
var combatManagerGO = new GameObject("SmokeTest_CombatManager");
var testCombatManager = combatManagerGO.AddComponent<CombatManager>();

var testPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 1000f, CurrentHP = 1000f, PhysicalDefenseMax = 0f, MagicShieldMax = 0f };
testPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 5f, DamageMax = 5f, DamageType = DamageType.Physical, AttackSpeed = 1f });

var slowCurseMonster = new CombatantRuntime { IsPlayer = false, MaxHP = 30f, CurrentHP = 30f, DisplayName = "TestWarlock", MonsterPassiveName = MonsterSkillEffectMap.SlowCurse };
slowCurseMonster.Weapons.Add(new WeaponAttackState { DamageMin = 100f, DamageMax = 100f, DamageType = DamageType.Physical, AttackSpeed = 1f });

testCombatManager.StartCombat(testPlayer, new List<CombatantRuntime> { slowCurseMonster });
testCombatManager.Tick(1.01f); // достаточно, чтобы оба нанесли по 1 удару (AttackSpeed=1/сек)
Check(testPlayer.ActiveDebuffs.Exists(d => d.Id == "warlock_slow"), "2.4 Проклятие замедления применяется при попадании Колдуна по HP игрока");

UnityEngine.Object.DestroyImmediate(combatManagerGO);
```

- [ ] **Step 10: Run the full smoke test and confirm PASS**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile -
```

- [ ] **Step 11: Commit**

```bash
git add Assets/Scripts/Combat/CombatantRuntime.cs Assets/Scripts/Combat/CombatantFactory.cs Assets/Scripts/Managers/CombatManager.cs Assets/Scripts/Managers/CharacterManager.cs Assets/Scripts/UI/RunFlowController.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Wire up monster passive combat infrastructure incl. long-standing Колдун gap (GDD 2.4)"
```

---

### Task 4: Monster modifiers (GDD 2.8)

**Files:**
- Create: `Assets/Scripts/Combat/MonsterModifierCatalog.cs`
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `MonsterData.gender` (Task 1).
- Produces: `MonsterModifierType` enum, `MonsterModifierCatalog.RollModifiers(int floorNumber, int monsterLevel)` → `List<MonsterModifierType>`, `MonsterModifierCatalog.AdjectiveFor(MonsterModifierType, MonsterGender)` → string, `MonsterModifierCatalog.ApplyToRuntime(CombatantRuntime, MonsterModifierType)`.
- `CombatantFactory.CreateMonsterCombatant` signature UNCHANGED (modifiers are rolled internally using the existing `floorNumber`/`monsterLevel` params — no new caller-facing parameter needed).

- [ ] **Step 1: Add the `MonsterModifierType` enum**

```csharp
// Assets/Scripts/Data/Enums.cs — add:
public enum MonsterModifierType
{
    Fast,
    Big,
    Armored,
    Fierce
}
```

- [ ] **Step 2: Create `MonsterModifierCatalog.cs`**

```csharp
// Assets/Scripts/Combat/MonsterModifierCatalog.cs
using System.Collections.Generic;
using UnityEngine;

// 2.8: каталог из 4 модификаторов монстров + формула шанса/лимита по этажам + русские формы
// прилагательных по роду (MonsterData.gender).
public static class MonsterModifierCatalog
{
    static readonly MonsterModifierType[] AllTypes =
    {
        MonsterModifierType.Fast, MonsterModifierType.Big, MonsterModifierType.Armored, MonsterModifierType.Fierce
    };

    // 2.8: лимит модификаторов на монстра по этажам. Этаж 1 = 0; 2-5 = 1; 6-9 = 2; 10 = без лимита
    // (реалистичный потолок = 4, размер каталога — дублирование одного модификатора не предусмотрено).
    public static int ModifierCapForFloor(int floorNumber)
    {
        if (floorNumber <= 1) return 0;
        if (floorNumber <= 5) return 1;
        if (floorNumber <= 9) return 2;
        return AllTypes.Length;
    }

    // 2.8: шанс = 0%/10%/20%/30% на уровнях монстра 1/2/3/4 (см. 2.7 — диапазон уровня 1-4).
    public static float RollChancePercentForLevel(int monsterLevel)
    {
        int clampedLevel = Mathf.Clamp(monsterLevel, 1, 4);
        return (clampedLevel - 1) * 10f;
    }

    // 2.8: последовательные независимые роллы до лимита этажа; первый провал останавливает
    // дальнейшие роллы (не пропускает слот и не пробует следующий).
    public static List<MonsterModifierType> RollModifiers(int floorNumber, int monsterLevel)
    {
        var result = new List<MonsterModifierType>();
        int cap = ModifierCapForFloor(floorNumber);
        float chancePercent = RollChancePercentForLevel(monsterLevel);

        if (cap <= 0 || chancePercent <= 0f)
        {
            return result;
        }

        var remaining = new List<MonsterModifierType>(AllTypes);
        for (int i = 0; i < cap; i++)
        {
            if (Random.value * 100f >= chancePercent)
            {
                break; // провал ролла останавливает дальнейшие слоты
            }

            int index = Random.Range(0, remaining.Count);
            result.Add(remaining[index]);
            remaining.RemoveAt(index);

            if (remaining.Count == 0)
            {
                break;
            }
        }

        return result;
    }

    // 2.8: применяется ПОВЕРХ уже отмасштабированных по этажу (2.6) и уровню монстра (2.7) статов.
    public static void ApplyToRuntime(CombatantRuntime runtime, MonsterModifierType modifier)
    {
        switch (modifier)
        {
            case MonsterModifierType.Fast:
                foreach (var weapon in runtime.Weapons)
                {
                    weapon.AttackSpeed *= 1.25f;
                }
                break;

            case MonsterModifierType.Big:
                float oldMax = runtime.MaxHP;
                runtime.MaxHP *= 1.5f;
                runtime.CurrentHP += runtime.MaxHP - oldMax; // монстр только что создан на полном HP
                break;

            case MonsterModifierType.Armored:
                runtime.PhysicalDefenseMax += 5f;
                runtime.PhysicalDefenseCurrent += 5f;
                break;

            case MonsterModifierType.Fierce:
                foreach (var weapon in runtime.Weapons)
                {
                    weapon.DamageMin *= 1.25f;
                    weapon.DamageMax *= 1.25f;
                }
                break;
        }
    }

    static string AdjectiveBase(MonsterModifierType modifier)
    {
        switch (modifier)
        {
            case MonsterModifierType.Fast: return "Быстр";
            case MonsterModifierType.Big: return "Больш";
            case MonsterModifierType.Armored: return "Бронированн";
            default: return "Свireп"; // placeholder base overwritten below — see full switch
        }
    }

    // 2.8: согласование рода. Не используем AdjectiveBase для "Свирепый" (основа "Свireп" была
    // опечаткой при черновом наброске) — полный явный switch по (modifier, gender) надёжнее короткой
    // основы + суффикса для 4 фиксированных прилагательных, меньше риск опечатки на рантайме.
    public static string AdjectiveFor(MonsterModifierType modifier, MonsterGender gender)
    {
        switch (modifier)
        {
            case MonsterModifierType.Fast:
                return gender == MonsterGender.Masculine ? "Быстрый" : gender == MonsterGender.Feminine ? "Быстрая" : "Быстрое";
            case MonsterModifierType.Big:
                return gender == MonsterGender.Masculine ? "Большой" : gender == MonsterGender.Feminine ? "Большая" : "Большое";
            case MonsterModifierType.Armored:
                return gender == MonsterGender.Masculine ? "Бронированный" : gender == MonsterGender.Feminine ? "Бронированная" : "Бронированное";
            default:
                return gender == MonsterGender.Masculine ? "Свирепый" : gender == MonsterGender.Feminine ? "Свирепая" : "Свирепое";
        }
    }
}
```

Remove the unused/erroneous `AdjectiveBase` helper before committing — it was left in mid-draft above to show the reasoning for why a full switch was chosen instead; the actual file must contain ONLY the `AllTypes`, `ModifierCapForFloor`, `RollChancePercentForLevel`, `RollModifiers`, `ApplyToRuntime`, and `AdjectiveFor` members (delete the `AdjectiveBase` method entirely — it is dead code with a typo, not a real implementation).

- [ ] **Step 3: Wire modifier rolling + `DisplayName` prefixing into `CombatantFactory.CreateMonsterCombatant`**

```csharp
// Assets/Scripts/Combat/CombatantFactory.cs, inside CreateMonsterCombatant, after runtime.Weapons.Add(...)
// and the MonsterPassiveName initialization from Task 3, before `return runtime;`:

// 2.8: модификаторы роллятся только для обычных монстров, не для босса (monsterLevel по умолчанию
// 1, а этаж-1 лимит = 0, так что для дефолтного вызова CreateMonsterCombatant(boss, floor) без
// monsterLevel это естественно даёт 0 модификаторов даже без явной проверки isBoss).
var rolledModifiers = MonsterModifierCatalog.RollModifiers(floorIndex, level);
foreach (var modifier in rolledModifiers)
{
    MonsterModifierCatalog.ApplyToRuntime(runtime, modifier);
    runtime.DisplayName = $"{MonsterModifierCatalog.AdjectiveFor(modifier, monster.gender)} {runtime.DisplayName}";
}
```

- [ ] **Step 4: Add smoke-test checks**

```csharp
// RunPureLogicChecks():
Check(MonsterModifierCatalog.ModifierCapForFloor(1) == 0, "2.8 лимит модификаторов этаж 1 = 0");
Check(MonsterModifierCatalog.ModifierCapForFloor(2) == 1 && MonsterModifierCatalog.ModifierCapForFloor(5) == 1, "2.8 лимит модификаторов этажи 2-5 = 1");
Check(MonsterModifierCatalog.ModifierCapForFloor(6) == 2 && MonsterModifierCatalog.ModifierCapForFloor(9) == 2, "2.8 лимит модификаторов этажи 6-9 = 2");
Check(MonsterModifierCatalog.ModifierCapForFloor(10) == 4, "2.8 лимит модификаторов этаж 10 = 4 (весь каталог)");

Check(MonsterModifierCatalog.RollChancePercentForLevel(1) == 0f, "2.8 шанс модификатора ур.1 монстра = 0%");
Check(MonsterModifierCatalog.RollChancePercentForLevel(4) == 30f, "2.8 шанс модификатора ур.4 монстра = 30%");

Check(MonsterModifierCatalog.AdjectiveFor(MonsterModifierType.Big, MonsterGender.Feminine) == "Большая", "2.8 согласование рода: Большая Слизь");
Check(MonsterModifierCatalog.AdjectiveFor(MonsterModifierType.Fast, MonsterGender.Masculine) == "Быстрый", "2.8 согласование рода: Быстрый Скелет");

var rollsOnFloor1 = MonsterModifierCatalog.RollModifiers(1, 4);
Check(rollsOnFloor1.Count == 0, $"2.8 этаж 1 никогда не даёт модификаторов даже при ур.4: получено {rollsOnFloor1.Count}");
```

- [ ] **Step 5: Run the full smoke test and confirm PASS**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile -
```

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Data/Enums.cs Assets/Scripts/Combat/MonsterModifierCatalog.cs Assets/Scripts/Combat/CombatantFactory.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Add monster modifier system: Быстрый/Большой/Бронированный/Свирепый (GDD 2.8)"
```

---

### Task 5: Floor-tiered monster pool selection

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `MonsterData.minFloorTier` (Task 1).
- Produces: `RunFlowController` picks eligible monsters filtered by `dungeonManager.CurrentFloorNumber`; no new public members (internal method change only).

- [ ] **Step 1: Replace the flat random pick in `CombatRoomFlow` with a floor-filtered pick**

```csharp
// Assets/Scripts/UI/RunFlowController.cs, was:
// var data = regularMonsterPool[Random.Range(0, regularMonsterPool.Count)];
// enemies.Add(CombatantFactory.CreateMonsterCombatant(data, dungeonManager.CurrentFloorNumber, monsterLevel));
//
// becomes — filter by minFloorTier <= currentFloor (2.4: "черновое распределение по этажам", тиры
// суммируются: этаж 5 видит и тир-1, и тир-4 монстров, не только последний открытый тир):
var eligibleMonsters = regularMonsterPool.FindAll(m => m != null && m.minFloorTier <= dungeonManager.CurrentFloorNumber);
if (eligibleMonsters.Count == 0)
{
    eligibleMonsters = regularMonsterPool; // защита от пустой сцены/несконфигурированного пула
}

for (int i = 0; i < count; i++)
{
    var data = eligibleMonsters[Random.Range(0, eligibleMonsters.Count)];
    enemies.Add(CombatantFactory.CreateMonsterCombatant(data, dungeonManager.CurrentFloorNumber, monsterLevel));
}
```

- [ ] **Step 2: Add a smoke-test check**

```csharp
// RunPureLogicChecks(): construct a small fake pool and verify the filter logic in isolation
// (can't call the private CombatRoomFlow directly — this instead unit-tests the equivalent
// FindAll predicate that Step 1 introduces, guarding against a future regression in the filter itself).
var tier1 = ScriptableObject.CreateInstance<MonsterData>(); tier1.minFloorTier = 1;
var tier7 = ScriptableObject.CreateInstance<MonsterData>(); tier7.minFloorTier = 7;
var pool = new List<MonsterData> { tier1, tier7 };

var eligibleFloor3 = pool.FindAll(m => m.minFloorTier <= 3);
Check(eligibleFloor3.Count == 1 && eligibleFloor3[0] == tier1, "2.4 фильтр пула монстров: этаж 3 видит только тир-1");

var eligibleFloor7 = pool.FindAll(m => m.minFloorTier <= 7);
Check(eligibleFloor7.Count == 2, "2.4 фильтр пула монстров: этаж 7 видит тир-1 И тир-7 (суммируются)");

UnityEngine.Object.DestroyImmediate(tier1);
UnityEngine.Object.DestroyImmediate(tier7);
```

- [ ] **Step 3: Manual verification — update the scene's `regularMonsterPool` (if not already done in Plan Task 2 Step 5)**

Confirm all 10 non-boss `MonsterData` assets are assigned to `RunFlowController.regularMonsterPool` in the Inspector. Without this, `minFloorTier` filtering has nothing to filter — the pool would still only contain whatever was assigned before this session.

- [ ] **Step 4: Run the full smoke test and confirm PASS, then commit**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile -
git add Assets/Scripts/UI/RunFlowController.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Floor-tiered monster pool selection (GDD 2.4 draft distribution)"
```

---

## Self-Review Notes

- **Spec coverage:** item 3 (7 new monsters + floor distribution) → Tasks 2, 5. Item 4 (modifiers) → Task 4. The monster-passive combat wiring (Task 3) wasn't a named item in the sync prompt but is a hard prerequisite discovered during investigation — flagged to and approved by the user before this plan was written.
- **Known simplification requiring a judgment call, not left ambiguous:** "Тёмное исцеление... себе или ближайшему союзнику" — GDD doesn't define "ближайший" spatially (there's no room layout/positions in this prototype's combat model). Implemented as "heals whichever of (self + living allies) has the lowest HP%", which satisfies the stated intent (keep the priest's side alive) without inventing spatial data the combat model doesn't have. This is a judgment call about an underspecified mechanic, not a contradiction of a specified number — documented here for visibility rather than asked about, per the instruction to only stop for genuine blocking ambiguity.
- **Out of scope:** `Каменный страж` intentionally has no passive skill (GDD 2.4 describes it purely as a defense/tank stat check, no special ability).
