using System.Collections.Generic;
using UnityEngine;

// Общие форматтеры отображения, использовавшиеся дублировано в RunFlowController/HubManager
// (RarityLabel) или существовавшие только в RunFlowController но нужные обоим экранам.
public static class DisplayFormat
{
    public static string RankLabel(int rank) => Mathf.Clamp(rank, 1, 5) switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => "V"
    };
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
            case ItemTier.Cursed: return "Проклятый";
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
            case BonusStatType.CritChancePercent: return $"+шанс критического удара: {SkillDescriptionFormatter.Value($"{value:F1}%")}";
            case BonusStatType.ArmorPenetrationFlat: return $"+пробивание физической защиты: {SkillDescriptionFormatter.Value($"{value:F1}")}";
            case BonusStatType.AttackSpeedPercent: return $"+скорость атаки: {SkillDescriptionFormatter.Value($"{value:F1}%")}";
            case BonusStatType.DamagePercent: return $"+урон: {SkillDescriptionFormatter.Value($"{value:F1}%")}";
            case BonusStatType.FlatHP: return $"+здоровье: {SkillDescriptionFormatter.Value($"{value:F1}")}";
            case BonusStatType.MaxPhysicalDefenseFlat: return $"+макс. физическая защита: {SkillDescriptionFormatter.Value($"{value:F1}")}";
            case BonusStatType.MagicShieldFlat: return $"+магический щит: {SkillDescriptionFormatter.Value($"{value:F1}")}";
            case BonusStatType.WeaponDamageFlat: return $"+урон оружия: {SkillDescriptionFormatter.Value($"{value:F1}")}";
            case BonusStatType.EvasionPercent: return $"+уклонение: {SkillDescriptionFormatter.Value($"{value:F1}%")}";
            case BonusStatType.ArmorIgnorePercent: return $"+игнорирование физической защиты: {SkillDescriptionFormatter.Value($"{value:F1}%")}";
            default: return string.Empty;
        }
    }

    public static string ItemStatsText(ItemData item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var lines = new List<string> { $"{SlotLabel(item)}, {RarityLabel(item.tier)}, уровень {SkillDescriptionFormatter.Value(item.itemLevel.ToString())}" };
        if (item.tier == ItemTier.Cursed) lines.Add($"Ранг эффекта: {SkillDescriptionFormatter.Value(RankLabel(item.EffectRank))} из V");

        if (item.slot == EquipmentSlot.Weapon && item.weaponSubtype != WeaponSubtype.None && item.weaponSubtype != WeaponSubtype.Shield)
        {
            DamageCalculator.ComputeDamageRange(item.EffectiveDamage, out float dmgMin, out float dmgMax);
            lines.Add($"Урон: {SkillDescriptionFormatter.Value($"{dmgMin:F0}–{dmgMax:F0}")} ({item.damageType}), скорость атаки: {SkillDescriptionFormatter.Value($"{item.attackSpeed:F2}/с")}");
            if (item.isTwoHanded && item.tier != ItemTier.Cursed)
            {
                lines.Add("Двуручное: занимает обе руки, но бьёт на 30% сильнее.");
            }
        }

        if (item.physicalDefense > 0f)
        {
            lines.Add($"Физическая защита: {SkillDescriptionFormatter.Value($"{item.EffectiveDefense:F0}")}");
        }

        if (item.maxPhysicalDefenseBonus > 0f)
        {
            lines.Add($"+максимальная физическая защита: {SkillDescriptionFormatter.Value($"{item.EffectiveMaxDefenseBonus:F0}")}");
        }

        if (item.MagicShieldEffective > 0f)
        {
            lines.Add($"Магический щит: {SkillDescriptionFormatter.Value($"{item.MagicShieldEffective:F0}")}");
        }

        if (item.HpBonusEffective > 0f)
        {
            lines.Add($"+здоровье: {SkillDescriptionFormatter.Value($"{item.HpBonusEffective:F0}")}");
        }

        if (item.rageBonusFlatPercent > 0f)
        {
            lines.Add($"+Ярость: {SkillDescriptionFormatter.Value($"{StatScaling.ScaleItemEffect(item.rageBonusFlatPercent, item.itemLevel):F1}%")}");
        }

        string bonusText = BonusStatText(item);
        if (!string.IsNullOrWhiteSpace(bonusText))
        {
            lines.Add(bonusText + $" (ранг {SkillDescriptionFormatter.Value(StatScaling.ItemEffectRank(item.itemLevel).ToString())} из V)");
            if (item.slot == EquipmentSlot.Ring && item.bonusStat.type == BonusStatType.MaxPhysicalDefenseFlat)
            {
                lines.Add("Если надеть второе такое кольцо, оно даст половину этого бонуса.");
            }
        }

        if (item.passiveSkill != null)
        {
            lines.Add($"Пассивный навык «{item.passiveSkill.skillName}»: {SkillDescriptionFormatter.Passive(item.passiveSkill, item.EffectRank)}");
        }


        string handUsage = ItemDescriptionFormatter.HandUsage(item);
        string positiveEffect = ItemDescriptionFormatter.PositiveEffect(item);
        string curse = ItemDescriptionFormatter.Curse(item);
        if (!string.IsNullOrWhiteSpace(handUsage)) lines.Add(handUsage);
        if (!string.IsNullOrWhiteSpace(positiveEffect)) lines.Add($"Эффект: {positiveEffect}");
        if (!string.IsNullOrWhiteSpace(curse)) lines.Add($"Проклятие: {curse}");

        return string.Join("\n", lines);
    }
}

