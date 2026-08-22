using UnityEngine;

[CreateAssetMenu(fileName = "NewPassiveSkill", menuName = "DungeonGirls/Passive Skill")]
public class PassiveSkillData : ScriptableObject
{
    public string skillName;
    public SkillCategory category;

    [TextArea(3, 10)]
    public string effectDescription;

    public int maxLevel = 5;
}
