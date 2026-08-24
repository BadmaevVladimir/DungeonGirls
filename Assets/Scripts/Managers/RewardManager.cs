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

    // 8.2 [ОБНОВЛЕНО 2026-08-25]: доля Эпического снижена ещё раз — Обычный 62% / Редкий 35% /
    // Эпический 3% (было 60/30/10). Сундук босса гарантированно даёт минимум Редкий предмет.
    public ItemTier RollItemRarity(bool isBoss)
    {
        float roll = Random.value * 100f;
        ItemTier rarity = roll < 62f ? ItemTier.Common : roll < 97f ? ItemTier.Rare : ItemTier.Epic;

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

    // 8.2 [DRAFT, обновлено после плейтеста]: уровень предмета в сундуке больше не всегда 1 —
    // случайный в диапазоне [уровеньПерсонажа; уровеньПерсонажа+2] (раньше весь лут падал
    // 1 уровня, разница между уровнями предмета была незаметна). ItemData — общий
    // ScriptableObject-ассет каталога, поэтому уровень выставляется на runtime-клоне, а не на
    // самом ассете (иначе он "утёк" бы во все последующие роллы), см. также EquipmentManager.
    ItemData RollItemLevel(ItemData baseItem, int characterLevel)
    {
        if (baseItem == null) return null;

        var clone = Instantiate(baseItem);
        clone.itemLevel = Random.Range(characterLevel, characterLevel + 3); // [char; char+2] включительно
        return clone;
    }

    // currencyBonus/noCurrency — модификаторы от квестов (5.4: "Загадка сфинкса" даёт +200
    // валюты забега в следующем бою при верном ответе). goldenTouchLevel — пассивка "Золотое
    // касание" (3.10, Корона Мидаса): +1% к валюте забега из сундука за уровень предмета.
    public ChestReward CalculateRewards(int floorNumber, bool isBoss, int characterLevel, int luckSkillLevel = 0, int currencyBonus = 0, bool noCurrency = false, int goldenTouchLevel = 0)
    {
        int currency = noCurrency ? 0 : Mathf.RoundToInt((CalculateCurrencyReward(floorNumber, isBoss) + currencyBonus) * (1f + goldenTouchLevel * 0.01f));
        ItemTier itemRarity = RollItemRarity(isBoss);
        ItemData rolledItem = null;
        if (itemCatalog != null && itemCatalog.TryGetRandomItem(itemRarity, out var baseItem))
        {
            rolledItem = RollItemLevel(baseItem, characterLevel);
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

    // 8.5 [ОБНОВЛЕНО 2026-08-25, под цель "20-30 поражений на полный макс всех 3 зданий"]:
    // победа = 80 мета/15 гача (без изменений). Поражение: мета-валюта переработана —
    // 50 x (число ПОЛНОСТЬЮ пройденных этажей) + 5 x (комнат пройдено НА этаже смерти), потолок
    // снят (раньше был 70). "Полностью пройденных этажей" = currentFloorNumber - 1, т.к. этаж
    // засчитывается только после победы над его боссом (DungeonManager.AdvanceToNextFloor).
    // Умер в первой комнате первого этажа (0 этажей пройдено, 0 комнат на этаже) -> 0 награды,
    // как явно требует ГДД. Гача-валюта за поражение НЕ меняется: 2 за каждую пройденную комнату
    // за ВЕСЬ забег (totalRoomsCleared), потолок 14.
    public RunCompletionReward CalculateRunCompletionReward(bool victory, int totalRoomsCleared, int currentFloorNumber = 0, int roomsClearedOnDeathFloor = 0)
    {
        int metaCurrency;
        int gachaCurrency;

        if (victory)
        {
            metaCurrency = 80;
            gachaCurrency = 15;
        }
        else
        {
            int floorsFullyCleared = Mathf.Max(0, currentFloorNumber - 1);
            metaCurrency = 50 * floorsFullyCleared + 5 * Mathf.Max(0, roomsClearedOnDeathFloor);
            gachaCurrency = Mathf.Min(totalRoomsCleared * 2, 14);
        }

        var reward = new RunCompletionReward
        {
            MetaCurrency = metaCurrency,
            GachaCurrency = gachaCurrency
        };

        Debug.Log($"[Reward] Итог забега: {reward.MetaCurrency} мета-валюты, {reward.GachaCurrency} гача-валюты (этажей пройдено: {Mathf.Max(0, currentFloorNumber - 1)}, комнат на этаже смерти: {roomsClearedOnDeathFloor}, комнат всего: {totalRoomsCleared}).");

        return reward;
    }

    // 3.6 [ОБНОВЛЕНО 2026-08-25]: источники опыта растут вместе с этажом, чтобы прокачка успевала
    // за расширением до 10 этажей. Босс остаётся флэт 50 (тот же переиспользуемый босс на всех этажах).
    public int GetExperienceReward(ExperienceSource source, int floorNumber)
    {
        int floorIndex = Mathf.Max(floorNumber, 1);
        switch (source)
        {
            case ExperienceSource.CombatRoom: return 10 + 3 * (floorIndex - 1);
            case ExperienceSource.SuccessfulEventOrTrap: return 5 + 1 * (floorIndex - 1);
            case ExperienceSource.Boss: return 50;
            default: return 0;
        }
    }

    public List<int> GrantExperience(RunCharacterProgress progress, ExperienceSource source, int floorNumber)
    {
        int amount = GetExperienceReward(source, floorNumber);
        var levelsGained = progress.AddExperience(amount);

        Debug.Log($"[Reward] +{amount} опыта ({source}, этаж {floorNumber}). Текущий уровень: {progress.Level}, опыт: {progress.Experience}.");

        return levelsGained;
    }
}
