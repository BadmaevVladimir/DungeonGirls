# Armor Wear Verification, Gacha Copy Cycle & Mentor Scaffolding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (1) Add a regression-guarding smoke-test check for the armor-wear rule (GDD 3.3), which was already implemented correctly in the previous session — no code change, verification only. (2) Replace the flat "+1 gear level per extra copy" gacha bonus with the GDD 3.5 4-step alternating cycle (gear → passive → gear → active). (3) Scaffold the mentor mechanic (GDD 1 п.3): main passive granted directly and permanently, remaining known skills (empty for the prototype's stub mentor) fed into the level-up option pool.

**Architecture:** A new pure-function `GachaCopyBonusCalculator` replaces the inline `Mathf.Max(0, copyCount - 1)` in `EquipmentManager`. `RunCharacterProgress` gains `ApplyGachaStartingBonus` to seed starting passive/active skill levels from the same calculator. A new `MentorData` ScriptableObject holds the stub mentor's main passive (as a direct float bonus, not a levelable skill-dictionary entry — see Task 3's design note) and an `otherKnownSkills` list (empty for the prototype). `LevelUpManager` gains a `MentorSkillPool` list merged into its existing general+class pool.

**Tech Stack:** Unity 6000.5.8f1, C#.

**Spec:** ГДД Данжнгерлс (рабочая версия), sections 1 (п.3), 3.1, 3.3, 3.5. User sync prompt items 6, 7, 8.

## Global Constraints

- Armor-wear priority order (already correct in `DamageCalculator.ApplyPhysicalDamage`, verify not regress): full pierce (−2) > normal pierce (−1) > wear-on-block (−1, 0 HP damage) > full block (no change). Threshold for wear: incoming damage ≥ 50% of current armor but < 100% of it.
- Gacha copy cycle (GDD 3.5): each copy beyond the first advances one step in a repeating 4-step cycle: 1) +1 starting gear level, 2) +1 starting unique-passive level (cap: passive max level, 5), 3) +1 starting gear level, 4) +1 starting unique-active level (cap: active max level, 3). Gear level itself is uncapped (GDD 3.10 — "решено... максимальный уровень предмета остаётся без потолка").
- Mentor (GDD 1 п.3): main passive skill is granted "сразу и всегда" at a fixed, non-upgradeable effect (the prototype's stub mentor's main passive, "Магнум Опус", is +10% magic damage). The mentor's OTHER known skills (empty for the prototype stub) are added to the level-up window's option pool, counting toward the same 5-slot known-passive limit as general/class skills.

---

### Task 1: Verify armor-wear rule with an explicit smoke-test check (no production code change)

**Files:**
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `DamageCalculator.ApplyPhysicalDamage(CombatantRuntime, float)` (existing, unchanged — confirmed correct by reading `Assets/Scripts/Combat/DamageCalculator.cs:27-46` before writing this plan: the 4-tier priority check already matches GDD 3.3 exactly, including the ≥50%-but-blocked "wear" case and its `ArmorWornOnBlock` result flag).

- [ ] **Step 1: Add the missing wear-case checks to `RunPureLogicChecks`**

The existing checks in `PlayModeSmokeTest.cs` (lines ~98-105) only cover full-block-no-wear and normal-pierce. Add the two missing cases from the GDD's own worked example (armor=10):

