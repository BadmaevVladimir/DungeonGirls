using System.Collections.Generic;
using UnityEngine;

// Общие форматтеры отображения, использовавшиеся дублировано в RunFlowController/HubManager
// (RarityLabel) или существовавшие только в RunFlowController но нужные обоим экранам.
public static class DisplayFormat
{
    public static string CharacterClassDisplayName(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Warrior => "Воин",
        CharacterClass.Rogue => "Плут",
        CharacterClass.Barbarian => "Варвар",
        _ => characterClass.ToString()
    };

    public static string RarityLabel(ItemTier tier)
    {
        switch (tier)
        {
            case ItemTier.Common: return "Обычный";
            case ItemTier.Rare: return "Редкий";
            default: return "Эпический";
        }
    }

    public static string SlotLabel(ItemData item)
    {
        if (item == null)
        {
            return "Снаряжение";
        }

        bool isRogueOnly = item.allowedClasses != null && item.allowedClasses.Length == 1 && item.allowedClasses[0] == CharacterClass.Rogue;
        bool isBarbarianOnly = item.allowedClasses != null && item.allowedClasses.Length == 1 && item.allowedClasses[0] == CharacterClass.Barbarian;

        switch (item.slot)
        {
            case EquipmentSlot.Helmet: return isRogueOnly ? "Капюшон" : isBarbarianOnly ? "Трофей" : "Шлем";
            case EquipmentSlot.Armor: return isRogueOnly ? "Кожаная броня" : isBarbarianOnly ? "Пояс" : "Нагрудник";
            case EquipmentSlot.Boots: return "Сапоги";
            case EquipmentSlot.Weapon: return item.weaponSubtype == WeaponSubtype.Shield ? "Щит" : item.isTwoHanded ? "Двуручное оружие" : "Оружие";
            case EquipmentSlot.Ring: return "Кольцо";
            default: return "Аксессуар";
        }
    }

    public static string BonusStatText(ItemData item)
    {
        BonusStat bonusStat = item != null ? item.bonusStat : null;
        if (bonusStat == null || bonusStat.type == BonusStatType.None || Mathf.Approximately(bonusStat.baseValue, 0f))
        {
            return string.Empty;
        }

        float value = bonusStat.type == BonusStatType.MaxPhysicalDefenseFlat
            ? ItemEffectBalance.ArmorAccessoryMaxDefense(bonusStat.baseValue, item.itemLevel)
            : StatScaling.ScaleItemEffect(bonusStat.baseValue, item.itemLevel);
        switch (bonusStat.type)
        {
            case BonusStatType.CritChancePercent: return $"+шанс крита: {value:F1}%";
            case BonusStatType.ArmorPenetrationFlat: return $"+пробивание брони: {value:F1}";
            case BonusStatType.AttackSpeedPercent: return $"+скорость атаки: {value:F1}%";
            case BonusStatType.DamagePercent: return $"+урон: {value:F1}%";
            case BonusStatType.FlatHP: return $"+HP: {value:F1}";
            case BonusStatType.MaxPhysicalDefenseFlat: return $"+макс. физ. защита: {value:F1}";
            case BonusStatType.MagicShieldFlat: return $"+магический щит: {value:F1}";
            case BonusStatType.WeaponDamageFlat: return $"+урон оружия: {value:F1}";
            case BonusStatType.EvasionPercent: return $"+уклонение: {value:F1}%";
            case BonusStatType.ArmorIgnorePercent: return $"+игнорирование брони: {value:F1}%";
            default: return string.Empty;
        }
    }

    public static string ItemStatsText(ItemData item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var lines = new List<string> { $"{SlotLabel(item)}, {RarityLabel(item.tier)}, ур. {item.itemLevel}" };

        if (item.slot == EquipmentSlot.Weapon && item.weaponSubtype != WeaponSubtype.None && item.weaponSubtype != WeaponSubtype.Shield)
        {
            DamageCalculator.ComputeDamageRange(item.EffectiveDamage, out float dmgMin, out float dmgMax);
            lines.Add($"Урон: {dmgMin:F0}-{dmgMax:F0} ({item.damageType}), скорость атаки: {item.attackSpeed:F2}/с");
            if (item.isTwoHanded)
            {
                lines.Add("Двуручное: занимает обе руки, но бьёт на 30% сильнее.");
            }
        }

        if (item.physicalDefense > 0f)
        {
            lines.Add($"Физ. защита: {item.EffectiveDefense:F0}");
        }

        if (item.maxPhysicalDefenseBonus > 0f)
        {
            lines.Add($"+макс. физ. защита: {item.EffectiveMaxDefenseBonus:F0}");
        }

        if (item.MagicShieldEffective > 0f)
        {
            lines.Add($"Магический щит: {item.MagicShieldEffective:F0}");
        }

        if (item.HpBonusEffective > 0f)
        {
            lines.Add($"+HP: {item.HpBonusEffective:F0}");
        }

        if (item.rageBonusFlatPercent > 0f)
        {
            lines.Add($"+Ярость: {StatScaling.ScaleItemEffect(item.rageBonusFlatPercent, item.itemLevel):F1}%");
        }

        string bonusText = BonusStatText(item);
        if (!string.IsNullOrWhiteSpace(bonusText))
        {
            lines.Add(bonusText + $" (ранг {StatScaling.ItemEffectRank(item.itemLevel)} из V — растёт с уровнем предмета)");
            if (item.slot == EquipmentSlot.Ring && item.bonusStat.type == BonusStatType.MaxPhysicalDefenseFlat)
            {
                lines.Add("Если надеть второе такое кольцо, оно даст половину этого бонуса.");
            }
        }

        if (item.passiveSkill != null)
        {
            lines.Add($"Пассивка «{item.passiveSkill.skillName}»: {item.passiveSkill.effectDescription}");
        }

        return string.Join("\n", lines);
    }
}
