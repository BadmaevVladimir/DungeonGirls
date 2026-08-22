using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "DungeonGirls/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public EquipmentSlot slot;
    public WeaponSubtype weaponSubtype = WeaponSubtype.None;
    public ItemTier tier;
    public int itemLevel = 1;
    public CharacterClass[] allowedClasses;

    public float baseDamage;
    public DamageType damageType = DamageType.Physical;
    public float attackSpeed;

    public float physicalDefense;
    public float maxPhysicalDefenseBonus;

    public BonusStat bonusStat;
    public PassiveSkillData passiveSkill;
}
