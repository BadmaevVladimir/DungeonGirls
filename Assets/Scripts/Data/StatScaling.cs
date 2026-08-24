using UnityEngine;

// 3.10/2.7: общая формула "минимум +1 к основному стату за уровень" — используется предметами
// (3.10, ItemData) и монстрами (2.7, CombatantFactory), чтобы оба места считали одинаково.
// ФинальныйСтат = БазовыйСтат + МАКС(1, ОКРУГЛ(БазовыйСтат × 0.1)) × (Уровень − 1).
public static class StatScaling
{
    public static float ApplyLevelBonus(float baseStat, int level)
    {
        // baseStat <= 0 значит "у этой сущности нет такого стата" — масштабировать нечего,
        // иначе формула ошибочно родила бы стат из ничего.
        if (baseStat <= 0f)
        {
            return baseStat;
        }

        float increment = Mathf.Max(1f, Mathf.Round(baseStat * 0.1f));
        return baseStat + increment * (level - 1);
    }
}
