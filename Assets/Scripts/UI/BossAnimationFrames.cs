using System.Collections.Generic;
using UnityEngine;

// (доп.): idle/attack/heavy-attack кадры боссов (PixelLab, направление south-east, отзеркалено по
// горизонтали, чтобы босс смотрел на игрока слева — см. Assets/Resources/CharacterAnimations/
// Boss_{folderKey}/{Idle,Attack,Heavy}/). Отдельно от MonsterAnimationFrames (та рассчитана на ровно
// idle+attack без heavy attack и хранит счётчики кадров в самом коде); здесь количество кадров не
// задаётся заранее — Load читает файлы по порядку, пока Resources.Load не вернёт null.
public static class BossAnimationFrames
{
    const int MaxFramesPerClip = 32;

    static readonly Dictionary<string, Sprite[]> idleCache = new Dictionary<string, Sprite[]>();
    static readonly Dictionary<string, Sprite[]> attackCache = new Dictionary<string, Sprite[]>();
    static readonly Dictionary<string, Sprite[]> heavyCache = new Dictionary<string, Sprite[]>();

    public static Sprite[] Idle(string folderKey) => Load(idleCache, folderKey, "Idle/idle");
    public static Sprite[] Attack(string folderKey) => Load(attackCache, folderKey, "Attack/frame");
    public static Sprite[] Heavy(string folderKey) => Load(heavyCache, folderKey, "Heavy/frame");

    static Sprite[] Load(Dictionary<string, Sprite[]> cache, string folderKey, string prefix)
    {
        if (string.IsNullOrEmpty(folderKey))
        {
            return null;
        }

        string cacheKey = folderKey + "/" + prefix;
        if (cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var frames = new List<Sprite>();
        for (int i = 0; i < MaxFramesPerClip; i++)
        {
            var sprite = Resources.Load<Sprite>($"CharacterAnimations/Boss_{folderKey}/{prefix}_{i}");
            if (sprite == null) break;
            frames.Add(sprite);
        }

        var result = frames.ToArray();
        cache[cacheKey] = result;
        return result;
    }
}