// Тексты используют те же функции баланса, что и бой. Поэтому описание всегда показывает
// значение текущего уровня/ранга, а не общую фразу о росте эффекта.
public static class SkillDescriptionFormatter
{
    const string Accent = "#E6B85C";

    public static string Value(string text) => $"<color={Accent}>{text}</color>";
    public static string Plain(string richText) =>
        (richText ?? string.Empty).Replace($"<color={Accent}>", string.Empty).Replace("</color>", string.Empty);
    static int Level(int level) => Mathf.Clamp(level, 1, 5);
    static string Percent(float value) => Value($"{value:0.#}%");
    static string Number(float value) => Value($"{value:0.#}");

    public static string Passive(PassiveSkillData skill, int level)
    {
        if (skill == null) return string.Empty;
        if (string.Equals(skill.skillName, "Магнум Опус", System.StringComparison.OrdinalIgnoreCase))
            return $"Повышает магический урон на {Percent(10f)}. Передаётся ученице сразу и целиком.";
        return Passive(skill.skillId, level, skill.effectDescription);
    }

    public static string Passive(SkillId skillId, int level, string fallback = "")
    {
        int currentLevel = Level(level);
        switch (skillId)
        {
            case SkillId.FieldRepair:
                return $"На привале восстанавливает {Percent(currentLevel * 10f)} максимальной физической защиты.";
            case SkillId.Freeze:
                int maxCharges = currentLevel * 2;
                string freeze = currentLevel >= 5
                    ? $" На {Value("10-м")} заряде цель замерзает на {Value("5 секунд")}. Физический удар разбивает лёд и наносит дополнительный магический урон, равный урону этого удара."
                    : string.Empty;
                return $"Удар по здоровью добавляет один заряд заморозки на {Value("3 секунды")}. Каждый заряд замедляет атаки на {Percent(5f)}; максимум — {Number(maxCharges)}.{freeze}";
            case SkillId.Luck:
                return $"Повышает шанс успеха в ловушках и событиях на {Percent(SuccessChanceCalculator.GetLuckBonusPercent(currentLevel))}." +
                    (currentLevel >= 5 ? $" Также даёт {Percent(10f)} шанс получить дополнительную награду из сундука." : string.Empty);
            case SkillId.Evasion:
                return $"Повышает шанс уклонения на {Percent(currentLevel * 5f)}. Общий шанс уклонения не может превышать {Percent(BalanceClamps.MaxEvasionChancePercent)}.";
            case SkillId.Sturdy:
                return $"Увеличивает физическую защиту снаряжения на {Percent(currentLevel * 5f)}.";
            case SkillId.CriticalHits:
                return $"Повышает шанс критического удара на {Percent(currentLevel * 10f)}. Критический удар наносит {Percent(150f)} обычного урона; общий шанс не может превышать {Percent(BalanceClamps.MaxCritChancePercent)}.";
            case SkillId.IAmTheWall:
                return $"Добавляет к урону оружия {Percent(currentLevel * 10f)} физической защиты, которую даёт щит.";
            case SkillId.Ambidexterity:
                float dualMultiplier = currentLevel switch { 1 => 90f, 2 => 100f, 3 => 110f, 4 => 120f, _ => 130f };
                return $"При двух оружиях каждое наносит {Percent(dualMultiplier)} своего обычного урона. Не влияет на сочетание оружия со щитом.";
            case SkillId.Thorns:
                return $"Отражает во врага {Percent(BalanceClamps.ThornsReflectPercent(currentLevel))} физического урона, который полностью остановила броня." +
                    (currentLevel >= 5 ? " Также отражает урон от ударов, пробивших броню." : string.Empty);
            case SkillId.Unyielding:
                return $"Пока действует отрицательный эффект, наносимый урон увеличен на {Percent(currentLevel * 5f)}.";
            case SkillId.Bleed:
                string duration = currentLevel >= 5 ? Value("до конца боя") : Value("3 секунды");
                return $"Физический урон по здоровью вызывает кровотечение: {Number(BleedRules.DamagePerSecond(currentLevel))} урона в секунду {duration}. Критический удар сразу наносит оставшийся урон кровотечения." +
                    (currentLevel >= 5 ? " Периодический урон кровотечения также может стать критическим." : string.Empty);
            case SkillId.Vampirism:
                return $"Критический удар восстанавливает здоровье в размере {Percent(ItemEffectBalance.VampirismHealPercentOfCritDamage(currentLevel))} нанесённого урона.";
            case SkillId.ArmorBreak:
                return $"Физический удар по здоровью имеет {Percent(ItemEffectBalance.ArmorBreakExtraWearChancePercent(currentLevel))} шанс дополнительно снизить физическую защиту цели на {Number(1f)}.";
            case SkillId.Piercing:
                return $"Наносит остальным врагам {Percent(ItemEffectBalance.PiercingSplashPercent(currentLevel))} урона по выбранной цели.";
            case SkillId.Repair:
                return $"На привале восстанавливает {Percent(ItemEffectBalance.RepairCampArmorPercent(currentLevel))} максимальной физической защиты.";
            case SkillId.Elusiveness:
                return $"Повышает шанс уклонения на {Percent(ItemEffectBalance.ElusivenessEvasionPercent(currentLevel))}. Суммарный бонус от предметов не может превышать {Percent(BalanceClamps.MaxItemEvasionPercent)}.";
            case SkillId.GoldenTouch:
                return $"Увеличивает количество валюты забега из сундуков на {Percent(ItemEffectBalance.GoldenTouchCurrencyBonusPercent(currentLevel))}.";
            case SkillId.ToughSole:
                return $"Снижает урон от сработавших ловушек на {Percent(ItemEffectBalance.ToughSoleTrapReductionPercent(currentLevel))}.";
            case SkillId.EyeForAnEye:
                return $"Повышает шанс критического удара на {Percent(CombatCriticalRules.EyeForAnEyeBonus(currentLevel))}. Критический удар даёт Скрытность на {Value("3 секунды")}.";
            case SkillId.PoisonedBlade:
                return $"Физический удар по здоровью добавляет один заряд яда на {Value("3 секунды")}; каждый заряд наносит {Number(1f)} урона в секунду. Максимум — {Number(currentLevel)}. В Скрытности удар добавляет два заряда, а максимум удваивается.";
            case SkillId.ByAThread:
                return $"После уклонения повышает скорость атаки на {Percent(currentLevel * 3f)} на {Value("3 секунды")}.";
            case SkillId.Elimination:
                float criticalDamage = currentLevel switch { 1 => 175f, 2 => 180f, 3 => 185f, 4 => 190f, _ => 200f };
                return $"Критический удар наносит {Percent(criticalDamage)} обычного урона.";
            case SkillId.SlipAway:
                return $"Повышает шанс уклонения на {Percent(currentLevel)}. После уклонения даёт Скрытность на {Value("3 секунды")}.";
            case SkillId.Stubbornness:
                return $"При Ярости выше {Percent(RageRules.StubbornnessThreshold(currentLevel))} новые отрицательные эффекты не действуют.";
            case SkillId.Frenzy:
                return $"Повышает скорость атаки на величину, равную {Percent(RageRules.SkillMultiplier(currentLevel) * 100f)} текущей Ярости.";
            case SkillId.CombatRegen:
                return $"После каждых {Number(BalanceClamps.CombatRegenHitsRequired(currentLevel))} полученных ударов восстанавливает {Percent(BalanceClamps.CombatRegenHealPercent)} максимального здоровья. Повторное срабатывание возможно через {Value($"{BalanceClamps.CombatRegenCooldownSeconds:0.#} секунды")}.";
            case SkillId.Intimidation:
                return $"Критический удар на {Value("3 секунды")} снижает скорость атаки цели на величину, равную {Percent(RageRules.SkillMultiplier(currentLevel) * 100f)} текущей Ярости.";
            case SkillId.Superstition:
                return $"Даёт сопротивление магическому урону, равное {Percent(RageRules.SkillMultiplier(currentLevel) * 100f)} текущей Ярости.";
            case SkillId.Shadow:
                float shadow = currentLevel switch { 1 => 10f, 2 => 15f, 3 => 20f, 4 => 25f, _ => 30f };
                return $"Во время Скрытности повышает шанс уклонения на {Percent(shadow)}.";
            case SkillId.ChampionOfTheTribe:
                return $"Шанс критического удара равен {Percent(RageRules.SkillMultiplier(currentLevel) * 100f)} текущей Ярости. Каждый процент шанса критического удара от других источников вместо этого добавляет {Percent(2f)} к урону критического удара.";
            case SkillId.Riposte:
                return $"Первая атака после уклонения наносит на {Percent(ItemEffectBalance.RiposteDamageMultiplier(currentLevel) * 100f)} больше урона.";
            case SkillId.EmbraceOfNight:
                return $"Атака в Скрытности дополнительно наносит магический урон в размере {Percent(ItemEffectBalance.EmbraceOfNightMagicDamagePercent(currentLevel))} урона атаки.";
            case SkillId.Execution:
                return $"Физическая атака дополнительно наносит {Percent(ItemEffectBalance.ExecutionMissingHealthPercent(currentLevel))} от недостающего здоровья цели.";
            case SkillId.GiantSlayer:
                return $"Наносит на {Percent(currentLevel * 5f)} больше урона цели, у которой максимальное здоровье выше, чем у героини.";
            case SkillId.JustAScratch:
                return $"В начале боя восстанавливает {Percent(ItemEffectBalance.JustAScratchHealPercent(currentLevel))} максимального здоровья.";
            default:
                return Formalize(fallback);
        }
    }

    public static string Active(ActiveSkillData skill, int level)
    {
        if (skill == null) return string.Empty;
        return Active(skill.skillId, level, skill.maxLevel, skill.cooldownSeconds, skill.effectDescription);
    }

    public static string Active(SkillId skillId, int level, int maxLevel, float cooldownSeconds, string fallback = "")
    {
        int currentLevel = Mathf.Clamp(level, 1, Mathf.Max(1, maxLevel));
        switch (skillId)
        {
            case SkillId.SmokeBomb:
                return $"Даёт Скрытность на {Value("3 секунды")}. Следующие {Number(currentLevel)} обычные атаки гарантированно становятся критическими. Перезарядка — {Value($"{cooldownSeconds:0.#} секунды")}.";
            case SkillId.Berserk:
                float resistance = currentLevel switch { 1 => 20f, 2 => 30f, _ => 40f };
                return $"Пока навык включён, физическое сопротивление повышено на {Percent(resistance)}, а каждую секунду героиня теряет {Percent(1f)} текущего здоровья, но не менее {Number(1f)}. Навык может привести к гибели.";
            default:
                float multiplier = currentLevel switch { 1 => 110f, 2 => 130f, _ => 150f };
                return $"Наносит выбранной цели {Number(3f)} удара, каждый силой {Percent(multiplier)} обычной атаки. Перезарядка — {Value($"{cooldownSeconds:0.#} секунды")}.";
        }
    }

    public static string Formalize(string text) => string.IsNullOrWhiteSpace(text) ? string.Empty : text
        .Replace("криты", "критические удары")
        .Replace("критом", "критическим ударом")
        .Replace("крита", "критического удара")
        .Replace("Пассивка", "Пассивный навык")
        .Replace("пассивка", "пассивный навык")
        .Replace("стаков", "зарядов")
        .Replace("стаки", "заряды")
        .Replace("стак", "заряд")
        .Replace("HP", "здоровья");
}

public static class ItemDescriptionFormatter
{
    static string P(float value) => SkillDescriptionFormatter.Value($"{value:0.#}%");
    static string N(float value) => SkillDescriptionFormatter.Value($"{value:0.#}");

    public static string HandUsage(ItemData item) => item == null ? string.Empty :
        SkillDescriptionFormatter.Formalize(item.handUsageDescription);

    public static string PositiveEffect(ItemData item)
    {
        if (item == null) return string.Empty;
        int rank = item.EffectRank;
        switch (item.cursedEffect)
        {
            case CursedEffectId.Oathbreaker: return $"Каждый критический удар приносит {N(CursedItemRules.OathbreakerCurrencyPerCrit)} единиц валюты забега.";
            case CursedEffectId.Executioner: return $"Атаки по цели с не более чем {P(25f)} здоровья наносят на {P(100f)} больше урона.";
            case CursedEffectId.BerserkerAxe: return $"Каждое попадание добавляет заряд Безумия, повышающий скорость атаки на {P(CursedItemRules.StackBonusPercent(rank, 1))}; максимум — {N(CursedItemRules.MaxStacks)} зарядов.";
            case CursedEffectId.RecklessCharge: return $"Повышает скорость атаки на {P(CursedItemRules.ChargeAttackSpeedPercent(rank))}.";
            case CursedEffectId.LastArgument: return $"Каждая атака дополнительно наносит урон в размере {P(CursedItemRules.LastArgumentBonusDamage(100f, rank))} максимального здоровья владельца.";
            case CursedEffectId.BetrayerAndAccomplice: return $"Во время Скрытности урон повышен на {P(CursedItemRules.StealthDamageBonusPercent(rank))}.";
            case CursedEffectId.ParanoiaBlades: return $"Каждое уклонение добавляет заряд Паранойи, повышающий скорость атаки на {P(CursedItemRules.StackBonusPercent(rank, 1))}; максимум — {N(CursedItemRules.MaxStacks)} зарядов.";
            case CursedEffectId.ThornAxe: return $"Во время кровотечения скорость атаки повышена на {P(CursedItemRules.ThornAttackSpeedBonusPercent(rank))}.";
        }

        switch (item.prototypeEffect)
        {
            case WeaponPrototypeEffectId.ResonanceScimitar:
                return $"Каждый уникальный положительный эффект повышает урон на {P(item.prototypePrimaryValue)}, максимум — {P(item.prototypePrimaryValue * item.prototypeMaxStacks)}. Каждый уникальный отрицательный эффект повышает скорость атаки на {P(item.prototypeSecondaryValue)}, максимум — {P(item.prototypeSecondaryValue * item.prototypeMaxStacks)}.";
            case WeaponPrototypeEffectId.SpellEater:
                return $"Физическая атака сначала наносит магическому щиту {P(100f)} своего урона. Если эта атака полностью уничтожает щит, каждая снятая единица щита добавляет {N(1f)} к урону оружия до конца боя.";
            case WeaponPrototypeEffectId.LightningSpear:
                return $"Каждая {N(item.prototypeMaxStacks)}-я успешная обычная атака дополнительно наносит магический урон в размере {P(item.prototypePrimaryValue)} урона атаки.";
            case WeaponPrototypeEffectId.Pendulum:
                return $"За каждую полную секунду без атаки урон следующего удара повышается на {P(item.prototypePrimaryValue)}, максимум — {P(item.prototypeSecondaryValue)}.";
            case WeaponPrototypeEffectId.DayAndNight:
                return $"Парные клинки: {P(item.prototypePrimaryValue)} урона наносится как физический, остальные {P(100f - item.prototypePrimaryValue)} — как магический.";
            case WeaponPrototypeEffectId.LastArgumentConversion:
                return $"Положительные бонусы скорости атаки не ускоряют оружие. Каждый {P(1f)} такого бонуса вместо этого повышает урон на {P(item.prototypePrimaryValue)}.";
            default:
                return SkillDescriptionFormatter.Formalize(item.positiveEffectDescription);
        }
    }

    public static string Curse(ItemData item)
    {
        if (item == null) return string.Empty;
        switch (item.cursedEffect)
        {
            case CursedEffectId.Oathbreaker: return "В конце привала героиня получает прямой урон здоровью, равный урону её обычного критического удара; физическая защита не расходуется.";
            case CursedEffectId.Executioner: return $"Атаки по цели с не менее чем {P(75f)} здоровья наносят на {P(25f)} меньше урона.";
            case CursedEffectId.BerserkerAxe: return $"Каждый заряд Безумия увеличивает получаемый урон на {P(CursedItemRules.StackBonusPercent(item.EffectRank, 1))}.";
            case CursedEffectId.RecklessCharge: return $"Каждая атака снижает физическую защиту на {P(3f)}; максимум — {N(CursedItemRules.RecklessMaxStacks)} зарядов. Заряды исчезают после {N(CursedItemRules.RecklessStackDecaySeconds)} секунд без атак.";
            case CursedEffectId.LastArgument: return "Обычное восстановление физической защиты невозможно. Смена нагрудника заполняет новую физическую защиту.";
            case CursedEffectId.BetrayerAndAccomplice: return $"Каждая атака во время Скрытности сокращает её оставшуюся длительность на {N(0.25f)} секунды.";
            case CursedEffectId.ParanoiaBlades: return $"Неуклонённая атака наносит на {P(5f)} больше урона за каждый заряд Паранойи, затем снимает все заряды.";
            case CursedEffectId.ThornAxe: return "Каждый критический удар вызывает у владельца кровотечение уровня, равного рангу предмета.";
            default: return SkillDescriptionFormatter.Formalize(item.curseDescription);
        }
    }
}
