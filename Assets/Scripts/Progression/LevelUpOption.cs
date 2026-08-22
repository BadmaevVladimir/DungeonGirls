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
    public int ResultingLevel;

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