```csharp
// RunPureLogicChecks() — add after the existing 3.3 блокировка/пробитие checks:

// 3.3 "Износ брони при блокировке": урон >= 0.5×брони но < брони — 0 урона по HP, но -1 брони.
var wearTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 20f, CurrentHP = 20f };
var wearResult = DamageCalculator.ApplyPhysicalDamage(wearTarget, 6f); // >= 5 (0.5*10), < 10
Check(wearResult.WasBlocked && wearResult.ArmorWornOnBlock && wearResult.DamageToHP == 0f && wearTarget.PhysicalDefenseCurrent == 9f,
    $"3.3 износ при блокировке (урон=6, броня=10): WasBlocked={wearResult.WasBlocked}, ArmorWornOnBlock={wearResult.ArmorWornOnBlock}, DamageToHP={wearResult.DamageToHP}, Defense={wearTarget.PhysicalDefenseCurrent} (ожидалось true/true/0/9)");

var noWearTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 20f, CurrentHP = 20f };
var noWearResult = DamageCalculator.ApplyPhysicalDamage(noWearTarget, 3f); // < 5 (0.5*10)
Check(noWearResult.WasBlocked && !noWearResult.ArmorWornOnBlock && noWearTarget.PhysicalDefenseCurrent == 10f,
    $"3.3 полная блокировка без последствий (урон=3, броня=10): ArmorWornOnBlock={noWearResult.ArmorWornOnBlock}, Defense={noWearTarget.PhysicalDefenseCurrent} (ожидалось false/10)");

// 3.3 "Полное пробитие": урон >= 2×брони — -2 брони вместо -1.
var fullPierceTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 20f, CurrentHP = 20f };
var fullPierceResult = DamageCalculator.ApplyPhysicalDamage(fullPierceTarget, 22f); // >= 20 (2*10)
Check(!fullPierceResult.WasBlocked && fullPierceResult.DamageToHP == 12f && fullPierceTarget.PhysicalDefenseCurrent == 8f,
    $"3.3 полное пробитие (урон=22, броня=10): DamageToHP={fullPierceResult.DamageToHP}, Defense={fullPierceTarget.PhysicalDefenseCurrent} (ожидалось 12/8)");

var normalPierceTarget = new CombatantRuntime { PhysicalDefenseMax = 10f, PhysicalDefenseCurrent = 10f, MaxHP = 20f, CurrentHP = 20f };
var normalPierceResult = DamageCalculator.ApplyPhysicalDamage(normalPierceTarget, 12f); // >= 10, < 20
Check(!normalPierceResult.WasBlocked && normalPierceResult.DamageToHP == 2f && normalPierceTarget.PhysicalDefenseCurrent == 9f,
    $"3.3 обычное пробитие (урон=12, броня=10): DamageToHP={normalPierceResult.DamageToHP}, Defense={normalPierceTarget.PhysicalDefenseCurrent} (ожидалось 2/9)");
```

These four cases together reproduce the GDD's own worked example table verbatim (armor=10; damage 22/12/6/3 → full pierce/−2, normal pierce/−1, wear/−1, full block/0), closing the gap where only 2 of the 4 tiers had regression coverage.

- [ ] **Step 2: Run the smoke test and confirm PASS**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile -
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Add full regression coverage for the 4-tier armor-wear rule (GDD 3.3, already correct — verification only)"
```

---

### Task 2: Gacha copy 4-step alternating cycle

**Files:**
- Create: `Assets/Scripts/Progression/GachaCopyBonusCalculator.cs`
- Modify: `Assets/Scripts/Managers/EquipmentManager.cs`
- Modify: `Assets/Scripts/Progression/RunCharacterProgress.cs`
- Modify: `Assets/Scripts/Managers/CharacterManager.cs`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Produces: `GachaCopyBonusCalculator.GachaBonus { int GearLevelBonus; int PassiveLevelBonus; int ActiveLevelBonus; }`, `GachaCopyBonusCalculator.CalculateBonus(int copyCount)`. `RunCharacterProgress.ApplyGachaStartingBonus(GachaCopyBonusCalculator.GachaBonus)`. `EquipmentManager.GetEffectiveStartingEquipment` signature UNCHANGED (still takes `copyCount`, now computes the bonus internally via the calculator instead of the old flat formula).

- [ ] **Step 1: Create `GachaCopyBonusCalculator.cs`**

```csharp
// Assets/Scripts/Progression/GachaCopyBonusCalculator.cs
using UnityEngine;

