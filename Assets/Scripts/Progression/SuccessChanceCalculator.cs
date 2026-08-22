using UnityEngine;

// 8.3: формула шанса успеха для ловушек (5.5) и текстовых квестов (5.4).
// Сами ловушки/квесты не реализованы (Фаза 3.5) — это переиспользуемый метод для будущих фаз.
public static class SuccessChanceCalculator
{
    public const float MinChancePercent = 5f;
    public const float MaxChancePercent = 95f;

    // bonusPercent — сумма прочих бонусов до клампа (класс/навыки, например "Удача", см. GetLuckBonusPercent).
    public static float CalculateSuccessChancePercent(int characterLevel, int challengeLevel, float bonusPercent = 0f)
    {
        float chance = 50f + (characterLevel - challengeLevel) * 10f + bonusPercent;
        return Mathf.Clamp(chance, MinChancePercent, MaxChancePercent);
    }

    // 3.9 "Удача": каждый уровень навыка = +1 эффективный уровень персонажа в формуле шанса,
    // что эквивалентно +10% к шансу успеха (см. шаг формулы выше).
    public static float GetLuckBonusPercent(int luckSkillLevel) => luckSkillLevel * 10f;
}
