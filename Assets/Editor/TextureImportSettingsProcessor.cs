using UnityEditor;
using UnityEngine;

// 10.6: принудительно применяет обязательные настройки импорта дизайнера ко ВСЕМ текстурам под
// Assets/Art/ — на импорте и на любом реимпорте, чтобы будущие добавленные дизайнером файлы не
// требовали ручной настройки каждый раз.
public class TextureImportSettingsProcessor : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith("Assets/Art/"))
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
