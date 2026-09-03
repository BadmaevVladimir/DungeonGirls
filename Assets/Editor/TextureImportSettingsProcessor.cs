using UnityEditor;
using UnityEngine;

// 10.6: принудительно применяет обязательные настройки импорта дизайнера ко ВСЕМ текстурам под
// Assets/Art/ — на импорте и на любом реимпорте, чтобы будущие добавленные дизайнером файлы не
// требовали ручной настройки каждый раз.
//
// Также покрывает Assets/Resources/UI/ и Assets/Resources/Icons/ — те же настройки (Point-фильтр,
// Sprite, без сжатия) нужны спрайтам, загружаемым через Resources.Load<Sprite> (карта деревни,
// фоны экранов Таверны/Кузницы, иконки ингредиентов/материалов), а не только предметам под Art/.
public class TextureImportSettingsProcessor : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.StartsWith("Assets/Art/") && !path.StartsWith("Assets/Resources/UI/") &&
            !path.StartsWith("Assets/Resources/Icons/"))
        {
            return;
        }

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.spritePixelsPerUnit = 64;
    }
}
