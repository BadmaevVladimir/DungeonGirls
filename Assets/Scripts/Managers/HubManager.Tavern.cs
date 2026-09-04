using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Экран Таверны (8.1/8.2 доработка Codex): готовка блюд по рецептам FoodRecipeCatalog через
// tavernService (создан один раз в HubManager.Start()). UI не хранит собственное состояние
// ресурсов/рецептов — Refresh() каждый раз перечитывает его из tavernService/saveManager и
// перестраивает видимые элементы, backend (TavernService.TryCook) остаётся source of truth.
public partial class HubManager
{
    VisualElement tavernScreen;
    Image tavernBackgroundImage;
    Label tavernLevelLabel;
    Label tavernUpgradeCostLabel;
    Button tavernUpgradeButton;
    Button tavernBackButton;
    VisualElement tavernIngredientBar;
    ScrollView tavernRecipeScrollView;
    VisualElement tavernDetailsPanel;
    Image tavernDetailsIcon;
    Label tavernDetailsName;
    Label tavernDetailsEffect;
    VisualElement tavernDetailsCostRows;
    Label tavernDetailsPreparedLabel;
    Button tavernCookButton;
    Label tavernFeedbackLabel;

    FoodRecipeData selectedRecipe;
    readonly Dictionary<string, Button> tavernRecipeCards = new Dictionary<string, Button>();

    void CacheTavernElements(VisualElement root)
    {
        tavernScreen = root.Q<VisualElement>("TavernScreen");
        tavernBackgroundImage = root.Q<Image>("TavernBackgroundImage");
        tavernLevelLabel = root.Q<Label>("TavernScreenLevelLabel");
        tavernUpgradeCostLabel = root.Q<Label>("TavernScreenUpgradeCostLabel");
        tavernUpgradeButton = root.Q<Button>("TavernScreenUpgradeButton");
        tavernBackButton = root.Q<Button>("TavernScreenBackButton");
        tavernIngredientBar = root.Q<VisualElement>("TavernIngredientBar");
        tavernRecipeScrollView = root.Q<ScrollView>("TavernRecipeScrollView");
        tavernDetailsPanel = root.Q<VisualElement>("TavernDetailsPanel");
        tavernDetailsIcon = root.Q<Image>("TavernDetailsIcon");
        tavernDetailsName = root.Q<Label>("TavernDetailsName");
        tavernDetailsEffect = root.Q<Label>("TavernDetailsEffect");
        tavernDetailsCostRows = root.Q<VisualElement>("TavernDetailsCostRows");
        tavernDetailsPreparedLabel = root.Q<Label>("TavernDetailsPreparedLabel");
        tavernCookButton = root.Q<Button>("TavernCookButton");
        tavernFeedbackLabel = root.Q<Label>("TavernFeedbackLabel");
    }

    void SetUpTavernScreen()
    {
        if (tavernBackgroundImage != null) tavernBackgroundImage.sprite = Resources.Load<Sprite>("UI/TavernInterior");
        tavernBackButton.clicked += OpenVillage;
        tavernUpgradeButton.clicked += () =>
        {
            if (saveManager.TryUpgradeBuilding(BuildingType.Tavern)) RefreshTavernScreen();
        };
        tavernCookButton.clicked += OnCookClicked;
    }

    // Общий для Tavern/Forge helper (partial class) — иконка (может быть null, плейсхолдер-стиль
    // 3.8) + подпись "Название: количество" в один ряд, тот же .resource-chip язык что и раньше.
    static VisualElement BuildResourceChip(string resourceId, int amount)
    {
        var chip = new VisualElement();
        chip.AddToClassList("resource-chip");
        var icon = PersistentResourceDisplay.Icon(resourceId);
        if (icon != null)
        {
            var image = new Image { sprite = icon, scaleMode = ScaleMode.ScaleToFit };
            image.AddToClassList("resource-chip-icon");
            chip.Add(image);
        }
        var label = new Label($"{PersistentResourceDisplay.Name(resourceId)}: {amount}");
        label.AddToClassList("resource-chip-label");
        chip.Add(label);
        return chip;
    }

    public void OpenTavern()
    {
        mainMenuScreen.style.display = DisplayStyle.None;
        tavernScreen.style.display = DisplayStyle.Flex;
        selectedRecipe = null;
        RefreshTavernScreen();
        tutorialManager?.QueueOnce(TutorialContent.Buildings);
    }

