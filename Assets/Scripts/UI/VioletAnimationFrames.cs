using UnityEngine;

// Кадры боевых спрайт-анимаций Вайолет (сгенерированы через PixelLab MCP, см.
// Assets/Resources/CharacterAnimations/Violet/). Тот же паттерн, что и JenniferAnimationFrames.
public static class VioletAnimationFrames
{
    static Sprite[] idle;
    static Sprite[] daggerAttack;
    static Sprite[] fastAttackLoop;

    public static Sprite[] Idle => idle ??= Load("Idle/idle", 4);
    public static Sprite[] DaggerAttack => daggerAttack ??= Load("DaggerAttack/frame", 9);
    public static Sprite[] FastAttackLoop => fastAttackLoop ??= Load("FastAttackLoop/frame", 8);

    static Sprite[] Load(string prefix, int count)
    {
        var frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = Resources.Load<Sprite>($"CharacterAnimations/Violet/{prefix}_{i}");
        }
        return frames;
    }
}
