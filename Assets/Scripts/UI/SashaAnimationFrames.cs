using UnityEngine;

// Кадры боевых спрайт-анимаций Саши (сгенерированы через PixelLab MCP, см.
// Assets/Resources/CharacterAnimations/Sasha/). Тот же паттерн, что и JenniferAnimationFrames.
public static class SashaAnimationFrames
{
    static Sprite[] idle;
    static Sprite[] axeAttack;
    static Sprite[] fastAttackLoop;

    public static Sprite[] Idle => idle ??= Load("Idle/idle", 4);
    public static Sprite[] AxeAttack => axeAttack ??= Load("AxeAttack/frame", 9);
    public static Sprite[] FastAttackLoop => fastAttackLoop ??= Load("FastAttackLoop/frame", 8);

    static Sprite[] Load(string prefix, int count)
    {
        var frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = Resources.Load<Sprite>($"CharacterAnimations/Sasha/{prefix}_{i}");
        }
        return frames;
    }
}
