using UnityEngine;

// GDD 11.1: 15% персонаж (три равные доли по 5%), 85% мета-валюта.
// Предметов в результате нет; ItemTier переиспользуется только как размер/цвет валютного приза.
public static class GachaPool
{
    public const float CharacterChance = 0.15f;
    public const int CharacterCount = 3;

    public struct Result
    {
        public bool IsCharacter;
        public int CharacterIndex;
        public ItemTier CurrencyTier;
        public int CurrencyAmount;
    }

    public static bool RollResult(float roll, out Result result) => RollResult(roll, roll, out result);

    public static bool RollResult(float roll, float rarityRoll, out Result result)
    {
        result = new Result();
        if (roll < 0f || roll >= 1f || rarityRoll < 0f || rarityRoll >= 1f) return false;

        if (roll < CharacterChance)
        {
            result.IsCharacter = true;
            float sliceWidth = CharacterChance / CharacterCount;
            result.CharacterIndex = Mathf.Clamp(Mathf.FloorToInt(roll / sliceWidth), 0, CharacterCount - 1);
            return true;
        }

        result.IsCharacter = false;
        if (rarityRoll < 0.62f)
        {
            result.CurrencyTier = ItemTier.Common;
            result.CurrencyAmount = 20;
        }
        else if (rarityRoll < 0.97f)
        {
            result.CurrencyTier = ItemTier.Rare;
            result.CurrencyAmount = 50;
        }
        else
        {
            result.CurrencyTier = ItemTier.Epic;
            result.CurrencyAmount = 150;
        }

        return true;
    }
}
