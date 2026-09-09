using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// ГДД 10 (G10), W11: фоновая музыка игрового цикла.
//
// Создаётся по образцу AudioSettingsManager — на том же GameObject, что и UIDocument, поэтому
// HubManager и RunFlowController получают один и тот же инстанс. Сцена в проекте одна
// (SampleScene), хаб и забег живут в ней как экраны, так что отдельного переживания загрузки
// сцены не требуется.
//
// Разделение обязанностей:
//   MusicCatalog  — какие файлы играть под контекст (чистая логика);
//   MusicLayerMix — какая громкость у слоя при данном напряжении (чистая логика);
//   MusicMixer    — жизненный цикл: контексты, интенсивность, override (чистая логика);
//   MusicPlayer   — всё, что требует Unity: клипы, AudioSource, dspTime.
//
// Здесь MusicPlayer реализует IMusicOutput, поэтому весь жизненный цикл покрыт EditMode-тестами
// на подставном выходе, а этот класс остаётся тонким.
public class MusicPlayer : MonoBehaviour, IMusicOutput
{
    public static MusicPlayer Instance { get; private set; }

    readonly MusicMixer mixer = new MusicMixer();
    public MusicMixer Mixer => mixer;

    // По источнику на слой. Источники не пересоздаются между контекстами: лишний AddComponent на
    // каждом бою — мусор в сцене и лишняя работа для аудиосистемы.
    readonly List<AudioSource> layerSources = new List<AudioSource>();
    AudioSource overrideSource;

    // Об отсутствующем треке сообщаем один раз на имя: пока дизайнер не написал Hub.wav, каждый
    // заход в деревню иначе засорял бы лог одинаковыми предупреждениями.
    readonly HashSet<string> reportedMissing = new HashSet<string>();

    // Кэш загруженных клипов: Resources.Load дешёвый, но повторный поиск на каждом бою не нужен.
    readonly Dictionary<string, AudioClip> loaded = new Dictionary<string, AudioClip>();

    bool attached;

    public static MusicPlayer GetOrCreate(UIDocument document)
    {
        if (document == null) return Instance;
        if (Instance == null)
        {
            Instance = document.GetComponent<MusicPlayer>();
            if (Instance == null) Instance = document.gameObject.AddComponent<MusicPlayer>();
        }
        Instance.EnsureAttached();
        return Instance;
    }

    void EnsureAttached()
    {
        if (attached) return;
        mixer.Attach(this, HasTrack);
        mixer.SetCategoryVolume(AudioSettingsManager.GetCategoryVolume(AudioCategory.Music));
        attached = true;
    }

    void Awake() => EnsureAttached();

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Музыка не должна замирать на паузе и в замедлении: unscaled.
    void Update() => mixer.Tick(Time.unscaledDeltaTime);

    // ==================== Публичный контракт для игрового кода ====================

    public void PlayHub() => mixer.PlayContext(MusicRequest.Hub());

    // Начало забега/выбор героини: тема героини запускается один раз и играет непрерывно.
    public void PlayRun(string characterId)
    {
        mixer.PlayContext(MusicRequest.Run(characterId));
        mixer.ExitCombat(); // на карте слышен только базовый слой
    }

    // Вход в бой. Для обычного боя контекст совпадает с контекстом забега — трек НЕ
    // перезапускается, поднимается только интенсивность. Бой босса — намеренная смена контекста.
    public void PlayCombat(string characterId, bool isBoss, float encounterBaseIntensity)
    {
        mixer.PlayContext(MusicRequest.Combat(characterId, isBoss));
        mixer.EnterCombat(encounterBaseIntensity);
    }

    public void PlayCombat(string characterId, bool isBoss) =>
        PlayCombat(characterId, isBoss, CombatMusicIntensity.BaseForEncounter(isBoss, false));

    public void SetCombatIntensity(float intensity) => mixer.SetIntensity(intensity);

    // Выход из боя: музыка продолжает играть, слои возвращаются к базовому. Именно это заменило
    // прежний StopMusic() после боя.
    public void ExitCombat() => mixer.ExitCombat();

