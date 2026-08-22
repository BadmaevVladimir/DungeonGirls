using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "DungeonGirls/Character")]
public class CharacterData : ScriptableObject
{
    public string characterName;
    public CharacterClass characterClass;

    public int baseHealth;
    public int healthPerLevel;

    public PassiveSkillData uniquePassiveSkill;
    public ActiveSkillData uniqueActiveSkill;

    public ItemData[] startingEquipment;
}
