using UnityEngine;

[CreateAssetMenu(fileName = "NewActiveSkill", menuName = "DungeonGirls/Active Skill")]
public class ActiveSkillData : ScriptableObject
{
    public string skillName;
    public SkillId skillId;

    [TextArea(3, 10)]
    public string effectDescription;

    public int maxLevel;
    public float cooldownSeconds;
    public ActiveSkillTargetType targetType;
}
