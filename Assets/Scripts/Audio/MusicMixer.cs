using System;
using System.Collections.Generic;
using UnityEngine;

// Жизненный цикл музыки забега: какой контекст играет, насколько подмешаны слои и заглушена ли
// основа временным override. Чистая логика поверх IMusicOutput — Unity здесь нет.
//
// Главное правило системы: музыка забега НЕПРЕРЫВНА. Бой не запускает её и не останавливает, он
// только поднимает интенсивность; выход из боя возвращает интенсивность к нулю, оставляя базовый
// слой играть со своей позиции. Stop вызывается ровно в трёх случаях: намеренная смена контекста
// (деревня, босс), намеренная тишина и уничтожение проигрывателя.
//
// Режиссёр этажа (FloorDirector) сюда не входит по решению от 09.09.2026: он может задать
// стартовую базу напряжения для встречи, но не управляет запуском, остановкой и позицией треков.
public sealed class MusicMixer
{
    // Запуск планируется чуть вперёд от текущего dspTime: мгновенный PlayScheduled рискует
    // попасть в уже отрендеренный буфер, и слои разъедутся на его длину.
    public const double ScheduleLeadSeconds = 0.05;

    // Подъём быстрый, спад медленный — иначе музыка «дышит фейдером» после каждого удара
    // (ГДД, «Расчёт напряжения боя»).
    public const float IntensityRiseSeconds = 1.0f;
    public const float IntensityFallSeconds = 3.0f;

    public const float DefaultOverrideFadeSeconds = 0.35f;

    readonly struct OverrideEntry
    {
        public readonly int Token;
        public readonly string TrackName; // null — только заглушить основу (джингл сундука)

        public OverrideEntry(int token, string trackName)
        {
            Token = token;
            TrackName = trackName;
        }
    }

    IMusicOutput output;
    Func<string, bool> trackExists;

    MusicTrackSet current = MusicTrackSet.Silence;
    float intensity;
    float intensityTarget;

    // 1 — основная тема слышна, 0 — полностью заглушена под override.
    float baseGain = 1f;
    float overrideFadeSeconds = DefaultOverrideFadeSeconds;

    readonly List<OverrideEntry> overrides = new List<OverrideEntry>();
    int nextOverrideToken = 1;
    string playingOverrideTrack;

    float categoryVolume = 1f;

    public string CurrentContextKey => current.ContextKey;
    public bool IsLayered => current.IsLayered;
    public int LayerCount => current.IsSilent ? 0 : current.Layers.Count;
    public float Intensity => intensity;
    public float IntensityTarget => intensityTarget;
    public float BaseGain => baseGain;
    public int OverrideDepth => overrides.Count;
    public bool IsOverrideActive => overrides.Count > 0;
    public string OverrideTrackName => playingOverrideTrack;

    public void Attach(IMusicOutput musicOutput, Func<string, bool> exists)
    {
        output = musicOutput;
        trackExists = exists;
    }

    public void SetCategoryVolume(float volume)
    {
        categoryVolume = Mathf.Clamp01(volume);
        ApplyVolumes(); // без перезапуска: настройка действует на уже играющие слои
    }

    // Намеренная смена музыкального контекста: деревня, забег, босс, тишина. Повторный вызов с
    // тем же контекстом не делает ничего — ни Stop, ни смены клипов, ни сброса позиции.
    public void PlayContext(MusicRequest request)
    {
        if (output == null) return;

        var next = MusicCatalog.Resolve(request, trackExists);
        if (!MusicCatalog.ShouldSwitch(current.ContextKey, next.ContextKey)) return;

        current = next;
        if (next.IsSilent)
        {
            output.StopAllLayers();
            return;
        }

        if (!output.PrepareLayers(next.Layers))
        {
            current = MusicTrackSet.Silence;
            output.StopAllLayers();
            return;
        }

        // Один момент времени на все слои — отсюда фазовое совпадение и одинаковая позиция.
        double start = output.DspTime + ScheduleLeadSeconds;
        for (int i = 0; i < next.Layers.Count; i++) output.ScheduleLayer(i, start);

        ApplyVolumes();
    }