// 3.5 [ОБНОВЛЕНО 2026-08-25]: заменяет старую формулу "+1 снаряжение за каждую копию сверх
// первой". Каждая лишняя копия даёт один шаг в цикле из 4: снаряжение -> пассивка -> снаряжение
// -> активка -> повтор. Копии считаются от 2-й общей копии (1-я копия = базовое владение, без
// бонуса) — т.е. i-й ЛИШНЕЙ копии (i = 1, 2, 3...) соответствует шаг (i-1) % 4.
public static class GachaCopyBonusCalculator
{
    public struct GachaBonus
    {
        public int GearLevelBonus;
        public int PassiveLevelBonus;
        public int ActiveLevelBonus;
    }

    // maxPassiveLevelBonus/maxActiveLevelBonus — потолки УРОВНЯ (не бонуса): пассивный навык
    // персонажа стартует с 1 ур. и максимум 5 (3.1) -> бонус клампится на 4; активный стартует
    // с 1 ур. и максимум 3 (3.1) -> бонус клампится на 2. Снаряжение без потолка (3.10).
    public static GachaBonus CalculateBonus(int copyCount)
    {
        int extraCopies = Mathf.Max(0, copyCount - 1);
        var bonus = new GachaBonus();

        for (int i = 0; i < extraCopies; i++)
        {
            switch (i % 4)
            {
                case 0: bonus.GearLevelBonus++; break;
                case 1: bonus.PassiveLevelBonus++; break;
                case 2: bonus.GearLevelBonus++; break;
                case 3: bonus.ActiveLevelBonus++; break;
            }
        }

        bonus.PassiveLevelBonus = Mathf.Min(bonus.PassiveLevelBonus, 4);
        bonus.ActiveLevelBonus = Mathf.Min(bonus.ActiveLevelBonus, 2);

        return bonus;
    }
}
```

- [ ] **Step 2: Use it in `EquipmentManager.GetEffectiveStartingEquipment`**

```csharp
// Assets/Scripts/Managers/EquipmentManager.cs — replace the bonus calculation line:
// was: int bonus = BuildingCatalog.ForgeStartingEquipmentBonus(forgeLevel) + Mathf.Max(0, copyCount - 1);
int bonus = BuildingCatalog.ForgeStartingEquipmentBonus(forgeLevel) + GachaCopyBonusCalculator.CalculateBonus(copyCount).GearLevelBonus;
```

- [ ] **Step 3: Add `RunCharacterProgress.ApplyGachaStartingBonus`**

```csharp
// Assets/Scripts/Progression/RunCharacterProgress.cs — add:

// 3.5: применяет бонус стартового уровня уникальных пассивного/активного навыков от лишних копий
// гачи (см. GachaCopyBonusCalculator). Клампится потолком уровня навыка (5 пассивный / 3 активный,
// см. 3.1) — CalculateBonus уже клампит сам бонус, но Min() здесь на итоговом уровне на случай,
// если maxLevel ассета персонажа когда-то станет отличаться от 5/3 (защита от рассинхрона данных).
public void ApplyGachaStartingBonus(GachaCopyBonusCalculator.GachaBonus bonus)
{
    UniquePassiveLevel = Mathf.Min(Character.uniquePassiveSkill.maxLevel, 1 + bonus.PassiveLevelBonus);
    UniqueActiveLevel = Mathf.Min(Character.uniqueActiveSkill.maxLevel, 1 + bonus.ActiveLevelBonus);
}
```

- [ ] **Step 4: Wire it into `CharacterManager.BeginRun`**

```csharp
// Assets/Scripts/Managers/CharacterManager.cs, inside BeginRun, was:
// if (equipmentManager != null)
// {
//     int forgeLevel = saveManager != null ? saveManager.GetBuildingLevel(BuildingType.Forge) : 0;
//     int copyCount = saveManager != null ? saveManager.GetCharacterCopies(character.characterName) : 0;
//     EquippedItems = equipmentManager.GetEffectiveStartingEquipment(character, forgeLevel, copyCount);
// }
// becomes:
if (equipmentManager != null)
{
    int forgeLevel = saveManager != null ? saveManager.GetBuildingLevel(BuildingType.Forge) : 0;
    int copyCount = saveManager != null ? saveManager.GetCharacterCopies(character.characterName) : 0;
    EquippedItems = equipmentManager.GetEffectiveStartingEquipment(character, forgeLevel, copyCount);
    Progress.ApplyGachaStartingBonus(GachaCopyBonusCalculator.CalculateBonus(copyCount));
}
```

(Note: `Combatant = CombatantFactory.CreatePlayerCombatant(...)` is called AFTER this block in the existing method — confirmed by reading the file — so the elevated `Progress.UniquePassiveLevel`/`UniqueActiveLevel` are already in place before the combatant is built. No reordering needed.)

- [ ] **Step 5: Add smoke-test checks**

```csharp
// RunPureLogicChecks():
var bonus1Copy = GachaCopyBonusCalculator.CalculateBonus(1); // базовое владение, 0 лишних копий
Check(bonus1Copy.GearLevelBonus == 0 && bonus1Copy.PassiveLevelBonus == 0 && bonus1Copy.ActiveLevelBonus == 0, "3.5 1 копия = 0 бонуса");

