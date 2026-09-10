using System.Collections.Generic;

public readonly struct ItemMechanicTag
{
    public readonly string Id;
    public readonly string Label;
    public readonly string Tooltip;
    public readonly string StyleClass;

    public ItemMechanicTag(string id, string label, string tooltip, string styleClass)
    {
        Id = id;
        Label = label;
        Tooltip = tooltip;
        StyleClass = styleClass;
    }
}

// Короткие теги показывают ключевую механику предмета. Полное условие остаётся в описании,
// а каждый тег имеет одинаковое пояснение во всех карточках и в окне снаряжения.
public static class ItemMechanicTags
{
    static readonly ItemMechanicTag Physical = new("physical", "ФИЗ", "Наносит физический урон. Его уменьшает физическая защита и физическое сопротивление.", "item-tag-damage");
    static readonly ItemMechanicTag Magical = new("magical", "МАГ", "Наносит магический урон. Сначала его уменьшает магическое сопротивление, затем урон принимают универсальный барьер и магический щит.", "item-tag-magic");
    static readonly ItemMechanicTag Direct = new("direct", "ПРЯМОЙ", "Снимает здоровье напрямую, минуя физическую защиту и магический щит. Не обходит неуязвимость.", "item-tag-danger");
    static readonly ItemMechanicTag MagicShieldDamage = new("magic-shield-damage", "ЩИТ−", "Дополнительный урон действует только на магический щит и не переносится на здоровье.", "item-tag-magic");
    static readonly ItemMechanicTag Armor = new("armor", "БРОНЯ", "Даёт физическую защиту. Она поглощает физические удары и изнашивается в бою.", "item-tag-defense");
    static readonly ItemMechanicTag MagicShield = new("magic-shield", "МАГ. ЩИТ", "Даёт магический щит, который поглощает магический урон и полностью восстанавливается после боя.", "item-tag-magic");
    static readonly ItemMechanicTag Health = new("health", "HP", "Увеличивает максимальное здоровье владельца.", "item-tag-health");
    static readonly ItemMechanicTag OneHanded = new("one-handed", "1 РУКА", "Занимает один слот руки и создаёт один источник обычных атак.", "item-tag-hand");
    static readonly ItemMechanicTag TwoHanded = new("two-handed", "2 РУКИ", "Занимает оба слота рук как один предмет.", "item-tag-hand");
    static readonly ItemMechanicTag Paired = new("paired", "ПАРНОЕ", "Создаёт два независимых источника обычных атак, по одному на каждый клинок.", "item-tag-hand");
    static readonly ItemMechanicTag Critical = new("critical", "КРИТ", "Эффект связан с шансом или результатом критического удара.", "item-tag-trigger");
    static readonly ItemMechanicTag Hit = new("hit", "ПОПАДАНИЕ", "Срабатывает при попадании, включая удар, полностью остановленный защитой, но исключая уклонение.", "item-tag-trigger");
    static readonly ItemMechanicTag HpDamage = new("hp-damage", "УРОН ПО HP", "Срабатывает только когда атака фактически снимает хотя бы одну единицу здоровья.", "item-tag-trigger");
    static readonly ItemMechanicTag Evasion = new("evasion", "УКЛОНЕНИЕ", "Повышает уклонение или срабатывает после него. Уклонение полностью отменяет атаку.", "item-tag-trigger");
    static readonly ItemMechanicTag Healing = new("healing", "ЛЕЧЕНИЕ", "Восстанавливает здоровье и учитывает усиление или ослабление получаемого лечения.", "item-tag-health");
    static readonly ItemMechanicTag Bleed = new("bleed", "КРОВЬ", "Связан с кровотечением — периодическим уроном, который может взаимодействовать с критическими ударами.", "item-tag-danger");
    static readonly ItemMechanicTag Stealth = new("stealth", "СКРЫТНОСТЬ", "Эффект действует во время Скрытности или помогает использовать её взаимодействия.", "item-tag-stealth");
    static readonly ItemMechanicTag ArmorWear = new("armor-wear", "ИЗНОС", "Помогает пробить или быстрее износить физическую защиту цели.", "item-tag-danger");
    static readonly ItemMechanicTag Splash = new("splash", "ДРУГИЕ ЦЕЛИ", "Часть эффекта распространяется на других живых врагов.", "item-tag-effect");
    static readonly ItemMechanicTag AttackSpeed = new("attack-speed", "СКОРОСТЬ", "Изменяет скорость обычных атак предмета или владельца.", "item-tag-effect");
    static readonly ItemMechanicTag Rage = new("rage", "ЯРОСТЬ", "Даёт Ярость или меняет силу эффекта в зависимости от неё.", "item-tag-danger");
    static readonly ItemMechanicTag Rest = new("rest", "ПРИВАЛ", "Эффект срабатывает или применяется во время привала.", "item-tag-effect");
    static readonly ItemMechanicTag Currency = new("currency", "МОНЕТЫ", "Изменяет получение валюты текущего забега.", "item-tag-effect");
    static readonly ItemMechanicTag Curse = new("curse", "ПРОКЛЯТИЕ", "Даёт сильный положительный эффект вместе с обязательным отрицательным свойством.", "item-tag-curse");
    static readonly ItemMechanicTag Gear = new("gear", "СНАРЯЖЕНИЕ", "Предмет занимает соответствующий слот и действует, пока экипирован.", "item-tag-effect");

