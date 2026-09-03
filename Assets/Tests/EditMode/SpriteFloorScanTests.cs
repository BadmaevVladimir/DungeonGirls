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
        // 10 строк, непрозрачны только строки 0-4 (сверху), 5-9 прозрачны — снизу 5 прозрачных строк из 10 = 0.5.
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
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color32[16];
        for (int i = 0; i < 16; i++) pixels[i] = new Color32(255, 255, 255, 0);
        // Одна почти-прозрачная строка (alpha 2%) в самом низу — ниже дефолтного порога 5%, должна игнорироваться.
        pixels[0] = new Color32(255, 255, 255, 5); // y=0 (низ), x=0 — alpha ~2%
        var tex2 = MakeTexture(4, 4, (x, y) => y >= 2); // непрозрачные (100%) строки 2-3, строки 0-1 прозрачные
        Assert.AreEqual(0.5f, SpriteFloorScan.BottomTransparentFraction(tex2), 0.001f);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(tex2);
    }
}
