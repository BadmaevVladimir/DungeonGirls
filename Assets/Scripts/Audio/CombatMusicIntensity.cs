using UnityEngine;

// ГДД «Механика адаптивного боевого микса», раздел «Расчёт напряжения боя»: локальный датчик
// напряжённости ТЕКУЩЕГО боя, значение 0–1.
//
// Чистая логика без Unity-зависимостей кроме Mathf: складывает наблюдаемые признаки сцены и
// отдаёт цель, а сглаживание (подъём быстро, спад медленно) живёт в MusicMixer. Разделение
// намеренное — датчик отвечает за «насколько сейчас горячо», микшер за «как быстро это слышно».
//
// Датчик НЕ смотрит на предметы, билд и историю этажа: стресс и доминирование прошлого этажа —
// дело FloorDirector, и в громкость слоёв они не попадают. Режиссёр влияет ровно через одну
// величину — стартовую базу встречи, передаваемую в Begin().
public sealed class CombatMusicIntensity
{
    // Тестовые стартовые базы по типу встречи. Ни одна из них не должна сама по себе открывать
    // следующий слой: это только исходная точка, поверх которой работает бой.
    public const float NormalEncounterBase = 0.20f;
    public const float ChallengeEncounterBase = 0.40f;
    public const float BossEncounterBase = 0.55f;

    // Обмен уроном затухает медленно: одиночный удар не должен дёргать микс.
    public const float ExchangeHalfLifeSeconds = 2.5f;

    // Импульс (телеграф, новая фаза босса, подкрепление) — короткий и заметный.
    public const float ImpulseHalfLifeSeconds = 1.2f;

    // Потолок для случая «здоровье низкое, но бой затих». Ниже MusicLayerMix.LeadFull, поэтому
    // низкое здоровье в одиночку не может включить пик — требование ГДД.
    public const float QuietCombatCeiling = 0.79f;

    // Ниже этого вклада обмен уроном считается прекратившимся.
    public const float ExchangeQuietThreshold = 0.05f;

    float encounterBase = NormalEncounterBase;
    float exchange;
    float impulse;
    float target;

    public float Target => target;

    public void Begin(float encounterBaseIntensity)
    {
        encounterBase = Mathf.Clamp01(encounterBaseIntensity);
        exchange = 0f;
        impulse = 0f;
        target = encounterBase;
    }

    public static float BaseForEncounter(bool isBoss, bool isChallenge) =>
        isBoss ? BossEncounterBase : isChallenge ? ChallengeEncounterBase : NormalEncounterBase;

    // Любой засчитанный удар в обе стороны: и урон героини, и полученный урон повышают
    // событийную плотность. referenceHp — максимум здоровья героини, чтобы шкала не зависела от
    // абсолютных чисел и не менялась от этажа к этажу.
    public void ReportDamage(float amount, float referenceHp)
    {
        if (amount <= 0f || referenceHp <= 0f) return;
        exchange = Mathf.Clamp01(exchange + amount / (0.5f * referenceHp));
    }

    // Опасный телеграф, новая фаза босса, появление подкрепления, крупный удар.
    public void ReportSpike(float strength = 0.15f)
    {
        impulse = Mathf.Clamp01(impulse + Mathf.Max(0f, strength));
    }

    // Вызывается несколько раз в секунду; покадровый пересчёт не требуется, но и не вредит.
    public float Tick(float deltaTime, int aliveEnemies, float playerHpFraction)
    {
        if (deltaTime > 0f)
        {
            exchange *= Decay(deltaTime, ExchangeHalfLifeSeconds);
            impulse *= Decay(deltaTime, ImpulseHalfLifeSeconds);
        }

        // Толпа давит: каждый противник сверх первого добавляет вес, но не больше трети шкалы.
        float crowd = Mathf.Clamp(0.10f * Mathf.Max(0, aliveEnemies - 1), 0f, 0.30f);

        // Низкое здоровье добавляет напряжение, но не может в одиночку включить пик.
        float lowHealth = playerHpFraction >= 0.35f
            ? 0f
            : 0.20f * (1f - Mathf.Clamp01(playerHpFraction / 0.35f));

        float exchangeWeight = 0.30f * exchange;
        float combat = encounterBase + crowd + exchangeWeight + impulse;
        float value = Mathf.Clamp01(combat + lowHealth);

        // Остался один слабый противник и обмен уроном прекратился — цель постепенно снижается
        // сама за счёт затухания exchange; потолок гарантирует, что пик при этом не звучит.
        if (exchangeWeight < ExchangeQuietThreshold && impulse < ExchangeQuietThreshold)
            value = Mathf.Min(value, QuietCombatCeiling);

        target = value;
        return target;
    }

    static float Decay(float deltaTime, float halfLifeSeconds) =>
        Mathf.Pow(0.5f, deltaTime / halfLifeSeconds);
}