    public static List<ItemMechanicTag> Get(ItemData item, int maximum = 5)
    {
        var result = new List<ItemMechanicTag>();
        if (item == null || maximum <= 0) return result;

        void Add(ItemMechanicTag tag)
        {
            if (result.Count >= maximum || result.Exists(existing => existing.Id == tag.Id)) return;
            result.Add(tag);
        }

        bool attacks = item.slot == EquipmentSlot.Weapon && item.weaponSubtype != WeaponSubtype.None && item.weaponSubtype != WeaponSubtype.Shield;
        if (attacks)
        {
            if (item.prototypeEffect == WeaponPrototypeEffectId.DayAndNight)
            {
                Add(Physical);
                Add(Magical);
            }
            else Add(item.damageType == DamageType.Magical ? Magical : Physical);

            Add(item.isTwoHanded ? TwoHanded : OneHanded);
            if (item.isPairedWeapon) Add(Paired);
        }

        if (item.physicalDefense > 0f || item.maxPhysicalDefenseBonus > 0f || item.weaponSubtype == WeaponSubtype.Shield) Add(Armor);
        if (item.magicShieldBonus > 0f) Add(MagicShield);
        if (item.hpBonus > 0f || item.bonusStat?.type == BonusStatType.FlatHP) Add(Health);

        if (item.cursedEffect != CursedEffectId.None) Add(Curse);
        AddEffectTags(item, Add);
        if (result.Count == 0) Add(Gear);
        return result;
    }

    static void AddEffectTags(ItemData item, System.Action<ItemMechanicTag> add)
    {
        switch (item.passiveSkill != null ? item.passiveSkill.skillId : SkillId.None)
        {
            case SkillId.Vampirism: add(Critical); add(HpDamage); add(Healing); break;
            case SkillId.ArmorBreak: case SkillId.Piercing: add(HpDamage); add(item.passiveSkill.skillId == SkillId.Piercing ? Splash : ArmorWear); break;
            case SkillId.Repair: case SkillId.JustAScratch: add(Rest); add(item.passiveSkill.skillId == SkillId.JustAScratch ? Healing : Armor); break;
            case SkillId.Elusiveness: case SkillId.Riposte: add(Evasion); break;
            case SkillId.GoldenTouch: add(Currency); break;
            case SkillId.EmbraceOfNight: add(Stealth); add(Magical); break;
            case SkillId.Execution: case SkillId.GiantSlayer: add(HpDamage); break;
        }

        switch (item.bonusStat?.type ?? BonusStatType.None)
        {
            case BonusStatType.CritChancePercent: add(Critical); break;
            case BonusStatType.EvasionPercent: add(Evasion); break;
            case BonusStatType.ArmorPenetrationFlat: case BonusStatType.ArmorIgnorePercent: add(ArmorWear); break;
            case BonusStatType.AttackSpeedPercent: add(AttackSpeed); break;
            case BonusStatType.DamagePercent: case BonusStatType.WeaponDamageFlat: add(HpDamage); break;
            case BonusStatType.MagicShieldFlat: add(MagicShield); break;
            case BonusStatType.MaxPhysicalDefenseFlat: add(Armor); break;
        }
        if (item.rageBonusFlatPercent > 0f) add(Rage);

        switch (item.prototypeEffect)
        {
            case WeaponPrototypeEffectId.SpellEater: add(MagicShieldDamage); add(ArmorWear); break;
            case WeaponPrototypeEffectId.LightningSpear: add(Magical); add(Hit); break;
            case WeaponPrototypeEffectId.ResonanceScimitar: add(AttackSpeed); break;
            case WeaponPrototypeEffectId.Pendulum: add(Hit); break;
            case WeaponPrototypeEffectId.LastArgumentConversion: add(AttackSpeed); break;
        }

        switch (item.cursedEffect)
        {
            case CursedEffectId.Oathbreaker: add(Critical); add(Currency); add(Rest); break;
            case CursedEffectId.Executioner: add(HpDamage); break;
            case CursedEffectId.BerserkerAxe: case CursedEffectId.RecklessCharge: add(Hit); add(AttackSpeed); break;
            case CursedEffectId.LastArgument: add(Direct); add(Armor); break;
            case CursedEffectId.BetrayerAndAccomplice: add(Stealth); break;
            case CursedEffectId.ParanoiaBlades: add(Evasion); add(AttackSpeed); break;
            case CursedEffectId.ThornAxe: add(Bleed); add(Critical); break;
        }
    }
}
