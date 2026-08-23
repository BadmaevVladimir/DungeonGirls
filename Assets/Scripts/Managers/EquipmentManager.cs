using System.Collections.Generic;
using UnityEngine;

public class EquipmentManager : MonoBehaviour
{
    public void EquipItem()
    {
    }

    public void GetAggregatedStats()
    {
    }

    // 3.5/8.1: стартовое снаряжение персонажа с учётом бонуса уровня от Кузницы и копий из гачи.
    // ItemData — общий ScriptableObject-ассет, поэтому бонус применяется к runtime-клонам,
    // а не к самому ассету (иначе бонус утёк бы во все остальные забеги/персонажей).
    public List<ItemData> GetEffectiveStartingEquipment(CharacterData character, int forgeLevel, int copyCount)
    {
        var result = new List<ItemData>();
        if (character == null || character.startingEquipment == null)
        {
            return result;
        }

        // 3.5: +1 уровень снаряжения за каждую копию персонажа сверх первой (1-я копия — базовое владение).
        int bonus = BuildingCatalog.ForgeStartingEquipmentBonus(forgeLevel) + Mathf.Max(0, copyCount - 1);

        foreach (var baseItem in character.startingEquipment)
        {
            if (baseItem == null) continue;
            var clone = Instantiate(baseItem);
            clone.itemLevel += bonus;
            result.Add(clone);
        }

        return result;
    }
}
