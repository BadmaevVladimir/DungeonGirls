public enum CharacterClass
{
    Warrior
}

public enum DamageType
{
    Physical,
    Magical
}

public enum EquipmentSlot
{
    Helmet,
    Armor,
    Boots,
    Weapon,
    Ring,
    Accessory
}

public enum WeaponSubtype
{
    None,
    Sword,
    Axe,
    Spear,
    Hammer,
    Shield
}

public enum ItemTier
{
    Common,
    Rare,
    Epic
}

public enum SkillCategory
{
    General,
    WarriorClass,
    Unique,
    ItemPassive,
    MonsterPassive
}

public enum ActiveSkillTargetType
{
    Self,
    SingleTarget,
    AoE
}

public enum BonusStatType
{
    None,
    CritChancePercent,
    ArmorPenetrationFlat,
    AttackSpeedPercent,
    DamagePercent,
    FlatHP,
    MaxPhysicalDefenseFlat,
    MagicShieldFlat,
    WeaponDamageFlat,
    EvasionPercent
}