    // Намеренная тишина (например, экран результатов). Не используется при выходе из боя.
    public void StopMusic() => mixer.PlayContext(MusicRequest.Silent());

    public int BeginOverride(MusicRequest? track = null,
                             float fadeSeconds = MusicMixer.DefaultOverrideFadeSeconds) =>
        mixer.BeginOverride(track, fadeSeconds);

    public void EndOverride(int token) => mixer.EndOverride(token);

    public void ClearOverrides() => mixer.ClearOverrides();

    // Громкость меняется в настройках во время игры — переприменяем её к уже играющим слоям,
    // не трогая ни клипы, ни позицию.
    public void RefreshVolume() =>
        mixer.SetCategoryVolume(AudioSettingsManager.GetCategoryVolume(AudioCategory.Music));

    // Прежнее имя свойства: сейчас это ключ контекста, он же базовое имя трека.
    public string CurrentTrackName => mixer.CurrentContextKey;

    // ==================== IMusicOutput ====================

    public double DspTime => AudioSettings.dspTime;

    public bool PrepareLayers(IReadOnlyList<string> trackNames)
    {
        if (trackNames == null || trackNames.Count == 0) return false;

        // Смена контекста — единственное место, где звук рвётся намеренно.
        StopAllLayers();

        while (layerSources.Count < trackNames.Count) layerSources.Add(CreateSource());

        for (int i = 0; i < trackNames.Count; i++)
        {
            var clip = LoadClip(trackNames[i]);
            if (clip == null) return false;
            layerSources[i].clip = clip;
            layerSources[i].volume = 0f; // до первого ApplyVolumes слой не должен хлопнуть
        }

        // Лишние источники прошлого набора остаются в списке, но без клипа и остановленными.
        for (int i = trackNames.Count; i < layerSources.Count; i++) layerSources[i].clip = null;
        return true;
    }

    public void ScheduleLayer(int index, double dspStartTime)
    {
        if (index < 0 || index >= layerSources.Count) return;
        layerSources[index].PlayScheduled(dspStartTime);
    }

    public void SetLayerVolume(int index, float volume)
    {
        if (index < 0 || index >= layerSources.Count) return;
        layerSources[index].volume = Mathf.Clamp01(volume);
    }

    public void StopAllLayers()
    {
        for (int i = 0; i < layerSources.Count; i++) layerSources[i].Stop();
    }

    public void PlayOverrideTrack(string trackName)
    {
        var clip = LoadClip(trackName);
        if (clip == null) return;
        if (overrideSource == null) overrideSource = CreateSource();
        overrideSource.clip = clip;
        overrideSource.volume = 0f;
        overrideSource.time = 0f;
        overrideSource.Play();
    }

    public void StopOverrideTrack()
    {
        if (overrideSource != null) overrideSource.Stop();
    }

    public void SetOverrideVolume(float volume)
    {
        if (overrideSource != null) overrideSource.volume = Mathf.Clamp01(volume);
    }

    // ==================== Загрузка ====================

    AudioSource CreateSource()
    {
        var created = gameObject.AddComponent<AudioSource>();
        created.playOnAwake = false;
        created.loop = true;
        created.spatialBlend = 0f; // музыка не позиционная
        created.volume = 0f;
        return created;
    }

    AudioClip LoadClip(string trackName) => HasTrack(trackName) ? loaded[trackName] : null;

    bool HasTrack(string trackName)
    {
        if (string.IsNullOrEmpty(trackName)) return false;
        if (loaded.TryGetValue(trackName, out var cached)) return cached != null;

        var clip = Resources.Load<AudioClip>(MusicCatalog.ResourcePath(trackName));
        loaded[trackName] = clip;
        if (clip == null && reportedMissing.Add(trackName))
        {
            // Не ошибка: трека может ещё не быть. Тишина — штатный исход, см. MusicCatalog.
            // Отсутствие ОДНОГО слоя — тоже штатный исход: набор целиком отвергается, и играет
            // контрольный микс (ГДД, «Отказоустойчивость»).
            Debug.Log($"[Music] Трек «{trackName}» не найден в Assets/Resources/{MusicCatalog.ResourceFolder}/ — этот контекст пока без музыки.");
        }
        return clip != null;
    }
}
