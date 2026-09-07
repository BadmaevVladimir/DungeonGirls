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
// Правила подбора трека вынесены в MusicCatalog (чистая логика, покрыта EditMode-тестами).
// Здесь остаётся только работа с Unity: загрузка клипа, один AudioSource, отсутствие наложения.
public class MusicPlayer : MonoBehaviour
{
    public static MusicPlayer Instance { get; private set; }

    AudioSource source;

    // Имя ИГРАЮЩЕГО сейчас трека (null = тишина). Сравнение с ним не даёт перезапускать музыку на
    // каждом обновлении экрана — именно перезапуск слышен как рывок и наложение.
    string currentTrackName;
    public string CurrentTrackName => currentTrackName;

    // Об отсутствующем треке сообщаем один раз на имя: пока дизайнер не написал Hub.wav, каждый
    // заход в деревню иначе засорял бы лог одинаковыми предупреждениями.
    readonly HashSet<string> reportedMissing = new HashSet<string>();

    // Кэш загруженных клипов: Resources.Load дешёвый, но повторный поиск на каждом бою не нужен.
    readonly Dictionary<string, AudioClip> loaded = new Dictionary<string, AudioClip>();

    public static MusicPlayer GetOrCreate(UIDocument document)
    {
        if (document == null) return Instance;
        if (Instance == null)
        {
            Instance = document.GetComponent<MusicPlayer>();
            if (Instance == null) Instance = document.gameObject.AddComponent<MusicPlayer>();
        }
        Instance.EnsureSource();
        return Instance;
    }

    void EnsureSource()
    {
        if (source != null) return;
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f; // музыка не позиционная
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void PlayHub() => Play(MusicRequest.Hub());

    public void PlayCombat(string characterId, bool isBoss) => Play(MusicRequest.Combat(characterId, isBoss));

    public void StopMusic() => Play(MusicRequest.Silent());

    public void Play(MusicRequest request)
    {
        EnsureSource();
        string nextTrack = MusicCatalog.ResolveTrackName(request, HasTrack);
        if (!MusicCatalog.ShouldSwitch(currentTrackName, nextTrack)) return;

        currentTrackName = nextTrack;
        if (nextTrack == null)
        {
            source.Stop();
            source.clip = null;
            return;
        }

        // Останавливаем явно перед сменой: без этого смена клипа у играющего источника даёт
        // слышимый стык, а при быстрых переходах — наложение хвоста предыдущего трека.
        source.Stop();
        TaggedAudio.Play(source, loaded[nextTrack], AudioCategory.Music);
        source.loop = true;
    }

    // Громкость меняется в настройках во время игры, а Play() уже отыграл — переприменяем
    // категорийную громкость к текущему треку.
    public void RefreshVolume()
    {
        if (source == null || currentTrackName == null) return;
        source.volume = AudioSettingsManager.GetCategoryVolume(AudioCategory.Music);
    }

    bool HasTrack(string trackName)
    {
        if (string.IsNullOrEmpty(trackName)) return false;
        if (loaded.TryGetValue(trackName, out var cached)) return cached != null;

        var clip = Resources.Load<AudioClip>(MusicCatalog.ResourcePath(trackName));
        loaded[trackName] = clip;
        if (clip == null && reportedMissing.Add(trackName))
        {
            // Не ошибка: трека может ещё не быть. Тишина — штатный исход, см. MusicCatalog.
            Debug.Log($"[Music] Трек «{trackName}» не найден в Assets/Resources/{MusicCatalog.ResourceFolder}/ — этот контекст пока без музыки.");
        }
        return clip != null;
    }
}
