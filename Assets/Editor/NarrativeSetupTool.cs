using System.Linq;
using UnityEditor;
using UnityEngine;

public static class NarrativeSetupTool
{
    const string ResourcesFolder = "Assets/Resources";
    const string LibraryPath = ResourcesFolder + "/NarrativeVisualLibrary.asset";

    [MenuItem("DungeonGirls/Narrative/Create or Update Default Visual Library")]
    public static void CreateOrUpdateDefaultVisualLibrary()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        var library = AssetDatabase.LoadAssetAtPath<NarrativeVisualLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<NarrativeVisualLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        library.characters = new[]
        {
            CreateCharacter("jennifer", "Assets/Art/Characters/Dialog_sprites/Jennifer_Dialog.png"),
            CreateCharacter("sasha", "Assets/Art/Characters/Dialog_sprites/Sasha_Dialog.png"),
            CreateCharacter("violet", "Assets/Art/Characters/Dialog_sprites/Violet_Dialog.png")
        };

        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[VN] Default visual library written to {LibraryPath}.");
    }

    static NarrativeVisualLibrary.CharacterVisuals CreateCharacter(string characterId, string assetPath)
    {
        // Jennifer currently has three accidental tiny sprite slices in addition to the real portrait,
        // so select the largest imported sprite instead of relying on sub-asset order.
        var sprite = AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<Sprite>()
            .OrderByDescending(candidate => candidate.rect.width * candidate.rect.height)
            .FirstOrDefault();

        if (sprite == null)
        {
            Debug.LogWarning($"[VN] No Sprite sub-asset found at {assetPath}.");
        }

        return new NarrativeVisualLibrary.CharacterVisuals
        {
            characterId = characterId,
            portraits = new[]
            {
                new NarrativeVisualLibrary.Portrait { emotion = "neutral", sprite = sprite }
            }
        };
    }
}
