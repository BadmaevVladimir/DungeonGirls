using System.Collections.Generic;
using UnityEngine;

// 8.2/3.4: пул предметов, из которого сундук/квест/торговец могут выдать конкретный ItemData
// нужной редкости (раньше сундук выдавал только ItemTier без самого предмета).
[CreateAssetMenu(fileName = "ItemCatalog", menuName = "DungeonGirls/Item Catalog")]
public class ItemCatalogData : ScriptableObject
{
    public ItemData[] items;

    public bool TryGetRandomItem(ItemTier tier, out ItemData result)
    {
        var candidates = new List<ItemData>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item != null && item.tier == tier)
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
