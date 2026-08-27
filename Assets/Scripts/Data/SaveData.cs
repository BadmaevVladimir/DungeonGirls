using System;
using System.Collections.Generic;
using UnityEngine.Serialization;

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
// не обязательно "лучшие"). Формула powerLevel остаётся открытым дизайн-вопросом в ГДД;
// технический слой хранит значение, но не придумывает формулу без решения дизайнера.
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
    public const int CurrentSaveVersion = 3; // 3 = именные ID violet/sasha вместо ошибочных классовых rogue/barbarian.

    public int saveVersion = SaveData.CurrentSaveVersion;

    public int metaCurrency;
    public int gachaCurrency;

    public int forgeLevel;
    public int templeLevel;
    public int tavernLevel;

    [FormerlySerializedAs("characterCopies")]
    public List<KeyCountEntry> gachaOwnedCharacters = new List<KeyCountEntry>();
    public List<VeteranCharacter> veteranDeck = new List<VeteranCharacter>();
    public List<KeyCountEntry> characterRunCounts = new List<KeyCountEntry>();
    public List<CharacterSceneList> seenVNScenes = new List<CharacterSceneList>();
}
