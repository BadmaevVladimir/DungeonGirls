using System.Linq;
using UnityEditor;
using UnityEngine;

public static class NarrativeSetupTool
{
    const string ResourcesFolder = "Assets/Resources";
    const string LibraryPath = ResourcesFolder + "/NarrativeVisualLibrary.asset";

    // Новые фоны/CG могут быть добавлены в проект до первого открытия редактора и ещё не иметь
    // .meta-файлов. После импорта Unity один раз пересобирает библиотеку автоматически, чтобы
    // сцены не требовали ручного запуска меню для получения ссылок на эти спрайты.
    [InitializeOnLoadMethod]
    static void ScheduleVisualLibraryRefresh()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) CreateOrUpdateDefaultVisualLibrary();
        };
    }

    [MenuItem("DungeonGirls/Narrative/Create or Update Default Visual Library")]
    public static void CreateOrUpdateDefaultVisualLibrary()
    {
        AssetDatabase.Refresh();
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
        library.backgrounds = new[]
        {
            CreateVisual("tavern_bg", "Assets/Art/Backgrounds/tavern_bg.png"),
            CreateVisual("Dungeon_dialog", "Assets/Art/Backgrounds/Dungeon_dialog.png"),
            CreateVisual("Hot_springs_bg", "Assets/Art/Backgrounds/Hot_springs_bg.png"),
            CreateVisual("Trap_room_bg", "Assets/Art/Backgrounds/Trap_room_bg.png"),
            CreateVisual("beer_room_bg", "Assets/Art/Backgrounds/beer_room_bg.png")
        };
        library.cgs = new[]
        {
            CreateVisual("Jennifer_01", "Assets/Art/CG Art/Jennifer_01.png"),
            CreateVisual("Jennifer_02", "Assets/Art/CG Art/Jennifer_02.png"),
            CreateVisual("Jennifer_03", "Assets/Art/CG Art/Jennifer_03.png"),
            CreateVisual("Jennifer_04", "Assets/Art/CG Art/Jennifer_04.png"),
            CreateVisual("Jennifer_05", "Assets/Art/CG Art/Jennifer_05.png"),
            CreateVisual("Violet_01", "Assets/Art/CG Art/Violet_01.png"),
            CreateVisual("Violet_02", "Assets/Art/CG Art/Violet_02.png"),
            CreateVisual("Violet_03", "Assets/Art/CG Art/Violet_03.png"),
            CreateVisual("Violet_04", "Assets/Art/CG Art/Violet_04.png"),
            CreateVisual("Violet_05", "Assets/Art/CG Art/Violet_05.png"),
            CreateVisual("Sasha_01", "Assets/Art/CG Art/Sasha_01.png"),
            CreateVisual("Sasha_02", "Assets/Art/CG Art/Sasha_02.png"),
            CreateVisual("Sasha_03", "Assets/Art/CG Art/Sasha_03.png"),
            CreateVisual("Sasha_04", "Assets/Art/CG Art/Sasha_04.png"),
            CreateVisual("Sasha_05", "Assets/Art/CG Art/Sasha_05.png")
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
        var sprite = LoadLargestSprite(assetPath);

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

    static NarrativeVisualLibrary.KeyedSprite CreateVisual(string id, string assetPath)
    {
        var sprite = LoadLargestSprite(assetPath);
        if (sprite == null) Debug.LogWarning($"[VN] No Sprite sub-asset found at {assetPath}.");
        return new NarrativeVisualLibrary.KeyedSprite { id = id, sprite = sprite };
    }

    static Sprite LoadLargestSprite(string assetPath) =>
        AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<Sprite>()
            .OrderByDescending(candidate => candidate.rect.width * candidate.rect.height)
            .FirstOrDefault();
}