    // Вход в бой: контекст тот же (тема героини уже играет), меняется только целевая
    // интенсивность. Для босса вызывающая сторона отдельно меняет контекст.
    public void EnterCombat(float encounterBaseIntensity)
    {
        intensityTarget = Mathf.Clamp01(encounterBaseIntensity);
    }

    public void SetIntensity(float target)
    {
        intensityTarget = Mathf.Clamp01(target);
    }

    // Замена прежнего StopMusic() после боя: музыка продолжает играть, слои уходят к базовому.
    public void ExitCombat()
    {
        intensityTarget = 0f;
    }

    // Временное заглушение основной темы. Свой трек необязателен: джинглу сундука нужна только
    // тишина под ним, особой комнате — ещё и отдельная музыка.
    //
    // Возвращает токен. Основная музыка вернётся, когда закроется ПОСЛЕДНИЙ открытый override —
    // вложенность безопасна. EndOverride с чужим или повторным токеном ничего не делает.
    public int BeginOverride(MusicRequest? track = null, float fadeSeconds = DefaultOverrideFadeSeconds)
    {
        overrideFadeSeconds = Mathf.Max(0.01f, fadeSeconds);

        string trackName = null;
        if (track.HasValue && output != null)
        {
            var resolved = MusicCatalog.Resolve(track.Value, trackExists);
            // Отдельный трек события — всегда один файл: слои для него не предусмотрены.
            if (!resolved.IsSilent) trackName = resolved.ContextKey;
        }

        int token = nextOverrideToken++;
        overrides.Add(new OverrideEntry(token, trackName));
        SyncOverrideTrack();
        return token;
    }

    public void EndOverride(int token)
    {
        for (int i = overrides.Count - 1; i >= 0; i--)
        {
            if (overrides[i].Token != token) continue;
            overrides.RemoveAt(i);
            SyncOverrideTrack();
            return;
        }
    }

    // Аварийный выход: например, забег закончился, пока джингл ещё шёл.
    public void ClearOverrides()
    {
        overrides.Clear();
        SyncOverrideTrack();
    }

    // Играет верхний override, у которого есть свой трек. Именно верхний, а не первый: вложенный
    // override события поверх события должен быть слышен, а при его закрытии — вернуть нижний.
    void SyncOverrideTrack()
    {
        if (output == null) return;

        string desired = null;
        for (int i = overrides.Count - 1; i >= 0; i--)
        {
            if (overrides[i].TrackName == null) continue;
            desired = overrides[i].TrackName;
            break;
        }

        if (string.Equals(desired, playingOverrideTrack, StringComparison.Ordinal)) return;

        playingOverrideTrack = desired;
        if (desired == null) output.StopOverrideTrack();
        else output.PlayOverrideTrack(desired);
    }

    public void Tick(float deltaTime)
    {
        if (output == null || deltaTime <= 0f) return;

        float intensitySeconds = intensityTarget > intensity ? IntensityRiseSeconds : IntensityFallSeconds;
        intensity = Mathf.MoveTowards(intensity, intensityTarget, deltaTime / intensitySeconds);

        float baseTarget = overrides.Count > 0 ? 0f : 1f;
        baseGain = Mathf.MoveTowards(baseGain, baseTarget, deltaTime / overrideFadeSeconds);

        ApplyVolumes();
    }

    // Итоговая громкость слоя складывается из независимых множителей, и это намеренно:
    // пользовательская настройка Music не смешана с балансом слоёв, а заглушение override не
    // трогает ни то, ни другое.
    public float LayerVolume(int index)
    {
        if (current.IsSilent || index < 0 || index >= current.Layers.Count) return 0f;
        return categoryVolume * MusicLayerMix.GainFor(current.Roles[index], intensity) * baseGain;
    }

    public float OverrideVolume => categoryVolume * (1f - baseGain);

    void ApplyVolumes()
    {
        if (output == null) return;
        if (!current.IsSilent)
            for (int i = 0; i < current.Layers.Count; i++) output.SetLayerVolume(i, LayerVolume(i));
        output.SetOverrideVolume(OverrideVolume);
    }
}
