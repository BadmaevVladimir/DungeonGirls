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
    Epic,
    // Добавлять только в конец: числовые значения старых ассетов/сохранений не меняются.
    Cursed
}

public enum CursedEffectId
{
    None = 0,
    Oathbreaker,
    Executioner,
    BerserkerAxe,
    RecklessCharge,
    LastArgument,
    BetrayerAndAccomplice,
    ParanoiaBlades,
    ThornAxe
}

public enum SkillCategory
{
    General,
    WarriorClass,
    Unique,
    ItemPassive,
    MonsterPassive,
    // 3.11: классовые пулы новых классов. Дописаны В КОНЕЦ намеренно — значения существующих
    // элементов не сдвигаются, уже сериализованные ассеты не ломаются.
    RogueClass,
    BarbarianClass
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

// 2.8: модификаторы монстров (роллятся по формуле шанса/лимита в MonsterModifierCatalog).
public enum MonsterModifierType
{
    Fast,
    Big,
    Armored,
    Fierce,
    // Добавлен в конец, чтобы не менять сериализованные числовые значения старых модификаторов.
    ArmorPiercing
}

// Стабильный идентификатор навыка — не меняется при переименовании skillName в инспекторе.
// Заменяет строковое сравнение по SkillEffectMap/MonsterSkillEffectMap константам в боевой логике.
public enum SkillId
{
    None = 0,
    FieldRepair, Freeze, Luck, Evasion, Sturdy, CriticalHits, IAmTheWall, Ambidexterity, Thorns,
    Unyielding, Bleed,
    Vampirism, ArmorBreak, Piercing, Repair, Elusiveness, GoldenTouch, ToughSole,
    EyeForAnEye, PoisonedBlade, ByAThread, Elimination, SlipAway,
    Stubbornness, Frenzy, CombatRegen, Intimidation, Superstition,
    Shadow, SmokeBomb, ChampionOfTheTribe, Berserk,
    Riposte, EmbraceOfNight, Execution, GiantSlayer, JustAScratch,
    MonsterSlowCurse, MonsterFluttering, MonsterArmorPiercingBlade, MonsterCorrosion,
    MonsterStunningScream, MonsterDarkHeal, MonsterDoubleStrike
}
