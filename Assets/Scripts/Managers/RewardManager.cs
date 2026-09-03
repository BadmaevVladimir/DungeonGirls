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
    public int Currency { get; }
    public ItemTier ItemRarity { get; }
    public ItemData Item { get; } // 3.4: конкретный предмет для сравнения/эквипа; null, если каталог пуст
    public bool BonusReward { get; } // 3.9 "Удача" ур.5: доп. шанс на дополнительную награду

    public ChestReward(int currency = 0, ItemTier itemRarity = ItemTier.Common,
        ItemData item = null, bool bonusReward = false)
    {
        Currency = currency;
        ItemRarity = itemRarity;
        Item = item;
        BonusReward = bonusReward;
    }
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
    [SerializeField] RoomRewardConfig roomRewardConfig;
    [SerializeField] bool unlockAllItemPrototypesForTesting;
    IReadOnlyList<string> researchedItemPrototypes;

    public const float RegularCombatChestChance = 0.50f;
    RoomRewardConfig Config
    {
        get
        {
            if (roomRewardConfig == null)
            {
                roomRewardConfig = ScriptableObject.CreateInstance<RoomRewardConfig>();
                roomRewardConfig.hideFlags = HideFlags.HideAndDontSave;
            }
            return roomRewardConfig;
        }
    }
    public float CombatChestDropChance => Mathf.Clamp01(Config.combatChestDropChance);
    public RoomRewardConfig RewardConfig => Config;

    public void SetRoomRewardConfig(RoomRewardConfig config) => roomRewardConfig = config;
    public void SetItemCatalog(ItemCatalogData catalog) => itemCatalog = catalog;

    public void SetPrototypeProgression(IReadOnlyList<string> researchedIds, bool? testOverride = null)
    {
        researchedItemPrototypes = researchedIds;
        if (testOverride.HasValue) unlockAllItemPrototypesForTesting = testOverride.Value;
    }

    public bool IsItemInLootPool(ItemData item)
    {
        if (item == null) return false;
        if (string.IsNullOrWhiteSpace(item.prototypeId) || unlockAllItemPrototypesForTesting) return true;
        if (researchedItemPrototypes == null) return false;
        for (int i = 0; i < researchedItemPrototypes.Count; i++)
            if (string.Equals(researchedItemPrototypes[i], item.prototypeId, System.StringComparison.Ordinal)) return true;
        return false;
    }

    public List<ItemData> GetCompatibleLootItems(CharacterClass? characterClass) =>
        itemCatalog != null ? itemCatalog.GetCompatibleItems(characterClass, IsItemInLootPool) : new List<ItemData>();

    // 8.2: валюта забега = 10 x номер этажа, ±20% разброс; сундук босса = x5 от базовой суммы.
    public int CalculateCurrencyReward(int floorNumber, bool isBoss)
        => CalculateCurrencyReward(floorNumber, isBoss, new UnityRewardRandom());

    int CalculateCurrencyReward(int floorNumber, bool isBoss, IRewardRandom random)
    {
        int baseCurrency = 10 * Mathf.Max(floorNumber, 1);
        float spread = Mathf.Lerp(-0.2f, 0.2f, random.Value());
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
        return ItemRarityTable.Roll(Random.value * 100f, isBoss);
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
            case ItemTier.Cursed: return 1.5f; // тот же основной tier-множитель, что у Epic
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
            if (itemCatalog != null && itemCatalog.TryGetRandomItem(tier, characterClass, IsItemInLootPool, out var baseItem))
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

    // Reusable flow для будущих special-room definitions: контент комнаты задаёт stable effect id,
    // а общий каталог/классовая фильтрация и runtime-клон остаются теми же, что у сундуков.
    public ItemData CreateGuaranteedCursedReward(CursedEffectId effect, CharacterClass characterClass, int itemLevel)
    {
        if (itemCatalog == null || !itemCatalog.TryGetGuaranteedCursedItem(effect, characterClass, out var baseItem)) return null;
        return CreateItemAtExactLevel(baseItem, itemLevel);
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
        if (itemCatalog != null && itemCatalog.TryGetRandomItem(itemRarity, characterClass, IsItemInLootPool, out var baseItem))
        {
            rolledItem = RollItemLevel(baseItem, characterLevel);
        }

        var reward = new ChestReward(currency, itemRarity, rolledItem, RollBonusReward(luckSkillLevel));

        Debug.Log($"[Reward] Сундук: {reward.Currency} валюты забега, редкость предмета — {reward.ItemRarity}{(reward.BonusReward ? ", + доп. награда (Удача)" : string.Empty)}.");

        return reward;
    }

    public RoomRewardResult CalculateRoomReward(int floorNumber, bool isBoss, int characterLevel,
        int luckSkillLevel = 0, int currencyBonus = 0, bool noCurrency = false,
        int goldenTouchLevel = 0, CharacterClass? characterClass = null, IRewardRandom random = null,
        IEnumerable<ResourceAmount> extraIngredients = null)
    {
        random ??= new UnityRewardRandom();
        bool hasChest = isBoss || random.Value() < CombatChestDropChance;
        float goldenTouchMultiplier = 1f + ItemEffectBalance.GoldenTouchCurrencyBonusPercent(goldenTouchLevel) / 100f;
        int currency = noCurrency ? 0 : Mathf.RoundToInt(
            (CalculateCurrencyReward(floorNumber, isBoss, random) + currencyBonus) * goldenTouchMultiplier);

        var ingredients = new List<ResourceAmount>();
        if (extraIngredients != null) ingredients.AddRange(extraIngredients);
        var ingredientDrop = RollIngredientReward(isBoss ? RewardRoomContext.Boss : RewardRoomContext.Combat, random);
        if (ingredientDrop.HasValue) ingredients.Add(ingredientDrop.Value);

        var materials = new List<ResourceAmount>();
        var materialDrop = RollForgeMaterial(isBoss ? RewardRoomContext.Boss : RewardRoomContext.Combat, random);
        if (materialDrop.HasValue) materials.Add(materialDrop.Value);

        ChestReward chest = hasChest
            ? CalculateChestReward(isBoss, characterLevel, luckSkillLevel, characterClass)
            : null;
        return new RoomRewardResult(currency, ingredients, hasChest,
            isBoss ? RewardRoomContext.Boss : RewardRoomContext.Combat, chest, materials);
    }

    public ResourceAmount? RollCombatIngredient(IRewardRandom random)
    {
        return RollIngredient(RewardRoomContext.Combat, random);
    }

    public ResourceAmount? RollIngredientReward(RewardRoomContext context, IRewardRandom random = null)
    {
        var config = Config;
        random ??= new UnityRewardRandom();
        if (context == RewardRoomContext.Boss) return RollIngredient(context, random);
        float chance = context switch
        {
            RewardRoomContext.Trap => config.successfulTrapIngredientDropChance,
            RewardRoomContext.Special => config.supportedSpecialIngredientDropChance,
            _ => config.combatIngredientDropChance
        };
        return random.Value() < chance ? RollIngredient(context, random) : null;
    }

    // Trap/Special content decides whether its approved reward exists; this only selects by the
    // approved contextual weights and deliberately does not invent an overall drop chance.
    public ResourceAmount? RollIngredient(RewardRoomContext context, IRewardRandom random = null)
    {
        var config = Config;
        IReadOnlyList<IngredientDropRule> table = context switch
        {
            RewardRoomContext.Trap => config.trapIngredientDrops,
            RewardRoomContext.Special => config.specialIngredientDrops,
            RewardRoomContext.Boss => config.bossIngredientDrops,
            _ => config.combatIngredientDrops
        };
        return RollWeighted(table, random ?? new UnityRewardRandom());
    }

    public ResourceAmount? RollForgeMaterial(RewardRoomContext context, IRewardRandom random = null)
    {
        if (context == RewardRoomContext.Special) return null;
        var config = Config;
        random ??= new UnityRewardRandom();
        if (context == RewardRoomContext.Boss)
            return RollWeighted(config.bossForgeMaterialDrops, random);
        float chance = context switch
        {
            RewardRoomContext.Trap => config.successfulTrapForgeMaterialChance,
            _ => config.normalCombatForgeMaterialChance
        };
        IReadOnlyList<IngredientDropRule> table = context switch
        {
            RewardRoomContext.Boss => config.bossForgeMaterialDrops,
            RewardRoomContext.Trap => config.trapForgeMaterialDrops,
            _ => config.combatForgeMaterialDrops
        };
        return random.Value() < chance ? RollWeighted(table, random) : null;
    }

    public List<ResourceAmount> RollAbandonedForgeMaterials(RareRoomConfig rareConfig, IRewardRandom random = null)
    {
        random ??= new UnityRewardRandom();
        int count = RareRoomRewardHooks.RollAbandonedForgeMaterialCount(rareConfig, random);
        var result = new List<ResourceAmount>(count);
        for (int i = 0; i < count; i++)
        {
            var drop = RollWeighted(Config.abandonedForgeMaterialDrops, random);
            if (drop.HasValue) result.Add(drop.Value);
        }
        return result;
    }

    public static float TotalWeight(IReadOnlyList<IngredientDropRule> rules)
    {
        float total = 0f;
        if (rules == null) return total;
        for (int i = 0; i < rules.Count; i++)
            if (rules[i] != null && !string.IsNullOrWhiteSpace(rules[i].resourceId))
                total += Mathf.Max(0f, rules[i].weight);
        return total;
    }

    static ResourceAmount? RollWeighted(IReadOnlyList<IngredientDropRule> rules, IRewardRandom random)
    {
        if (rules == null) return null;
        float total = TotalWeight(rules);
        if (total <= 0f) return null;
        float roll = random.Value() * total;
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule == null || string.IsNullOrWhiteSpace(rule.resourceId)) continue;
            roll -= Mathf.Max(0f, rule.weight);
            if (roll > 0f) continue;
            int min = Mathf.Max(1, rule.minAmount);
            int max = Mathf.Max(min, rule.maxAmount);
            return new ResourceAmount(rule.resourceId, random.Range(min, max + 1));
        }
        return null;
    }

    // Формулы редкости/уровня предмета и boss-minimum остаются прежними; валюта теперь room reward.
    ChestReward CalculateChestReward(bool isBoss, int characterLevel, int luckSkillLevel,
        CharacterClass? characterClass)
    {
        ItemTier itemRarity = RollItemRarity(isBoss);
        ItemData rolledItem = null;
        if (itemCatalog != null && itemCatalog.TryGetRandomItem(itemRarity, characterClass, IsItemInLootPool, out var baseItem))
            rolledItem = RollItemLevel(baseItem, characterLevel);
        return new ChestReward(0, itemRarity, rolledItem, RollBonusReward(luckSkillLevel));
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
