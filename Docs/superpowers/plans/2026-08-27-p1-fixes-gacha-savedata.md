# P1-фиксы, гача 11.1, SaveData/ветераны — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the 3 P1 bugs blocking a real 3-class demo (Тень leak, hardcoded character-select, hardcoded active-skill config), rewrite the gacha per GDD 11.1 (15% character / 85% meta-currency, no items), and extend/harden SaveData (schema fields + atomic writes + migration) so a completed run adds a veteran and run count.

**Architecture:** Introduce a stable `CharacterData.characterId` (lowercase, e.g. `"jennifer"`) as the identity key shared with Codex's VN work (`seenVNScenes: { characterId: sceneId[] }`), used everywhere SaveData persists per-character state. Land the SaveData schema/atomicity foundation *before* rewriting gacha persistence, since gacha now writes character-copy counts keyed by `characterId` — writing gacha against the old `characterName`-keyed scheme and then reworking it a task later would be wasted work. Character-select becomes a real screen backed by a `[SerializeField] CharacterData[] selectableCharacters` on `RunFlowController`, replacing the single `jenniferCharacter` field. Active-skill configuration and the manual Berserk toggle branch on `characterManager.Progress.Character.characterClass` instead of reading a fixed asset. Gacha's chest-reveal animation is extracted from `RunFlowController` into a shared static helper so `HubManager`'s gacha screen can reuse the exact same tween/burst/reel mechanics per GDD 11.1's explicit "reuses the chest animation" instruction, instead of a second parallel implementation.

**Tech Stack:** Unity 6000.5.8f1, C#, UI Toolkit (UXML/USS), DOTween, JsonUtility-based save file in `Application.persistentDataPath`.

**Spec:** User prompt (2026-08-27, "фикс P1-багов, гача, SaveData → ветераны"); GDD sections 3.11 (Rogue/Barbarian), 4.5, 8.1-8.6, 9.2-9.4, 11.1, 10.4/10.6 (fetched verbatim from Notion page `3c10227a-2824-81bb-a9c0-c2f212bddbfb` on 2026-08-27); Codex audit page `3c90227a-2824-8112-9c9b-f6b70c02a0ae` (2026-08-27).

## Global Constraints

