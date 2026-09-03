using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Компенсация "зависания" боевых спрайтов (2026-09-03) — разовый инструмент (запускается через
// -executeMethod SpriteFloorAnalyzer.Run), но ОСТАЁТСЯ в репозитории (в отличие от прошлых one-off
// скриптов в этом проекте) — таблицу нужно перегенерировать при добавлении новых боевых спрайтов.
// См. Docs/superpowers/specs/2026-09-03-combat-sprite-floor-alignment-design.md.
public static class SpriteFloorAnalyzer
{
    const string CharacterAnimationsRoot = "Assets/Resources/CharacterAnimations";
    const string OutputJsonPath = "Assets/Resources/CharacterAnimations/SpriteFloorOffsets.json";

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

    public static void Run()
    {
        var table = new Table();

        foreach (var folderKey in TopLevelFolderKeys())
        {
            float min = MinBottomTransparentFractionInFolder(Path.Combine(CharacterAnimationsRoot, folderKey));
            table.entries.Add(new Entry { key = folderKey, value = min });
            Debug.Log($"[SpriteFloorAnalyzer] {folderKey}: {min:F4}");
        }

        string json = JsonUtility.ToJson(table, true);
        File.WriteAllText(OutputJsonPath, json);
        AssetDatabase.ImportAsset(OutputJsonPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[SpriteFloorAnalyzer] Wrote {table.entries.Count} entries to {OutputJsonPath}");

        RunOnBossKits();

        AssetDatabase.SaveAssets();
        Debug.Log("[SpriteFloorAnalyzer] Done.");
    }

    static IEnumerable<string> TopLevelFolderKeys() =>
        Directory.GetDirectories(CharacterAnimationsRoot).Select(Path.GetFileName).OrderBy(k => k);

    static float MinBottomTransparentFractionInFolder(string folderPath)
    {
        var pngPaths = Directory.GetFiles(folderPath, "*.png", SearchOption.AllDirectories);
        float min = 1f;
        foreach (var pngPath in pngPaths)
        {
            float fraction = BottomTransparentFractionOfFile(pngPath);
            if (fraction < min)
            {
                min = fraction;
            }
        }
        // Папка без PNG (не должно происходить для реальных CharacterAnimations-папок) — 0f,
        // безопасный дефолт "не смещать".
        return pngPaths.Length > 0 ? min : 0f;
    }

    // Декодирует PNG-файл В ПАМЯТИ через LoadImage — работает независимо от Read/Write Enabled в
    // импорте ассета (не трогаем настройки импорта у 200+ существующих файлов), не требует
    // RenderTexture/GPU-контекста.
    static float BottomTransparentFractionOfFile(string relativeOrAbsolutePath)
    {
        string absolutePath = Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(Directory.GetCurrentDirectory(), relativeOrAbsolutePath);
        byte[] bytes = File.ReadAllBytes(absolutePath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(bytes);
        float fraction = SpriteFloorScan.BottomTransparentFraction(texture);
        Object.DestroyImmediate(texture);
        return fraction;
    }

    // ФИКС-ЗАМЕТКА: AssetDatabase.SaveAssets() ниже пересериализует ВЕСЬ BossKitData-ассет, а не
    // только floorPaddingFraction — при повторном запуске этого инструмента ожидайте, что diff
    // покажет переформатирование ВСЕХ текстовых полей (Cyrillic-строки → \uXXXX escape) как побочный
    // эффект. Это безвредно (значение строки не меняется, только YAML-кодировка), не путать с
    // реальным изменением контента.
    static void RunOnBossKits()
    {
        var guids = AssetDatabase.FindAssets("t:BossKitData");
        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var kit = AssetDatabase.LoadAssetAtPath<BossKitData>(assetPath);
            if (kit == null)
            {
                continue;
            }

            foreach (var phase in kit.phases)
            {
                if (phase.phaseSprite == null)
                {
                    continue;
                }

                string spritePath = AssetDatabase.GetAssetPath(phase.phaseSprite.texture);
                float fraction = BottomTransparentFractionOfFile(spritePath);
                phase.floorPaddingFraction = fraction;
                Debug.Log($"[SpriteFloorAnalyzer] {assetPath} / {phase.phaseName}: {fraction:F4}");
            }

            EditorUtility.SetDirty(kit);
        }
    }
}
