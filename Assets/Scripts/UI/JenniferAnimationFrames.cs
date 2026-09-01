using UnityEngine;

// Кадры боевых спрайт-анимаций Дженифер (сгенерированы через PixelLab MCP, см.
// Assets/Resources/CharacterAnimations/Jennifer/). Единственный анимированный персонаж на
// сегодня — у Плута/Варвара ещё нет character-select UI (см. project_rogue_barbarian_classes),
// поэтому без общего API "набор анимаций персонажа" на CharacterData; если появится второй
// анимированный персонаж, стоит обобщить вместо копирования этого класса.
public static class JenniferAnimationFrames
{
    static Sprite[] idle;
    static Sprite[] swordAttack;
    static Sprite[] skillBrightStrike;
    static Sprite[] fastAttackLoop;

    public static Sprite[] Idle => idle ??= Load("Idle/idle", 4);
    public static Sprite[] SwordAttack => swordAttack ??= Load("SwordAttack/frame", 9);
    public static Sprite[] SkillBrightStrike => skillBrightStrike ??= Load("SkillBrightStrike/bright", 11);
    // (доп.): непрерывная петля ударов — включается вместо SwordAttack, когда эффективный интервал
    // атаки короче длительности одного проигрывания SwordAttack (см. RunFlowController.Combat.cs,
    // OnAttackPerformed) — иначе анимация не успевала доиграть до следующего удара.
    public static Sprite[] FastAttackLoop => fastAttackLoop ??= Load("FastAttackLoop/frame", 8);

    static Sprite[] Load(string prefix, int count)
    {
        var frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = Resources.Load<Sprite>($"CharacterAnimations/Jennifer/{prefix}_{i}");
        }
        return frames;
    }
}
