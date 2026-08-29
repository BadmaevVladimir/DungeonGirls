using UnityEngine;

// 8.6: балансный аудит — жёсткие потолки для комбинаций источников одного и того же бонуса.
public static class BalanceClamps
{
    public const float MaxCritChancePercent = 75f;
    public const float MaxEvasionChancePercent = 75f;
    public const float MaxItemEvasionPercent = 30f;
    public const float MaxThornsReflectPercent = 50f;
    public const float CombatRegenHealPercent = 6f;
    public const float CombatRegenCooldownSeconds = 2f;
    public const float MaxArmorRestorePercent = 100f;

    // Навык "Критические атаки" + бонус-стат крита с оружия/колец/амулетов.
    public static float ClampCritChancePercent(float totalPercent) => Mathf.Clamp(totalPercent, 0f, MaxCritChancePercent);

    public static float ClampEvasionChancePercent(float totalPercent) => Mathf.Clamp(totalPercent, 0f, MaxEvasionChancePercent);

    public static float ClampItemEvasionPercent(float totalPercent) => Mathf.Clamp(totalPercent, 0f, MaxItemEvasionPercent);

    // "Шипы" растут равномерно и никогда не отражают больше половины заблокированного урона.
    public static float ThornsReflectPercent(int skillLevel) =>
        Mathf.Min(Mathf.Clamp(skillLevel, 0, 5) * 10f, MaxThornsReflectPercent);

    // "Боевая регенерация": чем выше уровень, тем меньше попаданий нужно для срабатывания.
    public static int CombatRegenHitsRequired(int skillLevel) => Mathf.Clamp(skillLevel, 1, 5) switch
    {
        1 => 6,
        2 => 5,
        3 => 4,
        4 => 3,
        5 => 2,
        _ => int.MaxValue
    };

    // "Полевой ремонт" персонажа + бонус Кузницы (мета-прогрессия, не реализована в этой фазе)
    // + пассивка "Ремонт" Молота кузнеца. Здания и предметы передаются вызывающей стороной,
    // сам кламп применяется к их сумме.
    public static float ClampArmorRestorePercent(float totalPercent) => Mathf.Clamp(totalPercent, 0f, MaxArmorRestorePercent);
}
