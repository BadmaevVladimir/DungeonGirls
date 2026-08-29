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
    public int ClearBonusMetaCurrency;
    public int ClearBonusGachaCurrency;
}

// 5.2: один предмет в ассортименте торговца — цена уже с учётом возможной скидки (Price), и
// OriginalPrice/HasDiscount для UI (зачёркнутая исходная цена и т.п.).
public class MerchantOffer
{
    public ItemData Item;
    public int OriginalPrice;
    public int Price;
    public bool HasDiscount;
}

public class RewardManager : MonoBehaviour
{
    [SerializeField] internal ItemCatalogData itemCatalog;

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

        return CreateItemAtExactLevel(baseItem, Random.Range(characterLevel, characterLevel + 3)); // [char; char+2] включительно
    }

    // Квестовые/гарантированные награды задают точный уровень. Всегда клонируем общий ассет,
    // чтобы изменение itemLevel не утекло в каталог, стартовое снаряжение или будущие роллы.
    public ItemData CreateItemAtExactLevel(ItemData baseItem, int itemLevel)
    {
        if (baseItem == null) return null;

        var clone = Instantiate(baseItem);
        clone.itemLevel = Mathf.Max(1, itemLevel);
        return clone;
    }

    // 5.2: множитель цены по редкости — Обычный x1.0 / Редкий x1.2 / Эпический x1.5.
    static float MerchantPriceMultiplier(ItemTier tier)
    {
        switch (tier)
        {
            case ItemTier.Rare: return 1.2f;
            case ItemTier.Epic: return 1.5f;
            default: return 1.0f;
        }
    }

    // 5.2: Цена = 100 x УровеньПредмета x Множитель(редкость).
    static int MerchantPrice(ItemData item) => Mathf.RoundToInt(100 * item.itemLevel * MerchantPriceMultiplier(item.tier));

    // 5.2: размер скидки, если гейт в 30% (см. GenerateMerchantOffers) сработал. Таблица вероятностей
    // суммируется в 100% ВНУТРИ этих 30% (это не отдельный шанс "скидки нет" — тот уже выше).
    static float RollDiscountPercent()
    {
        float roll = Random.value * 100f;
        if (roll < 50f) return 10f;
        if (roll < 80f) return 25f;
        if (roll < 94f) return 50f;
        if (roll < 99f) return 75f;
        return 90f;
    }

    // 5.2: 5 случайных предметов, та же таблица редкости, что и сундук (см. RollItemRarity), тот же
    // диапазон уровня [char; char+2] (см. RollItemLevel). 30% шанс, что ОДИН случайно выбранный из 5
    // получит скидку (роллится один раз на весь визит, не по каждому предмету).
    public List<MerchantOffer> GenerateMerchantOffers(int characterLevel)
    {
        return GenerateMerchantOffers(characterLevel, null);
    }

    public List<MerchantOffer> GenerateMerchantOffers(int characterLevel, CharacterClass? characterClass)
    {
        var offers = new List<MerchantOffer>();
        for (int i = 0; i < 5; i++)
        {
            ItemTier tier = RollItemRarity(false);
            ItemData item = null;
            if (itemCatalog != null && itemCatalog.TryGetRandomItem(tier, characterClass, out var baseItem))
            {
                item = RollItemLevel(baseItem, characterLevel);
            }

            int price = item != null ? MerchantPrice(item) : 0;
            offers.Add(new MerchantOffer { Item = item, OriginalPrice = price, Price = price, HasDiscount = false });
        }

        if (Random.value < 0.30f)
        {
            var discounted = offers[Random.Range(0, offers.Count)];
            if (discounted.Item != null)
            {
                float discountPercent = RollDiscountPercent();
                discounted.HasDiscount = true;
                discounted.Price = Mathf.RoundToInt(discounted.OriginalPrice * (1f - discountPercent / 100f));
            }
        }

        return offers;
    }

    // currencyBonus/noCurrency — модификаторы от квестов (5.4: "Загадка сфинкса" даёт +200
    // валюты забега в следующем бою при верном ответе). goldenTouchLevel — пассивка "Золотое
    // касание" (3.10, Корона Мидаса): +10/15/20/25/30% к валюте забега из сундука по рангу эффекта.
    public ChestReward CalculateRewards(int floorNumber, bool isBoss, int characterLevel, int luckSkillLevel = 0, int currencyBonus = 0, bool noCurrency = false, int goldenTouchLevel = 0, CharacterClass? characterClass = null)
    {
        float goldenTouchMultiplier = 1f + ItemEffectBalance.GoldenTouchCurrencyBonusPercent(goldenTouchLevel) / 100f;
        int currency = noCurrency ? 0 : Mathf.RoundToInt((CalculateCurrencyReward(floorNumber, isBoss) + currencyBonus) * goldenTouchMultiplier);
        ItemTier itemRarity = RollItemRarity(isBoss);
        ItemData rolledItem = null;
        if (itemCatalog != null && itemCatalog.TryGetRandomItem(itemRarity, characterClass, out var baseItem))
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

    // 8.5: Поражение даёт базовую награду: мета-валюта переработана —
    // 50 x (число ПОЛНОСТЬЮ пройденных этажей) + 5 x (комнат пройдено НА этаже смерти), потолок
    // снят (раньше был 70). "Полностью пройденных этажей" = currentFloorNumber - 1, т.к. этаж
    // засчитывается только после победы над его боссом (DungeonManager.AdvanceToNextFloor).
    // Умер в первой комнате первого этажа (0 этажей пройдено, 0 комнат на этаже) -> 0 награды,
    // как явно требует ГДД. Гача-валюта за поражение НЕ меняется: 2 за каждую пройденную комнату
    // за ВЕСЬ забег (totalRoomsCleared), потолок 14.
    public RunCompletionReward CalculateRunCompletionReward(bool victory, int totalRoomsCleared, int currentFloorNumber = 0, int roomsClearedOnDeathFloor = 0)
    {
        // При победе текущий этаж уже зачищен боссом, при смерти — ещё нет.
        int floorsFullyCleared = Mathf.Max(0, currentFloorNumber - (victory ? 0 : 1));
        int baseMetaCurrency = 50 * floorsFullyCleared + 5 * Mathf.Max(0, roomsClearedOnDeathFloor);
        int baseGachaCurrency = Mathf.Min(totalRoomsCleared * 2, 14);
        int clearBonusMetaCurrency = victory ? Mathf.RoundToInt(baseMetaCurrency * 0.25f) : 0;
        int clearBonusGachaCurrency = victory ? Mathf.RoundToInt(baseGachaCurrency * 0.25f) : 0;

        var reward = new RunCompletionReward
        {
            MetaCurrency = baseMetaCurrency + clearBonusMetaCurrency,
            GachaCurrency = baseGachaCurrency + clearBonusGachaCurrency,
            ClearBonusMetaCurrency = clearBonusMetaCurrency,
            ClearBonusGachaCurrency = clearBonusGachaCurrency
        };

        Debug.Log($"[Reward] Итог забега: {reward.MetaCurrency} мета-валюты, {reward.GachaCurrency} гача-валюты (бонус зачистки: +{reward.ClearBonusMetaCurrency}/+{reward.ClearBonusGachaCurrency}; этажей пройдено: {floorsFullyCleared}, комнат на этаже смерти: {roomsClearedOnDeathFloor}, комнат всего: {totalRoomsCleared}).");

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
