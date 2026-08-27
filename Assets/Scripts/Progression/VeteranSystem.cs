using System;
using System.Collections.Generic;

// Чистая логика прототипа ветеранов/наставников (GDD 1 п.3, 3.7).
public static class VeteranSystem
{
    public static string GradeForFloors(int floorsCleared, int totalFloors = DungeonManager.TotalFloors)
    {
        if (floorsCleared >= totalFloors) return "S+";
        if (floorsCleared >= 9) return "S";
        if (floorsCleared >= 7) return "A";
        if (floorsCleared >= 5) return "B";
        if (floorsCleared >= 3) return "C";
        return "C-";
    }

    // Диапазон включает гарантированный уникальный пассивный навык.
    public static bool TryGetTransferCountRange(int floorsCleared, out int minimum, out int maximum,
        int totalFloors = DungeonManager.TotalFloors)
    {
        minimum = 0;
        maximum = 0;
        if (floorsCleared < 1) return false;
        if (floorsCleared >= totalFloors) { minimum = 2; maximum = 5; return true; }
        if (floorsCleared >= 9) { minimum = 1; maximum = 5; return true; }
        if (floorsCleared >= 7) { minimum = 1; maximum = 4; return true; }
        if (floorsCleared >= 5) { minimum = 1; maximum = 3; return true; }
        if (floorsCleared >= 3) { minimum = 1; maximum = 2; return true; }
        minimum = 1;
        maximum = 1;
        return true;
    }

    public static bool IsEligibleMentor(VeteranCharacter veteran, string studentCharacterId) =>
        veteran != null &&
        veteran.floorsCleared >= 1 &&
        !string.IsNullOrWhiteSpace(veteran.uniquePassiveSkillName) &&
        !string.IsNullOrWhiteSpace(veteran.characterId) &&
        !string.Equals(veteran.characterId, studentCharacterId, StringComparison.OrdinalIgnoreCase);

    // Возвращает имена всех переданных навыков: гарантированный пассив всегда первый,
    // остальные — случайная выборка без повторов из неуникальных пассивных навыков ветерана.
    public static List<string> RollTransferredSkills(VeteranCharacter veteran, Random random)
    {
        var result = new List<string>();
        if (veteran == null || random == null ||
            !TryGetTransferCountRange(veteran.floorsCleared, out int minimum, out int maximum) ||
            string.IsNullOrWhiteSpace(veteran.uniquePassiveSkillName))
        {
            return result;
        }

        result.Add(veteran.uniquePassiveSkillName);
        int requestedTotal = random.Next(minimum, maximum + 1);
        int additionalNeeded = Math.Max(0, requestedTotal - 1);
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { veteran.uniquePassiveSkillName };
        if (veteran.finalSkills != null)
        {
            foreach (var entry in veteran.finalSkills)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.skillName) && seen.Add(entry.skillName))
                {
                    candidates.Add(entry.skillName);
                }
            }
        }

        for (int i = 0; i < additionalNeeded && candidates.Count > 0; i++)
        {
            int index = random.Next(0, candidates.Count);
            result.Add(candidates[index]);
            candidates.RemoveAt(index);
        }

        return result;
    }
}
