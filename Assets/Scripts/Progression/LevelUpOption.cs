public enum LevelUpOptionType
{
    NewPassiveSkill,
    UpgradePassiveSkill,
    UpgradeUniquePassive,
    UpgradeUniqueActive
}

// Один из 3 вариантов в окне левел-апа (3.5). UI подключится позже — пока это просто
// структура данных, которую тестовый прогон/будущий UI выбирает программно.
public class LevelUpOption
{
    public LevelUpOptionType Type;
    public PassiveSkillData Skill; // используется для NewPassiveSkill / UpgradePassiveSkill
    public ActiveSkillData ActiveSkill; // используется для UpgradeUniqueActive (Description/UI)
    public int ResultingLevel;

    // 7.2 [НОВОЕ]: текстовое описание эффекта для карточки левел-апа — берётся из уже существующих
    // полей effectDescription (3.9/3.10 — PassiveSkillData; 3.1 — уникальный активный навык персонажа,
    // ActiveSkillData). Уникальный пассивный навык персонажа тоже PassiveSkillData (см. CharacterData),
    // поэтому UpgradeUniquePassive использует то же поле Skill, что и NewPassiveSkill/UpgradePassiveSkill.
    public string Description
    {
        get
        {
            switch (Type)
            {
                case LevelUpOptionType.NewPassiveSkill:
                case LevelUpOptionType.UpgradePassiveSkill:
                case LevelUpOptionType.UpgradeUniquePassive:
                    return Skill != null ? Skill.effectDescription : string.Empty;

                case LevelUpOptionType.UpgradeUniqueActive:
                    return ActiveSkill != null ? ActiveSkill.effectDescription : string.Empty;

                default:
                    return string.Empty;
            }
        }
    }

    public override string ToString()
    {
        switch (Type)
        {
            case LevelUpOptionType.NewPassiveSkill:
                return $"Новый навык: {Skill.skillName} (ур. {ResultingLevel})";
            case LevelUpOptionType.UpgradePassiveSkill:
                return $"Улучшить «{Skill.skillName}» до ур. {ResultingLevel}";
            case LevelUpOptionType.UpgradeUniquePassive:
                return $"Улучшить уникальный пассивный навык до ур. {ResultingLevel}";
            case LevelUpOptionType.UpgradeUniqueActive:
                return $"Улучшить уникальный активный навык до ур. {ResultingLevel}";
            default:
                return "?";
        }
    }
}
