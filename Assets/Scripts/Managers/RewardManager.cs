using System.Collections.Generic;
using UnityEngine;

public enum ExperienceSource
{
    CombatRoom,
    SuccessfulEventOrTrap,
    Boss
}

// Итог открытия сундука (8.2). Сама рулетка/анимация — задача фазы с UI, здесь только результат.
public class ChestReward
{
    public int Currency;
    public ItemTier ItemRarity;
    public ItemData Item; // 3.4: конкретный предмет для сравнения/эквипа; null, если каталог пуст
    public bool BonusReward; // 3.9 "Удача" ур.5: доп. шанс на дополнительную награду
}

// 8.5: награды за завершённый забег (победа/поражение), отдельно от валюты забега из сундуков.
public struct RunCompletionReward
{
    public int MetaCurrency;
    public int GachaCurrency;
}

public class RewardManager : MonoBehaviour
{
    [SerializeField] ItemCatalogData itemCatalog;

    // 8.2: валюта забега = 10 x номер этажа, ±20% разброс; сундук босса = x5 от базовой суммы.
    public int CalculateCurrencyReward(int floorNumber, bool isBoss)
    {
        int baseCurrency = 10 * Mathf.Max(floorNumber, 1);
        float spread = Random.Range(-0.2f, 0.2f);
        int currency = Mathf.RoundToInt(baseCurrency * (1f + spread));

        if (isBoss)
        {
            currency *= 5;
        }

        return currency;
    }

    // 8.2: редкость предмета в сундуке — Обычный 60% / Редкий 30% / Эпический 10%;
    // сундук босса гарантированно даёт минимум Редкий предмет.
    public ItemTier RollItemRarity(bool isBoss)
    {
        float roll = Random.value * 100f;
        ItemTier rarity = roll < 60f ? ItemTier.Common : roll < 90f ? ItemTier.Rare : ItemTier.Epic;

        if (isBoss && rarity == ItemTier.Common)
        {
            rarity = ItemTier.Rare;
        }

        return rarity;
    }

    // 3.9 "Удача" ур.5: 10% шанс получить дополнительную награду из сундука в конце боя.
    public bool RollBonusReward(int luckSkillLevel)
    {
        return luckSkillLevel >= 5 && Random.value < 0.10f;
    }

    // currencyBonus/noCurrency — модификаторы от квестов (5.4: "Загадка сфинкса" даёт +200
    // валюты забега в следующем бою при верном ответе). goldenTouchLevel — пассивка "Золотое
    // касание" (3.10, Корона Мидаса): +1% к валюте забега из сундука за уровень предмета.
    public ChestReward CalculateRewards(int floorNumber, bool isBoss, int luckSkillLevel = 0, int currencyBonus = 0, bool noCurrency = false, int goldenTouchLevel = 0)
    {
        int currency = noCurrency ? 0 : Mathf.RoundToInt((CalculateCurrencyReward(floorNumber, isBoss) + currencyBonus) * (1f + goldenTouchLevel * 0.01f));
        ItemTier itemRarity = RollItemRarity(isBoss);
        ItemData rolledItem = null;
        if (itemCatalog != null)
        {
            itemCatalog.TryGetRandomItem(itemRarity, out rolledItem);
        }

        var reward = new ChestReward
        {
            Currency = currency,
            ItemRarity = itemRarity,
            Item = rolledItem,
            BonusReward = RollBonusReward(luckSkillLevel)
        };

        Debug.Log($"[Reward] Сундук: {reward.Currency} валюты забега, редкость предмета — {reward.ItemRarity}{(reward.BonusReward ? ", + доп. награда (Удача)" : string.Empty)}.");

        return reward;
    }

    // 8.5: [DRAFT] мета-валюта победа=80/поражение=30; гача-валюта=15 в любом случае.
    public RunCompletionReward CalculateRunCompletionReward(bool victory)
    {
        var reward = new RunCompletionReward
        {
            MetaCurrency = victory ? 80 : 30,
            GachaCurrency = 15
        };

        Debug.Log($"[Reward] Итог забега: {reward.MetaCurrency} мета-валюты, {reward.GachaCurrency} гача-валюты.");

        return reward;
    }

    // 3.6: опыт выдаётся отдельно от сундука, автоматически.
    public int GetExperienceReward(ExperienceSource source)
    {
        switch (source)
        {
            case ExperienceSource.CombatRoom: return 10;
            case ExperienceSource.SuccessfulEventOrTrap: return 5;
            case ExperienceSource.Boss: return 50;
            default: return 0;
        }
    }

    public List<int> GrantExperience(RunCharacterProgress progress, ExperienceSource source)
    {
        int amount = GetExperienceReward(source);
        var levelsGained = progress.AddExperience(amount);

        Debug.Log($"[Reward] +{amount} опыта ({source}). Текущий уровень: {progress.Level}, опыт: {progress.Experience}.");

        return levelsGained;
    }
}
