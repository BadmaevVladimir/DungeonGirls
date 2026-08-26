# Rogue + Barbarian Classes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two new playable character classes (Плут/Rogue, Варвар/Barbarian) — their base stats, exclusive equipment archetypes/slots, class skill pools, unique passive/active skills — plus a new general damage-resistance mechanic they both depend on, and import the designer's art for both.

**Architecture:** Follow the existing Jennifer/Warrior pattern exactly: `CharacterData`/`ItemData`/`PassiveSkillData`/`ActiveSkillData` ScriptableObject assets (hand-authored, no CreateAssetMenu-via-Editor-UI — see Task 3 for why this plan uses a batchmode generator script instead of hand-written YAML this time), combat behavior lives in `CombatantRuntime`/`WeaponAttackState`/`CombatManager`, class-gating uses the existing `ItemData.allowedClasses` field. The new damage-resistance mechanic is a single extra step in `DamageCalculator`, applied before existing armor/shield math — not a modification of that math.

**Tech Stack:** Unity 6000.5.8f1, C#, UI Toolkit (unaffected by this plan — no new screens). Testing via the project's existing `Assets/Editor/PlayModeSmokeTest.cs` batchmode convention (see `[[project_dungeongirls]]` memory for the exact run command — **never add `-quit`**).

**Spec:** This plan's spec is the live Notion GDD, section 3.11 "Новые классы" (both "Плут" and "Варвар" subsections) and the resistance-mechanic text embedded in 3.11's "Суеверность"/"Берсерк" entries (NOT section 3.3, despite what the originating prompt said — 3.3 itself is unchanged; verified by direct fetch 2026-08-27). The user-supplied prompt is a paraphrase and has two confirmed inaccuracies the plan corrects: (1) Плут CANNOT equip Щит — a later GDD correction removes it from Rogue's allowed list despite the prompt saying rings/shield are available; (2) the resistance mechanic lives in 3.11, not 3.3. Item mechanics not spelled out in the prompt (Клинок's Rare/Epic tier bonuses) are filled in from the direct GDD read below.

## Global Constraints

- Item main-stat level scaling uses `StatScaling.ApplyLevelBonus` (min +1/level rule) — ONLY for the primary stat (damage/defense), never for bonusStat/passives (those scale linearly as `baseValue × itemLevel`, see existing `CombatantFactory.AggregateEquipmentStats`).
- Item tier multipliers (3.10, already implemented as pre-baked balance numbers in each asset, not computed in code): Common ×1.0, Rare ×1.5, Epic ×2.2. Epic always inherits the SAME bonusStat as Rare in its line (general 3.10 rule) — apply this to Клинок's Rare "3% armor-ignore/level" bonus for Epic too, even though the GDD paragraph for "Моменто Мори" doesn't re-state it (covered by the general rule, not an open question).
- Stealth ("Скрытность") duration is always exactly 3 seconds everywhere it's granted/refreshed — never stacks, always resets the timer.
- All new class-skill/unique-skill numeric tables below are copied verbatim from the direct 2026-08-27 GDD fetch (see conversation) — do not re-derive them from the user's paraphrase if the two ever disagree; the GDD text quoted in each task is authoritative.
- Confirmed GDD correction: **Плут cannot equip Щит** (Shield) — added to the "cannot wear" list in a same-session designer correction, alongside Броня/Шлем/Топор/Молот/Копьё. Rings, accessories, boots, swords, blades remain available.
- Smoke test command (run after every task, Unity Editor must be closed first):
  ```
  "C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile -
  ```
  Never add `-quit`. Check `Get-Process -Name Unity` first; ask the user to close their Editor if it's open.
- **Two explicit open questions for the designer — implemented with a stated default, flagged, not silently resolved:**
  1. Barbarian "Берсерк" self-damage ("1% health/sec, min 1 HP") — GDD itself marks this `[ПРЕДПОЛОЖЕНИЕ: требует подтверждения]` for current-vs-max HP. This plan implements it as **1% of CURRENT HP per second, minimum 1 flat HP**, per the user's own prompt wording — but this is a guess the GDD explicitly hasn't confirmed. Report this prominently.
  2. Which of `Sasha.png`/`Violet.png` (both freshly added to `Assets/Art/Characters/`, no file→class mapping table was actually delivered despite the prompt saying one would be) is Rogue vs. Barbarian is **not knowable from the repo** — Task 8 leaves both `CharacterData.portrait` fields unassigned (falls back to placeholder per 3.8) rather than guessing wrong.

---

## File Structure

**New files:**
- `Assets/Scripts/Combat/StealthStatus.cs` — tiny data holder for Rogue's Stealth timer (mirrors `ActiveDebuff`'s shape).
- `Assets/Editor/RogueBarbarianContentGenerator.cs` — one-shot batchmode Editor script that creates all 39 new ScriptableObject assets (18 items, 5 item-passives, 10 class skills, 4 unique skills, 2 characters) via `AssetDatabase.CreateAsset`, run once via `-executeMethod`, then left in the repo as a record of the generated GUIDs (does not re-run destructively — see Task 3 for why this replaces the usual hand-authored-YAML convention for this volume).

**Modified files:**
- `Assets/Scripts/Data/Enums.cs` — `CharacterClass.Rogue`/`Barbarian`, `WeaponSubtype.Blade`/`TwoHandedAxe`, `BonusStatType.ArmorIgnorePercent`.
- `Assets/Scripts/Data/ItemData.cs` — `isTwoHanded` bool field.
- `Assets/Scripts/Combat/WeaponAttackState.cs` — `ArmorIgnorePercent` field (per-weapon, like `ArmorPenetrationFlat`).
- `Assets/Scripts/Combat/CombatantRuntime.cs` — Stealth status, Rage-dependent runtime fields, resistance fields, crit-override fields, poison-from-player tracking (separate from monster poison).
- `Assets/Scripts/Combat/DamageCalculator.cs` — resistance step + `armorIgnorePercent` param on `ApplyPhysicalDamage`.
- `Assets/Scripts/Managers/CombatManager.cs` — Stealth grant/refresh points, Rogue skill hooks, Barbarian skill hooks, crit-chance override redirect for "Чемпион племени", Berserk self-damage tick.
- `Assets/Scripts/Combat/CombatantFactory.cs` — Rage isn't stored (computed property, see `CombatantRuntime.Rage`), so no change here beyond what class-skill level copying already does generically; verify class-skill fields for Rogue/Barbarian are copied in `ApplyCharacterSkills`.
- `Assets/Scripts/Managers/CharacterManager.cs` — two-handed equip-replacement exception (3.4).
- `Assets/Scripts/Combat/SkillEffectMap.cs` and `Assets/Scripts/Combat/MonsterSkillEffectMap.cs`-equivalent (whichever file holds skill-name string constants — confirmed `SkillEffectMap.cs`) — new constants for all 10 class skills + 4 unique skills + 5 item passives.
- `Assets/Editor/PlayModeSmokeTest.cs` — new checks per task.

---

## Task 1: Enums, ItemData/WeaponAttackState fields, CombatantRuntime scaffolding

**Files:**
- Modify: `Assets/Scripts/Data/Enums.cs`
- Modify: `Assets/Scripts/Data/ItemData.cs`
- Modify: `Assets/Scripts/Combat/WeaponAttackState.cs`
- Create: `Assets/Scripts/Combat/StealthStatus.cs`
- Modify: `Assets/Scripts/Combat/CombatantRuntime.cs`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Produces: `CharacterClass.Rogue`, `CharacterClass.Barbarian`; `WeaponSubtype.Blade`, `WeaponSubtype.TwoHandedAxe`; `BonusStatType.ArmorIgnorePercent`; `ItemData.isTwoHanded : bool`; `WeaponAttackState.ArmorIgnorePercent : float`; `CombatantRuntime.IsStealthed : bool`, `CombatantRuntime.StealthTimer : float`, `CombatantRuntime.Rage : float` (computed property), `CombatantRuntime.PhysicalResistancePercent : float`, `CombatantRuntime.MagicalResistancePercent : float`, `CombatantRuntime.CritChanceOverridePercent : float?` (null = no override), `CombatantRuntime.CritChanceOverrideConversionActive : bool`.

- [ ] **Step 1: Add new enum values**