var bonus2Copies = GachaCopyBonusCalculator.CalculateBonus(2); // 1-я лишняя -> +1 снаряжение
Check(bonus2Copies.GearLevelBonus == 1 && bonus2Copies.PassiveLevelBonus == 0 && bonus2Copies.ActiveLevelBonus == 0, "3.5 2 копии = +1 снаряжение");

var bonus3Copies = GachaCopyBonusCalculator.CalculateBonus(3); // 2-я лишняя -> +1 пассивка
Check(bonus3Copies.GearLevelBonus == 1 && bonus3Copies.PassiveLevelBonus == 1 && bonus3Copies.ActiveLevelBonus == 0, "3.5 3 копии = +1 снаряжение, +1 пассивка");

var bonus4Copies = GachaCopyBonusCalculator.CalculateBonus(4); // 3-я лишняя -> +1 снаряжение (итого 2)
Check(bonus4Copies.GearLevelBonus == 2 && bonus4Copies.PassiveLevelBonus == 1 && bonus4Copies.ActiveLevelBonus == 0, "3.5 4 копии = +2 снаряжение, +1 пассивка");

var bonus5Copies = GachaCopyBonusCalculator.CalculateBonus(5); // 4-я лишняя -> +1 активка
Check(bonus5Copies.GearLevelBonus == 2 && bonus5Copies.PassiveLevelBonus == 1 && bonus5Copies.ActiveLevelBonus == 1, "3.5 5 копий = +2 снаряжение, +1 пассивка, +1 активка");

var bonus6Copies = GachaCopyBonusCalculator.CalculateBonus(6); // 5-я лишняя -> новый цикл, +1 снаряжение (итого 3)
Check(bonus6Copies.GearLevelBonus == 3 && bonus6Copies.PassiveLevelBonus == 1 && bonus6Copies.ActiveLevelBonus == 1, "3.5 6 копий = +3 снаряжение (новый цикл начался)");