    void RefreshTavernScreen()
    {
        int level = saveManager.GetBuildingLevel(BuildingType.Tavern);
        tavernLevelLabel.text = $"Уровень {level} / {BuildingCatalog.MaxLevel}";
        bool maxed = level >= BuildingCatalog.MaxLevel;
        tavernUpgradeButton.SetEnabled(!maxed && saveManager.Data.metaCurrency >= BuildingCatalog.UpgradeCost(level));
        tavernUpgradeCostLabel.text = maxed ? "Максимальный уровень" : $"Апгрейд: {BuildingCatalog.UpgradeCost(level)} мета-валюты";

        RefreshTavernIngredientBar();
        RefreshTavernRecipeList(level);

        // Выбранный рецепт мог перестать быть доступным (потратили ингредиенты на другой) —
        // деталка всё равно остаётся открытой на нём, но кнопка готовки/подписи обновляются.
        if (selectedRecipe != null) RefreshTavernDetails(level);
        else tavernDetailsPanel.EnableInClassList("hidden", true);
    }

    void RefreshTavernIngredientBar()
    {
        tavernIngredientBar.Clear();
        foreach (string resourceId in PersistentResourceIds.Ingredients)
            tavernIngredientBar.Add(BuildResourceChip(resourceId, tavernService.GetIngredientAmount(resourceId)));
    }