In `Assets/Scripts/Data/Enums.cs`, extend the three enums (append only — never reorder/renumber existing entries, Unity serializes enums by ordinal and this would silently corrupt every existing `.asset` file's `characterClass`/`weaponSubtype`/`bonusStat.type` value):

```csharp
public enum CharacterClass
{
    Warrior,
    Mage,
    Rogue,
    Barbarian
}
```

```csharp
public enum WeaponSubtype
{
    None,
    Sword,
    Axe,
    Spear,
    Hammer,
    Shield,
    Blade,
    TwoHandedAxe
}
```

```csharp
public enum BonusStatType
{
    None,
    CritChancePercent,
    ArmorPenetrationFlat,
    AttackSpeedPercent,
    DamagePercent,
    FlatHP,
    MaxPhysicalDefenseFlat,
    MagicShieldFlat,
    WeaponDamageFlat,
    EvasionPercent,
    ArmorIgnorePercent
}
```

- [ ] **Step 2: Add `isTwoHanded` to ItemData**

In `Assets/Scripts/Data/ItemData.cs`, add near `weaponSubtype`:

```csharp
// 3.11: двуручное оружие (Варвар) занимает ОБА слота оружия/рук как единый предмет — при
// экипировке заменяет оба текущих предмета в слотах рук одновременно (исключение из обычной
// независимой логики слотов 3.4, см. CharacterManager.EquipItem). Only one variant exists today
// (Двуручный топор, weaponSubtype = TwoHandedAxe) but the GDD explicitly leaves room for more.
public bool isTwoHanded;
```

- [ ] **Step 3: Add `ArmorIgnorePercent` to WeaponAttackState**

In `Assets/Scripts/Combat/WeaponAttackState.cs`, add alongside `ArmorPenetrationFlat`:

```csharp
// 3.11 (Клинок, Зазубренный клинок/Моменто Мори): игнорирует N% ТЕКУЩЕЙ брони цели при расчёте
// пробития этим конкретным оружием (см. DamageCalculator.ApplyPhysicalDamage armorIgnorePercent
// param) — отличается от ArmorPenetrationFlat (Топор/Молот, флэт-добавка к урону).
public float ArmorIgnorePercent;
```

- [ ] **Step 4: Create StealthStatus data holder**

Create `Assets/Scripts/Combat/StealthStatus.cs`:

```csharp
// 3.11 (Плут): "Скрытность" — булевое состояние с таймером, ВСЕГДА 3 секунды при
// накладывании/обновлении, не стакается. У самой Скрытности нет базового эффекта — только
// условие, которое проверяют навыки (см. CombatManager). Отдельный класс не нужен по факту (это
// два поля), но выделен для читаемости мест, где Скрытность накладывается/обновляется/тикает.
public static class StealthStatus
{
    public const float DurationSeconds = 3f;
}
```

- [ ] **Step 5: Add Rogue/Barbarian runtime fields to CombatantRuntime**

In `Assets/Scripts/Combat/CombatantRuntime.cs`, add:

```csharp
// 3.11 (Плут) — "Скрытность": булево состояние + таймер, длительность всегда 3с (StealthStatus).
// Персонаж начинает бой БЕЗ Скрытности (не устанавливается здесь, только объявлено — StartCombat
// в CombatManager не трогает эти поля, они остаются false/0 по умолчанию у нового CombatantRuntime).
public bool IsStealthed;
public float StealthTimer;

// 3.11 (Плут, классовые навыки) — уровни известны только игроку, копируются в
// CombatantFactory.ApplyCharacterSkills так же, как остальные Skill*Level поля.
public int SkillEyeForAnEyeLevel; // "В глаз"
public int SkillPoisonedBladeLevel; // "Отравленный клинок"
public int SkillByAThreadLevel; // "На волоске"
public int SkillEliminationLevel; // "Устранение" — переопределяет крит-множитель, см. CritDamageMultiplierPercent
public int SkillSlipAwayLevel; // "Ускользание"
public int UniqueShadowLevel; // пассивка "Тень" (только Плут)

// 3.11 (Плут) — "Отравленный клинок" накладывает СВОЙ яд на цель, отдельная сущность от
// монстрового PoisonStacks/PoisonTimer (Ядовитый паучок, 2.4) — не суммируются, тикают независимо.
public int RoguePoisonStacksOnTarget; // хранится на ЦЕЛИ (симметрично PoisonStacks у монстров)
public float RoguePoisonTimer;
public float RoguePoisonTickAccumulator;

// 3.11 (Устранение) — переопределяет базовый крит-множитель 150% (см. CombatManager.ResolveAttack
// `damage *= 1.5f`), null = нет навыка, используется база. Аналогичный паттерн для Barbarian ниже.
public float? CritDamageMultiplierOverridePercent;

// 3.11 (Варвар) — классовые навыки, копируются так же, как Rogue-поля выше.
public int SkillStubbornnessLevel; // "Упёртость"
public int SkillFrenzyLevel; // "Остервенелость" — общий индекс X (0.7/0.75/0.8/0.9/1.0) для 3 навыков
public int SkillCombatRegenLevel; // "Боевая регенерация"
public int SkillIntimidationLevel; // "Запугивание"
public int SkillSuperstitionLevel; // "Суеверность"
public int UniqueChampionOfTheTribeLevel; // пассивка "Чемпион племени"

// 3.11 (Боевая регенерация) — счётчик полученных ударов по HP, сбрасывается при срабатывании.
public int HitsTakenSinceLastRegen;

// 3.11 (Берсерк) — ручной тумблер, не кулдаун-навык. Уровень 0 = навык не изучен = тумблер
// недоступен (UI должен это учитывать так же, как ActiveSkillButton.SetEnabled для обычных
// активок, см. RunFlowController).
public int UniqueBerserkLevel;
public bool IsBerserkActive;
public float BerserkTickAccumulator;

// 3.11 (Часть 2, "% сопротивления урону") — общая механика, применяется в DamageCalculator ДО
// брони/щита. Суммируется, если несколько источников одного типа (сейчас только один источник на
// тип у Варвара — Суеверность даёт магическое, Берсерк даёт физическое — но поле уже суммируемое
// на будущее). Кламп на 100% — см. DamageCalculator.
public float PhysicalResistancePercent;
public float MagicalResistancePercent;

// 3.11 (Чемпион племени, Варвар) — крит-шанс ВСЕГДА равен Rage×X%, полностью заменяя обычную
// формулу; остальные источники крит-шанса конвертируются 1%→+2% крит-урона вместо суммирования в
// шанс. true только если навык изучен (уровень > 0).
public bool CritChanceReplacedByRage;

public bool HasActiveDebuff => ActiveDebuffs.Count > 0 || IsFrozen || FreezeStacks > 0;

// 3.11 (Варвар) — "Ярость" = % недостающего HP, ПЕРЕСЧИТЫВАЕТСЯ динамически (не хранимое поле).
// Флэт-бонусы (Пояс титана) добавляются здесь же поверх формулы — могут увести Rage выше 100%.
public float RageFlatBonusPercent;
public float Rage => MaxHP > 0f
    ? Mathf.Max(0f, (1f - Mathf.Clamp01(CurrentHP / MaxHP)) * 100f + RageFlatBonusPercent)
    : 0f;
```

Note: `HasActiveDebuff` already exists in the file — this step only ADDS the new fields around it, don't duplicate the property.

- [ ] **Step 6: Compile check**

Run the smoke test command from Global Constraints. Expected: `RESULT=PASS` with the same OK count as before this task (no behavior changed yet, only new unused fields/enum values) — confirms the project still compiles.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Data/Enums.cs Assets/Scripts/Data/ItemData.cs Assets/Scripts/Combat/WeaponAttackState.cs Assets/Scripts/Combat/StealthStatus.cs Assets/Scripts/Combat/CombatantRuntime.cs
git commit -m "Add Rogue/Barbarian data-model scaffolding (GDD 3.11, no behavior yet)"
```

---

## Task 2: Damage-resistance mechanic (GDD Part 2 / 3.11 "Суеверность"/"Берсерк")

**Files:**
- Modify: `Assets/Scripts/Combat/DamageCalculator.cs`
- Modify: `Assets/Scripts/Managers/CombatManager.cs`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `CombatantRuntime.PhysicalResistancePercent`, `CombatantRuntime.MagicalResistancePercent` (Task 1).
- Produces: `DamageCalculator.ApplyPhysicalDamage(CombatantRuntime target, float incomingDamage, float armorIgnorePercent = 0f)` (new optional param, existing 2-arg callers unaffected), `DamageCalculator.ApplyDamage(...)` now applies resistance as the first step for both damage types.

- [ ] **Step 1: Add the resistance step + armorIgnorePercent param to DamageCalculator**

In `Assets/Scripts/Combat/DamageCalculator.cs`, replace `ApplyPhysicalDamage`'s signature and add the resistance pre-step to `ApplyDamage`:

```csharp
// 3.11 Часть 2 (НОВОЕ): armorIgnorePercent — только для Клинка (Зазубренный/Моменто Мори,
// см. WeaponAttackState.ArmorIgnorePercent) — снижает ЭФФЕКТИВНУЮ броню для целей проверки
// блок/пробитие/износ, но НЕ влияет на то, сколько единиц брони теряется при пробитии (те
// правила остаются про АБСОЛЮТНУЮ броню, не эффективную).
public static DamageResult ApplyPhysicalDamage(CombatantRuntime target, float incomingDamage, float armorIgnorePercent = 0f)
{
    float effectiveDefense = target.PhysicalDefenseCurrent * (1f - Mathf.Clamp01(armorIgnorePercent / 100f));

    if (incomingDamage < effectiveDefense)
    {
        bool armorWorn = incomingDamage >= effectiveDefense * 0.5f;
        if (armorWorn)
        {
            target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - 1f);
        }

        return new DamageResult { DamageToHP = 0f, WasBlocked = true, ArmorWornOnBlock = armorWorn };
    }

    float remainder = incomingDamage - effectiveDefense;
    float armorLoss = incomingDamage >= effectiveDefense * 2f ? 2f : 1f;
    target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - armorLoss);
    target.CurrentHP -= remainder;

    return new DamageResult { DamageToHP = remainder, WasBlocked = false };
}
```

Then update `ApplyDamage` to apply resistance FIRST, before dispatching to physical/magical:

```csharp
// 3.11 Часть 2 (НОВОЕ): "% сопротивления урону" — общий множитель, первый шаг в цепочке расчёта,
// ДО брони/щита. Суммируется по всем источникам одного типа урона, клампится на 100% (0 урона
// дальше по цепочке, а не отрицательный урон).
public static DamageResult ApplyDamage(CombatantRuntime target, float incomingDamage, DamageType damageType, float armorIgnorePercent = 0f)
{
    float resistancePercent = damageType == DamageType.Physical ? target.PhysicalResistancePercent : target.MagicalResistancePercent;
    float damageAfterResistance = incomingDamage * (1f - Mathf.Clamp01(resistancePercent / 100f));

    return damageType == DamageType.Physical
        ? ApplyPhysicalDamage(target, damageAfterResistance, armorIgnorePercent)
        : ApplyMagicalDamage(target, damageAfterResistance);
}
```

- [ ] **Step 2: Wire the new `ApplyDamage` armorIgnorePercent param through CombatManager**

In `Assets/Scripts/Managers/CombatManager.cs`, `ResolveAttack` already computes `armorPenetrationDamage` from `weapon.ArmorPenetrationFlat` and calls `DamageCalculator.ApplyDamage(target, damage + armorPenetrationDamage, weapon.DamageType)`. Change that call to also pass the new Клинок-specific ignore percent:

```csharp
var result = DamageCalculator.ApplyDamage(target, damage + armorPenetrationDamage, weapon.DamageType, weapon.ArmorIgnorePercent);
```

(`weapon.ArmorIgnorePercent` defaults to 0 for every weapon except Клинок's Rare/Epic tiers — see Task 3 — so this is a no-op for all existing weapons.)

- [ ] **Step 3: Add smoke-test coverage for the resistance mechanic**

In `Assets/Editor/PlayModeSmokeTest.cs`, add near the other `DamageCalculator` checks (search for `3.3 обычное пробитие` to find the right neighborhood):

```csharp
// 3.11 Часть 2 (НОВОЕ): % сопротивления урону — первый шаг, до брони/щита.
var resistPhysicalTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 100f, CurrentHP = 100f, PhysicalResistancePercent = 50f };
var resistPhysicalResult = DamageCalculator.ApplyDamage(resistPhysicalTarget, 12f, DamageType.Physical); // 12 * 0.5 = 6 -> < 10 брони -> износ (>=5), не пробитие
Check(resistPhysicalResult.WasBlocked && resistPhysicalTarget.PhysicalDefenseCurrent == 9f,
    $"3.11 физ. сопротивление 50% снижает урон ДО брони: WasBlocked={resistPhysicalResult.WasBlocked}, Defense={resistPhysicalTarget.PhysicalDefenseCurrent} (ожидалось true/9, т.е. 12->6, износ не пробитие)");

var resistMagicalTarget = new CombatantRuntime { MagicShieldMax = 10f, MagicShieldCurrent = 10f, MaxHP = 100f, CurrentHP = 100f, MagicalResistancePercent = 50f };
var resistMagicalResult = DamageCalculator.ApplyDamage(resistMagicalTarget, 12f, DamageType.Magical); // 12 * 0.5 = 6, полностью гасится щитом (10)
Check(resistMagicalResult.WasBlocked && resistMagicalTarget.MagicShieldCurrent == 4f,
    $"3.11 маг. сопротивление 50% снижает урон ДО маг.щита: WasBlocked={resistMagicalResult.WasBlocked}, Shield={resistMagicalTarget.MagicShieldCurrent} (ожидалось true/4, т.е. 12->6, щит 10-6=4)");

