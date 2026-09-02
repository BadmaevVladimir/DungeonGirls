using UnityEngine;

// (доп.): диспетчер idle/атака по DisplayName игрока — RunFlowController.Combat.cs дальше не знает,
// у кого какие конкретно кадры, просто спрашивает "есть ли анимация для этого персонажа". Скилл-
// анимации (например SkillBrightStrike Дженифер) остаются завязаны на конкретный skillName в самом
// RunFlowController, т.к. они специфичны для одного навыка, а не общие для персонажа.
public static class PlayableCharacterAnimations
{
    public static Sprite[] Idle(string displayName) => displayName switch
    {
        "Дженифер" => JenniferAnimationFrames.Idle,
        "Саша" => SashaAnimationFrames.Idle,
        "Вайолет" => VioletAnimationFrames.Idle,
        _ => null
    };

    public static Sprite[] Attack(string displayName) => displayName switch
    {
        "Дженифер" => JenniferAnimationFrames.SwordAttack,
        "Саша" => SashaAnimationFrames.AxeAttack,
        "Вайолет" => VioletAnimationFrames.DaggerAttack,
        _ => null
    };

    // (доп.): непрерывная петля ударов — см. OnAttackPerformed в RunFlowController.Combat.cs.
    public static Sprite[] FastAttackLoop(string displayName) => displayName switch
    {
        "Дженифер" => JenniferAnimationFrames.FastAttackLoop,
        "Саша" => SashaAnimationFrames.FastAttackLoop,
        "Вайолет" => VioletAnimationFrames.FastAttackLoop,
        _ => null
    };

    // (доп.): кадр анимации Attack, на котором оружие фактически достаёт цель — до него идёт только
    // замах. Урон считается в модели мгновенно, поэтому без этой задержки цифра урона, тряска и
    // вспышка появлялись на кадре 0, ещё до самого удара (заметнее всего у Саши: кадры 0-4 — это
    // подъём топора, и он приходит вниз только на 5-м). См. OnAttackPerformed в
    // RunFlowController.Combat.cs. 0 = не задерживать фидбек.
    public static int AttackImpactFrame(string displayName) => displayName switch
    {
        "Дженифер" => 5, // выпад мечом вперёд
        "Саша" => 5,     // топор приходит вниз-вправо после замаха над головой
        "Вайолет" => 4,  // выпад кинжалом
        _ => 0
    };
}
