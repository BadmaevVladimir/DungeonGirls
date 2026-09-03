using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Экран Кузницы: исследование чертежей (ForgeBlueprintCatalog) через forgeService — создание
// прототипа НЕ выдаёт оружие сразу, только открывает его в общий loot pool (RewardManager,
// см. GetCompatibleLootItems/SetPrototypeProgression, уже реализовано Codex). Материалы редкие,
// поэтому исследование идёт через подтверждающий попап (Phase 9 ТЗ), в отличие от готовки блюд.
public partial class HubManager
{
    static readonly WeaponSubtype[] ForgeCategories =
    {
        WeaponSubtype.Sword, WeaponSubtype.Axe, WeaponSubtype.Spear,
        WeaponSubtype.Hammer, WeaponSubtype.Blade, WeaponSubtype.TwoHandedAxe
    };

    VisualElement forgeScreen;
    Image forgeBackgroundImage;
    Label forgeLevelLabel;
    Label forgeUpgradeCostLabel;
    Button forgeUpgradeButton;
    Button forgeBackButton;
    VisualElement forgeMaterialBar;
    VisualElement forgeCategoryRow;
    ScrollView forgeWeaponGrid;
    VisualElement forgeDetailsPanel;
    Image forgeDetailsIcon;
    Label forgeDetailsName;
    Label forgeDetailsMeta;
    Label forgeDetailsEffect;
    VisualElement forgeDetailsCostRows;
    Button forgeResearchButton;

    VisualElement forgeConfirmPopup;
    Label forgeConfirmLabel;
    Button forgeConfirmConfirmButton;
    Button forgeConfirmCancelButton;

    VisualElement forgeUnlockPopup;
    Label forgeUnlockLabel;
    Button forgeUnlockCloseButton;

    WeaponSubtype selectedForgeCategory = WeaponSubtype.Sword;
    ForgeBlueprintData selectedBlueprint;
    readonly Dictionary<string, Button> forgeCategoryButtons = new Dictionary<string, Button>();
    readonly Dictionary<string, Button> forgeWeaponCards = new Dictionary<string, Button>();

    void CacheForgeElements(VisualElement root)
    {
        forgeScreen = root.Q<VisualElement>("ForgeScreen");
        forgeBackgroundImage = root.Q<Image>("ForgeBackgroundImage");
        forgeLevelLabel = root.Q<Label>("ForgeScreenLevelLabel");
        forgeUpgradeCostLabel = root.Q<Label>("ForgeScreenUpgradeCostLabel");
        forgeUpgradeButton = root.Q<Button>("ForgeScreenUpgradeButton");
        forgeBackButton = root.Q<Button>("ForgeScreenBackButton");
        forgeMaterialBar = root.Q<VisualElement>("ForgeMaterialBar");
        forgeCategoryRow = root.Q<VisualElement>("ForgeCategoryRow");
        forgeWeaponGrid = root.Q<ScrollView>("ForgeWeaponGrid");
        forgeDetailsPanel = root.Q<VisualElement>("ForgeDetailsPanel");
        forgeDetailsIcon = root.Q<Image>("ForgeDetailsIcon");
        forgeDetailsName = root.Q<Label>("ForgeDetailsName");
        forgeDetailsMeta = root.Q<Label>("ForgeDetailsMeta");
        forgeDetailsEffect = root.Q<Label>("ForgeDetailsEffect");
        forgeDetailsCostRows = root.Q<VisualElement>("ForgeDetailsCostRows");
        forgeResearchButton = root.Q<Button>("ForgeResearchButton");

        forgeConfirmPopup = root.Q<VisualElement>("ForgeConfirmPopup");
        forgeConfirmLabel = root.Q<Label>("ForgeConfirmLabel");
        forgeConfirmConfirmButton = root.Q<Button>("ForgeConfirmConfirmButton");
        forgeConfirmCancelButton = root.Q<Button>("ForgeConfirmCancelButton");

        forgeUnlockPopup = root.Q<VisualElement>("ForgeUnlockPopup");
        forgeUnlockLabel = root.Q<Label>("ForgeUnlockLabel");
        forgeUnlockCloseButton = root.Q<Button>("ForgeUnlockCloseButton");
    }

