public enum CharacterClass
{
    Warrior,
    Mage,
    Rogue,
    Barbarian
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
    Shield,
    Blade,
    TwoHandedAxe
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

// 8.1: 3 здания деревни, апгрейдятся 0-5.
public enum BuildingType
{
    Forge,
    Temple,
    Tavern
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
    EvasionPercent,
    ArmorIgnorePercent
}

// 2.8: род существительного-названия монстра на русском, нужен для согласования прилагательного
// модификатора ("Быстрый Скелет" vs "Большая Слизь" vs, гипотетически, "Быстрое Существо").
public enum MonsterGender
{
    Masculine,
    Feminine,
    Neuter
}

// 2.8: 4 модификатора монстров (роллятся по формуле шанса/лимита в MonsterModifierCatalog).
public enum MonsterModifierType
{
    Fast,
    Big,
    Armored,
    Fierce
}
