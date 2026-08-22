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
    public bool BonusReward; // 3.9 "Удача" ур.5: доп. шанс на дополнительную награду
}

public class RewardManager : MonoBehaviour
{
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

    public ChestReward CalculateRewards(int floorNumber, bool isBoss, int luckSkillLevel = 0)
    {
        var reward = new ChestReward
        {
            Currency = CalculateCurrencyReward(floorNumber, isBoss),
            ItemRarity = RollItemRarity(isBoss),
            BonusReward = RollBonusReward(luckSkillLevel)
        };

        Debug.Log($"[Reward] Сундук: {reward.Currency} валюты забега, редкость предмета — {reward.ItemRarity}{(reward.BonusReward ? ", + доп. награда (Удача)" : string.Empty)}.");

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