// Клампы: 17 лишних копий пассивки было бы >4 без клампа (17/4 = 4 полных цикла проходят шаг 1 4 раза -> ровно 4, границу проверим бонусом побольше).
var bonusManyCopies = GachaCopyBonusCalculator.CalculateBonus(1 + 4 * 10); // 40 лишних копий -> 10 полных циклов -> 10 пассивки без клампа
Check(bonusManyCopies.PassiveLevelBonus == 4, $"3.5 кламп бонуса пассивки на 4 (макс. ур. 5): {bonusManyCopies.PassiveLevelBonus}");
Check(bonusManyCopies.ActiveLevelBonus == 2, $"3.5 кламп бонуса активки на 2 (макс. ур. 3): {bonusManyCopies.ActiveLevelBonus}");
```

- [ ] **Step 6: Run the full smoke test and confirm PASS**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile -
```

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Progression/GachaCopyBonusCalculator.cs Assets/Scripts/Managers/EquipmentManager.cs Assets/Scripts/Progression/RunCharacterProgress.cs Assets/Scripts/Managers/CharacterManager.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Replace flat gacha-copy gear bonus with 4-step alternating cycle (GDD 3.5)"
```

---

### Task 3: Mentor scaffolding (main passive + skill pool feed)

**Files:**
- Create: `Assets/Scripts/Data/MentorData.cs`
- Create: `Assets/ScriptableObjects/Mentors/Mentor_MagePlaceholder.asset(.meta)`
- Modify: `Assets/Scripts/Combat/CombatantRuntime.cs`
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs`
- Modify: `Assets/Scripts/Managers/CombatManager.cs`
- Modify: `Assets/Scripts/Progression/RunCharacterProgress.cs`
- Modify: `Assets/Scripts/Managers/LevelUpManager.cs`
- Modify: `Assets/Scripts/Managers/CharacterManager.cs`
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Produces: `MentorData` (ScriptableObject: `mentorName`, `mentorClass`, `mainPassiveSkill` (PassiveSkillData, for display/log only), `mainPassiveMagicDamageBonusPercent` (float, the mechanical effect), `otherKnownSkills` (`PassiveSkillData[]`, empty for the prototype stub)). `RunCharacterProgress.MentorMagicDamageBonusPercent` (float). `CombatantRuntime.MentorMagicDamageBonusPercent` (float). `LevelUpManager.MentorSkillPool` (`List<PassiveSkillData>`).

**Design note (judgment call, not a spec ambiguity):** GDD 1 п.3 fully specifies the mentor's main passive numerically ("+10% к магическому урону персонажа, уровень 1, не прокачивается"). Rather than representing it as an entry in `RunCharacterProgress.KnownSkillLevels` (which would require either capping the skill asset's `maxLevel` at 1 to block level-up upgrade offers, or special-casing it out of `LevelUpManager`'s upgrade-candidate scan — both are indirect ways to express "not levelable"), this plan represents it as a direct, permanent float bonus applied once at run start. This keeps "not upgradeable" true by construction (there's no dictionary entry for `LevelUpManager` to ever offer an upgrade on) while still using a real `PassiveSkillData` asset for its name/description text in UI and logs. The mentor's OTHER skills (the actual pool-feed mechanic) still use the existing `KnownSkillLevels`-based system normally, matching how the GDD says those specifically compete for the same 5 slots as general/class skills.

- [ ] **Step 1: Create `MentorData.cs`**

```csharp
// Assets/Scripts/Data/MentorData.cs
using UnityEngine;

// 1, п.3: наставник передаёт СВОЙ ОСНОВНОЙ пассивный навык напрямую (сразу и всегда, без
// возможности прокачки при передаче) + добавляет ОСТАЛЬНЫЕ известные ему навыки в пул вариантов
// окна левел-апа нового персонажа (кросс-классовость — намеренная фича, см. 3.5). Для прототипа —
// один наставник-заглушка класса Маг, чей единственный известный навык — как раз основной
// пассивный ("Магнум Опус") — поэтому otherKnownSkills для прототипа пуст (нечего добавлять в пул).
[CreateAssetMenu(fileName = "NewMentor", menuName = "DungeonGirls/Mentor")]
public class MentorData : ScriptableObject
{
    public string mentorName;
    public CharacterClass mentorClass;

    // Для отображения/лога — сама механика применяется через mainPassiveMagicDamageBonusPercent
    // (см. design note в плане реализации: не левелится, поэтому не хранится как обычная
    // прокачиваемая запись в RunCharacterProgress.KnownSkillLevels).
    public PassiveSkillData mainPassiveSkill;
    public float mainPassiveMagicDamageBonusPercent;

    public PassiveSkillData[] otherKnownSkills;
}
```

- [ ] **Step 2: Create the stub mentor asset**

Generate a fresh GUID (`openssl rand -hex 16`) and write:

