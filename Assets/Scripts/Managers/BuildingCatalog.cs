using System.Collections.Generic;

// 8.1: статичные данные 3 зданий деревни — тексты бонусов по уровням и шкала стоимости апгрейда.
// Плейсхолдер-стиль (3.8): весь контент — текст, без арта/ScriptableObject-ассетов.
public static class BuildingCatalog
{
    public const int MaxLevel = 5;

    // [DRAFT, 8.1]: одинаковая шкала для всех 3 зданий, индекс = текущий уровень (0-based).
    static readonly int[] UpgradeCosts = { 100, 200, 400, 800, 1500 };

    public static int UpgradeCost(int currentLevel) =>
        currentLevel >= 0 && currentLevel < UpgradeCosts.Length ? UpgradeCosts[currentLevel] : int.MaxValue;

    public static string DisplayName(BuildingType building)
    {
        switch (building)
        {
            case BuildingType.Forge: return "Кузница";
            case BuildingType.Temple: return "Храм";
            case BuildingType.Tavern: return "Таверна";
            default: return building.ToString();
        }
    }

    static readonly Dictionary<BuildingType, string[]> LevelBonuses = new Dictionary<BuildingType, string[]>
    {
        [BuildingType.Forge] = new[]
        {
            "1: +1 уровень стартового снаряжения персонажа",
            "2: +10 физической защиты (брони)",
            "3: +2 уровня стартового снаряжения (итого +3 суммарно с 1 уровнем)",
            "4: +20 физической защиты (брони)",
            "5: Восстанавливает 50% брони на привале"
        },
        [BuildingType.Temple] = new[]
        {
            "1: +10 магический щит",
            "2: Снижает/убирает дебафф 2-го этажа подземелья",
            "3: +20 магический щит",
            "4: Снижает/убирает дебафф 3-го этажа подземелья",
            "5: 1 попытка перезапуска забега в случае смерти"
        },
        [BuildingType.Tavern] = new[]
        {
            "1: +5 рационов на забег",
            "2: +10% восстановления здоровья на привале",
            "3: Ещё +5 рационов на забег (итого +10 суммарно с 1 уровнем)",
            "4: +20% восстановления здоровья на привале (итого +30% суммарно с 1 уровнем)",
            "5: Случайные бонусы для персонажа после привала (действует 3 комнаты)"
        }
    };

    public static string[] GetLevelBonuses(BuildingType building) => LevelBonuses[building];

    // 3.5/8.1: суммарный бонус к уровню стартового снаряжения от Кузницы (уровни 1 и 3 складываются).
    public static int ForgeStartingEquipmentBonus(int forgeLevel)
    {
        int bonus = 0;
        if (forgeLevel >= 1) bonus += 1;
        if (forgeLevel >= 3) bonus += 2;
        return bonus;
    }

    // 8.1 [ОБНОВЛЕНО]: Таверна ур.1 — флэт бонус урона ко всем атакам оружия персонажа, помимо
    // +5 рационов. Складывается с базовым уроном ДО расчёта диапазона/брони (см.
    // CombatantFactory.AggregateEquipmentStats), независимо от бонуса Кузницы.
    public static float TavernFlatDamageBonus(int tavernLevel) => tavernLevel >= 1 ? 1f : 0f;

    // 8.1 (ФИКС): раньше только текст в LevelBonuses выше и флэт-урон Таверны (ур.1) реально
    // применялись в геймплей — остальные 6 из 8 численных бонусов зданий были только описанием,
    // без эффекта. Ур.2/4 Храма (снижение дебаффов этажей) и ур.5 Храма (перезапуск забега) сюда
    // не входят — первое блокируется отсутствием самой механики дебаффов этажей (см. GDD 2.5,
    // не реализовано), второе требует отдельного потока управления забегом (retry-flow), не просто
    // числового бонуса — оба вне скоупа этого фикса.

    // Кузница ур.2/4: +10/+20 физической защиты (складывается, итого +30 на ур.4+).
    public static float ForgeArmorBonus(int forgeLevel)
    {
        float bonus = 0f;
        if (forgeLevel >= 2) bonus += 10f;
        if (forgeLevel >= 4) bonus += 20f;
        return bonus;
    }

    // Кузница ур.5: восстанавливает 50% брони на привале — складывается с "Полевым ремонтом"/
    // предметной пассивкой "Ремонт" в CampManager.RestAtCamp (тот же клампящийся totalRepairPercent).
    public static float ForgeCampArmorRestorePercent(int forgeLevel) => forgeLevel >= 5 ? 50f : 0f;

    // Храм ур.1/3: +10/+20 магического щита (складывается, итого +30 на ур.3+).
    public static float TempleMagicShieldBonus(int templeLevel)
    {
        float bonus = 0f;
        if (templeLevel >= 1) bonus += 10f;
        if (templeLevel >= 3) bonus += 20f;
        return bonus;
    }

    // Таверна ур.1/3: +5/+10 рационов на забег (складывается с базовыми рационами подземелья,
    // см. CampManager.StartingRations).
    public static int TavernRationsBonus(int tavernLevel)
    {
        int bonus = 0;
        if (tavernLevel >= 1) bonus += 5;
        if (tavernLevel >= 3) bonus += 5;
        return bonus;
    }

    // Таверна ур.2/4: +10/+20% восстановления здоровья на привале (складывается с базовыми 50%,
    // итого +30% на ур.4+) — процентные пункты, см. CampManager.RestAtCamp.
    public static float TavernCampHealBonusPercent(int tavernLevel)
    {
        float bonus = 0f;
        if (tavernLevel >= 2) bonus += 10f;
        if (tavernLevel >= 4) bonus += 20f;
        return bonus;
    }
}
