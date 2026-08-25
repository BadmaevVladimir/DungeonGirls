using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "DungeonGirls/Character")]
public class CharacterData : ScriptableObject
{
    public string characterName;
    public Sprite portrait; // 10.6: пиксель-арт портрет персонажа (64x64).
    public CharacterClass characterClass;

    public int baseHealth;
    public int healthPerLevel;

    public PassiveSkillData uniquePassiveSkill;
    public ActiveSkillData uniqueActiveSkill;

    public ItemData[] startingEquipment;
}
