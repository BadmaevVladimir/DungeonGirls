using System.Collections.Generic;

// Рантайм-прогресс персонажа внутри одного забега: уровень, опыт, известные навыки.
public class RunCharacterProgress
{
    public const int MaxKnownSkillSlots = 5; // 3.5: максимум 5 разных пассивных навыков одновременно (не считая уникальный)
    public const int MaxCharacterLevel = 15; // 3.6 [ОБНОВЛЕНО 2026-08-25]: потолок уровня поднят 10 -> 15 (расширение до 10 этажей требует более длинной кривой прокачки). Формула опыта до след. уровня (level x 25) НЕ меняется — кривая просто продолжена естественным образом до 15 (сумма на 15 ур. = 2625).

    public CharacterData Character;
    public int Level = 1;
    public int Experience;

    // Общие/классовые навыки (3.9), известные персонажу в этом забеге, и их текущий уровень.
    public Dictionary<PassiveSkillData, int> KnownSkillLevels = new Dictionary<PassiveSkillData, int>();

    public int UniquePassiveLevel = 1;
    public int UniqueActiveLevel = 1;
    public int NextActiveSkillCheckpoint = 5; // 3.5: апгрейд активного навыка становится доступен на уровнях 5, 10, 15...

    public RunCharacterProgress(CharacterData character)
    {
        Character = character;
    }

    public bool IsSkillKnown(PassiveSkillData skill) => KnownSkillLevels.ContainsKey(skill);

    public bool HasFreeSkillSlot => KnownSkillLevels.Count < MaxKnownSkillSlots;

    public bool IsActiveSkillUpgradeAvailable =>
        UniqueActiveLevel < Character.uniqueActiveSkill.maxLevel && Level >= NextActiveSkillCheckpoint;

    public int GetSkillLevel(string skillName)
    {
        foreach (var pair in KnownSkillLevels)
        {
            if (pair.Key.skillName == skillName)
            {
                return pair.Value;
            }
        }

        return 0;
    }

    // 3.6: опыт до следующего уровня = текущий уровень x 25.
    public static int ExperienceRequiredForLevel(int level) => level * 25;

    // Начисляет опыт и накручивает столько уровней, сколько накопленного опыта хватает
    // (с учётом потолка в 10 уровней для прототипа). Возвращает список новых достигнутых уровней.
    public List<int> AddExperience(int amount)
    {
        var levelsGained = new List<int>();
        Experience += amount;

        while (Level < MaxCharacterLevel && Experience >= ExperienceRequiredForLevel(Level))
        {
            Experience -= ExperienceRequiredForLevel(Level);
            Level++;
            levelsGained.Add(Level);
        }

        return levelsGained;
    }
}
