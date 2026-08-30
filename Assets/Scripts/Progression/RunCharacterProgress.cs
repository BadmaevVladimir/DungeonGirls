using System.Collections.Generic;
using UnityEngine;

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
    public int LevelUpRerollsRemaining { get; private set; }
    int lastAutoActiveUpgradeLevel;

    // 1, п.3: постоянный бонус к магическому урону от основного пассивного навыка наставника
    // ("Магнум Опус"). Не левелится — прикладной прямой процент, см. design note в плане Task 3.
    public float MentorMagicDamageBonusPercent;

    // Реальный наставник: уникальный пассив передаётся на 1 уровне и не прокачивается.
    public string MentorUniquePassiveSkillName;
    public int MentorUniquePassiveLevel;

    public RunCharacterProgress(CharacterData character)
    {
        Character = character;
    }

    public bool IsSkillKnown(PassiveSkillData skill) => KnownSkillLevels.ContainsKey(skill);

    public bool HasFreeSkillSlot => KnownSkillLevels.Count < MaxKnownSkillSlots;

    public void SetLevelUpRerolls(int amount)
    {
        LevelUpRerollsRemaining = Mathf.Max(0, amount);
    }

    public bool TrySpendLevelUpReroll()
    {
        if (LevelUpRerollsRemaining <= 0)
        {
            return false;
        }

        LevelUpRerollsRemaining--;
        return true;
    }

    public bool TryAutoUpgradeUniqueActiveAtLevel(int reachedLevel)
    {
        if (Character == null || Character.uniqueActiveSkill == null || reachedLevel <= lastAutoActiveUpgradeLevel || reachedLevel < 5 || reachedLevel % 5 != 0 ||
            UniqueActiveLevel >= Character.uniqueActiveSkill.maxLevel)
        {
            return false;
        }

        UniqueActiveLevel++;
        lastAutoActiveUpgradeLevel = reachedLevel;
        return true;
    }

    public int GetSkillLevel(SkillId skillId)
    {
        foreach (var pair in KnownSkillLevels)
        {
            if (pair.Key.skillId == skillId)
            {
                return pair.Value;
            }
        }

        return 0;
    }

    // MentorUniquePassiveSkillName остаётся строкой (персистентный формат SaveData/VeteranCharacter,
    // не меняем) — сравнивается через SkillEffectMap.ResolveId, а не напрямую по строке.
    public int GetEffectiveUniquePassiveLevel(SkillId skillId)
    {
        int ownLevel = Character != null && Character.uniquePassiveSkill != null &&
            Character.uniquePassiveSkill.skillId == skillId
            ? UniquePassiveLevel
            : 0;
        int mentorLevel = SkillEffectMap.ResolveId(MentorUniquePassiveSkillName) == skillId
            ? MentorUniquePassiveLevel
            : 0;
        return Mathf.Max(ownLevel, mentorLevel);
    }

    public int GetMentorUniquePassiveLevel(SkillId skillId) =>
        SkillEffectMap.ResolveId(MentorUniquePassiveSkillName) == skillId
            ? MentorUniquePassiveLevel
            : 0;

    // 3.5: применяет бонус стартового уровня уникальных пассивного/активного навыков от лишних копий
    // гачи (см. GachaCopyBonusCalculator). Клампится потолком уровня навыка (5 пассивный / 3 активный,
    // см. 3.1) — CalculateBonus уже клампит сам бонус, но Min() здесь на итоговом уровне на случай,
    // если maxLevel ассета персонажа когда-то станет отличаться от 5/3 (защита от рассинхрона данных).
    public void ApplyGachaStartingBonus(GachaCopyBonusCalculator.GachaBonus bonus)
    {
        int passiveMaxLevel = Character.uniquePassiveSkill != null ? Character.uniquePassiveSkill.maxLevel : 1;
        int activeMaxLevel = Character.uniqueActiveSkill != null ? Character.uniqueActiveSkill.maxLevel : 1;
        UniquePassiveLevel = Mathf.Min(passiveMaxLevel, 1 + bonus.PassiveLevelBonus);
        UniqueActiveLevel = Mathf.Min(activeMaxLevel, 1 + bonus.ActiveLevelBonus);
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
