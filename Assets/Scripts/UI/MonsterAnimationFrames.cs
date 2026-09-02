using System.Collections.Generic;
using UnityEngine;

// (доп.): кадры idle/attack-анимаций монстров (сгенерированы через PixelLab MCP, см.
// Assets/Resources/CharacterAnimations/Monster_{Key}/). В отличие от играбельных персонажей
// (JenniferAnimationFrames и т.п.) — один общий класс на все 10 монстров, т.к. у каждого только
// idle + одна атака, без скиллов и петли быстрых атак; ключ кэша — folder key ("Bat",
// "DarkKnight" и т.д.), см. MonsterAnimations для маппинга Russian monsterName → folder key.
public static class MonsterAnimationFrames
{
    static readonly Dictionary<string, Sprite[]> idleCache = new Dictionary<string, Sprite[]>();
    static readonly Dictionary<string, Sprite[]> attackCache = new Dictionary<string, Sprite[]>();

    public static Sprite[] Idle(string folderKey, int count)
    {
        if (!idleCache.TryGetValue(folderKey, out var frames))
        {
            frames = Load(folderKey, "Idle/idle", count);
            idleCache[folderKey] = frames;
        }
        return frames;
    }

    public static Sprite[] Attack(string folderKey, int count)
    {
        if (!attackCache.TryGetValue(folderKey, out var frames))
        {
            frames = Load(folderKey, "Attack/frame", count);
            attackCache[folderKey] = frames;
        }
        return frames;
    }

    static Sprite[] Load(string folderKey, string prefix, int count)
    {
        var frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = Resources.Load<Sprite>($"CharacterAnimations/Monster_{folderKey}/{prefix}_{i}");
        }
        return frames;
    }
}