    void RefreshTavernRecipeList(int tavernLevel)
    {
        tavernRecipeScrollView.Clear();
        tavernRecipeCards.Clear();
        // Resources.LoadAll does not guarantee a useful order. Keep recipes a player can use now
        // at the top, followed by recipes they can afford after finding ingredients, and only then
        // higher-level recipes. Otherwise a level-1 dish can be buried below level-4/5 cards and
        // make the whole feature look locked (especially on a fresh, level-0 tavern).
        var recipes = new List<FoodRecipeData>(FoodRecipeCatalog.All);
        recipes.Sort((left, right) =>
        {
            int stateOrder = RecipeStateSortOrder(tavernService.GetRecipeState(left, tavernLevel))
                .CompareTo(RecipeStateSortOrder(tavernService.GetRecipeState(right, tavernLevel)));
            if (stateOrder != 0) return stateOrder;
            int levelOrder = left.requiredTavernLevel.CompareTo(right.requiredTavernLevel);
            return levelOrder != 0 ? levelOrder : string.Compare(left.displayName, right.displayName,
                StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var recipe in recipes)
        {
            var state = tavernService.GetRecipeState(recipe, tavernLevel);
            int prepared = tavernService.GetPreparedCount(recipe.resultFoodId);
            var card = new Button(() => SelectRecipe(recipe, tavernLevel));
            card.AddToClassList("recipe-card");
            card.EnableInClassList("recipe-card-locked", state != TavernRecipeState.AvailableToCook);
            card.EnableInClassList("recipe-card-selected", selectedRecipe == recipe);

            var row = new VisualElement();
            row.AddToClassList("recipe-card-row");
            if (recipe.icon != null)
            {
                var image = new Image { sprite = recipe.icon, scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("recipe-card-icon");
                row.Add(image);
            }
            var text = new Label($"{recipe.displayName}\nУр. таверны {recipe.requiredTavernLevel} — {RecipeStateLabel(state)}" +
                (prepared > 0 ? $"\nПриготовлено: {prepared}" : string.Empty));
            text.AddToClassList("recipe-card-text");
            row.Add(text);
            card.Add(row);

            tavernRecipeScrollView.Add(card);
            tavernRecipeCards[recipe.recipeId] = card;
        }
    }

    static string RecipeStateLabel(TavernRecipeState state) => state switch
    {
        TavernRecipeState.AvailableToCook => "можно готовить",
        TavernRecipeState.LockedByTavernLevel => "нужен более высокий уровень таверны",
        TavernRecipeState.LockedRecipe => "рецепт не открыт",
        TavernRecipeState.NotEnoughIngredients => "не хватает ингредиентов",
        _ => string.Empty
    };

    static int RecipeStateSortOrder(TavernRecipeState state) => state switch
    {
        TavernRecipeState.AvailableToCook => 0,
        TavernRecipeState.NotEnoughIngredients => 1,
        TavernRecipeState.LockedRecipe => 2,
        TavernRecipeState.LockedByTavernLevel => 3,
        _ => 4
    };

    void SelectRecipe(FoodRecipeData recipe, int tavernLevel)
    {
        selectedRecipe = recipe;
        foreach (var pair in tavernRecipeCards)
            pair.Value.EnableInClassList("recipe-card-selected", pair.Key == recipe.recipeId);
        RefreshTavernDetails(tavernLevel);
    }

    void RefreshTavernDetails(int tavernLevel, string feedbackOverride = null)
    {
        tavernDetailsPanel.EnableInClassList("hidden", false);
        tavernDetailsIcon.sprite = selectedRecipe.icon;
        tavernDetailsIcon.EnableInClassList("hidden", selectedRecipe.icon == null);
        tavernDetailsName.text = selectedRecipe.displayName;
        tavernDetailsEffect.text = string.IsNullOrWhiteSpace(selectedRecipe.description)
            ? "Эффект на 3 комнаты после привала."
            : selectedRecipe.description;
        tavernDetailsPreparedLabel.text = $"Приготовлено порций: {tavernService.GetPreparedCount(selectedRecipe.resultFoodId)}";

        tavernDetailsCostRows.Clear();
        foreach (var cost in selectedRecipe.ingredientCosts)
        {
            int owned = tavernService.GetIngredientAmount(cost.resourceId);
            bool enough = owned >= cost.amount;
            var row = new Label($"{PersistentResourceDisplay.Name(cost.resourceId)}   {owned} / {cost.amount} {(enough ? "✓" : "✕")}");
            row.AddToClassList("resource-cost-row");
            row.AddToClassList(enough ? "resource-cost-ok" : "resource-cost-missing");
            tavernDetailsCostRows.Add(row);
        }

        var state = tavernService.GetRecipeState(selectedRecipe, tavernLevel);
        tavernCookButton.SetEnabled(state == TavernRecipeState.AvailableToCook);
        tavernCookButton.text = state switch
        {
            TavernRecipeState.LockedByTavernLevel => $"НУЖЕН УРОВЕНЬ {selectedRecipe.requiredTavernLevel}",
            TavernRecipeState.LockedRecipe => "РЕЦЕПТ НЕ ОТКРЫТ",
            TavernRecipeState.NotEnoughIngredients => "НЕ ХВАТАЕТ ИНГРЕДИЕНТОВ",
            _ => "ПРИГОТОВИТЬ"
        };

        if (!string.IsNullOrWhiteSpace(feedbackOverride))
        {
            SetTavernFeedback(feedbackOverride, false);
            return;
        }

        string blockedReason = state switch
        {
            TavernRecipeState.LockedByTavernLevel =>
                $"Сначала улучшите таверну до уровня {selectedRecipe.requiredTavernLevel} (сейчас {tavernLevel}).",
            TavernRecipeState.LockedRecipe => "Этот рецепт ещё не открыт.",
            TavernRecipeState.NotEnoughIngredients => "Соберите недостающие ингредиенты.",
            _ => null
        };
        SetTavernFeedback(blockedReason, true);
    }

    void SetTavernFeedback(string message, bool isError)
    {
        bool visible = !string.IsNullOrWhiteSpace(message);
        tavernFeedbackLabel.text = message ?? string.Empty;
        tavernFeedbackLabel.EnableInClassList("craft-feedback-error", isError && visible);
        tavernFeedbackLabel.EnableInClassList("hidden", !visible);
    }

    void OnCookClicked()
    {
        if (selectedRecipe == null) return;
        int level = saveManager.GetBuildingLevel(BuildingType.Tavern);
        if (!tavernService.TryCook(selectedRecipe, level))
        {
            RefreshTavernDetails(level);
            return;
        }

        RefreshTavernIngredientBar();
        RefreshTavernRecipeList(level);
        // RefreshTavernDetails used to hide the success label immediately after it was shown,
        // making a successful click look like it did nothing. Pass the result through the final
        // refresh so the confirmation remains visible with the updated counts.
        RefreshTavernDetails(level, "Приготовлено!");
    }
}
