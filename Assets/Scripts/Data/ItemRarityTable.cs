using UnityEngine;

public static class ItemRarityTable
{
    public const float CommonPercent = 60f;
    public const float RarePercent = 35f;
    public const float EpicPercent = 3f;
    public const float CursedPercent = 2f;
    public const float TotalPercent = CommonPercent + RarePercent + EpicPercent + CursedPercent;

    public static ItemTier Roll(float rollPercent, bool isBoss)
    {
        float roll = Mathf.Clamp(rollPercent, 0f, 99.9999f);
        ItemTier tier = roll < CommonPercent ? ItemTier.Common
            : roll < CommonPercent + RarePercent ? ItemTier.Rare
            : roll < CommonPercent + RarePercent + EpicPercent ? ItemTier.Epic
            : ItemTier.Cursed;
        return isBoss && tier == ItemTier.Common ? ItemTier.Rare : tier;
    }
}
