using UnityEngine;

// Чистые правила Кровотечения. CombatManager только применяет их к рантайм-состоянию и UI-событиям.
public static class BleedRules
{
    public const float DurationSeconds = 3f;

    public static float DamagePerSecond(int level) => level >= 4 ? 20f : Mathf.Clamp(level, 0, 3) * 5f;

    public static float NormalizeRemainingSeconds(float remainingSeconds) =>
        float.IsPositiveInfinity(remainingSeconds) ? DurationSeconds : Mathf.Max(0f, remainingSeconds);

    public static float DetonationDamage(float damagePerSecond, float remainingSeconds) =>
        Mathf.Max(0f, damagePerSecond) * NormalizeRemainingSeconds(remainingSeconds);

    public static bool CanTickCritically(int bleedLevel) => bleedLevel >= 5;
}
