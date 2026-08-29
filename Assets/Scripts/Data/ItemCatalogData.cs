using System.Collections.Generic;
using UnityEngine;

// 8.2/3.4: пул предметов, из которого сундук/квест/торговец могут выдать конкретный ItemData
// нужной редкости (раньше сундук выдавал только ItemTier без самого предмета).
[CreateAssetMenu(fileName = "ItemCatalog", menuName = "DungeonGirls/Item Catalog")]
public class ItemCatalogData : ScriptableObject
{
    public ItemData[] items;

    public static bool IsAllowedForClass(ItemData item, CharacterClass? characterClass)
    {
        if (item == null)
        {
            return false;
        }

        return !characterClass.HasValue || item.allowedClasses == null || item.allowedClasses.Length == 0 ||
            System.Array.IndexOf(item.allowedClasses, characterClass.Value) >= 0;
    }

    // Единый совместимый пул используется не только при выборе настоящей награды, но и UI-рулеткой.
    // Так визуальная лента не обещает предметы, которые выбранный персонаж не сможет получить.
    public List<ItemData> GetCompatibleItems(CharacterClass? characterClass)
    {
        var compatible = new List<ItemData>();
        if (items == null)
        {
            return compatible;
        }

        foreach (var item in items)
        {
            if (IsAllowedForClass(item, characterClass))
            {
                compatible.Add(item);
            }
        }

        return compatible;
    }

    public bool TryGetItem(string itemName, ItemTier tier, WeaponSubtype weaponSubtype,
        CharacterClass? characterClass, out ItemData result)
    {
        if (items != null)
        {
            foreach (var item in items)
            {
                if (IsAllowedForClass(item, characterClass) && item.tier == tier &&
                    item.weaponSubtype == weaponSubtype &&
                    string.Equals(item.itemName, itemName, System.StringComparison.OrdinalIgnoreCase))
                {
                    result = item;
                    return true;
                }
            }
        }

        result = null;
        return false;
    }

    public bool TryGetRandomItem(ItemTier tier, out ItemData result)
    {
        return TryGetRandomItem(tier, null, out result);
    }

    // 3.1/8.2/5.2: совместимость с классом фильтруется ДО розыгрыша конкретного предмета.
    // Пустой allowedClasses означает универсальный предмет. nullable нужен только для старых
    // вызовов/инструментов редактора, где персонажа ещё нет в контексте.
    public bool TryGetRandomItem(ItemTier tier, CharacterClass? characterClass, out ItemData result)
    {
        var candidates = new List<ItemData>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (item.tier == tier && IsAllowedForClass(item, characterClass))
                {
                    candidates.Add(item);
                }
            }
        }

        if (candidates.Count == 0)
        {
            result = null;
            return false;
        }

        result = candidates[Random.Range(0, candidates.Count)];
        return true;
    }
}
