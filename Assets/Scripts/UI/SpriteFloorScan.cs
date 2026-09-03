using UnityEngine;

// Компенсация "зависания" боевых спрайтов (2026-09-03): каждый PNG-кадр анимации персонажа/монстра
// может иметь свой прозрачный отступ снизу холста — UI Toolkit Image всегда центрирует картинку в
// рамке (ни ScaleToFit, ни ScaleAndCrop не прижимают к нижнему краю), поэтому даже когда сама рамка
// стоит на правильной линии пола (см. RunFlowController.Combat.cs ComputeStageFloorGap), видимые
// "ноги" персонажа внутри неё до линии пола не доходят. Эта функция — чистая математика: сколько
// пустоты снизу текстуры. Используется офлайн-анализатором (Assets/Editor/SpriteFloorAnalyzer.cs),
// сама по себе ничего не читает с диска и не требует Read/Write Enabled на текстуре — вызывающая
// сторона обязана передать уже CPU-читаемую текстуру (см. комментарий в SpriteFloorAnalyzer о том,
// как это делается для реальных PNG-ассетов без изменения их настроек импорта).
public static class SpriteFloorScan
{
    public static float BottomTransparentFraction(Texture2D texture, float alphaThreshold = 0.05f)
    {
        int width = texture.width;
        int height = texture.height;
        var pixels = texture.GetPixels32();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a / 255f > alphaThreshold)
                {
                    return (float)y / height;
                }
            }
        }

        // Полностью прозрачная текстура — не должно встречаться в реальных ассетах, но безопасный
        // дефолт "не смещать" лучше, чем деление на некорректное значение или исключение.
        return 0f;
    }
}