```yaml
# Assets/ScriptableObjects/Mentors/Mentor_MagePlaceholder.asset
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
  m_Script: {fileID: 11500000, guid: <MentorData script guid — see Step 2a>, type: 3}
  m_Name: Mentor_MagePlaceholder
  m_EditorClassIdentifier: Assembly-CSharp::MentorData
  mentorName: Наставник-заглушка (Маг)
  mentorClass: 0
  mainPassiveSkill: {fileID: 11400000, guid: <Skill_MagnumOpus's fresh guid — see Step 3>, type: 2}
  mainPassiveMagicDamageBonusPercent: 10
  otherKnownSkills: []
```
Paired `.meta` follows the same shape as every other asset in this plan/repo.

**Step 2a:** `MentorData`'s own script GUID doesn't exist yet — Unity assigns it automatically when the `.cs` file is first imported (from Step 1). Since this plan is being executed by an agent working directly with files (not through the Unity Editor UI), the script GUID must be read from the `.meta` file Unity generates for `MentorData.cs` on the next import. **Practical execution order:** create `MentorData.cs` (Step 1) and let Unity import it (e.g. by running the batch smoke test once, which forces a script recompile) BEFORE writing this asset file, then read `Assets/Scripts/Data/MentorData.cs.meta`'s `guid:` value to fill in `<MentorData script guid>` above. (`CharacterClass.Warrior = 0` — `mentorClass: 0` above is a placeholder for "Маг" which doesn't exist as an enum value yet; see Step 2b.)

**Step 2b:** `CharacterClass` enum currently only has `Warrior` (see `Assets/Scripts/Data/Enums.cs`). The mentor is class "Маг" per GDD, which has no gameplay-mechanical use in the prototype (no Mage-class player character or skill pool exists) — add `Mage` to the enum purely for data-fidelity on this one asset field, with no other code depending on it:

```csharp
// Assets/Scripts/Data/Enums.cs
public enum CharacterClass
{
    Warrior,
    Mage
}
```
Then use `mentorClass: 1` in the asset YAML above.

- [ ] **Step 3: Create the `Skill_MagnumOpus` passive-skill asset (display/log text only — see design note)**

Same YAML shape as the other `PassiveSkillData` assets in this session (Plan 2 Task 2), script guid `33564269414bad2439b83f3f7597e022`, `category: 2` (`SkillCategory.Unique`, matching how the player's own unique passive would be categorized — the mentor's main passive is conceptually the same kind of thing, just on a different character):

```yaml
# Assets/ScriptableObjects/Skills/Unique/Skill_MagnumOpus.asset
  m_Name: Skill_MagnumOpus
  skillName: Магнум Опус
  category: 2
  effectDescription: Основной пассивный навык наставника-заглушки (класс Маг). +10% к магическому урону персонажа. Уровень 1, не прокачивается при передаче подопечному — передаётся сразу и всегда.
  maxLevel: 1
```
(`maxLevel: 1` here is honest documentation of "not levelable", even though the mechanical effect bypasses the levelable dictionary entirely per the design note — keeps the asset self-describing if anyone inspects it in the Editor.)

- [ ] **Step 4: Add `MentorMagicDamageBonusPercent` to `RunCharacterProgress` and `CombatantRuntime`**

```csharp
// Assets/Scripts/Progression/RunCharacterProgress.cs — add:
public float MentorMagicDamageBonusPercent;
```

```csharp
// Assets/Scripts/Combat/CombatantRuntime.cs — add alongside the other skill-derived fields:
public float MentorMagicDamageBonusPercent;
```

- [ ] **Step 5: Copy it onto the combatant in `CombatantFactory.ApplyCharacterSkills`**

```csharp
// Assets/Scripts/Combat/CombatantFactory.cs, inside ApplyCharacterSkills, add alongside the other
// `runtime.SkillXxxLevel = progress.GetSkillLevel(...)` assignments at the end of the method:
runtime.MentorMagicDamageBonusPercent = progress.MentorMagicDamageBonusPercent;
```

- [ ] **Step 6: Apply the bonus in `CombatManager.ResolveAttack`**