// armorIgnorePercent (Клинок): снижает ЭФФЕКТИВНУЮ броню для проверки, но абсолютная деградация
// (-1/-2) остаётся по правилам 3.3 без изменений.
var armorIgnoreTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 100f, CurrentHP = 100f };
var armorIgnoreResult = DamageCalculator.ApplyDamage(armorIgnoreTarget, 6f, DamageType.Physical, armorIgnorePercent: 50f); // эфф. броня 5, урон 6 >= 5 -> обычное пробитие
Check(!armorIgnoreResult.WasBlocked && armorIgnoreResult.DamageToHP == 1f && armorIgnoreTarget.PhysicalDefenseCurrent == 9f,
    $"3.11 armorIgnorePercent 50% (Клинок): WasBlocked={armorIgnoreResult.WasBlocked}, DamageToHP={armorIgnoreResult.DamageToHP}, Defense={armorIgnoreTarget.PhysicalDefenseCurrent} (ожидалось false/1/9)");
```

- [ ] **Step 4: Run smoke test, verify the 4 new checks pass, all prior checks still pass**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Combat/DamageCalculator.cs Assets/Scripts/Managers/CombatManager.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Add generic %-damage-resistance mechanic, applied before armor/shield (GDD 3.11 Part 2)"
```

---

## Task 3: Content asset generator (18 items + 5 item-passives + 10 class skills + 4 unique skills + 2 characters)

**Why a generator script instead of hand-authored YAML:** every prior GDD-sync session in this project hand-wrote `.asset`/`.asset.meta` YAML pairs directly (see `[[project_dungeongirls]]` memory), because Unity's Editor UI (`CreateAssetMenu`) needs an interactive session this project doesn't have. That convention works for a handful of assets per session, but hand-inventing 39 GUIDs by hand for this task is exactly the kind of mechanical, error-prone work a script does more reliably — `AssetDatabase.CreateAsset` generates real, collision-free GUIDs automatically and can run in the SAME `-batchmode -executeMethod` invocation already used for the smoke test, no interactive Editor needed. This is a deliberate, one-time deviation from the hand-authored convention — flag it as such in the final report, don't treat it as the new default without asking.