- Do NOT touch `VNManager.PlayScene`/`PlayQuest`, VN/dialogue content, or scene-trigger logic — Codex's zone. `HubManager`/`RunFlowController`/gacha logic/`CombatantFactory` are explicitly listed in Codex's journal as **forbidden for Codex this round** — safe for us to edit.
- `seenVNScenes` gets a field on `SaveData` (`Dictionary<string, List<string>>`-equivalent, keyed by `characterId`) but no read/write logic beyond existing-and-empty — Codex populates it.
- GDD 11.1: gacha pool is 15% character (5% each Jennifer/Rogue/Barbarian) + 85% meta-currency (**not** gacha-currency) sized by the 62/35/3 → 20/50/150 rarity table reused from `RewardManager.RollItemRarity`. No items in the gacha pool at all — remove the item branch entirely.
- GDD 9.4: `SaveData` needs `saveVersion: int`, `metaCurrency: int`, `gachaCurrency: int` (already present), `buildingLevels` (already present as 3 flat fields — keep, don't restructure), `veteranDeck: VeteranCharacter[]`, `gachaOwnedCharacters: { characterId: copiesCount }[]` (rename/rekey existing `characterCopies`), `characterRunCounts: { characterId: completedRunsCount }[]`, `seenVNScenes: { characterId: sceneId[] }`.
- GDD 4.5 (context only, not in this plan's scope to fix): combat statuses fully reset at `EndCombat` except magic shield (already resets) and armor wear (persists by design) — Codex flagged this as P3/open design question; GDD text fetched today marks it "Решено" but the fix itself is not part of this plan's task list, only recorded for the report.
- GDD 3.11 confirms in the exact fetched text: "Активка «Берсерк» — НЕ работает как обычный активный навык (нет кулдауна, нет авто-режима, нет длительности) — это переключатель вкл/выкл" — `CombatManager.SetBerserkActive`/`TryActivateUniqueActiveSkill`'s hard bail already implement this correctly; only the UI/integration layer (`RunFlowController`) is missing.
- Existing `Assets/Editor/PlayModeSmokeTest.cs` must still pass in full (currently 232 OK per Codex's 2026-08-27 batchmode run) after every task, plus the new checks this plan adds.
- Don't rename `AddCharacterCopy`/`GetCharacterCopies` call sites outside what's listed per task — check callers with Grep before editing signatures.
- Follow existing code conventions: Russian comments explaining *why*, `[DRAFT]`/`(ФИКС)`/`(НОВОЕ)` tags for open questions or fixes, GDD section references in comments.

---

## Task 1: Fix "Тень"/"Дымовая граната" leaking to non-Rogue classes

**Files:**
- Modify: `Assets/Scripts/Combat/CombatantFactory.cs:217-227`
- Test: `Assets/Editor/PlayModeSmokeTest.cs` (add to `RunPureLogicChecks`)

**Interfaces:**
- Consumes: `RunCharacterProgress.Character.characterClass` (existing), `RunCharacterProgress.UniquePassiveLevel`/`UniqueActiveLevel` (existing, start at 1 for any character).
- Produces: `CombatantRuntime.UniqueShadowLevel`/`UniqueSmokeBombLevel` are now 0 for any `characterClass != CharacterClass.Rogue`, matching the existing `UniqueChampionOfTheTribeLevel`/`UniqueBerserkLevel` pattern for Barbarian at lines 250-252.

- [ ] **Step 1: Write the failing test**

Add to `RunPureLogicChecks()` in `Assets/Editor/PlayModeSmokeTest.cs`, right after the existing 3.11 Barbarian-related checks (search for `UniqueChampionOfTheTribeLevel` or add near the end of the method, before the closing brace):

```csharp
        // 3.11 (ФИКС, Codex P1 2026-08-27): "Тень"/"Дымовая граната" — уникальные навыки Плута,
        // раньше копировались БЕЗ проверки класса (в отличие от уникальных навыков Варвара).
        // Не-Плут, получивший Скрытность через "Ускользание" (SlipAway) или наставника, не должен
        // получать бонус уклонения "Тени" — только у Плута UniqueShadowLevel может быть > 0.
        var nonRogueCharacter = ScriptableObject.CreateInstance<CharacterData>();
        nonRogueCharacter.characterName = "ТестВарвар";
        nonRogueCharacter.characterClass = CharacterClass.Barbarian;
        nonRogueCharacter.baseHealth = 100;
        var nonRogueProgress = new RunCharacterProgress(nonRogueCharacter);
        // UniquePassiveLevel/UniqueActiveLevel стартуют с 1 у ЛЮБОГО персонажа (см. RunCharacterProgress) —
        // именно поэтому безусловное копирование раньше давало Тени ненулевой уровень у Варвара.
        var nonRogueCombatant = CombatantFactory.CreatePlayerCombatant(nonRogueCharacter, 1, nonRogueProgress);
        Check(nonRogueCombatant.UniqueShadowLevel == 0 && nonRogueCombatant.UniqueSmokeBombLevel == 0,
            $"3.11 ФИКС «Тень»/«Дымовая граната» не текут на не-Плута: UniqueShadowLevel={nonRogueCombatant.UniqueShadowLevel}, UniqueSmokeBombLevel={nonRogueCombatant.UniqueSmokeBombLevel} (ожидалось 0/0)");
        UnityEngine.Object.DestroyImmediate(nonRogueCharacter);

        var rogueCharacter = ScriptableObject.CreateInstance<CharacterData>();
        rogueCharacter.characterName = "ТестПлут";
        rogueCharacter.characterClass = CharacterClass.Rogue;
        rogueCharacter.baseHealth = 100;
        var rogueProgress = new RunCharacterProgress(rogueCharacter);
        var rogueCombatant = CombatantFactory.CreatePlayerCombatant(rogueCharacter, 1, rogueProgress);
        Check(rogueCombatant.UniqueShadowLevel == 1 && rogueCombatant.UniqueSmokeBombLevel == 1,
            $"3.11 ФИКС «Тень»/«Дымовая граната» остаются у Плута: UniqueShadowLevel={rogueCombatant.UniqueShadowLevel}, UniqueSmokeBombLevel={rogueCombatant.UniqueSmokeBombLevel} (ожидалось 1/1, т.к. UniquePassiveLevel/UniqueActiveLevel стартуют с 1)");
        UnityEngine.Object.DestroyImmediate(rogueCharacter);
```

- [ ] **Step 2: Run test to verify it fails**

Run via Unity batchmode (matches how Codex ran it):
```
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile "C:\Unity Projects\DungeonGirls\Temp\SmokeTest_Task1.log"
```
Expected: FAIL — the "не-Плут" check reports `UniqueShadowLevel=1, UniqueSmokeBombLevel=1` (leaked from the unconditional copy).

- [ ] **Step 3: Fix `CombatantFactory.cs`**

Replace lines 217-227 (the unconditional `runtime.UniqueShadowLevel = progress.UniquePassiveLevel; runtime.UniqueSmokeBombLevel = progress.UniqueActiveLevel;`) with a class-gated version matching the Barbarian pattern below it:

```csharp
        // 3.11 (Плут) — классовые навыки + уникальная пассивка/активка. ФИКС (Codex P1 2026-08-27):
        // UniqueShadowLevel/UniqueSmokeBombLevel раньше копировались из progress.UniquePassiveLevel/
        // UniqueActiveLevel БЕЗУСЛОВНО — но, в отличие от предположения в старом комментарии здесь,
        // UniquePassiveLevel/UniqueActiveLevel — ОБЩИЕ поля прогресса, стартующие с 1 у ЛЮБОГО
        // персонажа (см. RunCharacterProgress), а не только у Плута. Скрытность (IsStealthed) может
        // появиться не только от уникальной активки Плута, но и от общего навыка "Ускользание"
        // (SlipAway) — доступного другим классам через наставника/общий пул. Без проверки класса
        // Воин/Варвар со Скрытностью от "Ускользания" получал бы +10-30% уклонения "Тени", используя
        // уровень СВОЕЙ уникальной пассивки. Тот же паттерн явной проверки класса уже используется
        // ниже для UniqueChampionOfTheTribeLevel/UniqueBerserkLevel (Варвар).
        runtime.SkillEyeForAnEyeLevel = progress.GetSkillLevel(SkillEffectMap.EyeForAnEye);
        runtime.SkillPoisonedBladeLevel = progress.GetSkillLevel(SkillEffectMap.PoisonedBlade);
        runtime.SkillByAThreadLevel = progress.GetSkillLevel(SkillEffectMap.ByAThread);
        runtime.SkillSlipAwayLevel = progress.GetSkillLevel(SkillEffectMap.SlipAway);
        bool isRogue = progress.Character != null && progress.Character.characterClass == CharacterClass.Rogue;
        runtime.UniqueShadowLevel = isRogue ? progress.UniquePassiveLevel : 0;
        runtime.UniqueSmokeBombLevel = isRogue ? progress.UniqueActiveLevel : 0;
```

(Keep the `CritDamageMultiplierOverridePercent` line and everything else in the method unchanged — only the two `Unique...Level` assignment lines change.)

- [ ] **Step 4: Run test to verify it passes**

Re-run the same batchmode command as Step 2. Expected: both new checks PASS, and total OK count is 232 + 2 = 234 with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Combat/CombatantFactory.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "fix: gate Rogue's Shadow/Smoke Bomb unique skills to CharacterClass.Rogue (GDD 3.11 code review)"
```

---

## Task 2: Stable `characterId` on `CharacterData`

**Files:**
- Modify: `Assets/Scripts/Data/CharacterData.cs`
- Modify (asset, add field value via Unity YAML or Editor): `Assets/ScriptableObjects/Characters/Character_Jennifer.asset`, `Character_Rogue.asset`, `Character_Barbarian.asset`

**Interfaces:**
- Produces: `CharacterData.characterId` (string, e.g. `"jennifer"`, `"rogue"`, `"barbarian"`) — the stable identity key every later task (SaveData persistence, gacha, veterans) uses instead of `characterName` (display text, can change/localize without breaking saves). This is also the exact key format Codex's `seenVNScenes: { characterId: sceneId[] }` expects per the audit page.

- [ ] **Step 1: Add the field**

Edit `Assets/Scripts/Data/CharacterData.cs`:

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "DungeonGirls/Character")]
public class CharacterData : ScriptableObject
{
    // 9.4 (ФИКС, Codex P2 2026-08-27): стабильный ключ для SaveData/гачи/ветеранов — в отличие от
    // characterName (отображаемый текст, может меняться/локализоваться), characterId не должен
    // меняться после первого релиза персонажа. Формат — lowercase-строка (см. GDD 10.4 пример
    // ВН-контента: "characterId": "jennifer"), общий с форматом, который использует Codex для
    // seenVNScenes.
    public string characterId;
    public string characterName;
    public Sprite portrait; // 10.6: пиксель-арт портрет персонажа (64x64).
    public CharacterClass characterClass;

    public int baseHealth;
    public int healthPerLevel;

    public PassiveSkillData uniquePassiveSkill;
    public ActiveSkillData uniqueActiveSkill;

    public ItemData[] startingEquipment;
}
```

- [ ] **Step 2: Set `characterId` on the 3 real character assets**

These are `.asset` YAML files (Unity ScriptableObject serialization) — edit directly with the Edit tool, adding the new field's value. First read each file to find the exact insertion point (after `m_Script:` and other standard header fields, alongside `characterName:`):

```bash
grep -n "characterName" "Assets/ScriptableObjects/Characters/Character_Jennifer.asset" "Assets/ScriptableObjects/Characters/Character_Rogue.asset" "Assets/ScriptableObjects/Characters/Character_Barbarian.asset"
```

For each file, add a `characterId:` line immediately before the existing `characterName:` line, with these exact values:
- `Character_Jennifer.asset` → `characterId: jennifer`
- `Character_Rogue.asset` → `characterId: rogue`
- `Character_Barbarian.asset` → `characterId: barbarian`

(Leave `Character_Placeholder2.asset` untouched — it drops out of scene wiring in Task 6 and doesn't need an id.)

- [ ] **Step 3: Verify via batchmode smoke test**

Run the same batchmode command as Task 1 Step 2. Expected: still 234 OK, 0 errors (no behavior changed yet, just a new unused-so-far field) — this step only confirms the asset YAML edits didn't corrupt the files (Unity would log a YAML parse error on scene/asset load if so).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Data/CharacterData.cs "Assets/ScriptableObjects/Characters/Character_Jennifer.asset" "Assets/ScriptableObjects/Characters/Character_Rogue.asset" "Assets/ScriptableObjects/Characters/Character_Barbarian.asset"
git commit -m "feat: add stable characterId to CharacterData (GDD 9.4, shared key with Codex's seenVNScenes)"
```

---

## Task 3: Extend `SaveData` schema + migration + atomic writes

**Files:**
- Modify: `Assets/Scripts/Data/SaveData.cs`
- Modify: `Assets/Scripts/Managers/SaveManager.cs`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Produces: `SaveData.saveVersion` (int, current = 2), `SaveData.veteranDeck` (`List<VeteranCharacter>`), `SaveData.characterRunCounts` (`List<KeyCountEntry>`, keyed by `characterId`), `SaveData.seenVNScenes` (`List<CharacterSceneList>`, keyed by `characterId` — Codex populates the scene-id lists, this task only makes the field exist and round-trip through save/load without errors). `SaveManager.SaveGame()` now writes atomically (temp file + replace). `SaveManager` building-upgrade and other multi-field operations now mutate `Data` in memory and call `SaveGame()` exactly once.
- Consumes (renamed): `Data.characterCopies` (List<KeyCountEntry>, keyed by displayName) → replaced by `Data.gachaOwnedCharacters` (List<KeyCountEntry>, keyed by `characterId`) to match GDD 9.4's field name and Codex's stable-id recommendation. `Data.gachaItemCounts` is dropped entirely (GDD 11.1: no items in the gacha pool).

- [ ] **Step 1: Write the failing tests**

Add to `RunPureLogicChecks()` in `Assets/Editor/PlayModeSmokeTest.cs`:

```csharp
        // 9.4 (ФИКС, Codex P2 2026-08-27): SaveData расширена полями из актуального ГДД —
        // saveVersion/veteranDeck/characterRunCounts/seenVNScenes, gachaOwnedCharacters вместо
        // characterCopies (переключение ключа с displayName на стабильный characterId).
        var freshSave = new SaveData();
        Check(freshSave.saveVersion == SaveData.CurrentSaveVersion,
            $"9.4 новый SaveData имеет текущую версию: saveVersion={freshSave.saveVersion} (ожидалось {SaveData.CurrentSaveVersion})");
        Check(freshSave.veteranDeck != null && freshSave.veteranDeck.Count == 0,
            "9.4 veteranDeck инициализирован пустым списком");
        Check(freshSave.characterRunCounts != null && freshSave.gachaOwnedCharacters != null && freshSave.seenVNScenes != null,
            "9.4 characterRunCounts/gachaOwnedCharacters/seenVNScenes инициализированы (не null)");

        // 9.4/Codex P2: миграция старого сохранения без saveVersion (симулирует файл с диска до
        // этого фикса — JsonUtility молча оставит новые поля в дефолте, а не упадёт, но saveVersion
        // будет 0) — TryMigrate должен довести его до текущей версии без потери уже прочитанных полей.
        var staleSave = new SaveData { saveVersion = 0, metaCurrency = 500 };
        staleSave.veteranDeck = null; // симулируем JSON без этого поля вовсе (JsonUtility даёт null для отсутствующих списков в старом файле)
        SaveManager.MigrateIfNeeded(staleSave);
        Check(staleSave.saveVersion == SaveData.CurrentSaveVersion && staleSave.metaCurrency == 500 && staleSave.veteranDeck != null,
            $"9.4 миграция старого save: saveVersion={staleSave.saveVersion}, metaCurrency сохранена={staleSave.metaCurrency}, veteranDeck заполнен дефолтом={staleSave.veteranDeck != null} (ожидалось true/500/true)");
```

- [ ] **Step 2: Run test to verify it fails**

Same batchmode command as Task 1. Expected: FAIL — `SaveData.CurrentSaveVersion` and `SaveManager.MigrateIfNeeded` don't exist yet (compile error, which the smoke test harness reports as an exception/error in `RunPureLogicChecks`).

- [ ] **Step 3: Rewrite `SaveData.cs`**

```csharp
using System;
using System.Collections.Generic;

// 9.2/9.3/9.4: всё, что сохраняется между сессиями. saveVersion (НОВОЕ, Codex P2 2026-08-27) —
// для миграций при изменении схемы (см. SaveManager.MigrateIfNeeded). gachaOwnedCharacters заменяет
// старый characterCopies — тот же список пар ключ/счётчик, но ключ теперь стабильный characterId
// (CharacterData.characterId), а не отображаемое имя. gachaItemCounts убран целиком: GDD 11.1
// закрепляет "предметов в пуле гачи нет".
[Serializable]
public class KeyCountEntry
{
    public string key;
    public int count;
}

// 9.4: список открытых ВН-сцен ОДНОГО персонажа. JsonUtility не сериализует Dictionary напрямую,
// поэтому seenVNScenes — список таких записей (одна на characterId), как и остальные keyed-поля
// здесь (KeyCountEntry). Codex читает/пишет sceneIds; это поле только гарантирует наличие структуры.
[Serializable]
public class CharacterSceneList
{
    public string characterId;
    public List<string> sceneIds = new List<string>();
}

// 9.4: снимок персонажа на момент завершения забега (победа ИЛИ поражение — "финальные" статы,
// не обязательно "лучшие"). powerLevel: формула — открытый вопрос в ГДД (1, п.8); здесь взята
// простая монотонная DRAFT-формула (уровень×100 + сумма уровней известных навыков×10 + число
// надетых предметов×5) — достаточно, чтобы ветераны сортировались осмысленно в UI колоды, не
// претендует на финальный баланс-расчёт (см. отчёт по этой задаче).
[Serializable]
public class VeteranSkillEntry
{
    public string skillName;
    public int level;
}

[Serializable]
public class VeteranCharacter
{
    public string characterId;
    public float finalHP;
    public List<VeteranSkillEntry> finalSkills = new List<VeteranSkillEntry>();
    public List<string> finalEquipment = new List<string>(); // itemName — см. существующая конвенция (gachaItemCounts/UI уже используют itemName как идентичность предмета)
    public int powerLevel;
}

[Serializable]
public class SaveData
{
    public const int CurrentSaveVersion = 2; // 1 = схема до этого фикса (без saveVersion, неявно); 2 = текущая (9.4, Codex-аудит 2026-08-27)

    public int saveVersion = SaveData.CurrentSaveVersion;

    public int metaCurrency;
    public int gachaCurrency;

    public int forgeLevel;
    public int templeLevel;
    public int tavernLevel;

    public List<KeyCountEntry> gachaOwnedCharacters = new List<KeyCountEntry>();
    public List<VeteranCharacter> veteranDeck = new List<VeteranCharacter>();
    public List<KeyCountEntry> characterRunCounts = new List<KeyCountEntry>();
    public List<CharacterSceneList> seenVNScenes = new List<CharacterSceneList>();
}
```

- [ ] **Step 4: Add migration + atomic writes to `SaveManager.cs`**

Rewrite `Assets/Scripts/Managers/SaveManager.cs` in full:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 9.2/9.3: персистентность мета-прогрессии между сессиями — JSON в Application.persistentDataPath.
// Единственный источник правды для валют/уровней зданий/гача-копий/ветеранов (Фаза 5+).
// ФИКС (Codex P2 2026-08-27): SaveGame() теперь пишет во временный файл и атомарно заменяет
// основной (File.Replace/Move) вместо прямой перезаписи — обрыв процесса посреди записи больше не
// может повредить единственный save. Операции, меняющие несколько полей (апгрейд здания и т.п.),
// мутируют Data в памяти одной транзакцией и вызывают SaveGame() РОВНО ОДИН РАЗ — раньше апгрейд
// здания списывал валюту и сохранял, затем повышал уровень и сохранял снова, что при сбое между
// двумя записями могло списать валюту без апгрейда.
public class SaveManager : MonoBehaviour
{
    const string SaveFileName = "dungeongirls_save.json";

    public SaveData Data { get; private set; } = new SaveData();

    string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);
    string TempSavePath => SavePath + ".tmp";

    void Awake()
    {
        LoadGame();
    }

    public void LoadGame()
    {
        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);
                Data = JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
                MigrateIfNeeded(Data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] Не удалось прочитать сохранение ({SavePath}): {e.Message}. Начинаем с чистого прогресса.");
                Data = new SaveData();
            }
        }
        else
        {
            Data = new SaveData();
        }
    }

    // 9.4 (ФИКС, Codex P2 2026-08-27): минимальная миграция по saveVersion — а не полный
    // downgrade/reset прогресса на несовпадении версии. JsonUtility молча оставляет отсутствующие
    // в старом JSON поля в их C#-дефолте при десериализации (для List<T> это null, не пустой
    // список — в отличие от инициализатора поля в объявлении класса, который применяется только
    // при `new SaveData()`, не при FromJson) — поэтому здесь только НОРМАЛИЗУЮТСЯ потенциально-null
    // коллекции до пустых списков и проставляется текущая версия. Числовые поля (metaCurrency и
    // т.д.) при отсутствии в JSON уже корректно остаются 0 через JsonUtility без нашего участия.
    public static void MigrateIfNeeded(SaveData data)
    {
        if (data.veteranDeck == null) data.veteranDeck = new List<VeteranCharacter>();
        if (data.gachaOwnedCharacters == null) data.gachaOwnedCharacters = new List<KeyCountEntry>();
        if (data.characterRunCounts == null) data.characterRunCounts = new List<KeyCountEntry>();
        if (data.seenVNScenes == null) data.seenVNScenes = new List<CharacterSceneList>();

        data.saveVersion = SaveData.CurrentSaveVersion;
    }

    public void SaveGame()
    {
        string json = JsonUtility.ToJson(Data, true);

        // Атомарная запись: пишем во временный файл рядом, затем заменяем основной за одну
        // файловую операцию. File.Replace требует существующий целевой файл — на самом первом
        // сохранении (SavePath ещё не существует) используем File.Move как эквивалент.
        File.WriteAllText(TempSavePath, json);
        if (File.Exists(SavePath))
        {
            File.Replace(TempSavePath, SavePath, null);
        }
        else
        {
            File.Move(TempSavePath, SavePath);
        }
    }

    // ==================== Мета-валюта / гача-валюта (8.5) ====================

    public void AddMetaCurrency(int amount)
    {
        Data.metaCurrency += amount;
        SaveGame();
    }

    public void AddGachaCurrency(int amount)
    {
        Data.gachaCurrency += amount;
        SaveGame();
    }

    public bool TrySpendMetaCurrency(int amount)
    {
        if (Data.metaCurrency < amount) return false;
        Data.metaCurrency -= amount;
        SaveGame();
        return true;
    }

    public bool TrySpendGachaCurrency(int amount)
    {
        if (Data.gachaCurrency < amount) return false;
        Data.gachaCurrency -= amount;
        SaveGame();
        return true;
    }

    // ==================== Здания деревни (8.1) ====================

    public int GetBuildingLevel(BuildingType building)
    {
        switch (building)
        {
            case BuildingType.Forge: return Data.forgeLevel;
            case BuildingType.Temple: return Data.templeLevel;
            case BuildingType.Tavern: return Data.tavernLevel;
            default: return 0;
        }
    }

    void SetBuildingLevel(BuildingType building, int level)
    {
        switch (building)
        {
            case BuildingType.Forge: Data.forgeLevel = level; break;
            case BuildingType.Temple: Data.templeLevel = level; break;
            case BuildingType.Tavern: Data.tavernLevel = level; break;
        }
    }

    // ФИКС (Codex P2 2026-08-27): списание валюты и повышение уровня теперь одна транзакция в
    // памяти с ОДНИМ вызовом SaveGame() в конце — раньше это были два отдельных TrySpendMetaCurrency
    // (со своим SaveGame внутри) + SetBuildingLevel + SaveGame, т.е. 2 записи на диск с окном сбоя
    // между ними, где валюта уже списана, а уровень ещё не повышен.
    public bool TryUpgradeBuilding(BuildingType building)
    {
        int level = GetBuildingLevel(building);
        if (level >= BuildingCatalog.MaxLevel) return false;

        int cost = BuildingCatalog.UpgradeCost(level);
        if (Data.metaCurrency < cost) return false;

        Data.metaCurrency -= cost;
        SetBuildingLevel(building, level + 1);
        SaveGame();
        return true;
    }

    // ==================== Гача (8.5/11.1) ====================

    // ФИКС (Codex P2 2026-08-27): ключ — стабильный CharacterData.characterId, не отображаемое имя.
    public int GetCharacterCopies(string characterId) => FindEntry(Data.gachaOwnedCharacters, characterId)?.count ?? 0;

    public void AddCharacterCopy(string characterId)
    {
        FindOrCreateEntry(Data.gachaOwnedCharacters, characterId).count++;
        SaveGame();
    }

    // ==================== Ветераны / прохождения (9.2, раздел завершения забега) ====================

    // ФИКС (Codex P2 2026-08-27): добавление ветерана и инкремент счётчика прохождений — одна
    // транзакция в памяти, один SaveGame() — раньше этой пары не существовало вовсе (см. Task 8).
    public void AddVeteranAndIncrementRunCount(VeteranCharacter veteran)
    {
        Data.veteranDeck.Add(veteran);
        FindOrCreateEntry(Data.characterRunCounts, veteran.characterId).count++;
        SaveGame();
    }

    public int GetRunCount(string characterId) => FindEntry(Data.characterRunCounts, characterId)?.count ?? 0;

    // 7.1: кнопка «Сбросить прогресс» в хабе — полностью очищает SaveData (мета-валюта,
    // гача-валюта, уровни зданий, гача-данные, колода ветеранов, счётчики прохождений/отношений,
    // открытые ВН-сцены), возвращая игру в состояние первого запуска.
    public void ResetProgress()
    {
        Data = new SaveData();
        SaveGame();
    }

    static KeyCountEntry FindEntry(List<KeyCountEntry> list, string key) => list.Find(e => e.key == key);

    static KeyCountEntry FindOrCreateEntry(List<KeyCountEntry> list, string key)
    {
        var entry = FindEntry(list, key);
        if (entry == null)
        {
            entry = new KeyCountEntry { key = key, count = 0 };
            list.Add(entry);
        }
        return entry;
    }
}
```

- [ ] **Step 5: Fix call sites broken by the `characterCopies` → `gachaOwnedCharacters` rename**

Grep for the old field/method usages and update each caller to pass `characterId` instead of `characterName`:

```bash
grep -rn "GetCharacterCopies\|AddCharacterCopy\|characterCopies\|GetItemCount\|AddItemCopy\|gachaItemCounts" Assets/Scripts
```

Expected hits and fixes:
- `Assets/Scripts/Managers/CharacterManager.cs:57` — `saveManager.GetCharacterCopies(character.characterName)` → `saveManager.GetCharacterCopies(character.characterId)`.
- `Assets/Scripts/Managers/HubManager.cs` — old `AddItemCopy`/`GetItemCount`/`gachaCharacters` gacha logic is fully replaced in Task 6, not patched here; leave as-is for this task (it still compiles against the old `SaveManager.AddItemCopy`/`GetItemCount`, which no longer exist — **this task will NOT compile in isolation until Task 6 lands**, since `HubManager.cs` calls `saveManager.AddItemCopy`/`GetItemCount`). To keep every task independently buildable, delete `AddItemCopy`/`GetItemCount` calls from `HubManager.TryPullGacha` in this task by replacing the method body with a temporary pass-through (full rewrite happens in Task 6):

```csharp
    void TryPullGacha()
    {
        // [DRAFT, временная заглушка до Task 6 — полная реализация под GDD 11.1]
        saveManager.TrySpendGachaCurrency(GachaPullCost);
        RefreshGachaScreen();
    }
```

(This keeps the build green between tasks; Task 6 replaces this method body entirely.)

- [ ] **Step 6: Run test to verify it passes**

Same batchmode command. Expected: 234 + 3 = 237 OK, 0 errors, and the build compiles clean (check the log for `error CS` — should be none).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Data/SaveData.cs Assets/Scripts/Managers/SaveManager.cs Assets/Scripts/Managers/CharacterManager.cs Assets/Scripts/Managers/HubManager.cs Assets/Editor/PlayModeSmokeTest.cs
git commit -m "feat: extend SaveData schema (saveVersion, veteranDeck, characterRunCounts, seenVNScenes) with migration and atomic writes (GDD 9.4, Codex P2)"
```

---

## Task 4: Character selection screen

**Files:**
- Modify: `Assets/UI/GameRoot.uxml`
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Modify (scene wiring): `Assets/Scenes/SampleScene.unity`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `CharacterData.characterId`/`characterName`/`portrait`/`characterClass` (Task 2), `[SerializeField] CharacterData[] selectableCharacters` (new, replaces `jenniferCharacter`).
- Produces: `RunFlowController.SelectedCharacter` (public `CharacterData` property, set by the selection screen, read by `RunLoop`/results/camp text) — later tasks (5) read this instead of `jenniferCharacter`.

- [ ] **Step 1: Add the character select screen to `GameRoot.uxml`**

Insert a new screen block right after `MainMenuScreen` closes (after line 18) in `Assets/UI/GameRoot.uxml`:

```xml
    <ui:VisualElement name="CharacterSelectScreen" class="full-screen hidden" style="flex-direction: column;">
        <ui:Label text="Выберите персонажа" class="title-label" />
        <ui:VisualElement name="CharacterSelectCardsContainer" class="buildings-row" />
        <ui:Button name="CharacterSelectBackButton" text="Назад" class="button-secondary" />
    </ui:VisualElement>
```

Change `StartRunButton`'s role: it now opens `CharacterSelectScreen` instead of starting the run directly (wired in Step 2 below — no further UXML change needed, `StartRunButton` stays where it is).

- [ ] **Step 2: Wire the screen in `RunFlowController.cs`**

Replace the single-field header:

```csharp
    [Header("Контент (Фаза 2)")]
    [SerializeField] CharacterData jenniferCharacter;
```

with:

```csharp
    [Header("Контент (Фаза 2)")]
    // ФИКС (Codex P1 2026-08-27): единственное поле jenniferCharacter заменено массивом выбираемых
    // персонажей + экраном выбора — раньше BeginRun ВСЕГДА стартовал Дженифер, Плут/Варвар были
    // недостижимы из реального флоу забега несмотря на то, что их данные/классовые пулы существуют.
    [SerializeField] CharacterData[] selectableCharacters;
```

Add a new field to track selection and a public accessor, near the other state fields (after `totalRoomsThisFloorCached`):

```csharp
    CharacterData selectedCharacter;
    public CharacterData SelectedCharacter => selectedCharacter;
```

Add new UI element fields (near the other top-level screen fields):

```csharp
    VisualElement characterSelectScreen;
    VisualElement characterSelectCardsContainer;
    Button characterSelectBackButton;
```

In `CacheElements`, after the `mainMenuScreen`/`startRunButton` block:

```csharp
        characterSelectScreen = root.Q<VisualElement>("CharacterSelectScreen");
        characterSelectCardsContainer = root.Q<VisualElement>("CharacterSelectCardsContainer");
        characterSelectBackButton = root.Q<Button>("CharacterSelectBackButton");
```

In `OnEnable`, change the `StartRunButton` wiring from starting the run directly to opening character select, and wire the new back button:

```csharp
        startRunButton.clicked += OpenCharacterSelect;
        characterSelectBackButton.clicked += () =>
        {
            characterSelectScreen.style.display = DisplayStyle.None;
            mainMenuScreen.style.display = DisplayStyle.Flex;
        };
```

Add the new methods (near `BeginRunFromMenu`):

```csharp
    public void OpenCharacterSelect()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        characterSelectScreen.style.display = DisplayStyle.Flex;

        characterSelectCardsContainer.Clear();
        foreach (var character in selectableCharacters)
        {
            if (character == null) continue;

            var card = new VisualElement();
            card.AddToClassList("building-card");

            var portraitImage = new Image { sprite = character.portrait };
            portraitImage.style.width = 64;
            portraitImage.style.height = 64;
            card.Add(portraitImage);

            var nameLabel = new Label(character.characterName);
            nameLabel.AddToClassList("building-card-title");
            card.Add(nameLabel);

            var classLabel = new Label(character.characterClass.ToString());
            classLabel.AddToClassList("body-label");
            card.Add(classLabel);

            var pickButton = new Button { text = "Выбрать" };
            pickButton.AddToClassList("button-primary");
            pickButton.clicked += () => BeginRunWithCharacter(character);
            card.Add(pickButton);

            characterSelectCardsContainer.Add(card);
        }
    }

    void BeginRunWithCharacter(CharacterData character)
    {
        selectedCharacter = character;
        characterSelectScreen.style.display = DisplayStyle.None;
        StartCoroutine(RunLoop());
    }
```

Remove the old `BeginRunFromMenu` method entirely (its role — `StartCoroutine(RunLoop())` — moved into `BeginRunWithCharacter` above, gated on an actual character choice).

In `RunLoop()`, replace:

```csharp
        characterManager.BeginRun(jenniferCharacter, equipmentManager, saveManager);
```

with:

```csharp
        characterManager.BeginRun(selectedCharacter, equipmentManager, saveManager);
```

Fix the two other `jenniferCharacter`/hardcoded-name references found via Grep in this task's file:
- Line ~467 (`ConfigureUniqueActiveSkill(3, activeMultiplier, jenniferCharacter.uniqueActiveSkill...`) — this line is fully rewritten in **Task 5**, leave untouched here except it will still compile against `jenniferCharacter` unless Task 5 lands together. To keep this task independently buildable, do a minimal find-replace here too: `jenniferCharacter.uniqueActiveSkill` → `characterManager.Progress.Character.uniqueActiveSkill` (still not correct per-class behavior — Task 5 replaces this whole block — but compiles and doesn't regress Jennifer's own flow).
- Line ~1086 (`campText.text = "Дженифер отдыхает у привала..."`) → `campText.text = $"{characterManager.Character.characterName} отдыхает у привала..."`.
- Line ~1616 (`resultsBodyLabel.text = $"Дженифер достигла {characterManager.Level} уровня.\n"`) → `resultsBodyLabel.text = $"{characterManager.Character.characterName} достигла {characterManager.Level} уровня.\n"`.

- [ ] **Step 3: Wire 3 characters in `SampleScene.unity`**

The `RunFlowController` component currently serializes `jenniferCharacter: {fileID: 11400000, guid: fc548e39806d27b4ca5fa23e3fa061de, type: 2}` at line 582. Replace that single-field line with an array field. Read the surrounding YAML first (`grep -n -B2 -A2 "jenniferCharacter" Assets/Scenes/SampleScene.unity`) to confirm indentation, then replace:

```yaml
  jenniferCharacter: {fileID: 11400000, guid: fc548e39806d27b4ca5fa23e3fa061de, type: 2}
```

with:

```yaml
  selectableCharacters:
  - {fileID: 11400000, guid: fc548e39806d27b4ca5fa23e3fa061de, type: 2}
  - {fileID: 11400000, guid: baceb1dfdd8632e4fb0223bcf5b0fd46, type: 2}
  - {fileID: 11400000, guid: a7d9a670808583c4a997485355cbcea9, type: 2}
```

(guids confirmed via `Character_Jennifer.asset.meta`/`Character_Rogue.asset.meta`/`Character_Barbarian.asset.meta` — Jennifer, Rogue, Barbarian in that order.)

- [ ] **Step 4: Add integration smoke test — one run per character**

This can't run a full multi-minute dungeon crawl inside the smoke test's Play Mode window, so scope it to what Codex's recommendation asks for: confirm `BeginRun` + first `RefreshCombatStats` succeed without exceptions for all 3 characters. Add to `RunPlayModeChecks()` in `PlayModeSmokeTest.cs` (find the method, add near existing `BeginRun`-related checks):

```csharp
        // Codex P1 2026-08-27: интеграционный smoke — BeginRun не должен падать ни для одного из
        // 3 персонажей (раньше только Дженифер была реально доступна из RunFlowController).
        var runFlowController = UnityEngine.Object.FindFirstObjectByType<RunFlowController>();
        if (runFlowController == null)
        {
            Errors.Add("Task 4: RunFlowController не найден в сцене для проверки selectableCharacters.");
        }
        else
        {
            var selectableField = typeof(RunFlowController).GetField("selectableCharacters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var selectable = selectableField?.GetValue(runFlowController) as CharacterData[];
            Check(selectable != null && selectable.Length == 3,
                $"Task 4: RunFlowController.selectableCharacters содержит 3 персонажа: count={(selectable != null ? selectable.Length : -1)} (ожидалось 3)");

            if (selectable != null)
            {
                var testCharacterManager = new GameObject("SmokeTestCharacterManager").AddComponent<CharacterManager>();
                foreach (var character in selectable)
                {
                    try
                    {
                        testCharacterManager.BeginRun(character);
                        Check(testCharacterManager.Combatant != null && testCharacterManager.IsAlive,
                            $"Task 4: BeginRun успешен для {character.characterName} ({character.characterClass}): Combatant создан, IsAlive={testCharacterManager.IsAlive}");
                    }
                    catch (System.Exception e)
                    {
                        Errors.Add($"Task 4: BeginRun выбросил исключение для {character.characterName}: {e}");
                    }
                }
                UnityEngine.Object.DestroyImmediate(testCharacterManager.gameObject);
            }
        }
```

- [ ] **Step 5: Run test to verify it passes**

Same batchmode command. Expected: 237 + 4 = 241 OK, 0 errors. This step doubles as verification the UXML/scene edits don't break scene load (a YAML error would show as a console error caught by `OnLog`).

- [ ] **Step 6: Commit**

```bash
git add Assets/UI/GameRoot.uxml Assets/Scripts/UI/RunFlowController.cs Assets/Scenes/SampleScene.unity Assets/Editor/PlayModeSmokeTest.cs
git commit -m "feat: add character selection screen, wire Rogue/Barbarian into the run flow (GDD 7.2, Codex P1)"
```

---

## Task 5: Per-class active skill configuration + Berserk manual toggle

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Modify: `Assets/UI/GameRoot.uxml`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `characterManager.Progress.Character.characterClass`/`uniqueActiveSkill` (existing), `CombatManager.ConfigureUniqueActiveSkill`/`SetBerserkActive` (existing, unchanged signatures).
- Produces: combat active-skill UI now shows either the existing cooldown button (Jennifer/Rogue) or a Berserk toggle (Barbarian), decided by `characterManager.Progress.Character.characterClass`.

- [ ] **Step 1: Write the failing test**

Add to `RunPureLogicChecks()`:

```csharp
        // Codex P1 2026-08-27: конфигурация активного навыка в бою должна зависеть от текущего
        // персонажа, не быть жёстко зашитой под "3 быстрые атаки" Дженифер. Дымовая граната Плута
        // конфигурируется с hitCount=0 (сама не бьёт — см. CombatManager.TryActivateUniqueActiveSkill).
        var rogueForSkillTest = ScriptableObject.CreateInstance<CharacterData>();
        rogueForSkillTest.characterName = "ТестПлутАктивка";
        rogueForSkillTest.characterClass = CharacterClass.Rogue;
        rogueForSkillTest.baseHealth = 100;
        Check(RunFlowController.ResolveActiveSkillHitCount(rogueForSkillTest.characterClass) == 0,
            $"Task 5: ResolveActiveSkillHitCount(Rogue) == 0 (Дымовая граната не бьёт сама): {RunFlowController.ResolveActiveSkillHitCount(rogueForSkillTest.characterClass)}");
        Check(RunFlowController.ResolveActiveSkillHitCount(CharacterClass.Warrior) == 3,
            $"Task 5: ResolveActiveSkillHitCount(Warrior) == 3 (3 быстрые атаки Дженифер): {RunFlowController.ResolveActiveSkillHitCount(CharacterClass.Warrior)}");
        UnityEngine.Object.DestroyImmediate(rogueForSkillTest);
```

- [ ] **Step 2: Run test to verify it fails**

Same batchmode command. Expected: FAIL — `RunFlowController.ResolveActiveSkillHitCount` doesn't exist yet.

- [ ] **Step 3: Implement per-class configuration in `RunFlowController.cs`**

Add a static helper (near the top of the class, e.g. right after `RollMonsterCount`):

```csharp
    // Codex P1 (ФИКС, 2026-08-27): раньше CombatRoomFlow всегда передавал hitCount=3 и конфиг из
    // jenniferCharacter.uniqueActiveSkill — Плут получал бы конфигурацию Дженифер (неверный
    // hitCount/имя навыка), а Варвар вообще не имеет кулдаун-активки (Берсерк — ручной тумблер, см.
    // ниже). Единственный текущий кейс с hitCount != 3 — Дымовая граната Плута (не бьёт сама, см.
    // CombatManager.TryActivateUniqueActiveSkill, которое жёстко возвращает до hit-loop для неё
    // независимо от переданного числа) — hitCount=0 здесь просто отражает намерение корректно.
    public static int ResolveActiveSkillHitCount(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Rogue => 0, // Дымовая граната — не бьёт сама
        _ => 3 // "3 быстрые атаки" (Дженифер/Воин) — единственный hit-loop навык прототипа кроме Дымовой гранаты
    };
```

Replace the old hardcoded configuration block in `CombatRoomFlow` (around line 465-467):

```csharp
        int activeLevel = characterManager.Progress.UniqueActiveLevel;
        float activeMultiplier = activeLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f };
        combatManager.ConfigureUniqueActiveSkill(3, activeMultiplier, jenniferCharacter.uniqueActiveSkill.cooldownSeconds, autoModeToggle.value, jenniferCharacter.uniqueActiveSkill.skillName);
```

with:

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
        }
        else
        {
            int activeLevel = characterManager.Progress.UniqueActiveLevel;
            float activeMultiplier = activeLevel switch { 1 => 1.10f, 2 => 1.30f, _ => 1.50f };
            int hitCount = ResolveActiveSkillHitCount(activeCharacter.characterClass);
            combatManager.ConfigureUniqueActiveSkill(hitCount, activeMultiplier, activeCharacter.uniqueActiveSkill.cooldownSeconds, autoModeToggle.value, activeCharacter.uniqueActiveSkill.skillName);
        }
```

- [ ] **Step 4: Add the Berserk toggle UI**

In `Assets/UI/GameRoot.uxml`, inside `CombatPanel`'s `combat-controls-row` (next to `AutoModeToggle`/`ActiveSkillButton`), add:

```xml
                    <ui:Toggle name="BerserkToggle" label="Берсерк" value="false" class="hidden" />
```

In `RunFlowController.cs`, add a field:

```csharp
    Toggle berserkToggle;
```

In `CacheElements`, after `activeSkillButton = root.Q<Button>("ActiveSkillButton");`:

```csharp
        berserkToggle = root.Q<Toggle>("BerserkToggle");
```

In `OnEnable`, after the existing `activeSkillButton.clicked += ...` line:

```csharp
        berserkToggle.RegisterValueChangedCallback(evt => combatManager.SetBerserkActive(evt.newValue));
```

In `UpdateCombatUI()`, show/hide the two controls based on class — find the block that currently sets `activeSkillButton.SetEnabled(...)`/`.text = ...` near the end of the method and wrap it:

```csharp
        bool isBarbarianCombat = characterManager.Progress.Character.characterClass == CharacterClass.Barbarian;
        activeSkillButton.EnableInClassList("hidden", isBarbarianCombat);
        autoModeToggle.EnableInClassList("hidden", isBarbarianCombat);
        berserkToggle.EnableInClassList("hidden", !isBarbarianCombat);

        if (!isBarbarianCombat)
        {
            bool ready = combatManager.IsActiveSkillReady;
            activeSkillButton.SetEnabled(!autoModeToggle.value && ready);
            activeSkillButton.text = ready ? "Активный навык (готов)" : $"Активный навык ({combatManager.ActiveSkillCooldownRemaining:F1}с)";
        }
        else
        {
            berserkToggle.SetValueWithoutNotify(player.IsBerserkActive);
        }
```

(Replace the existing unconditional `bool ready = ...` / `activeSkillButton.SetEnabled(...)` / `.text = ...` three lines at the end of `UpdateCombatUI` with the block above.)

- [ ] **Step 5: Run test to verify it passes**

Same batchmode command. Expected: 241 + 2 = 243 OK, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/UI/RunFlowController.cs Assets/UI/GameRoot.uxml Assets/Editor/PlayModeSmokeTest.cs
git commit -m "fix: configure combat active skill per selected character, add Barbarian Berserk toggle (GDD 3.11, Codex P1)"
```

---

## Task 6: Rewrite gacha per GDD 11.1

**Files:**
- Modify: `Assets/Scripts/Managers/HubManager.cs`
- Modify (scene wiring): `Assets/Scenes/SampleScene.unity`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `RewardManager.RollItemRarity(bool isBoss)` → reused for the currency-tier roll only (not exposed on `RewardManager` as public API change — `HubManager` gets its own reference or a small static helper; see Step 1). `SaveManager.AddCharacterCopy(string characterId)`/`AddMetaCurrency(int)` (Task 3).
- Produces: `HubManager.gachaCharacters` (3-element array, Jennifer/Rogue/Barbarian) replaces the old 2-character + item-name pool.

- [ ] **Step 1: Write the failing test (pool composition, no items)**

Add to `RunPureLogicChecks()`:

```csharp
        // GDD 11.1 (ФИКС, Codex P2 2026-08-27): пул гачи — 15% персонаж (5%/5%/5%) + 85% мета-валюта
        // по таблице 62/35/3 -> 20/50/150. Предметов в пуле нет вовсе.
        Check(GachaPool.RollResult(0.01f, out var charResult) && charResult.IsCharacter,
            $"11.1 roll=1% попадает в ветку персонажа (0-15%): IsCharacter={charResult.IsCharacter}");
        Check(GachaPool.RollResult(0.14f, out var charResult2) && charResult2.IsCharacter,
            $"11.1 roll=14% попадает в ветку персонажа (0-15%): IsCharacter={charResult2.IsCharacter}");
        Check(GachaPool.RollResult(0.16f, out var currencyResult) && !currencyResult.IsCharacter,
            $"11.1 roll=16% попадает в ветку мета-валюты (15-100%): IsCharacter={currencyResult.IsCharacter}");
        Check(GachaPool.RollResult(0.99f, out var currencyResult2) && !currencyResult2.IsCharacter,
            $"11.1 roll=99% попадает в ветку мета-валюты: IsCharacter={currencyResult2.IsCharacter}");
```

- [ ] **Step 2: Run test to verify it fails**

Same batchmode command. Expected: FAIL — `GachaPool` class doesn't exist yet.

- [ ] **Step 3: Add a pure-logic `GachaPool` static class**

Create `Assets/Scripts/Managers/GachaPool.cs`:

```csharp
using UnityEngine;

// GDD 11.1: пул призыва гачи — 15% персонаж (поровну между 3: 5%/5%/5%) + 85% мета-валюта по той
// же таблице редкости, что и сундук (8.2: 62/35/3), переиспользуемой как РАЗМЕР ПРИЗА, а не тип
// предмета. Чистая логика (без UnityEngine.Random) вынесена отдельно от HubManager ради
// детерминированного статистического теста (см. Task 8) — принимает roll [0,1) снаружи.
public static class GachaPool
{
    public const float CharacterChance = 0.15f;
    public const int CharacterCount = 3; // Дженифер/Плут/Варвар — по 5% каждый

    public struct Result
    {
        public bool IsCharacter;
        public int CharacterIndex; // валиден только если IsCharacter — индекс в HubManager.gachaCharacters (0/1/2)
        public ItemTier CurrencyTier; // валиден только если !IsCharacter
        public int CurrencyAmount; // валиден только если !IsCharacter
    }

    // roll — равномерное [0,1); rarityRoll — отдельный равномерный [0,1) для ветки валюты (та же
    // таблица 62/35/3, что и RewardManager.RollItemRarity, но своя реализация здесь — избегаем
    // зависимости HubManager от RewardManager ради разделения ответственности хаба/забега).
    public static bool RollResult(float roll, out Result result) => RollResult(roll, roll, out result);

    public static bool RollResult(float roll, float rarityRoll, out Result result)
    {
        result = new Result();

        if (roll < CharacterChance)
        {
            result.IsCharacter = true;
            // Каждому из 3 персонажей — равная 1/3 доля 15%-диапазона.
            float sliceWidth = CharacterChance / CharacterCount;
            result.CharacterIndex = Mathf.Clamp(Mathf.FloorToInt(roll / sliceWidth), 0, CharacterCount - 1);
            return true;
        }

        result.IsCharacter = false;
        float rarityRoll100 = rarityRoll * 100f;
        if (rarityRoll100 < 62f)
        {
            result.CurrencyTier = ItemTier.Common;
            result.CurrencyAmount = 20;
        }
        else if (rarityRoll100 < 97f) // 62 + 35
        {
            result.CurrencyTier = ItemTier.Rare;
            result.CurrencyAmount = 50;
        }
        else
        {
            result.CurrencyTier = ItemTier.Epic;
            result.CurrencyAmount = 150;
        }
        return true;
    }
}
```

- [ ] **Step 4: Rewrite `HubManager.cs` gacha section**

Replace the `[Header("Гача-контент...")]` block:

```csharp
    [Header("Гача-контент (11.1: 3 персонажа, по порядку Дженифер/Плут/Варвар)")]
    [SerializeField] CharacterData[] gachaCharacters;
```

Remove `gachaItemNames` entirely.

Replace `TryPullGacha()` (the Task 3 Step 5 placeholder) with:

```csharp
    void TryPullGacha()
    {
        if (!saveManager.TrySpendGachaCurrency(GachaPullCost))
        {
            return;
        }

        GachaPool.RollResult(Random.value, Random.value, out var result);

        string resultText;
        if (result.IsCharacter && gachaCharacters != null && result.CharacterIndex < gachaCharacters.Length && gachaCharacters[result.CharacterIndex] != null)
        {
            var character = gachaCharacters[result.CharacterIndex];
            saveManager.AddCharacterCopy(character.characterId);
            int copies = saveManager.GetCharacterCopies(character.characterId);
            resultText = $"Персонаж: {character.characterName} (копия №{copies})";
        }
        else
        {
            saveManager.AddMetaCurrency(result.CurrencyAmount);
            resultText = $"Мета-валюта: +{result.CurrencyAmount} ({RarityLabel(result.CurrencyTier)})";
        }

        gachaResultLabel.text = resultText;
        gachaResultPopup.style.display = DisplayStyle.Flex;

        RefreshGachaScreen();
    }

    static string RarityLabel(ItemTier tier) => tier switch
    {
        ItemTier.Common => "Обычный",
        ItemTier.Rare => "Редкий",
        _ => "Эпический"
    };
```

- [ ] **Step 5: Wire 3 characters in `SampleScene.unity`**

Replace the `gachaCharacters:` array at lines 218-220 (currently Jennifer + `Character_Placeholder2`) with the same 3-character list used in Task 4 Step 3:

```yaml
  gachaCharacters:
  - {fileID: 11400000, guid: fc548e39806d27b4ca5fa23e3fa061de, type: 2}
  - {fileID: 11400000, guid: baceb1dfdd8632e4fb0223bcf5b0fd46, type: 2}
  - {fileID: 11400000, guid: a7d9a670808583c4a997485355cbcea9, type: 2}
```

- [ ] **Step 6: Run test to verify it passes**

Same batchmode command. Expected: 243 + 4 = 247 OK, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Managers/GachaPool.cs Assets/Scripts/Managers/HubManager.cs Assets/Scenes/SampleScene.unity Assets/Editor/PlayModeSmokeTest.cs
git commit -m "feat: rewrite gacha to GDD 11.1 (15% character / 85% meta-currency, no items)"
```

---

## Task 7: Gacha chest-reveal animation (2 landing branches)

**Files:**
- Create: `Assets/Scripts/UI/ChestRevealAnimator.cs`
- Modify: `Assets/Scripts/UI/RunFlowController.cs` (extract shared logic, keep behavior identical)
- Modify: `Assets/Scripts/Managers/HubManager.cs`
- Modify: `Assets/UI/GameRoot.uxml`

**Interfaces:**
- Produces: `ChestRevealAnimator.PlayReel(VisualElement strip, VisualElement viewport, Sprite winningIcon, ...)` — the exact tween/reel-building logic currently inline in `RunFlowController.ChestRevealFlow`, extracted so `HubManager` can call the identical mechanic per GDD 11.1 ("Шаги 1-4 те же, что у сундука из 8.2").

- [ ] **Step 1: Extract the shared reel/shake/burst helpers**

Move `ShakeChest`, `ShakeElement`, `SpawnChestBurst`, and the core reel-building/tween loop out of `RunFlowController` into a new static class `Assets/Scripts/UI/ChestRevealAnimator.cs`. Keep them as coroutine-returning static methods taking `MonoBehaviour` (for `StartCoroutine`) plus the `VisualElement`s involved, so both `RunFlowController` and `HubManager` can call them:

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

// 8.2/11.1: анимация вскрытия сундука/приза гачи — извлечена из RunFlowController.ChestRevealFlow,
// т.к. GDD 11.1 явно требует переиспользовать ту же механику для экрана гачи ("Шаги 1-4 те же, что
// у сундука из 8.2"), а не строить вторую параллельную реализацию тряски/ленты/burst'а.
public static class ChestRevealAnimator
{
    public const int ReelPadding = 3;
    public const int ReelLength = 20;
    public const float IconWidth = 64f;

    public static IEnumerator Shake(VisualElement element, float duration, Vector3 amplitude, int vibrato)
    {
        Vector3 shakeOffset = Vector3.zero;
        bool shakeComplete = false;
        DG.Tweening.DOTween.Punch(
            () => shakeOffset,
            v =>
            {
                shakeOffset = v;
                element.style.translate = new Translate(v.x, v.y, 0);
            },
            amplitude,
            duration,
            vibrato
        ).OnComplete(() => shakeComplete = true);

        while (!shakeComplete)
        {
            yield return null;
        }

        element.style.translate = new Translate(0, 0, 0);
    }

    public static IEnumerator ShakeChest(VisualElement chestSprite) => Shake(chestSprite, 1f, new Vector3(6f, 4f, 0f), 10);

    // Строит ленту из totalIcons слотов вокруг winningIndex и прокручивает её до центра viewport.
    // onBuildSlot(i, isWinningSlot) должен добавить один Image-элемент в strip и вернуть его —
    // вызывающий код сам решает, как выглядит каждый слот (иконка предмета vs силуэт персонажа/
    // сумма валюты), ChestRevealAnimator знает только про позиционирование/тайминг/скип.
    public static IEnumerator PlayReel(
        MonoBehaviour host,
        VisualElement strip,
        VisualElement viewport,
        System.Action<int, bool> onBuildSlot,
        Button skipButton,
        int winningIndex)
    {
        int totalIcons = ReelLength + ReelPadding * 2;
        for (int i = 0; i < totalIcons; i++)
        {
            onBuildSlot(i, i == winningIndex);
        }

        yield return null; // один кадр на пересчёт layout viewport'а

        float viewportCenter = viewport.resolvedStyle.width / 2f;
        strip.style.left = viewportCenter - IconWidth / 2f - ReelPadding * IconWidth;

        bool skipped = false;
        void OnSkip() => skipped = true;
        skipButton.clicked += OnSkip;

        float targetLeft = viewportCenter - IconWidth / 2f - winningIndex * IconWidth;
        const float tweenDuration = 4f;

        bool tweenComplete = false;
        var tween = DG.Tweening.DOTween.To(
            () => strip.style.left.value.value,
            x => strip.style.left = x,
            targetLeft,
            tweenDuration
        ).SetEase(DG.Tweening.Ease.OutCubic).OnComplete(() => tweenComplete = true);

        while (!tweenComplete && !skipped)
        {
            yield return null;
        }

        if (skipped)
        {
            tween.Kill();
            strip.style.left = targetLeft;
        }

        skipButton.clicked -= OnSkip;
    }

    public static void SpawnBurst(VisualElement anchor, VisualElement container)
    {
        const int burstCount = 8;
        const float burstDistance = 48f;
        const float burstDuration = 0.5f;
        const float dotSize = 8f;
        var burstColor = new Color(1f, 217f / 255f, 51f / 255f, 1f);

        float centerX = anchor.layout.x + anchor.layout.width / 2f;
        float centerY = anchor.layout.y + anchor.layout.height / 2f;

        for (int i = 0; i < burstCount; i++)
        {
            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.width = dotSize;
            dot.style.height = dotSize;
            dot.style.borderTopLeftRadius = dotSize / 2f;
            dot.style.borderTopRightRadius = dotSize / 2f;
            dot.style.borderBottomLeftRadius = dotSize / 2f;
            dot.style.borderBottomRightRadius = dotSize / 2f;
            dot.style.backgroundColor = burstColor;
            dot.style.left = centerX - dotSize / 2f;
            dot.style.top = centerY - dotSize / 2f;
            container.Add(dot);

            float angle = i / (float)burstCount * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            float dx = Mathf.Cos(angle) * burstDistance;
            float dy = Mathf.Sin(angle) * burstDistance;

            float progress = 0f;
            DG.Tweening.DOTween.To(() => progress, x => progress = x, 1f, burstDuration)
                .SetEase(DG.Tweening.Ease.OutCubic)
                .OnUpdate(() =>
                {
                    dot.style.left = centerX - dotSize / 2f + dx * progress;
                    dot.style.top = centerY - dotSize / 2f + dy * progress;
                    dot.style.opacity = 1f - progress;
                })
                .OnComplete(() =>
                {
                    if (dot.parent != null)
                    {
                        dot.RemoveFromHierarchy();
                    }
                });
        }
    }
}
```

- [ ] **Step 2: Update `RunFlowController.ChestRevealFlow` to call the shared helper**

Replace the body of `ChestRevealFlow`'s shake/reel/burst calls with calls into `ChestRevealAnimator` (`yield return ChestRevealAnimator.ShakeChest(chestSpriteImage);`, build slots via a local closure passed as `onBuildSlot`, `yield return ChestRevealAnimator.PlayReel(this, chestReelStrip, chestReelViewport, BuildSlot, chestSkipButton, winningIndex);`, `ChestRevealAnimator.SpawnBurst(chestSpriteImage, chestRevealContainer);`). Delete the now-unused private `ShakeChest`/`ShakeElement`/`SpawnChestBurst` methods from `RunFlowController.cs` (keep `ShakeElement` only if still referenced elsewhere via Grep — it's also used for `OnHitResolved`'s combat shake, so keep that specific usage calling `ChestRevealAnimator.Shake` instead of a local copy). Re-run the full smoke test after this refactor before proceeding — this step is pure extraction and must not change `RunPlayModeChecks` results.

- [ ] **Step 3: Add the Gacha screen's reel UI to `GameRoot.uxml`**

Inside `GachaScreen` (`Assets/UI/GameRoot.uxml`), add reel elements mirroring `ChestRevealContainer`'s structure (same USS classes already defined for the run-screen version, reused here):

```xml
        <ui:VisualElement name="GachaRevealContainer" class="chest-reveal-container hidden">
            <ui:Image name="GachaChestSpriteImage" class="chest-sprite" />
            <ui:VisualElement name="GachaReelViewport" class="chest-reel-viewport">
                <ui:VisualElement name="GachaReelStrip" class="chest-reel-strip" />
            </ui:VisualElement>
            <ui:Button name="GachaSkipButton" text="Пропустить" class="button-secondary" />
        </ui:VisualElement>
```

- [ ] **Step 4: Wire the 2-branch landing in `HubManager.cs`**

Add fields (mirroring `RunFlowController`'s chest fields) and a coroutine version of `TryPullGacha` that plays the reel before revealing the result:

```csharp
    [Header("Гача-анимация (11.1, переиспользует механику сундука 8.2)")]
    [SerializeField] Texture2D gachaChestClosedTexture;
    [SerializeField] Texture2D gachaChestOpenTexture;
    [SerializeField] Sprite characterSilhouetteRayOverlay; // общий переиспользуемый луч/звезда за портретом
    [SerializeField] ItemCatalogData currencyIconCatalog; // для визуального шума ленты валюты — переиспользует иконки предметов как декоративный шум, без влияния на результат (см. GDD 11.1: шум ленты не обязан быть предметным конкретно, только "иконки")

    VisualElement gachaRevealContainer;
    Image gachaChestSpriteImage;
    VisualElement gachaReelViewport;
    VisualElement gachaReelStrip;
    Button gachaSkipButton;
```

Cache them in `CacheElements`:

```csharp
        gachaRevealContainer = root.Q<VisualElement>("GachaRevealContainer");
        gachaChestSpriteImage = root.Q<Image>("GachaChestSpriteImage");
        gachaReelViewport = root.Q<VisualElement>("GachaReelViewport");
        gachaReelStrip = root.Q<VisualElement>("GachaReelStrip");
        gachaSkipButton = root.Q<Button>("GachaSkipButton");
```

Replace `TryPullGacha` (from Task 6) with a coroutine-driving version:

```csharp
    void TryPullGacha()
    {
        if (!saveManager.TrySpendGachaCurrency(GachaPullCost))
        {
            return;
        }

        StartCoroutine(GachaPullFlow());
    }

    IEnumerator GachaPullFlow()
    {
        GachaPool.RollResult(Random.value, Random.value, out var result);

        gachaRevealContainer.style.display = DisplayStyle.Flex;
        gachaChestSpriteImage.image = gachaChestClosedTexture;
        gachaReelStrip.Clear();

        yield return ChestRevealAnimator.ShakeChest(gachaChestSpriteImage);
        gachaChestSpriteImage.image = gachaChestOpenTexture;

        int winningIndex = ChestRevealAnimator.ReelPadding + ChestRevealAnimator.ReelLength - 2;

        void BuildSlot(int index, bool isWinning)
        {
            var icon = new Image();
            icon.AddToClassList("chest-reel-icon");
            if (currencyIconCatalog != null && currencyIconCatalog.items != null && currencyIconCatalog.items.Length > 0)
            {
                var noiseItem = currencyIconCatalog.items[Random.Range(0, currencyIconCatalog.items.Length)];
                icon.sprite = noiseItem.icon;
            }
            gachaReelStrip.Add(icon);
        }

        yield return ChestRevealAnimator.PlayReel(this, gachaReelStrip, gachaReelViewport, BuildSlot, gachaSkipButton, winningIndex);
        ChestRevealAnimator.SpawnBurst(gachaChestSpriteImage, gachaRevealContainer);

        yield return new WaitForSeconds(0.3f);
        gachaRevealContainer.style.display = DisplayStyle.None;

        string resultText;
        if (result.IsCharacter && gachaCharacters != null && result.CharacterIndex < gachaCharacters.Length && gachaCharacters[result.CharacterIndex] != null)
        {
            var character = gachaCharacters[result.CharacterIndex];
            saveManager.AddCharacterCopy(character.characterId);
            int copies = saveManager.GetCharacterCopies(character.characterId);
            resultText = $"Персонаж: {character.characterName} (копия №{copies})";
        }
        else
        {
            saveManager.AddMetaCurrency(result.CurrencyAmount);
            resultText = $"Мета-валюта: +{result.CurrencyAmount} ({RarityLabel(result.CurrencyTier)})";
        }

        gachaResultLabel.text = resultText;
        gachaResultPopup.style.display = DisplayStyle.Flex;
        RefreshGachaScreen();
    }
```

Note: `HubManager` needs `using System.Collections;` added at the top for `IEnumerator`.

The silhouette-reveal branch (portrait desaturated → color tween → burst, per GDD 11.1's storyboard) is a further visual refinement layered onto the "character" landing case above — implement it as a `Color`-tween on a portrait `Image` inserted at `winningIndex`'s slot instead of a plain icon, gated by `if (result.IsCharacter)` inside `BuildSlot`. Given this is cosmetic polish beyond the pass/fail-testable core (pool math + no-items + reel plays), keep the implementation minimal for this task and note it as a candidate follow-up polish pass in the final report if time-boxed out — the pool/currency mechanics and animation reuse are the testable, spec-required core.

- [ ] **Step 5: Manual verification**

This task is UI/animation and not meaningfully unit-testable beyond what Task 6/8 already cover. Verify manually: open the project in the Unity Editor, enter Play Mode, navigate Hub → Гача, pull until a character result and a currency result are both observed, confirm no console errors and the reel animation plays and lands correctly for both branches. Take screenshots for the final report (character landing + currency landing).

- [ ] **Step 6: Run the full smoke test**

Same batchmode command as before — confirms the `RunFlowController` refactor in Step 2 didn't regress any existing chest/combat-shake checks.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/UI/ChestRevealAnimator.cs Assets/Scripts/UI/RunFlowController.cs Assets/Scripts/Managers/HubManager.cs Assets/UI/GameRoot.uxml
git commit -m "feat: extend chest-reveal animation to the gacha screen (GDD 11.1 storyboard, 2 landing branches)"
```

---

## Task 8: Gacha statistical distribution test (controlled RNG)

**Files:**
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `GachaPool.RollResult(float roll, float rarityRoll, out GachaPool.Result result)` (Task 6) — deterministic, no `UnityEngine.Random` dependency, so the test can drive it with an evenly-spaced or seeded sample instead of relying on statistical luck.

- [ ] **Step 1: Write the test**

Add to `RunPureLogicChecks()`:

```csharp
        // GDD 11.1 (Codex "Пробелы тестирования" 2026-08-27): статистический тест распределения
        // с контролируемым RNG — раньше гача проверялась только на списание валюты и показ попапа,
        // не на реальные вероятности/состав пула/отсутствие предметов. Используем System.Random с
        // фиксированным сидом вместо UnityEngine.Random — воспроизводимо между запусками теста.
        var gachaRng = new System.Random(12345);
        int trials = 100000;
        int characterHits = 0;
        int commonCurrencyHits = 0, rareCurrencyHits = 0, epicCurrencyHits = 0;
        var characterIndexCounts = new int[GachaPool.CharacterCount];

        for (int i = 0; i < trials; i++)
        {
            float roll = (float)gachaRng.NextDouble();
            float rarityRoll = (float)gachaRng.NextDouble();
            GachaPool.RollResult(roll, rarityRoll, out var result);

            if (result.IsCharacter)
            {
                characterHits++;
                characterIndexCounts[result.CharacterIndex]++;
            }
            else
            {
                switch (result.CurrencyTier)
                {
                    case ItemTier.Common: commonCurrencyHits++; break;
                    case ItemTier.Rare: rareCurrencyHits++; break;
                    case ItemTier.Epic: epicCurrencyHits++; break;
                }
                // 11.1: предметов в пуле гачи нет — GachaPool.Result структурно не может нести
                // предмет (нет соответствующего поля), это утверждение проверяется самим фактом,
                // что переключение switch выше исчерпывающе по ItemTier без default-ветки на "item".
            }
        }

        float characterPercent = characterHits / (float)trials * 100f;
        Check(Mathf.Abs(characterPercent - 15f) < 0.5f,
            $"11.1 статистика: доля персонажа ≈15% за {trials} прогонов: {characterPercent:F2}% (допуск ±0.5%)");

        for (int idx = 0; idx < GachaPool.CharacterCount; idx++)
        {
            float perCharacterPercent = characterIndexCounts[idx] / (float)trials * 100f;
            Check(Mathf.Abs(perCharacterPercent - 5f) < 0.3f,
                $"11.1 статистика: персонаж #{idx} ≈5% за {trials} прогонов: {perCharacterPercent:F2}% (допуск ±0.3%)");
        }

        float commonPercent = commonCurrencyHits / (float)trials * 100f;
        float rarePercent = rareCurrencyHits / (float)trials * 100f;
        float epicPercent = epicCurrencyHits / (float)trials * 100f;
        // Доли внутри 85%-ветки валюты: 85% × 62/35/3 = 52.7/29.75/2.55 от ОБЩЕГО числа прогонов.
        Check(Mathf.Abs(commonPercent - 52.7f) < 1f,
            $"11.1 статистика: Обычная валюта (20) ≈52.7% от общего: {commonPercent:F2}% (допуск ±1%)");
        Check(Mathf.Abs(rarePercent - 29.75f) < 1f,
            $"11.1 статистика: Редкая валюта (50) ≈29.75% от общего: {rarePercent:F2}% (допуск ±1%)");
        Check(Mathf.Abs(epicPercent - 2.55f) < 0.5f,
            $"11.1 статистика: Эпическая валюта (150) ≈2.55% от общего: {epicPercent:F2}% (допуск ±0.5%)");
```

- [ ] **Step 2: Run test to verify it fails (before Task 6/7 land) or passes (after)**

If this task runs after Task 6/7 are already committed (recommended order — see task list), this is a normal TDD red→green within Task 6 instead. If run standalone for review purposes, the failing case would be `GachaPool` not existing — same as Task 6 Step 2.

- [ ] **Step 3: Run to verify pass**

Same batchmode command. Expected: all 6 new checks PASS (total climbing to 247 + 6 = 253, assuming Task 6/7 already landed). If any percentage assertion fails outside tolerance, first check the tolerance math above against `GachaPool.RollResult`'s actual thresholds (0.15 for character split into 3×0.05 slices; 62/97/100 for currency tiers) before assuming a code bug — 100k trials should comfortably converge within the stated tolerances for these probabilities (standard error for a 15% split at n=100000 is ~0.11%, well inside the ±0.5% band).

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/PlayModeSmokeTest.cs
git commit -m "test: add gacha distribution statistical test with controlled RNG (GDD 11.1)"
```

(If Task 6 and Task 8 are executed together by the same worker, fold this test into Task 6 Step 1 instead of a separate commit — listed separately here only because the user's brief calls it out as its own deliverable.)

---

## Task 9: Veteran deck on run completion + minimal Hub screens

**Files:**
- Modify: `Assets/Scripts/UI/RunFlowController.cs`
- Modify: `Assets/Scripts/Managers/HubManager.cs`
- Modify: `Assets/UI/GameRoot.uxml`
- Test: `Assets/Editor/PlayModeSmokeTest.cs`

**Interfaces:**
- Consumes: `SaveManager.AddVeteranAndIncrementRunCount(VeteranCharacter)` (Task 3), `CharacterManager.Combatant`/`Progress`/`Character`/`EquippedItems` (existing).
- Produces: `RunFlowController.BuildVeteranSnapshot()` (private helper, called from `ShowResultsFlow`) that constructs a `VeteranCharacter` from the just-finished run. `HubManager.OpenVeteranDeck`/`OpenCharacters` now show real (if minimal) lists instead of empty method bodies.

- [ ] **Step 1: Write the failing test**

Add to `RunPureLogicChecks()`:

```csharp
        // 9.2 (Codex P2 2026-08-27): завершение забега должно атомарно добавить ветерана и
        // инкрементировать счётчик прохождений — раньше этой пары не существовало вовсе.
        var veteranTestSave = new SaveData();
        var veteranTestManager = new GameObject("SmokeTestSaveManager_Veteran").AddComponent<SaveManager>();
        veteranTestManager.LoadGame(); // создаёт пустой Data, реальный файл не тронут этим объектом до первого SaveGame
        var snapshot = new VeteranCharacter { characterId = "smoketest_veteran", finalHP = 42f, powerLevel = 999 };
        int runCountBefore = veteranTestManager.GetRunCount("smoketest_veteran");
        veteranTestManager.Data.veteranDeck.Clear(); // изолируем от реального содержимого диска для этой проверки счётчика
        veteranTestManager.AddVeteranAndIncrementRunCount(snapshot);
        Check(veteranTestManager.Data.veteranDeck.Count == 1 && veteranTestManager.Data.veteranDeck[0].characterId == "smoketest_veteran",
            $"9.2 AddVeteranAndIncrementRunCount добавляет ветерана: count={veteranTestManager.Data.veteranDeck.Count}");
        Check(veteranTestManager.GetRunCount("smoketest_veteran") == runCountBefore + 1,
            $"9.2 AddVeteranAndIncrementRunCount инкрементирует характер-run-count: {veteranTestManager.GetRunCount("smoketest_veteran")} (ожидалось {runCountBefore + 1})");
        UnityEngine.Object.DestroyImmediate(veteranTestManager.gameObject);
```

(This test mutates and then discards an in-memory `SaveData`/temp save file via a throwaway `GameObject`'s `SaveManager` — the real player save on disk is untouched since `PlayModeSmokeTest.Run`'s `originalSaveBytes` backup/restore in `Finish()` already covers any accidental `SaveGame()` calls made by this or other test-created `SaveManager` instances pointed at the same `Application.persistentDataPath` file.)

- [ ] **Step 2: Run test to verify it fails**

Same batchmode command. Expected: FAIL — `SaveManager.AddVeteranAndIncrementRunCount`/`GetRunCount` don't exist yet if Task 3 hasn't landed first in this worker's session; if Task 3 already landed (recommended order), this becomes a normal TDD step confirming the method works standalone before wiring it into `RunFlowController`.

- [ ] **Step 3: Build the veteran snapshot in `RunFlowController.ShowResultsFlow`**

Add a helper method:

```csharp
    // 9.2/9.4: снимок персонажа на завершение забега (победа ИЛИ поражение — "финальный" снимок,
    // не только для побед). powerLevel — DRAFT-формула, см. комментарий на VeteranCharacter в
    // SaveData.cs; финальная формула — открытый вопрос ГДД (1, п.8).
    VeteranCharacter BuildVeteranSnapshot()
    {
        var veteran = new VeteranCharacter
        {
            characterId = characterManager.Character.characterId,
            finalHP = characterManager.Combatant.MaxHP,
            powerLevel = characterManager.Level * 100
        };

        int totalSkillLevels = 0;
        foreach (var pair in characterManager.Progress.KnownSkillLevels)
        {
            veteran.finalSkills.Add(new VeteranSkillEntry { skillName = pair.Key.skillName, level = pair.Value });
            totalSkillLevels += pair.Value;
        }
        veteran.powerLevel += totalSkillLevels * 10;

        foreach (var item in characterManager.EquippedItems)
        {
            if (item != null)
            {
                veteran.finalEquipment.Add(item.itemName);
            }
        }
        veteran.powerLevel += veteran.finalEquipment.Count * 5;

        return veteran;
    }
```

In `ShowResultsFlow`, right after the existing `if (saveManager != null) { saveManager.AddMetaCurrency(...); saveManager.AddGachaCurrency(...); }` block, add:

```csharp
        if (saveManager != null)
        {
            saveManager.AddVeteranAndIncrementRunCount(BuildVeteranSnapshot());
        }
```

- [ ] **Step 4: Minimal `HubManager.OpenVeteranDeck`/`OpenCharacters`**

Add UXML screens to `Assets/UI/GameRoot.uxml` (siblings of `BuildingsScreen`/`GachaScreen`):

```xml
    <ui:VisualElement name="VeteranDeckScreen" class="full-screen hidden" style="flex-direction: column;">
        <ui:Label text="Колода ветеранов" class="title-label" />
        <ui:ScrollView name="VeteranDeckScrollView" />
        <ui:Button name="VeteranDeckBackButton" text="Назад" class="button-secondary" />
    </ui:VisualElement>

    <ui:VisualElement name="CharactersScreen" class="full-screen hidden" style="flex-direction: column;">
        <ui:Label text="Персонажи" class="title-label" />
        <ui:ScrollView name="CharactersScrollView" />
        <ui:Button name="CharactersBackButton" text="Назад" class="button-secondary" />
    </ui:VisualElement>
```

Add a `VeteranDeckButton`/`CharactersButton` to `MainMenuScreen` (alongside the existing `BuildingsButton`/`GachaButton`):

```xml
        <ui:Button name="VeteranDeckButton" text="Ветераны" class="button-secondary" />
        <ui:Button name="CharactersButton" text="Персонажи" class="button-secondary" />
```

In `HubManager.cs`, add fields, cache them in `CacheElements`, wire buttons in `Start()`, and implement:

```csharp
    VisualElement veteranDeckScreen;
    ScrollView veteranDeckScrollView;
    Button veteranDeckButton;
    Button veteranDeckBackButton;

    VisualElement charactersScreen;
    ScrollView charactersScrollView;
    Button charactersButton;
    Button charactersBackButton;
```

```csharp
        veteranDeckScreen = root.Q<VisualElement>("VeteranDeckScreen");
        veteranDeckScrollView = root.Q<ScrollView>("VeteranDeckScrollView");
        veteranDeckButton = root.Q<Button>("VeteranDeckButton");
        veteranDeckBackButton = root.Q<Button>("VeteranDeckBackButton");

        charactersScreen = root.Q<VisualElement>("CharactersScreen");
        charactersScrollView = root.Q<ScrollView>("CharactersScrollView");
        charactersButton = root.Q<Button>("CharactersButton");
        charactersBackButton = root.Q<Button>("CharactersBackButton");
```

```csharp
        veteranDeckButton.clicked += OpenVeteranDeck;
        veteranDeckBackButton.clicked += OpenVillage;
        charactersButton.clicked += OpenCharacters;
        charactersBackButton.clicked += OpenVillage;
```

Replace the empty `OpenVeteranDeck`/`OpenCharacters` methods:

```csharp
    public void OpenVeteranDeck()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        veteranDeckScreen.style.display = DisplayStyle.Flex;

        veteranDeckScrollView.Clear();
        foreach (var veteran in saveManager.Data.veteranDeck)
        {
            var row = new Label($"{veteran.characterId} — сила {veteran.powerLevel}, HP {veteran.finalHP:F0}, навыков {veteran.finalSkills.Count}, снаряжения {veteran.finalEquipment.Count}");
            row.AddToClassList("body-label");
            veteranDeckScrollView.Add(row);
        }

        if (saveManager.Data.veteranDeck.Count == 0)
        {
            var empty = new Label("Пока нет завершённых забегов.");
            empty.AddToClassList("body-label");
            veteranDeckScrollView.Add(empty);
        }
    }

    public void OpenCharacters()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        charactersScreen.style.display = DisplayStyle.Flex;

        charactersScrollView.Clear();
        foreach (var character in gachaCharacters)
        {
            if (character == null) continue;

            int copies = saveManager.GetCharacterCopies(character.characterId);
            int runs = saveManager.GetRunCount(character.characterId);
            // seenVNScenes: Codex заполняет содержимое; здесь только читается количество без падения
            // на отсутствующем персонаже (пустой список — валидное "ещё не открыто ни одной сцены").
            var sceneEntry = saveManager.Data.seenVNScenes.Find(e => e.characterId == character.characterId);
            int seenScenes = sceneEntry != null ? sceneEntry.sceneIds.Count : 0;

            var row = new Label($"{character.characterName} ({character.characterClass}) — копий: {copies}, прохождений: {runs}, открытых сцен: {seenScenes}");
            row.AddToClassList("body-label");
            charactersScrollView.Add(row);
        }
    }
```

Also update `OpenVillage()` (existing method) to hide the two new screens alongside the existing ones:

```csharp
    public void OpenVillage()
    {
        buildingsScreen.style.display = DisplayStyle.None;
        gachaScreen.style.display = DisplayStyle.None;
        veteranDeckScreen.style.display = DisplayStyle.None;
        charactersScreen.style.display = DisplayStyle.None;
        mainMenuScreen.style.display = DisplayStyle.Flex;
    }
```

- [ ] **Step 5: Run test to verify it passes**

Same batchmode command. Expected: 253 + 2 = 255 OK, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/UI/RunFlowController.cs Assets/Scripts/Managers/HubManager.cs Assets/UI/GameRoot.uxml Assets/Editor/PlayModeSmokeTest.cs
git commit -m "feat: add veteran on run completion, minimal veteran deck / characters hub screens (GDD 9.2, 7.1)"
```

---

## Task 10: Full batchmode smoke run + Notion report

**Files:** none (verification + documentation task)

- [ ] **Step 1: Run the full smoke suite one final time**

```
"C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Unity Projects\DungeonGirls" -executeMethod PlayModeSmokeTest.Run -logFile "C:\Unity Projects\DungeonGirls\Temp\SmokeTest_Final.log"
```

Confirm: exit with `RESULT=PASS`, 0 errors, final OK count (expected 255, confirm exact number from the log — Codex's baseline was 232, this plan adds 23 new checks across Tasks 1, 3, 4, 5, 6, 8, 9). Confirm the real save file backup/restore log lines are present (matches Codex's prior run behavior).

- [ ] **Step 2: Manual playtest for screenshots**

Open the project in Unity Editor, enter Play Mode manually (not batchmode), and capture:
- Character select screen showing all 3 cards (Jennifer/Rogue/Barbarian).
- Gacha screen mid-pull showing a character landing result.
- Gacha screen mid-pull showing a meta-currency landing result.
- Buildings screen after a successful upgrade (level incremented, currency deducted).

- [ ] **Step 3: Write the Notion report**

Create a new child page under the working GDD page (`3c10227a-2824-81bb-a9c0-c2f212bddbfb`), following the same journaling convention Codex and prior Claude Code sessions used (see `3c60227a-2824-8186-b7a1-e34b283b7d50`/`3c70227a-2824-811d-ad2a-c35bf534e4e3` for format reference). Cover, per the user's original request:
- Part 1: each of the 3 P1 fixes, with file/line references and the regression test added for each.
- Part 2: gacha rewrite details — pool math, removed item branch, animation reuse, statistical test results (actual measured percentages from the batchmode log).
- Part 3: SaveData schema changes, migration behavior, atomicity fix, veteran/run-count addition.
- Full smoke-test result (exact final OK/error counts, `RESULT=PASS`/`FAIL`, log path).
- Screenshots from Step 2 (attach or embed).
- **Explicitly flag the `characterId`/`seenVNScenes` schema assumption** (Task 2's `characterId` field, Task 3's `CharacterSceneList` shape) as something Codex should re-check before writing VN read/write logic against it, per the coordination note in the user's original prompt.

- [ ] **Step 4: Final commit (if any docs-only changes were made)**

Only if this plan file itself needs a completion note appended, or if a follow-up polish item (Task 7's silhouette-reveal deferral) was written up separately — otherwise no commit needed for this task, it's verification + Notion-only.
