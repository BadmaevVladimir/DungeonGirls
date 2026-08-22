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
}
