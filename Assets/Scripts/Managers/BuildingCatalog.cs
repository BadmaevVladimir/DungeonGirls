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
}
