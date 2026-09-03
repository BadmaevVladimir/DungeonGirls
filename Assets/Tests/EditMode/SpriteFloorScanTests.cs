using NUnit.Framework;
using UnityEngine;

public class SpriteFloorScanTests
{
    static Texture2D MakeTexture(int width, int height, System.Func<int, int, bool> isOpaqueAt)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool opaque = isOpaqueAt(x, y);
                pixels[y * width + x] = opaque ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    [Test]
    public void FullyOpaque_ReturnsZero()
    {
        var tex = MakeTexture(8, 8, (x, y) => true);
        Assert.AreEqual(0f, SpriteFloorScan.BottomTransparentFraction(tex), 0.001f);
        Object.DestroyImmediate(tex);
    }

    [Test]
    public void FullyTransparent_ReturnsZero()
    {
        // Полностью прозрачная текстура — безопасный дефолт "не смещать", не должно бросать/зависать.
        var tex = MakeTexture(8, 8, (x, y) => false);
        Assert.AreEqual(0f, SpriteFloorScan.BottomTransparentFraction(tex), 0.001f);
        Object.DestroyImmediate(tex);
    }

    [Test]
    public void OpaqueOnlyInTopHalf_ReturnsHalf()
    {
        // 10 строк, непрозрачны только строки 5-9 (сверху), 0-4 прозрачны — снизу 5 прозрачных строк из 10 = 0.5.
        var tex = MakeTexture(4, 10, (x, y) => y >= 5);
        Assert.AreEqual(0.5f, SpriteFloorScan.BottomTransparentFraction(tex), 0.001f);
        Object.DestroyImmediate(tex);
    }

    [Test]
    public void SinglePixelAtVeryBottom_ReturnsZero()
    {
        var tex = MakeTexture(4, 10, (x, y) => y == 0 && x == 0);
        Assert.AreEqual(0f, SpriteFloorScan.BottomTransparentFraction(tex), 0.001f);
        Object.DestroyImmediate(tex);
    }

    [Test]
    public void AlphaBelowThreshold_TreatedAsTransparent()
    {
        // Строка 0 (низ) — alpha 12 (~4.7%), НИЖЕ порога 5% по умолчанию — должна считаться прозрачной.
        // Строка 3 — alpha 255 (100%), ВЫШЕ порога — первая настоящая непрозрачная строка.
        var tex = new Texture2D(2, 5, TextureFormat.RGBA32, false);
        var pixels = new Color32[10];
        for (int i = 0; i < 10; i++) pixels[i] = new Color32(255, 255, 255, 0);
        pixels[0] = new Color32(255, 255, 255, 12); // y=0, x=0 — alpha ~4.7%, ниже 5%
        pixels[1] = new Color32(255, 255, 255, 12); // y=0, x=1 — то же
        pixels[6] = new Color32(255, 255, 255, 255); // y=3, x=0 — 100%, первая настоящая непрозрачная строка
        pixels[7] = new Color32(255, 255, 255, 255); // y=3, x=1 — то же
        tex.SetPixels32(pixels);
        tex.Apply();

        Assert.AreEqual(0.6f, SpriteFloorScan.BottomTransparentFraction(tex), 0.001f); // 3/5 = 0.6

        Object.DestroyImmediate(tex);
    }
}
