using UnityEngine;

// 3.10/2.7: общая формула "минимум +1 к основному стату за уровень" — используется предметами
// (3.10, ItemData) и монстрами (2.7, CombatantFactory), чтобы оба места считали одинаково.
// ФинальныйСтат = БазовыйСтат + МАКС(1, ОКРУГЛ(БазовыйСтат × 0.1)) × (Уровень − 1).
public static class StatScaling
{
    public static float ScaleItemEffect(float baseValue, int itemRank)
    {
        return baseValue * Mathf.Clamp(itemRank, 1, 5);
    }

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

    public static float ApplyTierAndLevel(float baseStat, float tierMultiplier, int level)
    {
        if (baseStat <= 0f) return baseStat;
        float increment = Mathf.Max(1f, Mathf.Round(baseStat * 0.1f));
        return baseStat * tierMultiplier + increment * (Mathf.Max(1, level) - 1);
    }
}

// Единый баланс вторичных эффектов предметов. Ранг всегда 1–5 и хранится в ItemData.itemRank,
// поэтому значения остаются стабильными и не растут бесконечно вместе с уровнем лута.
public static class ItemEffectBalance
{
    static int Rank(int value) => Mathf.Clamp(value, 0, 5);
    static float Value(int rank, float perRank) => Rank(rank) * perRank;

    public static float ToughSoleTrapReductionPercent(int rank) => rank <= 0 ? 0f : 5f + Value(rank, 5f);
    public static float GoldenTouchCurrencyBonusPercent(int rank) => rank <= 0 ? 0f : 5f + Value(rank, 5f);
    public static float RepairCampArmorPercent(int rank) => Value(rank, 5f);
    public static float ElusivenessEvasionPercent(int rank) => Value(rank, 4f);
    public static float PiercingSplashPercent(int rank) => Value(rank, 6f);
    public static float EmbraceOfNightMagicDamagePercent(int rank) => Value(rank, 8f);
    public static float VampirismHealPercentOfCritDamage(int rank) => Value(rank, 8f);
    public static float ExecutionMissingHealthPercent(int rank) => Value(rank, 3f);
    public static float RiposteDamageMultiplier(int rank) => Value(rank, 0.25f);
    public static float JustAScratchHealPercent(int rank) => Value(rank, 3f);
    public static float ArmorBreakExtraWearChancePercent(int rank) => Value(rank, 20f);

    // Броня от универсальных украшений намеренно растёт медленнее старой линейной формулы.
    // baseValue в данных теперь означает шаг: кольцо 2 -> 4/6/8/10/12, амулет 3 -> 6/9/12/15/18.
    public static float ArmorAccessoryMaxDefense(float baseValue, int itemRank) =>
        baseValue * (Mathf.Clamp(itemRank, 1, 5) + 1);

    public const float SecondArmorRingMultiplier = 0.5f;
}
