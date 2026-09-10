using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

// Размер босса не должен "прыгать" при смене клипа анимации (2026-09-10). UI Toolkit Image
// вписывает КАДР ЦЕЛИКОМ в рамку фиксированного размера (см. BuildEnemyStageEntries,
// bossSpriteSize), поэтому видимый масштаб персонажа задаётся не его собственными пикселями,
// а размером холста PNG: тот же силуэт на холсте 132x132 отрисуется на 27% мельче, чем на 96x96.
//
// Кадры боссов (в отличие от обычных монстров, у которых PixelLab-кадры обрезаны по контенту и
// холст поэтому гуляет от кадра к кадру) — это позы на общем холсте с прозрачными полями, так что
// инвариант простой: внутри одной папки Boss_* холст у всех кадров Idle/Attack/Heavy одинаковый.
public class BossAnimationCanvasTests
{
    const string CharacterAnimationsRoot = "Assets/Resources/CharacterAnimations";

    // Читает ширину/высоту прямо из IHDR-чанка PNG — без Texture2D/GPU и без требований к
    // настройкам импорта ассета.
    static (int width, int height) ReadPngSize(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        reader.ReadBytes(16); // 8 байт сигнатуры + 4 длина чанка + 4 "IHDR"
        int width = System.BitConverter.ToInt32(reader.ReadBytes(4).Reverse().ToArray(), 0);
        int height = System.BitConverter.ToInt32(reader.ReadBytes(4).Reverse().ToArray(), 0);
        return (width, height);
    }

    static IEnumerable<string> BossFolders() =>
        Directory.GetDirectories(CharacterAnimationsRoot, "Boss_*").OrderBy(p => p);

    [Test]
    public void BossFoldersExist()
    {
        Assert.IsNotEmpty(BossFolders().ToList(), "Ожидались папки анимаций боссов Boss_* — тест ниже иначе всегда зелёный.");
    }

    [Test]
    public void AllFramesOfABossShareOneCanvasSize()
    {
        foreach (string folder in BossFolders())
        {
            var frames = Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories).OrderBy(p => p).ToList();
            Assert.IsNotEmpty(frames, $"{folder}: нет ни одного PNG-кадра.");

            var sizes = frames.ToDictionary(p => p, ReadPngSize);
            var distinct = sizes.Values.Distinct().ToList();
            string detail = string.Join("\n  ", sizes.Select(kv => $"{Path.GetFileName(Path.GetDirectoryName(kv.Key))}/{Path.GetFileName(kv.Key)} = {kv.Value.width}x{kv.Value.height}"));
            Assert.AreEqual(1, distinct.Count, $"{Path.GetFileName(folder)}: кадры на холстах разного размера — босс будет менять видимый размер при смене клипа.\n  {detail}");
        }
    }

    [Test]
    public void AllPhasesOfOneBossKitShareOneCanvasSize()
    {
        // Смена фазы посреди боя перезагружает кадры (см. UpdateCombatUI), но НЕ пересобирает рамку
        // спрайта — её размер выставлен один раз в BuildEnemyStageEntries. Значит разный холст у фаз
        // одного и того же босса означал бы, что он скачком меняет размер при переходе в фазу 2.
        var guids = AssetDatabase.FindAssets("t:BossKitData");
        Assert.IsNotEmpty(guids, "Не найдено ни одного BossKitData — тест иначе всегда зелёный.");

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var kit = AssetDatabase.LoadAssetAtPath<BossKitData>(assetPath);
            if (kit == null)
            {
                continue;
            }

            var perPhase = new List<string>();
            var sizes = new HashSet<(int, int)>();
            foreach (var phase in kit.phases)
            {
                string folder = Path.Combine(CharacterAnimationsRoot, "Boss_" + phase.animationFolderKey);
                if (string.IsNullOrEmpty(phase.animationFolderKey) || !Directory.Exists(folder))
                {
                    // Фаза без PixelLab-анимации показывает статичный phaseSprite — не наш случай.
                    continue;
                }

                var size = ReadPngSize(Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories).OrderBy(p => p).First());
                sizes.Add(size);
                perPhase.Add($"{phase.animationFolderKey} = {size.width}x{size.height}");
            }

            if (perPhase.Count > 1)
            {
                Assert.AreEqual(1, sizes.Count, $"{Path.GetFileName(assetPath)}: фазы на холстах разного размера — босс сменит размер при переходе фазы: {string.Join(" | ", perPhase)}");
            }
        }
    }
}
