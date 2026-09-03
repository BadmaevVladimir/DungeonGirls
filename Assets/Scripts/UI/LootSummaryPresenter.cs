using UnityEngine.UIElements;

public static class LootSummaryPresenter
{
    public static void Populate(VisualElement rows, RoomRewardResult reward)
    {
        rows.Clear();
        AddRow(rows, $"Валюта забега: +{reward.Currency}", "loot-summary-currency");
        for (int i = 0; i < reward.Ingredients.Count; i++)
        {
            var stack = reward.Ingredients[i];
            if (string.IsNullOrWhiteSpace(stack.resourceId) || stack.amount <= 0) continue;
            AddRow(rows, $"{PersistentResourceDisplay.Name(stack.resourceId)}: +{stack.amount}", "loot-summary-ingredient");
        }
        for (int i = 0; i < reward.ForgeMaterials.Count; i++)
        {
            var stack = reward.ForgeMaterials[i];
            if (string.IsNullOrWhiteSpace(stack.resourceId) || stack.amount <= 0) continue;
            AddRow(rows, $"{PersistentResourceDisplay.Name(stack.resourceId)}: +{stack.amount}", "loot-summary-material");
        }
        if (reward.HasChest) AddRow(rows, "Сундук со снаряжением", "loot-summary-chest");
    }

    static void AddRow(VisualElement rows, string text, string className)
    {
        var label = new Label(text);
        label.AddToClassList("loot-summary-row");
        label.AddToClassList(className);
        rows.Add(label);
    }
}