```csharp
// Assets/Scripts/Managers/CombatManager.cs, inside ResolveAttack, right after:
// float damage = Random.Range(weapon.DamageMin, weapon.DamageMax) * damageMultiplier;
// add:
if (attacker.IsPlayer && weapon.DamageType == DamageType.Magical && attacker.MentorMagicDamageBonusPercent > 0f)
{
    damage *= 1f + attacker.MentorMagicDamageBonusPercent / 100f;
}
```

- [ ] **Step 7: Wire mentor selection + pool feed into `RunFlowController`**

```csharp
// Assets/Scripts/UI/RunFlowController.cs — add a serialized field near jenniferCharacter:
[SerializeField] MentorData mentorData;
```

```csharp
// Inside RunLoop(), after characterManager.BeginRun(...) and before campManager.BeginRun():
characterManager.Progress.MentorMagicDamageBonusPercent = mentorData != null ? mentorData.mainPassiveMagicDamageBonusPercent : 0f;
characterManager.RefreshCombatStats(); // применить бонус к уже собранному Combatant

levelUpManager.MentorSkillPool = mentorData != null && mentorData.otherKnownSkills != null
    ? new List<PassiveSkillData>(mentorData.otherKnownSkills)
    : new List<PassiveSkillData>();

if (mentorData != null)
{
    LogEvent($"[Наставник] {mentorData.mentorName} передаёт «{(mentorData.mainPassiveSkill != null ? mentorData.mainPassiveSkill.skillName : "?")}»: +{mentorData.mainPassiveMagicDamageBonusPercent:F0}% магического урона.");
}
```

(Reminder: `Progress.ApplyGachaStartingBonus` from Task 2 already runs inside `BeginRun`, before this point — no ordering conflict, both just set different fields on the same `Progress` object.)

- [ ] **Step 8: Add `LevelUpManager.MentorSkillPool` and merge it into the pool**

```csharp
// Assets/Scripts/Managers/LevelUpManager.cs — add near GeneralSkillPool/WarriorSkillPool:
public List<PassiveSkillData> MentorSkillPool = new List<PassiveSkillData>();
```

```csharp
// Inside GenerateLevelUpOptions, was:
// var pool = GeneralSkillPool.Concat(GetClassPool(progress.Character.characterClass)).ToList();
// becomes:
var pool = GeneralSkillPool.Concat(GetClassPool(progress.Character.characterClass)).Concat(MentorSkillPool).ToList();
```

(This single `.Concat` is the entire "pool" mechanic — mentor skills already flow through the exact same `IsSkillKnown`/`HasFreeSkillSlot`/5-slot-limit logic as general/class skills immediately below this line, satisfying "учитываются в том же общем лимите" without any further change.)

- [ ] **Step 9: Add smoke-test checks**