    void SetUpForgeScreen()
    {
        if (forgeBackgroundImage != null) forgeBackgroundImage.sprite = Resources.Load<Sprite>("UI/ForgeInterior");
        forgeBackButton.clicked += OpenVillage;
        forgeUpgradeButton.clicked += () =>
        {
            if (saveManager.TryUpgradeBuilding(BuildingType.Forge)) RefreshForgeScreen();
        };

        foreach (var category in ForgeCategories)
        {
            var categoryCapture = category;
            var button = new Button(() => SelectForgeCategory(categoryCapture)) { text = ForgeCategoryDisplayName(category) };
            button.AddToClassList("forge-category-tab");
            forgeCategoryRow.Add(button);
            forgeCategoryButtons[category.ToString()] = button;
        }

        forgeResearchButton.clicked += () =>
        {
            if (selectedBlueprint == null) return;
            forgeConfirmLabel.text = $"Выковать прототип «{selectedBlueprint.displayName}»?\n\n" +
                "Материалы будут потрачены.\nПосле этого оружие сможет выпадать в будущих забегах.";
            forgeConfirmPopup.style.display = DisplayStyle.Flex;
        };
        forgeConfirmCancelButton.clicked += () => forgeConfirmPopup.style.display = DisplayStyle.None;
        forgeConfirmConfirmButton.clicked += OnResearchConfirmed;
        forgeUnlockCloseButton.clicked += () => forgeUnlockPopup.style.display = DisplayStyle.None;
    }

    static string ForgeCategoryDisplayName(WeaponSubtype category) => category switch
    {
        WeaponSubtype.Sword => "Мечи",
        WeaponSubtype.Axe => "Топоры",
        WeaponSubtype.Spear => "Копья",
        WeaponSubtype.Hammer => "Молоты",
        WeaponSubtype.Blade => "Клинки",
        WeaponSubtype.TwoHandedAxe => "Двуручные топоры",
        _ => category.ToString()
    };

