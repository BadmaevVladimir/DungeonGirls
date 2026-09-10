using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

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
}
