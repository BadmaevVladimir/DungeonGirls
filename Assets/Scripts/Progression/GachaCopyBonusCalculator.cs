// Assets/Scripts/Progression/GachaCopyBonusCalculator.cs
using UnityEngine;

// 3.5 [ОБНОВЛЕНО 2026-08-25]: заменяет старую формулу "+1 снаряжение за каждую копию сверх
// первой". Каждая лишняя копия даёт один шаг в цикле из 4: снаряжение -> пассивка -> снаряжение
// -> активка -> повтор. Копии считаются от 2-й общей копии (1-я копия = базовое владение, без
// бонуса) — т.е. i-й ЛИШНЕЙ копии (i = 1, 2, 3...) соответствует шаг (i-1) % 4.
public static class GachaCopyBonusCalculator
{
    public struct GachaBonus
    {
        public int GearLevelBonus;
        public int PassiveLevelBonus;
        public int ActiveLevelBonus;
    }

    // maxPassiveLevelBonus/maxActiveLevelBonus — потолки УРОВНЯ (не бонуса): пассивный навык
    // персонажа стартует с 1 ур. и максимум 5 (3.1) -> бонус клампится на 4; активный стартует
    // с 1 ур. и максимум 3 (3.1) -> бонус клампится на 2. Снаряжение без потолка (3.10).
    public static GachaBonus CalculateBonus(int copyCount)
    {
        int extraCopies = Mathf.Max(0, copyCount - 1);
        var bonus = new GachaBonus();

        for (int i = 0; i < extraCopies; i++)
        {
            switch (i % 4)
            {
                case 0: bonus.GearLevelBonus++; break;
                case 1: bonus.PassiveLevelBonus++; break;
                case 2: bonus.GearLevelBonus++; break;
                case 3: bonus.ActiveLevelBonus++; break;
            }
        }

        bonus.PassiveLevelBonus = Mathf.Min(bonus.PassiveLevelBonus, 4);
        bonus.ActiveLevelBonus = Mathf.Min(bonus.ActiveLevelBonus, 2);

        return bonus;
    }
}