```csharp
// RunPlayModeChecks() — exercise the magic-damage bonus end-to-end via a live CombatManager:
var mentorTestGO = new GameObject("SmokeTest_MentorCombat");
var mentorTestCombatManager = mentorTestGO.AddComponent<CombatManager>();

var mentorTestPlayer = new CombatantRuntime { IsPlayer = true, MaxHP = 100f, CurrentHP = 100f, MentorMagicDamageBonusPercent = 10f };
mentorTestPlayer.Weapons.Add(new WeaponAttackState { DamageMin = 10f, DamageMax = 10f, DamageType = DamageType.Magical, AttackSpeed = 1f });
var mentorTestDummy = new CombatantRuntime { IsPlayer = false, MaxHP = 1000f, CurrentHP = 1000f, MagicShieldMax = 0f };

mentorTestCombatManager.StartCombat(mentorTestPlayer, new List<CombatantRuntime> { mentorTestDummy });
mentorTestCombatManager.Tick(1.01f);
// 10 базового урона * 1.10 (Магнум Опус) = 11, весь урон должен пройти по HP (0 маг. щита у болвана).
Check(mentorTestDummy.CurrentHP <= 989f && mentorTestDummy.CurrentHP >= 988f,
    $"1/п.3 Магнум Опус +10% маг. урона применяется: HP болвана = {mentorTestDummy.CurrentHP} (ожидалось ~989, т.е. 1000-11)");
UnityEngine.Object.DestroyImmediate(mentorTestGO);

// Пул наставника сливается с общим/классовым пулом левел-апа.
var levelUpManagerGO = new GameObject("SmokeTest_MentorPool");
var testLevelUpManager = levelUpManagerGO.AddComponent<LevelUpManager>();
var fakeMentorSkill = ScriptableObject.CreateInstance<PassiveSkillData>();
fakeMentorSkill.skillName = "ТестНавыкНаставника";
fakeMentorSkill.maxLevel = 5;
testLevelUpManager.MentorSkillPool = new List<PassiveSkillData> { fakeMentorSkill };
testLevelUpManager.GeneralSkillPool = new List<PassiveSkillData>();
testLevelUpManager.WarriorSkillPool = new List<PassiveSkillData>();

var fakeCharacter = ScriptableObject.CreateInstance<CharacterData>();
fakeCharacter.characterClass = CharacterClass.Warrior;
fakeCharacter.uniquePassiveSkill = ScriptableObject.CreateInstance<PassiveSkillData>();
fakeCharacter.uniquePassiveSkill.maxLevel = 5;
fakeCharacter.uniqueActiveSkill = ScriptableObject.CreateInstance<ActiveSkillData>();
fakeCharacter.uniqueActiveSkill.maxLevel = 3;
var fakeProgress = new RunCharacterProgress(fakeCharacter);

var mentorOptions = testLevelUpManager.GenerateLevelUpOptions(fakeProgress);
Check(mentorOptions.Exists(o => o.Skill == fakeMentorSkill), "3.5/1п.3 навык из пула наставника попадает в варианты левел-апа");

UnityEngine.Object.DestroyImmediate(levelUpManagerGO);
UnityEngine.Object.DestroyImmediate(fakeMentorSkill);
UnityEngine.Object.DestroyImmediate(fakeCharacter.uniquePassiveSkill);
UnityEngine.Object.DestroyImmediate(fakeCharacter.uniqueActiveSkill);
UnityEngine.Object.DestroyImmediate(fakeCharacter);
```

- [ ] **Step 10: Run the full smoke test and confirm PASS**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile -
```

- [ ] **Step 11: Assign the mentor asset in the Inspector**

Select the `RunFlowController` GameObject in the scene, drag `Mentor_MagePlaceholder` into the new `Mentor Data` field.

- [ ] **Step 12: Commit**

```bash
git add Assets/Scripts/Data/MentorData.cs Assets/Scripts/Data/Enums.cs Assets/ScriptableObjects/Mentors/ Assets/ScriptableObjects/Skills/Unique/Skill_MagnumOpus.asset* Assets/Scripts/Combat/CombatantRuntime.cs Assets/Scripts/Combat/CombatantFactory.cs Assets/Scripts/Managers/CombatManager.cs Assets/Scripts/Progression/RunCharacterProgress.cs Assets/Scripts/Managers/LevelUpManager.cs Assets/Scripts/UI/RunFlowController.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "Scaffold mentor mechanic: main passive as permanent bonus + skill pool feed (GDD 1 п.3)"
```

---

## Self-Review Notes

- **Spec coverage:** item 6 (armor wear) → Task 1 (verification only, already correct). Item 7 (gacha cycle) → Task 2. Item 8 (mentor) → Task 3.
- **Judgment call flagged, not silently resolved:** the mentor's main passive representation (direct float bonus vs. a capped-at-1 `KnownSkillLevels` entry) — see Task 3's design note. Both approaches are spec-compliant; the direct-bonus approach was chosen for simplicity and to make "not upgradeable" true by construction rather than by a cap value that could be edited by mistake later.
- **`CharacterClass.Mage` addition:** purely a data-fidelity addition for the mentor asset's `mentorClass` field — no other system reads or branches on it. If a future session gives the mentor real gameplay mechanics tied to being a Mage, that's new scope, not covered here.
