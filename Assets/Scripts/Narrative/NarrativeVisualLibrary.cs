using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NarrativeVisualLibrary", menuName = "DungeonGirls/Narrative/Visual Library")]
public class NarrativeVisualLibrary : ScriptableObject
{
    [Serializable]
    public class Portrait
    {
        public string emotion = "neutral";
        public Sprite sprite;
    }

    [Serializable]
    public class CharacterVisuals
    {
        public string characterId;
        public Portrait[] portraits = Array.Empty<Portrait>();
    }

    [Serializable]
    public class KeyedSprite
    {
        public string id;
        public Sprite sprite;
    }

    public CharacterVisuals[] characters = Array.Empty<CharacterVisuals>();
    public KeyedSprite[] backgrounds = Array.Empty<KeyedSprite>();
    public KeyedSprite[] cgs = Array.Empty<KeyedSprite>();

    public bool TryGetPortrait(string characterId, string emotion, out Sprite sprite)
    {
        sprite = null;
        if (characters == null) return false;

        foreach (var character in characters)
        {
            if (character == null || !string.Equals(character.characterId, characterId, StringComparison.OrdinalIgnoreCase)) continue;

            Sprite neutral = null;
            Sprite first = null;
            if (character.portraits != null)
            {
                foreach (var portrait in character.portraits)
                {
                    if (portrait == null || portrait.sprite == null) continue;
                    if (first == null) first = portrait.sprite;
                    if (string.Equals(portrait.emotion, "neutral", StringComparison.OrdinalIgnoreCase)) neutral = portrait.sprite;
                    if (!string.IsNullOrWhiteSpace(emotion) && string.Equals(portrait.emotion, emotion, StringComparison.OrdinalIgnoreCase))
                    {
                        sprite = portrait.sprite;
                        return true;
                    }
                }
            }

            sprite = neutral != null ? neutral : first;
            return sprite != null;
        }

        return false;
    }

    public bool TryGetBackground(string id, out Sprite sprite) => TryGetKeyedSprite(backgrounds, id, out sprite);
    public bool TryGetCg(string id, out Sprite sprite) => TryGetKeyedSprite(cgs, id, out sprite);

    static bool TryGetKeyedSprite(KeyedSprite[] entries, string id, out Sprite sprite)
    {
        sprite = null;
        if (entries == null || string.IsNullOrWhiteSpace(id)) return false;
        foreach (var entry in entries)
        {
            if (entry != null && entry.sprite != null && string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
            {
                sprite = entry.sprite;
                return true;
            }
        }
        return false;
    }
}
