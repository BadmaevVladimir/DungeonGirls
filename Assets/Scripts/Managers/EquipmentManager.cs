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

        // 3.5: бонусные копии персонажа расходуются по циклу из 4 шагов (снаряжение → пассивка → снаряжение → активка),
        // поэтому уровень снаряжения растёт не за каждую лишнюю копию, а за 2 из каждых 4 (см. GachaCopyBonusCalculator).
        int bonus = BuildingCatalog.ForgeStartingEquipmentBonus(forgeLevel) + GachaCopyBonusCalculator.CalculateBonus(copyCount).GearLevelBonus;

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