    public void OpenForge()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        forgeScreen.style.display = DisplayStyle.Flex;
        selectedBlueprint = null;
        RefreshForgeScreen();
        tutorialManager?.QueueOnce(TutorialContent.Buildings);
    }

    void RefreshForgeScreen()
    {
        int level = saveManager.GetBuildingLevel(BuildingType.Forge);
        forgeLevelLabel.text = $"Уровень {level} / {BuildingCatalog.MaxLevel}";
        bool maxed = level >= BuildingCatalog.MaxLevel;
        forgeUpgradeButton.SetEnabled(!maxed && saveManager.Data.metaCurrency >= BuildingCatalog.UpgradeCost(level));
        forgeUpgradeCostLabel.text = maxed ? "Максимальный уровень" : $"Апгрейд: {BuildingCatalog.UpgradeCost(level)} мета-валюты";

        RefreshForgeMaterialBar();
        foreach (var pair in forgeCategoryButtons)
            pair.Value.EnableInClassList("forge-category-tab-active", pair.Key == selectedForgeCategory.ToString());
        RefreshForgeWeaponGrid();

        if (selectedBlueprint != null) RefreshForgeDetails();
        else forgeDetailsPanel.EnableInClassList("hidden", true);
    }

    void RefreshForgeMaterialBar()
    {
        forgeMaterialBar.Clear();
        foreach (string resourceId in PersistentResourceIds.ForgeMaterials)
            forgeMaterialBar.Add(BuildResourceChip(resourceId, forgeService.GetMaterialAmount(resourceId)));
    }

    void SelectForgeCategory(WeaponSubtype category)
    {
        selectedForgeCategory = category;
        RefreshForgeScreen();
    }

    void RefreshForgeWeaponGrid()
    {
        forgeWeaponGrid.Clear();
        forgeWeaponCards.Clear();
        var blueprints = ForgeBlueprintCatalog.All.Where(b => b.weaponCategory == selectedForgeCategory);
        foreach (var blueprint in blueprints)
        {
            var state = forgeService.GetBlueprintState(blueprint);
            var card = new Button(() => SelectBlueprint(blueprint));
            card.AddToClassList("forge-weapon-card");
            card.EnableInClassList("forge-weapon-card-locked", state == ForgeBlueprintState.BlueprintLocked);
            card.EnableInClassList("forge-weapon-card-created", state == ForgeBlueprintState.PrototypeCreated);
            card.EnableInClassList("forge-weapon-card-selected", selectedBlueprint == blueprint);

            var row = new VisualElement();
            row.AddToClassList("recipe-card-row");
            if (blueprint.itemPrototype != null && blueprint.itemPrototype.icon != null)
            {
                // Плейсхолдер-стиль (3.8): переиспользует существующую иконку Epic-архетипа
                // (Sword.png и т.п.) — своей иконки у прототипного оружия по договорённости нет.
                var image = new Image { sprite = blueprint.itemPrototype.icon, scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("recipe-card-icon");
                row.Add(image);
            }
            var text = new Label($"{blueprint.displayName}\nEPIC\n{ForgeStateLabel(state)}");
            text.AddToClassList("recipe-card-text");
            row.Add(text);
            card.Add(row);

            forgeWeaponGrid.Add(card);
            forgeWeaponCards[blueprint.blueprintId] = card;
        }

        if (forgeWeaponGrid.childCount == 0)
        {
            var empty = new Label("В этой категории пока нет чертежей.");
            empty.AddToClassList("body-label");
            forgeWeaponGrid.Add(empty);
        }
    }

    static string ForgeStateLabel(ForgeBlueprintState state) => state switch
    {
        ForgeBlueprintState.AvailableToResearch => "можно исследовать",
        ForgeBlueprintState.BlueprintLocked => "чертёж не открыт",
        ForgeBlueprintState.NotEnoughMaterials => "не хватает материалов",
        ForgeBlueprintState.PrototypeCreated => "прототип создан",
        _ => string.Empty
    };

    void SelectBlueprint(ForgeBlueprintData blueprint)
    {
        selectedBlueprint = blueprint;
        foreach (var pair in forgeWeaponCards)
            pair.Value.EnableInClassList("forge-weapon-card-selected", pair.Key == blueprint.blueprintId);
        RefreshForgeDetails();
    }

    void RefreshForgeDetails()
    {
        forgeDetailsPanel.EnableInClassList("hidden", false);
        var item = selectedBlueprint.itemPrototype;
        forgeDetailsIcon.sprite = item != null ? item.icon : null;
        forgeDetailsIcon.EnableInClassList("hidden", item == null || item.icon == null);
        forgeDetailsName.text = selectedBlueprint.displayName;

        string classRestriction = "любой класс";
        if (item != null && item.allowedClasses != null && item.allowedClasses.Length > 0)
            classRestriction = string.Join(", ", item.allowedClasses.Select(DisplayFormat.CharacterClassDisplayName));
        string baseStats = item != null
            ? $"Урон {item.baseDamage:F0}, скорость атаки {item.attackSpeed:F1}/сек"
            : string.Empty;
        forgeDetailsMeta.text = $"{ForgeCategoryDisplayName(selectedBlueprint.weaponCategory)} · Epic · {classRestriction}" +
            (string.IsNullOrEmpty(baseStats) ? string.Empty : $"\n{baseStats}");

        forgeDetailsEffect.text = (string.IsNullOrWhiteSpace(selectedBlueprint.description)
            ? "Уникальный эффект прототипа."
            : selectedBlueprint.description) +
            "\n\nСоздание прототипа не выдаёт оружие сразу.\nПосле исследования оно навсегда добавляется в пул добычи.";

        forgeDetailsCostRows.Clear();
        foreach (var cost in selectedBlueprint.materialCost)
        {
            int owned = forgeService.GetMaterialAmount(cost.resourceId);
            bool enough = owned >= cost.amount;
            var row = new Label($"{PersistentResourceDisplay.Name(cost.resourceId)}   {owned} / {cost.amount} {(enough ? "✓" : "✕")}");
            row.AddToClassList("resource-cost-row");
            row.AddToClassList(enough ? "resource-cost-ok" : "resource-cost-missing");
            forgeDetailsCostRows.Add(row);
        }

        var state = forgeService.GetBlueprintState(selectedBlueprint);
        forgeResearchButton.text = state == ForgeBlueprintState.PrototypeCreated ? "ПРОТОТИП СОЗДАН" : "ВЫКОВАТЬ ПРОТОТИП";
        forgeResearchButton.SetEnabled(state == ForgeBlueprintState.AvailableToResearch);
    }

    void OnResearchConfirmed()
    {
        forgeConfirmPopup.style.display = DisplayStyle.None;
        if (selectedBlueprint == null || !forgeService.TryResearch(selectedBlueprint)) return;

        forgeUnlockLabel.text = $"НОВЫЙ ПРОТОТИП\n\n{selectedBlueprint.displayName}\n\n" +
            "Теперь это оружие может появляться в добыче.";
        forgeUnlockPopup.style.display = DisplayStyle.Flex;

        RefreshForgeMaterialBar();
        RefreshForgeWeaponGrid();
        RefreshForgeDetails();
    }
}
