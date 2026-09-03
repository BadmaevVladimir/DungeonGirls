using System;
using System.Collections.Generic;

public static class PersistentResourceIds
{
    public const string RawMeat = "raw_meat";
    public const string RootVegetables = "root_vegetables";
    public const string Grain = "grain";
    public const string CaveMushrooms = "cave_mushrooms";
    public const string HealingHerbs = "healing_herbs";
    public const string Dairy = "dairy";
    public const string MonsterEggs = "monster_eggs";
    public const string EtherealSpice = "ethereal_spice";
    public const string TemperedSteel = "tempered_steel";
    public const string MagicCrystal = "magic_crystal";
    public const string MonsterCore = "monster_core";
    public const string AncientShard = "ancient_shard";

    public static readonly string[] Ingredients =
    {
        RawMeat, RootVegetables, Grain, CaveMushrooms, HealingHerbs, Dairy, MonsterEggs, EtherealSpice
    };

    public static readonly string[] ForgeMaterials =
    {
        TemperedSteel, MagicCrystal, MonsterCore, AncientShard
    };
}

public static class PersistentResourceDisplay
{
    public static string Name(string id) => id switch
    {
        PersistentResourceIds.RawMeat => "Сырое мясо",
        PersistentResourceIds.RootVegetables => "Корнеплоды",
        PersistentResourceIds.Grain => "Зерно",
        PersistentResourceIds.CaveMushrooms => "Пещерные грибы",
        PersistentResourceIds.HealingHerbs => "Лечебные травы",
        PersistentResourceIds.Dairy => "Молочные продукты",
        PersistentResourceIds.MonsterEggs => "Яйца монстров",
        PersistentResourceIds.EtherealSpice => "Эфирная приправа",
        PersistentResourceIds.TemperedSteel => "Закалённая сталь",
        PersistentResourceIds.MagicCrystal => "Магический кристалл",
        PersistentResourceIds.MonsterCore => "Ядро монстра",
        PersistentResourceIds.AncientShard => "Древний осколок",
        _ => id
    };
}

[Serializable]
public struct ResourceAmount
{
    public string resourceId;
    public int amount;

    public ResourceAmount(string resourceId, int amount)
    {
        this.resourceId = resourceId;
        this.amount = amount;
    }
}

// Лёгкая транзакционная оболочка над JsonUtility-совместимым списком.
public sealed class ResourceInventory
{
    readonly List<KeyCountEntry> entries;
    readonly Action persist;

    public event Action Changed;

    public ResourceInventory(List<KeyCountEntry> entries, Action persist = null)
    {
        this.entries = entries ?? throw new ArgumentNullException(nameof(entries));
        this.persist = persist;
    }

    public int GetAmount(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) return 0;
        var entry = entries.Find(candidate => candidate != null &&
            string.Equals(candidate.key, resourceId, StringComparison.Ordinal));
        return Math.Max(0, entry?.count ?? 0);
    }

    public bool CanAfford(IReadOnlyList<ResourceAmount> cost)
    {
        if (cost == null) return true;
        var required = Sum(cost);
        foreach (var pair in required)
            if (pair.Value < 0 || GetAmount(pair.Key) < pair.Value) return false;
        return true;
    }

    public bool TrySpend(IReadOnlyList<ResourceAmount> cost)
    {
        if (!CanAfford(cost)) return false;
        var required = Sum(cost);
        foreach (var pair in required)
            FindOrCreate(pair.Key).count -= pair.Value;
        Commit();
        return true;
    }

    public bool Add(string resourceId, int amount)
    {
        if (string.IsNullOrWhiteSpace(resourceId) || amount < 0) return false;
        if (amount == 0) return true;
        var entry = FindOrCreate(resourceId);
        long updated = (long)Math.Max(0, entry.count) + amount;
        if (updated > int.MaxValue) return false;
        entry.count = (int)updated;
        Commit();
        return true;
    }

    internal void Commit()
    {
        persist?.Invoke();
        Changed?.Invoke();
    }

    internal KeyCountEntry FindOrCreate(string id)
    {
        var entry = entries.Find(candidate => candidate != null &&
            string.Equals(candidate.key, id, StringComparison.Ordinal));
        if (entry != null) return entry;
        entry = new KeyCountEntry { key = id, count = 0 };
        entries.Add(entry);
        return entry;
    }

    static Dictionary<string, int> Sum(IReadOnlyList<ResourceAmount> amounts)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (amounts == null) return result;
        for (int i = 0; i < amounts.Count; i++)
        {
            var value = amounts[i];
            if (string.IsNullOrWhiteSpace(value.resourceId)) continue;
            result.TryGetValue(value.resourceId, out int current);
            result[value.resourceId] = current + value.amount;
        }
        return result;
    }
}
