using System.Collections.Generic;
using UnityEngine;

// ГДД «Инструмент — MCP-сервер openDAW и слои музыки»: кривые громкости слоёв от напряжения боя.
//
// Чистая логика, отдельно от MusicMixer, потому что это единственное место, где живёт баланс
// звучания: подвинуть точку вступления lead можно, не трогая ни жизненный цикл музыки, ни Unity.
//
// Порядок ролей — порядок появления, от базового к пиковому.
public static class MusicLayerMix
{
    public const string Harmony = "harmony";
    public const string Drums = "drums";
    public const string Lead = "lead";

    // Роль единственного слоя, когда набора стемов нет и играет контрольный микс.
    public const string FullMixLayer = "mix";

    public static readonly IReadOnlyList<string> LayerRoles = new[] { Harmony, Drums, Lead };

    // Базовый слой звучит всегда: вне боя интенсивность 0, и слышен именно он.
    //
    // Базовый — harmony, а не drums. Решение дизайнера от 09.09.2026 (спек «Боевые темы трёх
    // героинь и тема деревни»): лёгкий пульс (хэт, шейкер) вынесен в harmony, поэтому вступление
    // drums читается как «бой набрал ход», а не как включение метронома. Таблица зон в ГДД
    // старше этого решения и описывает прежнее распределение.
    public const string BaseLayer = Harmony;

    // Точки входа и полного уровня по шкале напряжения 0–1.
    public const float DrumsFadeIn = 0.12f;
    public const float DrumsFull = 0.50f;
    public const float LeadFadeIn = 0.60f;
    public const float LeadFull = 0.90f;

    public static float GainFor(string role, float intensity)
    {
        intensity = Mathf.Clamp01(intensity);
        switch (role)
        {
            case Drums: return Ramp(intensity, DrumsFadeIn, DrumsFull);
            case Lead: return Ramp(intensity, LeadFadeIn, LeadFull);

            // Базовый слой и одиночный контрольный микс не зависят от напряжения: их задача —
            // не пропадать. Иначе выход из боя означал бы тишину, а не «вернулись к минимуму».
            default: return 1f;
        }
    }

    // SmoothStep, а не линейный подъём: у линейного слышен излом в точке полного уровня.
    static float Ramp(float value, float from, float to)
    {
        if (value <= from) return 0f;
        if (value >= to) return 1f;
        float t = (value - from) / (to - from);
        return t * t * (3f - 2f * t);
    }
}
