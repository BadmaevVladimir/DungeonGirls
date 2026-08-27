using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LevelUpManager : MonoBehaviour
{
    // Пулы навыков заполняются извне (позже — из Инспектора/загрузчика контента; сейчас — тестовым прогоном).
    public List<PassiveSkillData> GeneralSkillPool = new List<PassiveSkillData>();
    public List<PassiveSkillData> WarriorSkillPool = new List<PassiveSkillData>();
    // 3.11: классовые пулы новых классов (Плут/Варвар) — см. design note в плане Task 3c.
    public List<PassiveSkillData> RogueSkillPool = new List<PassiveSkillData>();
    public List<PassiveSkillData> BarbarianSkillPool = new List<PassiveSkillData>();
    // 1, п.3 / 3.5: навыки наставника (кроме его основного, не левелящегося пассива) конкурируют
    // за те же 5 слотов, что общие/классовые навыки — см. design note в плане Task 3.
    public List<PassiveSkillData> MentorSkillPool = new List<PassiveSkillData>();

    const int OptionsPerWindow = 3;
    const float UpgradeExistingChance = 0.5f; // 3.5: шанс варианта-апгрейда уже известного навыка (черновое значение)

    List<PassiveSkillData> GetClassPool(CharacterClass characterClass)
    {
        switch (characterClass)
        {
            case CharacterClass.Warrior:
                return WarriorSkillPool;
            case CharacterClass.Rogue:
                return RogueSkillPool;
            case CharacterClass.Barbarian:
                return BarbarianSkillPool;
            default:
                return new List<PassiveSkillData>();
        }
    }

    // 3.5: окно из 3 вариантов на левел-апе. Часть вариантов — новые навыки, часть — апгрейды
    // уже известных (включая уникальные пассивный/активный навыки персонажа, которые конкурируют
    // за те же слоты). UI пока нет — вызывающая сторона получает список и выбирает программно.
    public List<LevelUpOption> GenerateLevelUpOptions(RunCharacterProgress progress)
    {
        var pool = GeneralSkillPool.Concat(GetClassPool(progress.Character.characterClass)).Concat(MentorSkillPool).ToList();

        var newCandidates = new List<LevelUpOption>();
        foreach (var skill in pool)
        {
            // TODO(открытый вопрос ГДД 3.5): что делать, если все 5 слотов заняты, а в пуле
            // остался ещё не изученный навык — заменять, скрывать полностью или что-то ещё, не определено.
            // Временное решение: такой навык просто не предлагается, пока есть свободный слот.
            if (!progress.IsSkillKnown(skill) && progress.HasFreeSkillSlot)
            {
                newCandidates.Add(new LevelUpOption
                {
                    Type = LevelUpOptionType.NewPassiveSkill,
                    Skill = skill,
                    ResultingLevel = 1
                });
            }
        }

        var upgradeCandidates = new List<LevelUpOption>();
        foreach (var pair in progress.KnownSkillLevels)
        {
            if (pair.Value < pair.Key.maxLevel)
            {
                upgradeCandidates.Add(new LevelUpOption
                {
                    Type = LevelUpOptionType.UpgradePassiveSkill,
                    Skill = pair.Key,
                    ResultingLevel = pair.Value + 1
                });
            }
        }

        if (progress.UniquePassiveLevel < progress.Character.uniquePassiveSkill.maxLevel)
        {
            upgradeCandidates.Add(new LevelUpOption
            {
                Type = LevelUpOptionType.UpgradeUniquePassive,
                Skill = progress.Character.uniquePassiveSkill,
                ResultingLevel = progress.UniquePassiveLevel + 1
            });
        }

        if (progress.IsActiveSkillUpgradeAvailable)
        {
            upgradeCandidates.Add(new LevelUpOption
            {
                Type = LevelUpOptionType.UpgradeUniqueActive,
                ActiveSkill = progress.Character.uniqueActiveSkill,
                ResultingLevel = progress.UniqueActiveLevel + 1
            });
        }

        var result = new List<LevelUpOption>();
        for (int i = 0; i < OptionsPerWindow; i++)
        {
            bool wantUpgrade = Random.value < UpgradeExistingChance;
            var primary = wantUpgrade ? upgradeCandidates : newCandidates;
            var fallback = wantUpgrade ? newCandidates : upgradeCandidates;

            var source = primary.Count > 0 ? primary : fallback;
            if (source.Count == 0)
            {
                break; // недостаточно кандидатов — окно короче 3 вариантов
            }

            var chosen = source[Random.Range(0, source.Count)];
            result.Add(chosen);

            // Каждый навык предлагается в окне не больше раза.
            newCandidates.Remove(chosen);
            upgradeCandidates.Remove(chosen);
        }

        return result;
    }

    public void ApplyChoice(RunCharacterProgress progress, LevelUpOption option)
    {
        switch (option.Type)
        {
            case LevelUpOptionType.NewPassiveSkill:
                progress.KnownSkillLevels[option.Skill] = 1;
                Debug.Log($"[LevelUp] Изучен новый навык: {option.Skill.skillName} (ур. 1).");
                break;

            case LevelUpOptionType.UpgradePassiveSkill:
                progress.KnownSkillLevels[option.Skill] = option.ResultingLevel;
                Debug.Log($"[LevelUp] {option.Skill.skillName} повышен до ур. {option.ResultingLevel}.");
                break;

            case LevelUpOptionType.UpgradeUniquePassive:
                progress.UniquePassiveLevel = option.ResultingLevel;
                Debug.Log($"[LevelUp] Уникальный пассивный навык повышен до ур. {option.ResultingLevel}.");
                break;

            case LevelUpOptionType.UpgradeUniqueActive:
                progress.UniqueActiveLevel = option.ResultingLevel;
                progress.NextActiveSkillCheckpoint += 5;
                Debug.Log($"[LevelUp] Уникальный активный навык повышен до ур. {option.ResultingLevel}.");
                break;
        }
    }
}
