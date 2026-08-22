using UnityEngine;

// 8.6: балансный аудит — жёсткие потолки для комбинаций источников одного и того же бонуса.
public static class BalanceClamps
{
    public const float MaxCritChancePercent = 75f;
    public const float MaxArmorRestorePercent = 100f;

    // Навык "Критические атаки" + бонус-стат крита с оружия/колец/амулетов.
    public static float ClampCritChancePercent(float totalPercent) => Mathf.Clamp(totalPercent, 0f, MaxCritChancePercent);

    // "Полевой ремонт" персонажа + бонус Кузницы (мета-прогрессия, не реализована в этой фазе)
    // + пассивка "Ремонт" Молота кузнеца. Здания и предметы передаются вызывающей стороной,
    // сам кламп применяется к их сумме.
    public static float ClampArmorRestorePercent(float totalPercent) => Mathf.Clamp(totalPercent, 0f, MaxArmorRestorePercent);
}