**Files:**
- Modify: `Assets/Scripts/Combat/SkillEffectMap.cs` (or wherever skill-name string constants live — confirm exact file/class name by reading it first; the codebase's existing constants for Vampirism/ArmorBreak/Piercing/Repair/Elusiveness/GoldenTouch/ToughSole/Freeze/Luck/Evasion/etc. are the pattern to match).
- Create: `Assets/Editor/RogueBarbarianContentGenerator.cs`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `ItemData`/`PassiveSkillData`/`ActiveSkillData`/`CharacterData`/`BonusStat` (existing classes), `StatScaling` (existing, for reference only — the generator does NOT pre-apply level scaling, it writes base values exactly like every other item asset in the repo does).
- Produces: 39 new `.asset` files under `Assets/ScriptableObjects/{Items,Skills,Characters}/...` with real Unity-assigned GUIDs, plus new `SkillEffectMap` string constants for every new skill/passive name referenced by combat code in Tasks 4-6.

- [ ] **Step 1: Read the exact file holding skill-name constants**

Run `Grep` for `SkillEffectMap` to confirm the file path and the exact declaration pattern (e.g. `public const string Vampirism = "vampirism";` or similar) before writing new constants — match the existing naming/casing convention exactly, don't invent a different style.

- [ ] **Step 2: Add new SkillEffectMap constants**

Add one constant per new named skill (exact Russian display names come from the GDD quotes in this plan's tasks below; the constant identifiers are English, matching existing style):

```csharp
// 3.11: Плут — классовые навыки.
public const string EyeForAnEye = "eye_for_an_eye"; // "В глаз"
public const string PoisonedBlade = "poisoned_blade"; // "Отравленный клинок"
public const string ByAThread = "by_a_thread"; // "На волоске"
public const string Elimination = "elimination"; // "Устранение"
public const string SlipAway = "slip_away"; // "Ускользание"
public const string Shadow = "shadow"; // уникальная пассивка "Тень"
public const string SmokeBomb = "smoke_bomb"; // уникальная активка "Дымовая граната"
public const string Riposte = "riposte"; // Капюшон Дуэльянта
public const string EmbraceOfNight = "embrace_of_night"; // Объятия ночи
public const string Execution = "execution"; // Моменто Мори, "Казнь"

// 3.11: Варвар — классовые навыки.
public const string Stubbornness = "stubbornness"; // "Упёртость"
public const string Frenzy = "frenzy"; // "Остервенелость"
public const string CombatRegen = "combat_regen"; // "Боевая регенерация"
public const string Intimidation = "intimidation"; // "Запугивание"
public const string Superstition = "superstition"; // "Суеверность"
public const string ChampionOfTheTribe = "champion_of_the_tribe"; // уникальная пассивка
public const string Berserk = "berserk"; // уникальная активка
public const string GiantSlayer = "giant_slayer"; // Головоруб
public const string JustAScratch = "just_a_scratch"; // Эпический трофей
```

(Use whatever the file's ACTUAL naming convention turns out to be from Step 1 — the above is illustrative; match reality, don't paste this blind if the real file uses a different casing/format.)

- [ ] **Step 3: Write the generator script**

Create `Assets/Editor/RogueBarbarianContentGenerator.cs`. This is long — write it in full, matching every number from this plan's Global Constraints and the task tables below exactly. Structure:

```csharp
using UnityEditor;
using UnityEngine;

// One-shot batchmode content generator for GDD 3.11 (Плут/Варвар). Run via:
// -executeMethod RogueBarbarianContentGenerator.Generate
// Idempotency: NOT idempotent by design (re-running creates duplicate assets at new paths if the
// originals were deleted, or throws if AssetDatabase.CreateAsset hits an existing path — this is
// intentional, a content generator is a one-time tool, not a build step). Delete the generated
// .asset files first if you need to regenerate.
public static class RogueBarbarianContentGenerator
{
    const string ItemsRoot = "Assets/ScriptableObjects/Items";
    const string SkillsRoot = "Assets/ScriptableObjects/Skills";
    const string CharactersRoot = "Assets/ScriptableObjects/Characters";

    public static void Generate()
    {
        EnsureFolder("Assets/ScriptableObjects/Items/Blades");
        EnsureFolder("Assets/ScriptableObjects/Items/Hoods");
        EnsureFolder("Assets/ScriptableObjects/Items/Leathers");
        EnsureFolder("Assets/ScriptableObjects/Items/TwoHandedAxes");
        EnsureFolder("Assets/ScriptableObjects/Items/Belts");
        EnsureFolder("Assets/ScriptableObjects/Items/Trophies");
        EnsureFolder("Assets/ScriptableObjects/Skills/Rogue");
        EnsureFolder("Assets/ScriptableObjects/Skills/Barbarian");
        EnsureFolder("Assets/ScriptableObjects/Skills/Unique");

        // ---- Item-passive skills (created first, referenced by item assets below) ----
        var riposte = CreatePassive("Skill_Riposte", "Рипост",
            "Первая атака после успешного уклонения наносит доп. флэт-урон, равный уровню капюшона.",
            5, "Assets/ScriptableObjects/Skills/Rogue");
        var embraceOfNight = CreatePassive("Skill_EmbraceOfNight", "Объятия ночи",
            "Все атаки во время Скрытности наносят доп. магический урон = УровеньПредмета × ОбычныйУронАтаки × 1%.",
            5, "Assets/ScriptableObjects/Skills/Rogue");
        var execution = CreatePassive("Skill_Execution", "Казнь",
            "Наносит физ. урон = 1% от недостающего HP противника за каждый уровень предмета.",
            5, "Assets/ScriptableObjects/Skills/Rogue");
        var giantSlayer = CreatePassive("Skill_GiantSlayer", "Убийца великанов",
            "Если макс. HP противника больше макс. HP Варвара — оружие наносит +5% урона за каждый уровень предмета против этой цели.",
            5, "Assets/ScriptableObjects/Skills/Barbarian");
        var justAScratch = CreatePassive("Skill_JustAScratch", "Просто царапина",
            "В начале боя восстанавливает УровеньПредмета × 1% от максимума HP персонажа.",
            5, "Assets/ScriptableObjects/Skills/Barbarian");

        // ---- Клинок (Rogue weapon, 3 tiers) ----
        // База: урон=4 (как Копьё), скорость=2.2/сек, itemLevel=1, слот=Weapon, subtype=Blade,
        // allowedClasses=[Rogue]. Тир-множитель уже запечён в baseDamage (3.10 конвенция).
        CreateWeapon("Item_Blade_Common_Blade", "Клинок", ItemTier.Common, WeaponSubtype.Blade,
            baseDamage: 4f, attackSpeed: 2.2f, bonusStat: null, folder: "Assets/ScriptableObjects/Items/Blades",
            classes: new[] { CharacterClass.Rogue });
        CreateWeapon("Item_Blade_Rare_JaggedBlade", "Зазубренный клинок", ItemTier.Rare, WeaponSubtype.Blade,
            baseDamage: 6f, attackSpeed: 2.2f, // 4 * 1.5 tier mult
            bonusStat: new BonusStat { type = BonusStatType.ArmorIgnorePercent, baseValue = 3f },
            folder: "Assets/ScriptableObjects/Items/Blades", classes: new[] { CharacterClass.Rogue });
        CreateWeapon("Item_Blade_Epic_MomentoMori", "Моменто Мори", ItemTier.Epic, WeaponSubtype.Blade,
            baseDamage: 8.8f, attackSpeed: 2.2f, // 4 * 2.2 tier mult
            bonusStat: new BonusStat { type = BonusStatType.ArmorIgnorePercent, baseValue = 3f },
            passive: execution, folder: "Assets/ScriptableObjects/Items/Blades", classes: new[] { CharacterClass.Rogue });

        // ---- Капюшон (Rogue helmet-slot replacement, 3 tiers) ----
        CreateHelmetLike("Item_Hood_Common_Hood", "Капюшон", ItemTier.Common,
            physicalDefense: 3f, magicShieldFlat: 5f, bonusStat: null, passive: null,
            folder: "Assets/ScriptableObjects/Items/Hoods", classes: new[] { CharacterClass.Rogue });
        CreateHelmetLike("Item_Hood_Rare_DarkHood", "Тёмный капюшон", ItemTier.Rare,
            physicalDefense: 5f, magicShieldFlat: 8f,
            bonusStat: new BonusStat { type = BonusStatType.EvasionPercent, baseValue = 1f }, passive: null,
            folder: "Assets/ScriptableObjects/Items/Hoods", classes: new[] { CharacterClass.Rogue });
        CreateHelmetLike("Item_Hood_Epic_DuelistHood", "Капюшон Дуэльянта", ItemTier.Epic,
            physicalDefense: 7f, magicShieldFlat: 11f,
            bonusStat: new BonusStat { type = BonusStatType.EvasionPercent, baseValue = 1f }, passive: riposte,
            folder: "Assets/ScriptableObjects/Items/Hoods", classes: new[] { CharacterClass.Rogue });

        // ---- Кожанка (Rogue armor-slot replacement, 3 tiers) ----
        CreateArmorLike("Item_Leather_Common_Leather", "Кожанка", ItemTier.Common,
            physicalDefense: 7f, magicShieldFlat: 8f, bonusStat: null, passive: null,
            folder: "Assets/ScriptableObjects/Items/Leathers", classes: new[] { CharacterClass.Rogue });
        CreateArmorLike("Item_Leather_Rare_ThickLeather", "Плотная кожанка", ItemTier.Rare,
            physicalDefense: 11f, magicShieldFlat: 12f,
            bonusStat: new BonusStat { type = BonusStatType.CritChancePercent, baseValue = 1.5f }, passive: null,
            folder: "Assets/ScriptableObjects/Items/Leathers", classes: new[] { CharacterClass.Rogue });
        CreateArmorLike("Item_Leather_Epic_EmbraceOfNight", "Объятия ночи", ItemTier.Epic,
            physicalDefense: 15f, magicShieldFlat: 18f,
            bonusStat: new BonusStat { type = BonusStatType.CritChancePercent, baseValue = 1.5f }, passive: embraceOfNight,
            folder: "Assets/ScriptableObjects/Items/Leathers", classes: new[] { CharacterClass.Rogue });

        // ---- Двуручный топор (Barbarian weapon, 3 tiers) ----
        CreateWeapon("Item_TwoHandedAxe_Common_GreatAxe", "Двуручный топор", ItemTier.Common, WeaponSubtype.TwoHandedAxe,
            baseDamage: 20f, attackSpeed: 0.7f, bonusStat: null, folder: "Assets/ScriptableObjects/Items/TwoHandedAxes",
            classes: new[] { CharacterClass.Barbarian }, isTwoHanded: true);
        CreateWeapon("Item_TwoHandedAxe_Rare_TemperedGreatAxe", "Закалённый двуручный топор", ItemTier.Rare, WeaponSubtype.TwoHandedAxe,
            baseDamage: 30f, attackSpeed: 0.7f, // 20 * 1.5
            bonusStat: new BonusStat { type = BonusStatType.CritChancePercent, baseValue = 1.5f },
            folder: "Assets/ScriptableObjects/Items/TwoHandedAxes", classes: new[] { CharacterClass.Barbarian }, isTwoHanded: true);
        CreateWeapon("Item_TwoHandedAxe_Epic_Headsplitter", "Головоруб", ItemTier.Epic, WeaponSubtype.TwoHandedAxe,
            baseDamage: 44f, attackSpeed: 0.7f, // 20 * 2.2
            bonusStat: new BonusStat { type = BonusStatType.CritChancePercent, baseValue = 1.5f }, passive: giantSlayer,
            folder: "Assets/ScriptableObjects/Items/TwoHandedAxes", classes: new[] { CharacterClass.Barbarian }, isTwoHanded: true);

        // ---- Пояс (Barbarian armor-slot replacement, 3 tiers — NO physicalDefense, ever) ----
        CreateArmorLike("Item_Belt_Common_Belt", "Пояс", ItemTier.Common,
            physicalDefense: 0f, magicShieldFlat: 0f,
            bonusStat: new BonusStat { type = BonusStatType.FlatHP, baseValue = 12f }, passive: null,
            folder: "Assets/ScriptableObjects/Items/Belts", classes: new[] { CharacterClass.Barbarian });
        CreateBeltWithTwoBonusStats("Item_Belt_Rare_ChampionBelt", "Пояс чемпиона", ItemTier.Rare,
            hpPerLevel: 12f, damagePercentPerLevel: 2f, rageFlatPercentPerLevel: 0f, passive: null,
            folder: "Assets/ScriptableObjects/Items/Belts", classes: new[] { CharacterClass.Barbarian });
        CreateBeltWithTwoBonusStats("Item_Belt_Epic_TitanBelt", "Пояс титана", ItemTier.Epic,
            hpPerLevel: 12f, damagePercentPerLevel: 2f, rageFlatPercentPerLevel: 1f, passive: null,
            folder: "Assets/ScriptableObjects/Items/Belts", classes: new[] { CharacterClass.Barbarian });

        // ---- Трофей (Barbarian helmet-slot replacement, 3 tiers — NO maxPhysicalDefenseBonus) ----
        CreateTrophy("Item_Trophy_Common_Trophy", "Трофей", ItemTier.Common, flatDamagePerLevel: 3f, passive: null,
            folder: "Assets/ScriptableObjects/Items/Trophies", classes: new[] { CharacterClass.Barbarian });
        CreateTrophy("Item_Trophy_Rare_RareTrophy", "Редкий трофей", ItemTier.Rare, flatDamagePerLevel: 4.5f, passive: null, // 3*1.5
            folder: "Assets/ScriptableObjects/Items/Trophies", classes: new[] { CharacterClass.Barbarian });
        CreateTrophy("Item_Trophy_Epic_EpicTrophy", "Эпический трофей", ItemTier.Epic, flatDamagePerLevel: 6.6f, // 3*2.2
            passive: justAScratch, folder: "Assets/ScriptableObjects/Items/Trophies", classes: new[] { CharacterClass.Barbarian });

        // ---- Class skill pools (PassiveSkillData, maxLevel=5, LevelUpManager reads generic pool lists) ----
        CreatePassive("Skill_EyeForAnEye", "В глаз",
            "Шанс критической атаки: 1ур=+2%, 2ур=+5%, 3ур=+7.5%, 4ур=+10%, 5ур=+12.5%. Крит накладывает Скрытность на 3с.",
            5, "Assets/ScriptableObjects/Skills/Rogue");
        CreatePassive("Skill_PoisonedBlade", "Отравленный клинок",
            "Пробивающие атаки накладывают стак Яда (3с, урон/сек=стаки, макс=уровень навыка). Удваивается в Скрытности.",
            5, "Assets/ScriptableObjects/Skills/Rogue");
        CreatePassive("Skill_ByAThread", "На волоске",
            "После уклонения: +скорость атаки на 3с — 1ур=+3%, 2ур=+6%, 3ур=+9%, 4ур=+12%, 5ур=+15%.",
            5, "Assets/ScriptableObjects/Skills/Rogue");
        CreatePassive("Skill_Elimination", "Устранение",
            "Крит-множитель урона: 1ур=175%, 2ур=180%, 3ур=185%, 4ур=190%, 5ур=200% (заменяет базовые 150%).",
            5, "Assets/ScriptableObjects/Skills/Rogue");
        CreatePassive("Skill_SlipAway", "Ускользание",
            "После уклонения даёт Скрытность на 3с. Шанс уклонения: 1ур=+1% ... 5ур=+5%.",
            5, "Assets/ScriptableObjects/Skills/Rogue");

        CreatePassive("Skill_Stubbornness", "Упёртость",
            "Если Ярость выше порога — игнорирует все дебафы: 1ур=90%, 2ур=80%, 3ур=70%, 4ур=60%, 5ур=50%.",
            5, "Assets/ScriptableObjects/Skills/Barbarian");
        CreatePassive("Skill_Frenzy", "Остервенелость",
            "Скорость атаки += Ярость×X%: 1ур=0.7, 2ур=0.75, 3ур=0.8, 4ур=0.9, 5ур=1.0.",
            5, "Assets/ScriptableObjects/Skills/Barbarian");
        CreatePassive("Skill_CombatRegen", "Боевая регенерация",
            "Каждые N полученных ударов восстанавливает 10% HP: 1ур=5, 2ур=4, 3ур=3, 4ур=2, 5ур=1.",
            5, "Assets/ScriptableObjects/Skills/Barbarian");
        CreatePassive("Skill_Intimidation", "Запугивание",
            "При крите снижает скорость атаки цели на Ярость×X% на 3с (X как у Остервенелости).",
            5, "Assets/ScriptableObjects/Skills/Barbarian");
        CreatePassive("Skill_Superstition", "Суеверность",
            "Сопротивление магическому урону = Ярость×X% (X как у Остервенелости).",
            5, "Assets/ScriptableObjects/Skills/Barbarian");

        // ---- Unique passive/active per character (maxLevel per GDD: Shadow=5, SmokeBomb=3, ChampionOfTheTribe=5, Berserk=3) ----
        var shadow = CreatePassive("Skill_Shadow", "Тень",
            "Пока активна Скрытность: +шанс уклонения — 1ур=+10%, 2ур=+15%, 3ур=+20%, 4ур=+25%, 5ур=+30%.",
            5, "Assets/ScriptableObjects/Skills/Unique");
        var smokeBomb = CreateActive("Skill_SmokeBomb", "Дымовая граната", 10f,
            "КД 10с. Даёт Скрытность на 3с. Первые N обычных атак — гарантированный крит: 1ур=1, 2ур=2, 3ур=3.",
            3, "Assets/ScriptableObjects/Skills/Unique");
        var championOfTheTribe = CreatePassive("Skill_ChampionOfTheTribe", "Чемпион племени",
            "Шанс крита ВСЕГДА = Ярость×X% (заменяет остальные источники, которые конвертируются 1%→+2% крит-урона).",
            5, "Assets/ScriptableObjects/Skills/Unique");
        var berserk = CreateActive("Skill_Berserk", "Берсерк", 0f, // 0 КД — это тумблер, не обычная активка
            "Ручной тумблер. Активен: -1%HP/сек (мин. 1 HP), физ. сопротивление 1ур=10%, 2ур=20%, 3ур=30%.",
            3, "Assets/ScriptableObjects/Skills/Unique");

        // ---- Characters ----
        CreateCharacter("Character_Rogue", "Плут", CharacterClass.Rogue, baseHealth: 15, healthPerLevel: 15,
            uniquePassive: shadow, uniqueActive: smokeBomb, activeCooldown: 10f);
        CreateCharacter("Character_Barbarian", "Варвар", CharacterClass.Barbarian, baseHealth: 30, healthPerLevel: 25,
            uniquePassive: championOfTheTribe, uniqueActive: berserk, activeCooldown: 0f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RogueBarbarianContentGenerator] Done: 18 items, 5 item-passives, 10 class skills, 4 unique skills, 2 characters.");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static PassiveSkillData CreatePassive(string assetName, string displayName, string description, int maxLevel, string folder)
    {
        var skill = ScriptableObject.CreateInstance<PassiveSkillData>();
        skill.skillName = displayName;
        skill.effectDescription = description;
        skill.maxLevel = maxLevel;
        AssetDatabase.CreateAsset(skill, $"{folder}/{assetName}.asset");
        return skill;
    }

    static ActiveSkillData CreateActive(string assetName, string displayName, float cooldownSeconds, string description, int maxLevel, string folder)
    {
        var skill = ScriptableObject.CreateInstance<ActiveSkillData>();
        skill.skillName = displayName;
        skill.cooldownSeconds = cooldownSeconds;
        skill.maxLevel = maxLevel;
        AssetDatabase.CreateAsset(skill, $"{folder}/{assetName}.asset");
        return skill;
    }

    static void CreateWeapon(string assetName, string displayName, ItemTier tier, WeaponSubtype subtype,
        float baseDamage, float attackSpeed, BonusStat bonusStat, string folder, CharacterClass[] classes,
        PassiveSkillData passive = null, bool isTwoHanded = false)
    {
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.itemName = displayName;
        item.slot = EquipmentSlot.Weapon;
        item.weaponSubtype = subtype;
        item.tier = tier;
        item.itemLevel = 1;
        item.allowedClasses = classes;
        item.baseDamage = baseDamage;
        item.attackSpeed = attackSpeed;
        item.bonusStat = bonusStat;
        item.passiveSkill = passive;
        item.isTwoHanded = isTwoHanded;
        AssetDatabase.CreateAsset(item, $"{folder}/{assetName}.asset");
    }

    static void CreateHelmetLike(string assetName, string displayName, ItemTier tier,
        float physicalDefense, float magicShieldFlat, BonusStat bonusStat, PassiveSkillData passive, string folder, CharacterClass[] classes)
    {
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.itemName = displayName;
        item.slot = EquipmentSlot.Helmet;
        item.tier = tier;
        item.itemLevel = 1;
        item.allowedClasses = classes;
        item.maxPhysicalDefenseBonus = physicalDefense; // Helmet-slot items only ever grant MAX bonus, never physicalDefense itself (3.3)
        item.bonusStat = magicShieldFlat > 0f
            ? new BonusStat { type = BonusStatType.MagicShieldFlat, baseValue = magicShieldFlat }
            : bonusStat;
        // NOTE: Капюшон needs BOTH a magic-shield flat AND (on Rare/Epic) an evasion% bonus stat —
        // ItemData only has ONE bonusStat slot. Resolve by keeping magicShieldFlat on the dedicated
        // path used by every other helmet-like item (folded into maxPhysicalDefenseBonus's sibling
        // mechanism is NOT available — see the FLAGGED ISSUE below) and layering the evasion bonus
        // via bonusStat. THIS IS A REAL DATA-MODEL GAP, not a trivial substitution — see Step 4.
        item.passiveSkill = passive;
        AssetDatabase.CreateAsset(item, $"{folder}/{assetName}.asset");
    }

    // CreateArmorLike, CreateBeltWithTwoBonusStats, CreateTrophy, CreateCharacter: same shape as
    // CreateHelmetLike/CreateWeapon above — implement analogously, reading ItemData/CharacterData's
    // actual fields first (Read the file, don't guess field names).
}
```

- [ ] **Step 4: Resolve the flagged ItemData data-model gap before finishing the generator**

The draft above surfaces a REAL blocker: `ItemData` has exactly one `bonusStat` slot (`BonusStat bonusStat`), but Капюшон needs a flat magic-shield bonus (its "main" secondary stat, present on ALL 3 tiers) **plus** an evasion% bonus stat that only Rare/Epic get — two simultaneous bonus stats on one item, which the current `ItemData`/`CombatantFactory.AggregateEquipmentStats` shape doesn't support (see Task 1 of the 8.1/3.10 fix session — `AggregateEquipmentStats` reads `item.bonusStat` as a single field, singular). Same shape of problem for Кожанка (magic shield on all tiers + crit% on Rare/Epic) and for Пояс (HP on all tiers + damage% on Rare/Epic + Rage% on Epic only — THREE simultaneous bonuses).

Before writing this generator's final version, re-read `Assets/Scripts/Combat/CombatantFactory.cs`'s `AggregateEquipmentStats` (modified in the 2026-08-26 session, commit `13c177c`) and decide one of:
1. Add a SECOND bonus-stat field to `ItemData` (e.g. `BonusStat secondaryBonusStat`) and extend `AggregateEquipmentStats` to read both — most consistent with the existing single-bonusStat-type-switch shape, but touches the same switch statement fixed in the previous session.
2. Model the "always-present" bonus (magic shield on Капюшон/Кожанка, flat HP on Пояс) as a NEW dedicated `ItemData` field instead of a `bonusStat` (parallel to how `physicalDefense`/`maxPhysicalDefenseBonus` are already dedicated fields, not bonusStats) — e.g. `magicShieldBonus : float` scaled via the SAME `StatScaling.ApplyLevelBonus` main-stat formula as physicalDefense (this matches the GDD's own framing: Капюшон/Кожанка/Пояс's magic-shield/HP number is presented as a primary stat of the item, analogous to a Ring's or Armor's main stat, NOT as a linear-per-level "bonus" like crit%/atkspeed% — re-read the GDD quotes above: "Обычный тир: +3 физ.защ, +5 маг.щит" reads as two co-equal primary stats, not a primary stat plus a bonus).

**Recommendation (not a silent decision — confirm with a fresh read of the exact GDD wording, and flag this choice explicitly in the final report either way):** option 2 is more consistent with the GDD's own phrasing and doesn't touch the just-fixed `AggregateEquipmentStats` switch. Add `public float magicShieldBonus;` and `public float MagicShieldEffective => StatScaling.ApplyLevelBonus(magicShieldBonus, itemLevel);` to `ItemData` (mirrors `EffectiveDefense`/`EffectiveMaxDefenseBonus`), read it in `AggregateEquipmentStats` unconditionally (add `magicShield += item.MagicShieldEffective;` next to the existing `physicalDefense`/`maxPhysicalDefenseBonus` accumulation), and keep `bonusStat` for ONLY the Rare/Epic-exclusive secondary bonus (EvasionPercent for Капюшон, CritChancePercent for Кожанка). This still leaves Пояс needing THREE simultaneous numbers (HP main-stat + damage% bonusStat + Rage% epic-only) — HP main-stat can reuse the same new pattern (`public float hpBonus` + `HpBonusEffective`), damage% fits the existing single `bonusStat` slot, and Rage% (Epic-only, `Пояс титана`) needs a THIRD field since it coexists with damage% on the same item — add a narrowly-scoped `public float rageBonusFlatPercent;` (no scaling — GDD says flat `УровеньПредмета × 1%`, i.e. linear like a bonusStat, but it's a fourth simultaneous number so it can't share the `bonusStat` slot either). Wire `rageBonusFlatPercent * itemLevel` into `CombatantRuntime.RageFlatBonusPercent` in `CombatantFactory`.

Update `CreateWeapon`/`CreateHelmetLike`/etc. in the generator to use whichever fields you land on, and update `AggregateEquipmentStats` (Task 4) accordingly. This step is the single highest-risk part of this plan — budget real time for it, don't rush the field design.

- [ ] **Step 5: Run the generator**

```
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod RogueBarbarianContentGenerator.Generate -logFile -
```

Verify via `git status` that exactly 39 new `.asset` files (+39 `.meta` files) appeared under `Assets/ScriptableObjects/`, and spot-check 2-3 of them by reading the YAML to confirm field values match this plan's tables.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Combat/SkillEffectMap.cs Assets/Scripts/Data/ItemData.cs Assets/Editor/RogueBarbarianContentGenerator.cs Assets/ScriptableObjects/
git commit -m "Generate Rogue/Barbarian content assets: 18 items, 5 item-passives, 10 class skills, 4 unique skills, 2 characters (GDD 3.11)"
```

---

## Task 4: Rogue combat behavior — Stealth, class skills, unique passive/active

**Files:**
- Modify: `Assets/Scripts/Managers/CombatManager.cs`
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs` (copy new Skill*Level fields in `ApplyCharacterSkills`, same pattern as existing ones)
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-3 (`CombatantRuntime` Rogue fields, `StealthStatus.DurationSeconds`, `SkillEffectMap` constants, `WeaponAttackState.ArmorIgnorePercent`).
- Produces: working combat behavior for every Rogue skill listed in GDD 3.11's "Классовые навыки — Плут" quote (Task 3's tables) plus "Тень"/"Дымовая граната".

- [ ] **Step 1: Grant/refresh Stealth on crit (В глаз) and on evade (Ускользание), tick the Stealth timer**

In `CombatManager.Tick`/`UpdateStatusEffects` (wherever `ActiveDebuffs`/`FreezeStackTimer` etc. already tick down per-combatant), add Stealth countdown:

```csharp
if (combatant.IsStealthed)
{
    combatant.StealthTimer -= deltaTime;
    if (combatant.StealthTimer <= 0f)
    {
        combatant.IsStealthed = false;
    }
}
```

In `ResolveAttack`, after the `isCrit` roll, if `attacker.SkillEyeForAnEyeLevel > 0 && isCrit`, grant/refresh Stealth:

```csharp
void GrantOrRefreshStealth(CombatantRuntime combatant)
{
    combatant.IsStealthed = true;
    combatant.StealthTimer = StealthStatus.DurationSeconds;
}
```

Call `GrantOrRefreshStealth(attacker)` after a crit when `SkillEyeForAnEyeLevel > 0`. Find the existing evade-roll site (the `evadeChancePercent`/`Random.value * 100f < evadeChancePercent` block near the top of `ResolveAttack`, which currently only logs and `return`s) and, on the DEFENDER's side, call `GrantOrRefreshStealth(target)` when `target.SkillSlipAwayLevel > 0`, plus apply "На волоске"'s attack-speed buff there too (needs a NEW temporary-attack-speed-buff mechanism — reuse the existing `ActiveDebuffs` list shape, which already supports `AttackSpeedMultiplier` per entry and is summed multiplicatively in `GetEffectiveAttackSpeed`; add an entry with `Id = "by_a_thread"` and a multiplier `1f + level-indexed percent`).

- [ ] **Step 1b: "Ускользание" own evasion% bonus (separate from its Stealth-on-evade effect wired in Step 1)**

In the `evadeChancePercent` formula (the same one Task 1's `ItemEvasionBonusPercent` was added to in the 2026-08-26 session), add:

```csharp
float slipAwayBonus = target.SkillSlipAwayLevel switch { 1 => 1f, 2 => 2f, 3 => 3f, 4 => 4f, 5 => 5f, _ => 0f };
evadeChancePercent += slipAwayBonus;
```

- [ ] **Step 2: "В глаз" crit chance bonus**

In `ResolveAttack`'s crit-chance formula (`critChancePercent = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems - attacker.CritChanceDebuffPercent`), add the Eye-for-an-Eye table (`2/5/7.5/10/12.5` by level — NOT `level*2.5`, the table is irregular, use a `switch`):

```csharp
float eyeForAnEyeBonus = attacker.SkillEyeForAnEyeLevel switch
{
    1 => 2f, 2 => 5f, 3 => 7.5f, 4 => 10f, 5 => 12.5f, _ => 0f
};
critChancePercent += eyeForAnEyeBonus;
```

- [ ] **Step 3: "Устранение" crit-multiplier override**

Replace the hardcoded `damage *= 1.5f;` crit line with a lookup that checks `attacker.CritDamageMultiplierOverridePercent` (set from `SkillEliminationLevel` — wire this assignment in `CombatantFactory.ApplyCharacterSkills`, table `175/180/185/190/200`):

```csharp
float critMultiplier = attacker.CritDamageMultiplierOverridePercent ?? 150f;
if (isCrit)
{
    damage *= critMultiplier / 100f;
}
```

- [ ] **Step 4: "Отравленный клинок" — Rogue's own poison, separate from monster poison**

After a successful (non-blocked) physical pierce (`!result.WasBlocked && weapon.DamageType == DamageType.Physical`), if `attacker.SkillPoisonedBladeLevel > 0`, apply to `target.RoguePoisonStacksOnTarget`/`RoguePoisonTimer` — mirror the existing `ApplyBleed`/monster-poison shape but on the NEW fields, doubling stacks/max when `attacker.IsStealthed`:

```csharp
void ApplyRoguePoison(CombatantRuntime attacker, CombatantRuntime target)
{
    int maxStacks = attacker.SkillPoisonedBladeLevel;
    int stacksToAdd = 1;
    if (attacker.IsStealthed)
    {
        maxStacks *= 2;
        stacksToAdd = 2;
    }

    target.RoguePoisonStacksOnTarget = Mathf.Min(target.RoguePoisonStacksOnTarget + stacksToAdd, maxStacks);
    target.RoguePoisonTimer = 3f;
}
```

Add a `TickRoguePoison` alongside the existing `TickPoison`/`TickBleed` in `UpdateStatusEffects`, same shape (1/sec tick, `damagePerSecond = target.RoguePoisonStacksOnTarget` — the GDD says "урон/сек = текущее число стаков", i.e. 1:1, unlike monster poison's `stacks * 4f`).

- [ ] **Step 5: "Тень" (unique passive) and "Дымовая граната" (unique active)**

"Тень": in the evasion-chance formula (`evadeChancePercent = ...`), add a Stealth-gated bonus:

```csharp
if (target.IsStealthed && target.UniqueShadowLevel > 0)
{
    evadeChancePercent += target.UniqueShadowLevel switch { 1 => 10f, 2 => 15f, 3 => 20f, 4 => 25f, 5 => 30f, _ => 0f };
}
```

"Дымовая граната": needs a NEW guaranteed-crit-for-N-attacks counter (distinct from `IsStealthed`, since Stealth can be granted/refreshed by other means during its window — the GDD explicitly says "первые атаки... пока действует Скрытность ОТ ЭТОГО НАВЫКА", i.e. scoped to this activation, not to Stealth in general). Add `public int SmokeBombGuaranteedCritsRemaining;` to `CombatantRuntime` (Task 1 follow-up — note this field was missed in Task 1's list, add it there or here), set it to `UniqueSmokeBombLevel` (1/2/3) when the active fires (alongside granting Stealth), and in `ResolveAttack`'s `isCrit` roll, short-circuit to guaranteed crit + decrement when `attacker.SmokeBombGuaranteedCritsRemaining > 0` — but ONLY for regular weapon attacks, not for the unique-active's own hits if it has any (GDD: "обычные атаки оружием по скорости атаки персонажа, не активация других навыков" — this already matches since `TryActivateUniqueActiveSkill`'s own hit loop calls `ResolveAttack` too; the guard needs to distinguish "this call is a normal timer-driven attack" from "this call is from another active skill". Simplest correct fix: only decrement/consume `SmokeBombGuaranteedCritsRemaining` in the `TickCombatant`-driven call path, not in `TryActivateUniqueActiveSkill`'s loop — pass a bool `isRegularAttack` param through `ResolveAttack`, default `true`, and pass `false` from the active-skill hit loop).

- [ ] **Step 6: Copy new Rogue skill levels in CombatantFactory.ApplyCharacterSkills**

Add alongside the existing `runtime.SkillFreezeLevel = progress.GetSkillLevel(...)` lines:

```csharp
runtime.SkillEyeForAnEyeLevel = progress.GetSkillLevel(SkillEffectMap.EyeForAnEye);
runtime.SkillPoisonedBladeLevel = progress.GetSkillLevel(SkillEffectMap.PoisonedBlade);
runtime.SkillByAThreadLevel = progress.GetSkillLevel(SkillEffectMap.ByAThread);
runtime.SkillSlipAwayLevel = progress.GetSkillLevel(SkillEffectMap.SlipAway);
runtime.UniqueShadowLevel = progress.UniquePassiveLevel; // same pattern as existing unique-passive copying — verify against how Jennifer's unique passive level is already copied and match it exactly
runtime.CritDamageMultiplierOverridePercent = progress.GetSkillLevel(SkillEffectMap.Elimination) switch
{
    1 => 175f, 2 => 180f, 3 => 185f, 4 => 190f, 5 => 200f, _ => (float?)null
};
```

- [ ] **Step 7: Smoke-test coverage**

Add checks (pattern-match the existing mentor/skill-cooldown `CombatManager` tests in `PlayModeSmokeTest.cs`, which already build a real `CombatManager` + `CombatantRuntime` pair and call `StartCombat`/`Tick`): one for Stealth grant-on-crit-then-timeout, one for "Устранение"'s crit-multiplier override producing the expected damage number, one for "Отравленный клинок" stack-doubling in Stealth. Write these against the real `CombatManager`/`DamageCalculator` (not by re-deriving the formula in the test) so they catch real regressions.

- [ ] **Step 8: Run full smoke test, verify pass, commit**

```bash
git add Assets/Scripts/Managers/CombatManager.cs Assets/Scripts/Combat/CombatantFactory.cs Assets/Scripts/Combat/CombatantRuntime.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Wire Rogue class skills and unique passive/active into combat (GDD 3.11)"
```

---

## Task 5: Barbarian combat behavior — Rage-driven skills, crit override, Berserk toggle

**Files:**
- Modify: `Assets/Scripts/Managers/CombatManager.cs`
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `CombatantRuntime.Rage` (computed, Task 1), resistance fields (Task 2).
- Produces: working combat behavior for every Barbarian skill in GDD 3.11's "Классовые навыки — Варвар" quote plus "Чемпион племени"/"Берсерк".

- [ ] **Step 1: Shared X-by-level lookup**

Five different Barbarian skills all key off the same `0.7/0.75/0.8/0.9/1.0` table (Остервенелость, Запугивание, Суеверность, Чемпион племени all read it; Упёртость has its OWN different table `90/80/70/60/50`). Add one shared helper near the top of `CombatManager.cs` (or a static utility if one already exists for similar per-level tables — check `BalanceClamps.cs` first):

```csharp
static float RageSkillMultiplier(int level) => level switch
{
    1 => 0.7f, 2 => 0.75f, 3 => 0.8f, 4 => 0.9f, 5 => 1.0f, _ => 0f
};
```

- [ ] **Step 2: "Остервенелость" attack-speed bonus**

In `CombatantRuntime.GetEffectiveAttackSpeed`, after the existing `ItemAttackSpeedBonusPercent` multiply, add a Rage-based one. This needs `Rage` (a runtime-computed property already on the same class) and the skill level — both already on `CombatantRuntime`, no new params needed:

```csharp
if (SkillFrenzyLevel > 0)
{
    multiplier *= 1f + (Rage * RageSkillMultiplierTable(SkillFrenzyLevel) / 100f) / 100f;
}
```

(Add `RageSkillMultiplierTable` as a `static` method ON `CombatantRuntime` itself if `CombatManager`'s helper from Step 1 isn't visible there — `CombatantRuntime` is a plain class, not a `MonoBehaviour`, keep it self-contained rather than reaching into `CombatManager`.)

- [ ] **Step 3: "Упёртость" debuff immunity**

Find every place code CHECKS "does this combatant have an active debuff" or APPLIES a new debuff to a combatant (freeze stacks, bleed, poison, crit-chance debuff, `ActiveDebuffs.Add`, `RoguePoisonStacksOnTarget` from Task 4) and gate each with:

```csharp
bool ignoresDebuffs = target.SkillStubbornnessLevel > 0 && target.Rage > StubbornnessThreshold(target.SkillStubbornnessLevel);
```

```csharp
static float StubbornnessThreshold(int level) => level switch { 1 => 90f, 2 => 80f, 3 => 70f, 4 => 60f, 5 => 50f, _ => 101f };
```

This touches MULTIPLE call sites (`ApplyFreezeOnHit`, `ApplyBleed`, the Harpy crit-debuff, `ApplyMonsterPassiveOnAttack`'s `SlowCurse` case, Task 4's `ApplyRoguePoison`). Grep for every debuff-application site in `CombatManager.cs` before starting this step — do not implement it piecemeal and miss one; list every site found, then gate each.

- [ ] **Step 4: "Боевая регенерация" hit counter**

In `ResolveAttack`, AFTER `DamageCalculator.ApplyDamage` returns (both blocked and unblocked cases count as "получен удар" per GDD — re-read the exact wording "каждые N полученных ударов" — this plan assumes ANY resolved attack against the target counts, whether or not it dealt HP damage; if the designer meant only unblocked/HP-damaging hits, that's a discrepancy to flag, not guess past), increment the counter and check the threshold:

```csharp
if (target.SkillCombatRegenLevel > 0)
{
    target.HitsTakenSinceLastRegen++;
    int threshold = target.SkillCombatRegenLevel switch { 1 => 5, 2 => 4, 3 => 3, 4 => 2, 5 => 1, _ => int.MaxValue };
    if (target.HitsTakenSinceLastRegen >= threshold && target.IsAlive)
    {
        target.HitsTakenSinceLastRegen = 0;
        target.CurrentHP = Mathf.Min(target.MaxHP, target.CurrentHP + target.MaxHP * 0.10f);
    }
}
```

Place this AFTER the existing `if (!target.IsAlive) { Log(...) }` death check at the bottom of `ResolveAttack`, guarded by `target.IsAlive` as shown (GDD: "сначала урон... затем, если персонаж выжил — восстановление").

- [ ] **Step 5: "Запугивание" attack-speed debuff on crit**

Where `isCrit` is already computed in `ResolveAttack`, if `attacker.SkillIntimidationLevel > 0`, apply an `ActiveDebuffs` entry to `target` (same mechanism as `warlock_slow`) with `AttackSpeedMultiplier = 1f - (attacker.Rage * RageSkillMultiplier(attacker.SkillIntimidationLevel) / 100f)`, `RemainingTime = 3f`. Clamp the multiplier to a sane floor (`Mathf.Max(0.01f, ...)`, matching the existing freeze-stack speed floor) since at high Rage this could theoretically go negative.

- [ ] **Step 6: "Суеверность" magic resistance**

In `CombatantFactory.ApplyCharacterSkills` — wait, this can't be a fixed value copied once at combatant-creation time, because `Rage` changes every frame as HP changes. Resistance must be computed LIVE at damage-resolution time, not cached. Change `CombatantRuntime.MagicalResistancePercent`/`PhysicalResistancePercent` from Task 1's plain fields into something that accounts for this — the cleanest fix: keep them as plain fields but RECOMPUTE them every `CombatManager.Tick` (alongside the other per-frame status updates), not just once at creation:

```csharp
void UpdateResistances(CombatantRuntime combatant)
{
    combatant.MagicalResistancePercent = combatant.SkillSuperstitionLevel > 0
        ? combatant.Rage * RageSkillMultiplier(combatant.SkillSuperstitionLevel) / 100f
        : 0f;

    combatant.PhysicalResistancePercent = combatant.IsBerserkActive
        ? combatant.UniqueBerserkLevel switch { 1 => 10f, 2 => 20f, 3 => 30f, _ => 0f }
        : 0f;
}
```

Call `UpdateResistances(combatant)` for `Player` and every `Enemy` at the top of `Tick`, before `CheckCombatEnd`. This REPLACES Task 1's assumption that these fields are copied once in `ApplyCharacterSkills` — update Task 1's comment there if it said otherwise (it didn't specify a copy site, so no correction needed, just don't add one).

- [ ] **Step 7: "Берсерк" toggle — self-damage tick, resistance (via Step 6), no death protection**

Add `CombatManager.SetBerserkActive(bool active)` (mirrors `SetActiveSkillAutoMode`'s public-toggle shape) that sets `Player.IsBerserkActive` (only ever the player toggles this — GDD is explicit this is player-only, no monster ever has it). In `Tick`, after existing per-second tick logic (bleed/poison tick pattern — 1-second accumulator), add:

```csharp
if (Player.IsBerserkActive && Player.IsAlive)
{
    Player.BerserkTickAccumulator += deltaTime;
    while (Player.BerserkTickAccumulator >= 1f && Player.IsAlive)
    {
        Player.BerserkTickAccumulator -= 1f;
        float tickDamage = Mathf.Max(1f, Player.CurrentHP * 0.01f); // [ПРЕДПОЛОЖЕНИЕ, см. Global Constraints] — от ТЕКУЩЕГО HP, не максимума; GDD сам помечает это неподтверждённым
        Player.CurrentHP = Mathf.Max(0f, Player.CurrentHP - tickDamage);
    }
}
```

Do NOT add any guard preventing this from reducing HP to 0 — GDD explicitly says no death protection. `CheckCombatEnd` (called every `Tick`) already handles death naturally once `CurrentHP` hits 0.

Note: `RunFlowController` will need a UI toggle button eventually (out of scope for this plan per its own "no new screens" architecture note — flag as a follow-up, `CombatManager.SetBerserkActive` is the hook a future UI task wires to, same as `TryActivateUniqueActiveSkill` already is for the normal active-skill button).

- [ ] **Step 8: "Чемпион племени" crit-chance override + conversion**

In `ResolveAttack`'s crit-chance computation, BEFORE the existing formula, check for the override:

```csharp
float critChancePercent;
if (attacker.CritChanceReplacedByRage)
{
    critChancePercent = Mathf.Clamp(attacker.Rage * RageSkillMultiplier(attacker.UniqueChampionOfTheTribeLevel), 0f, 100f);
    // Все прочие источники крит-шанса (навык "Критические атаки", предметный CritChanceBonusFromItems)
    // вместо суммирования в шанс конвертируются в крит-урон по курсу 1%->+2%.
    float convertedSources = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems;
    critMultiplier += convertedSources * 2f; // добавляется к critMultiplier из Task 4 Step 3, ДО деления на 100
}
else
{
    critChancePercent = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems - attacker.CritChanceDebuffPercent + eyeForAnEyeBonus;
    critChancePercent = Mathf.Max(0f, critChancePercent);
    critChancePercent = BalanceClamps.ClampCritChancePercent(critChancePercent);
}
```

This requires reordering `ResolveAttack` slightly so `critMultiplier` (Task 4 Step 3) is computed BEFORE this block, not after — re-read the current method body and slot this in correctly rather than pasting blind; the exact current statement order matters here (`critMultiplier` needs to exist as a mutable local before this branch runs, then get used when `isCrit` is later applied).

Set `attacker.CritChanceReplacedByRage = attacker.UniqueChampionOfTheTribeLevel > 0` in `CombatantFactory.ApplyCharacterSkills`.

- [ ] **Step 9: Copy new Barbarian skill levels in CombatantFactory.ApplyCharacterSkills**

```csharp
runtime.SkillStubbornnessLevel = progress.GetSkillLevel(SkillEffectMap.Stubbornness);
runtime.SkillFrenzyLevel = progress.GetSkillLevel(SkillEffectMap.Frenzy);
runtime.SkillCombatRegenLevel = progress.GetSkillLevel(SkillEffectMap.CombatRegen);
runtime.SkillIntimidationLevel = progress.GetSkillLevel(SkillEffectMap.Intimidation);
runtime.SkillSuperstitionLevel = progress.GetSkillLevel(SkillEffectMap.Superstition);
runtime.UniqueChampionOfTheTribeLevel = progress.UniquePassiveLevel; // match existing unique-passive copy pattern
runtime.UniqueBerserkLevel = progress.UniqueActiveLevel; // match existing unique-active copy pattern (see jenniferCharacter.uniqueActiveSkill wiring in RunFlowController for the analogous read site)
runtime.CritChanceReplacedByRage = runtime.UniqueChampionOfTheTribeLevel > 0;
```

- [ ] **Step 10: Smoke-test coverage**

Add checks for: Rage computed correctly at various HP fractions (pure property test, no combat needed), Упёртость blocking a freeze-stack application above threshold and allowing it below, Боевая регенерация firing exactly at the Nth hit and not before, Берсерк's resistance reducing incoming physical damage via a real `CombatManager` fight, Чемпион племени's crit-chance override producing `Rage × X%` exactly and NOT being affected by adding `SkillCriticalHitsLevel`.

- [ ] **Step 11: Run full smoke test, verify pass, commit**

```bash
git add Assets/Scripts/Managers/CombatManager.cs Assets/Scripts/Combat/CombatantFactory.cs Assets/Scripts/Combat/CombatantRuntime.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Wire Barbarian class skills, crit-override, and Berserk toggle into combat (GDD 3.11)"
```

---

## Task 6: Two-handed weapon equip exception + Rogue dual-Клинок no-penalty rule

**Files:**
- Modify: `Assets/Scripts/Managers/CharacterManager.cs` (3.4 exception)
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs` (dual-wield penalty logic in `AggregateEquipmentStats`)
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `ItemData.isTwoHanded` (Task 1), `CharacterManager.GetComparisonCandidates`/`EquipItem` (existing).

- [ ] **Step 1: Two-handed replace-both-slots logic**

In `CharacterManager.EquipItem`, before the existing single-`replacing` removal logic, special-case `newItem.isTwoHanded`:

```csharp
public void EquipItem(ItemData newItem, ItemData replacing)
{
    if (newItem.isTwoHanded)
    {
        // 3.4 исключение (Варвар, Двуручное оружие): оба текущих предмета в слотах оружия/рук
        // заменяются одновременно, а не по одному, как в обычной логике сравнения слотов.
        var currentWeapons = EquippedItems.FindAll(i => i != null && i.slot == EquipmentSlot.Weapon);
        foreach (var current in currentWeapons)
        {
            EquippedItems.Remove(current);
        }
        EquippedItems.Add(newItem);
        RefreshCombatStats();
        return;
    }

    if (replacing != null)
    {
        EquippedItems.Remove(replacing);
    }

    EquippedItems.Add(newItem);
    RefreshCombatStats();
}
```

Also check `GetComparisonCandidates` — when the NEW item `isTwoHanded`, the UI should present BOTH currently-equipped weapon-slot items as what's being replaced (not the normal "up to `SlotCapacity`" candidate list), so the player sees what they're giving up. Re-read `RunFlowController.ItemCompareFlow`'s candidate-button rendering before deciding whether this needs a UI change too, or whether showing "занять свободный слот" / one candidate is acceptable for a first pass — if the UI doesn't clearly communicate "this replaces BOTH your weapons," flag it as a follow-up rather than silently shipping a confusing screen.

- [ ] **Step 2: Клинок's no-dual-wield-penalty rule**

In `CombatantFactory.AggregateEquipmentStats`, the current dual-wield logic applies `dualWieldMultiplier` (the `Ambidexterity`-derived penalty/bonus) uniformly to every weapon when `isDualWielding`. Change this to skip the multiplier for `WeaponSubtype.Blade` specifically:

```csharp
foreach (var item in realWeaponItems)
{
    float itemDamage = item.EffectiveDamage;
    if (isDualWielding && item.weaponSubtype != WeaponSubtype.Blade)
    {
        itemDamage *= dualWieldMultiplier;
    }
    // ... rest unchanged (tavernFlatDamage, weaponDamageFlatBonus, ComputeDamageRange, weapons.Add)
}
```

This correctly handles all three cases from the GDD: Клинок+Клинок (neither penalized), Клинок+Меч (Клинок full power, Меч penalized normally), Меч+Меч (both penalized as today) — because the check is per-weapon-item, not global.

- [ ] **Step 3: Смoke-test coverage**

Add checks: equipping a two-handed weapon over an existing Sword+Shield removes BOTH from `EquippedItems` and leaves exactly the two-handed weapon in the Weapon slot; a Клинок+Клинок pair both get 100% damage (no `dualWieldMultiplier` applied) via `CombatantFactory.CreatePlayerCombatant`; a Клинок+Меч pair has the Клинок at 100% and the Меч at the base 75% penalty.

- [ ] **Step 4: Run full smoke test, verify pass, commit**

```bash
git add Assets/Scripts/Managers/CharacterManager.cs Assets/Scripts/Combat/CombatantFactory.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Two-handed weapon equip exception (Barbarian) and no-penalty dual Blade (Rogue) — GDD 3.4/3.11"
```

---

## Task 6b: Item-passive behaviors (Рипост, Объятия ночи, Казнь, Убийца великанов, Просто царапина)

**Self-review finding:** Task 3 created 5 item-passive `PassiveSkillData` assets and attached them to the Epic-tier items, but no earlier task wires their actual combat effect — the existing pattern for weapon-bound passives (Vampirism/ArmorBreak/Piercing on `WeaponAttackState`) and armor-bound passives (Elusiveness/GoldenTouch/ToughSole as `CombatantRuntime` int levels) must be extended the same way for these 5. Without this task the new Epic items would be mechanically identical to their Rare tier.

**Files:**
- Modify: `Assets/Scripts/Combat/WeaponAttackState.cs` (2 new per-weapon fields)
- Modify: `Assets/Scripts/Combat/CombatantRuntime.cs` (3 new character-level fields)
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs` (set all 5 from `passiveName` checks, same loops as existing item passives)
- Modify: `Assets/Scripts/Managers/CombatManager.cs` (apply the 3 per-hit ones in `ResolveAttack`, the 1 combat-start one in `StartCombat`)
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

- [ ] **Step 1: Add the 5 fields**

`WeaponAttackState.cs` (weapon-bound — Казнь on Клинок, Убийца великанов on Двуручный топор):

```csharp
public int ExecutionLevel; // "Казнь" (Моменто Мори) — физ. урон = 1% недостающего HP цели за уровень
public int GiantSlayerLevel; // "Убийца великанов" (Головоруб) — +5% урона за уровень против цели с большим макс.HP
```

`CombatantRuntime.cs` (armor-bound, character-level — same shape as `ItemElusivenessLevel`):

```csharp
public int ItemRiposteLevel; // "Рипост" (Капюшон Дуэльянта) — доп. флэт-урон = уровень капюшона на первой атаке после уклонения
public int ItemEmbraceOfNightLevel; // "Объятия ночи" — доп. маг. урон в Скрытности = уровень × обычный урон атаки × 1%
public int ItemJustAScratchLevel; // "Просто царапина" (Эпический трофей) — разовое лечение в начале боя
```

- [ ] **Step 2: Set them in CombatantFactory**

In the `realWeaponItems` loop (where `VampirismLevel`/`ArmorBreakLevel`/`PiercingLevel` are already set from `passiveName`), add:

```csharp
ExecutionLevel = passiveName == SkillEffectMap.Execution ? item.itemLevel : 0,
GiantSlayerLevel = passiveName == SkillEffectMap.GiantSlayer ? item.itemLevel : 0
```

to the same `weapons.Add(new WeaponAttackState { ... })` object initializer.

In the main `items` loop (where `ItemElusivenessLevel`/`ItemGoldenTouchLevel`/`ItemToughSoleLevel` are already accumulated from `passiveName`), add three more `else if` branches:

```csharp
else if (passiveName == SkillEffectMap.Riposte)
{
    riposteLevel += item.itemLevel;
}
else if (passiveName == SkillEffectMap.EmbraceOfNight)
{
    embraceOfNightLevel += item.itemLevel;
}
else if (passiveName == SkillEffectMap.JustAScratch)
{
    justAScratchLevel += item.itemLevel;
}
```

(Add matching `out int riposteLevel`/`out int embraceOfNightLevel`/`out int justAScratchLevel` params to `AggregateEquipmentStats`'s signature and zero-init them, same pattern as the existing 3 `out int` level params — then assign `runtime.ItemRiposteLevel = riposteLevel;` etc. in `CreatePlayerCombatant`, same as `runtime.ItemElusivenessLevel = elusivenessLevel;`.)

- [ ] **Step 3: Apply "Казнь" and "Убийца великанов" in ResolveAttack**

After `damage` is computed but before the crit roll (both are flat/percent damage modifiers, apply alongside the existing Mentor/Unyielding/DamagePercent block from earlier tasks):

```csharp
if (weapon.ExecutionLevel > 0 && weapon.DamageType == DamageType.Physical)
{
    float missingHpPercent = target.MaxHP > 0f ? (1f - target.CurrentHP / target.MaxHP) : 0f;
    damage += target.MaxHP * missingHpPercent * (weapon.ExecutionLevel * 0.01f);
}

if (weapon.GiantSlayerLevel > 0 && target.MaxHP > attacker.MaxHP)
{
    damage *= 1f + weapon.GiantSlayerLevel * 0.05f;
}
```

- [ ] **Step 4: Apply "Рипост" and "Объятия ночи"**

"Рипост": at the evasion-success site (Task 4 Step 1's `GrantOrRefreshStealth(target)` neighborhood — the block that currently just logs+returns on a successful evade), if `target.ItemRiposteLevel > 0`, arm a one-shot flag (`public bool RiposteArmed;` on `CombatantRuntime`, add in this step) instead of dealing damage immediately (the bonus applies to the DEFENDER's own NEXT attack, not as a reactive hit right now — re-read the GDD wording: "первая атака ПОСЛЕ успешного уклонения", i.e. the next time this combatant attacks, not an immediate riposte-strike). In `ResolveAttack`, when `attacker.RiposteArmed`, add `+ attacker.ItemRiposteLevel` flat to `damage` and clear the flag:

```csharp
if (attacker.RiposteArmed)
{
    damage += attacker.ItemRiposteLevel;
    attacker.RiposteArmed = false;
}
```

"Объятия ночи": in `ResolveAttack`, when `attacker.IsStealthed && attacker.ItemEmbraceOfNightLevel > 0`, deal bonus magical damage as a SEPARATE `DamageCalculator.ApplyDamage(target, ..., DamageType.Magical)` call right after the main hit resolves (it's explicitly "доп. магический урон", i.e. a second damage instance, not added to the physical `damage` variable — magical damage goes through the shield, physical through armor, they can't be merged into one `ApplyDamage` call):

```csharp
if (attacker.IsStealthed && attacker.ItemEmbraceOfNightLevel > 0)
{
    float bonusMagicDamage = damage * attacker.ItemEmbraceOfNightLevel * 0.01f;
    var embraceResult = DamageCalculator.ApplyDamage(target, bonusMagicDamage, DamageType.Magical);
    HitResolved?.Invoke(target, embraceResult.DamageToHP, false, embraceResult.WasBlocked); // 4.7: второй отдельный всплывающий урон, не крит
}
```

- [ ] **Step 5: Apply "Просто царапина" in StartCombat**

In `CombatManager.StartCombat`, after `Player.Target = GetDefaultTarget();`:

```csharp
if (Player.ItemJustAScratchLevel > 0)
{
    Player.CurrentHP = Mathf.Min(Player.MaxHP, Player.CurrentHP + Player.MaxHP * Player.ItemJustAScratchLevel * 0.01f);
}
```

- [ ] **Step 6: Smoke-test coverage**

One check per passive: Казнь dealing bonus damage proportional to target's missing HP, Убийца великанов only triggering when target's MaxHP exceeds attacker's, Рипост arming on evade and consuming on the next attack (not before), Объятия ночи firing only while Stealthed, Просто царапина healing exactly once at `StartCombat`.

- [ ] **Step 7: Run full smoke test, verify pass, commit**

```bash
git add Assets/Scripts/Combat/WeaponAttackState.cs Assets/Scripts/Combat/CombatantRuntime.cs Assets/Scripts/Combat/CombatantFactory.cs Assets/Scripts/Managers/CombatManager.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Wire the 5 Rogue/Barbarian item passives: Riposte, Embrace of Night, Execution, Giant Slayer, Just a Scratch (GDD 3.11)"
```

---

## Task 7: Art import (GDD Part 3 / 10.6)

**Files:**
- Modify: `Assets/Editor/` — reuse or extend whatever existing import-settings script/editor tool handled the 10.6 art pass in the prior session (check `git log --oneline --all -- 'Assets/Art/*'` and the commit referenced in `[[project_gdd_sync_plans]]` memory, plan 6, for the exact mechanism used — likely an `AssetPostprocessor` or a manual `TextureImporter` batch script; match that pattern, don't invent a new one).
- Modify: the 18 item `.asset` files from Task 3 (`icon` field) and the 2 character `.asset` files (`portrait` field, EXCEPT leave both null per Global Constraints).

- [ ] **Step 1: Confirm the already-present art files and their existing folder placement**

The designer's files are ALREADY in the repo (added 2026-08-27, before this plan was written) — no separate delivery is needed despite the prompt saying one would follow:

| File | Existing path | Maps to |
|---|---|---|
| `Knife.png` | `Assets/Art/Items/Weapons/` | Клинок (all 3 tiers, shared icon per 3.8/10.6 convention) |
| `Big_Axe.png` | `Assets/Art/Items/Weapons/` | Двуручный топор (all 3 tiers) |
| `Hood.png` | `Assets/Art/Items/Armor/` | Капюшон (all 3 tiers) |
| `Leather_Armor.png` | `Assets/Art/Items/Armor/` | Кожанка (all 3 tiers) |
| `Belt.png` | `Assets/Art/Items/Armor/` | Пояс (all 3 tiers) |
| `Trophy.png` | `Assets/Art/Items/Armor/` | Трофей (all 3 tiers) |
| `Sasha.png` | `Assets/Art/Characters/` | Плут OR Варвар — **unconfirmed, do not guess** |
| `Violet.png` | `Assets/Art/Characters/` | Плут OR Варвар — **unconfirmed, do not guess** |

The existing folder structure ALREADY matches 10.6's convention exactly (`Assets/Art/Items/Weapons`, `Assets/Art/Items/Armor` — the belt/hood/leather/trophy items all sit under the SAME `Armor` folder as the existing Helmet/Boots/Armor/Shield sprites, there's no need for new subfolders per-category; 10.6 only calls out `Items/(Weapons|Armor|Rings|...)` as top-level buckets, and these are all torso/head-slot replacements, i.e. "Armor" bucket is correct). No new art files need to be requested — Part 3's premise that files are still incoming is already outdated; verify this with the user before treating any ID as missing.

- [ ] **Step 2: Apply import settings to the 8 new files**

Match 10.6 exactly: Texture Type = Sprite (2D and UI), Filter Mode = Point (no filter), Compression = None, Alpha Is Transparency = on, Generate Mip Maps = off, Pixels Per Unit = 64. Use whatever script/method the prior session used (Step 0 of this task) — do not hand-edit `.meta` YAML by guessing the importer's serialized field layout; if no reusable script exists, write a small one-shot `AssetPostprocessor.OnPreprocessTexture` or a `-executeMethod` batch script following the same shape as `RogueBarbarianContentGenerator.cs`.

- [ ] **Step 3: Wire sprites into the item/character assets**

For each of the 6 item icons, set `icon` on ALL 3 tier `.asset` files of that archetype (Клинок Common/Rare/Epic all point at the same `Knife.png` sprite, etc. — matches the existing convention where e.g. `Item_Sword_Common_IronSword`/`Item_Sword_Rare_SteelGladius`/`Item_Sword_Epic_BloodSword` likely all share `Sword.png`; verify this assumption by reading one existing multi-tier weapon's `.asset` files before assuming). Leave `Character_Rogue.portrait` and `Character_Barbarian.portrait` unset.

- [ ] **Step 4: Smoke-test coverage**

Add a check that the 6 new item archetypes' `.icon` fields are non-null across all 3 tiers each (18 checks or one loop), and an explicit check (not a failure — an informational `Info.Add`, since this is a KNOWN gap, not a bug) noting the two character portraits are intentionally unassigned pending designer confirmation.

- [ ] **Step 5: Run full smoke test, verify pass, commit**

```bash
git add Assets/ScriptableObjects/ Assets/Art/ Assets/Editor/ Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Import Rogue/Barbarian item art, character portraits left unassigned pending designer confirmation (GDD 10.6)"
```

---

## Final Task: Whole-plan review

- [ ] Re-read the entire live GDD 3.11 quote captured in this plan's tasks one more time against the final diff (`git diff 190669b..HEAD` or whatever the plan's start commit was) — confirm every numbered bullet has a corresponding implemented behavior or an explicit flagged gap in the final report.
- [ ] Run the full smoke test one final time, capture the OK count.
- [ ] Write the final report per the original prompt's "Что нужно от тебя в конце" section: what's implemented per class, what to playtest (resistance mechanic, two-handed equip, dual-Клинок), and EVERY assumption made in this plan (the Берсерк tick-basis guess, the ItemData multi-bonus-stat field design from Task 3 Step 4, the character-portrait mapping, whether "Боевая регенерация" counts blocked hits, whether a character-select UI is in scope) — do not omit any of them even if they seem minor.
- [ ] Screenshots: this plan's execution has no interactive Unity GUI access in any session so far this project (see `[[project_gdd_sync_plans]]` memory — this is a recurring, structural gap, not something Task 7 or any other task here can close). State this plainly in the final report instead of silently skipping the screenshot request.
