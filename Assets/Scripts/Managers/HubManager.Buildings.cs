using UnityEngine.UIElements;

public partial class HubManager
{
    // ==================== Здания (8.1) ====================

    void RefreshBuildingsScreen()
    {
        metaCurrencyLabel.text = $"Мета-валюта: {saveManager.Data.metaCurrency}";

        for (int i = 0; i < BuildingOrder.Length; i++)
        {
            var building = BuildingOrder[i];
            int level = saveManager.GetBuildingLevel(building);

            buildingLevelLabels[i].text = $"{BuildingCatalog.DisplayName(building)} — уровень {level}/{BuildingCatalog.MaxLevel}";
            buildingBonusLabels[i].text = string.Join("\n", BuildingCatalog.GetLevelBonuses(building));

            if (level >= BuildingCatalog.MaxLevel)
            {
                buildingCostLabels[i].text = "Максимальный уровень";
                buildingUpgradeButtons[i].SetEnabled(false);
            }
            else
            {
                int cost = BuildingCatalog.UpgradeCost(level);
                buildingCostLabels[i].text = $"Следующий уровень: {cost} мета-валюты";
                buildingUpgradeButtons[i].SetEnabled(saveManager.Data.metaCurrency >= cost);
            }
        }
    }

    void TryUpgradeBuilding(BuildingType building)
    {
        if (saveManager.TryUpgradeBuilding(building))
        {
            RefreshBuildingsScreen();
            RefreshVillagePlates(); // уровень на плашке здания на карте деревни
        }
    }

}
