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
            // Заголовок карточки: игроку важно, что это за навык и на какой уровень он выйдет.
            // Для уже взятых показываем переход «было → станет», иначе непонятно, что даёт выбор.
            case LevelUpOptionType.NewPassiveSkill:
                return $"{Skill.skillName} — новый навык";
            case LevelUpOptionType.UpgradePassiveSkill:
                return $"{Skill.skillName}: уровень {ResultingLevel - 1} → {ResultingLevel}";
            case LevelUpOptionType.UpgradeUniquePassive:
                return $"{(Skill != null ? Skill.skillName : "Уникальный навык")}: уровень {ResultingLevel - 1} → {ResultingLevel}";
            case LevelUpOptionType.UpgradeUniqueActive:
                return $"{(ActiveSkill != null ? ActiveSkill.skillName : "Уникальный приём")}: уровень {ResultingLevel - 1} → {ResultingLevel}";
            default:
                return "?";
        }
    }
}
