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
