using System.Collections.Generic;
using UnityEngine;

// Компенсация "зависания" боевых спрайтов (2026-09-03) — рантайм-сторона таблицы, сгенерированной
// Assets/Editor/SpriteFloorAnalyzer.cs (см. SpriteFloorScan.cs). Один JSON на все анимированные
// (Resources-загружаемые) кадры персонажей/монстров — боссы используют отдельное поле прямо на
// BossPhaseData.floorPaddingFraction (их спрайты не в Resources/, см. CombatSpriteFloorOffset).
public static class SpriteFloorOffsets
{
    const string ResourcePath = "CharacterAnimations/SpriteFloorOffsets";

    [System.Serializable]
    class Entry
    {
        public string key;
        public float value;
    }

    [System.Serializable]
    class Table
    {
        public List<Entry> entries = new List<Entry>();
    }

    static Dictionary<string, float> cachedTable;

    public static float GetOffsetFraction(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return 0f;
        }

        cachedTable ??= Load();
        return cachedTable.TryGetValue(key, out var value) ? value : 0f;
    }

    static Dictionary<string, float> Load()
    {
        var textAsset = Resources.Load<TextAsset>(ResourcePath);
        // Таблица ещё не сгенерирована анализатором (или ассет не существует) — безопасный дефолт:
        // пустая таблица, GetOffsetFraction вернёт 0f для всех ключей (текущее поведение, без регрессии).
        return textAsset != null ? ParseTable(textAsset.text) : new Dictionary<string, float>();
    }

    public static Dictionary<string, float> ParseTable(string json)
    {
        var table = JsonUtility.FromJson<Table>(json) ?? new Table();
        var result = new Dictionary<string, float>();
        foreach (var entry in table.entries)
        {
            result[entry.key] = entry.value;
        }
        return result;
    }
}
