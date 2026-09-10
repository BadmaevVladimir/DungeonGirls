using System.Collections.Generic;

// Размеры спрайтов на боевой сцене. Вынесено из RunFlowController.Combat.cs в чистую логику
// (2026-09-11), потому что старое правило «босс = 518px» опиралось на допущение «бой с боссом
// всегда 1 на 1», а групповые боссы это допущение сломали: Тени-Близнецы ставят на сцену две
// сущности с китом, Свечник — себя плюс три свечи.
//
// UI Toolkit Image вписывает кадр ЦЕЛИКОМ в рамку, поэтому рамка здесь задаёт видимый размер
// персонажа, а не обрезку. Отсюда и правило: чем больше тел на сцене, тем меньше рамка.
public static class BossStageLayout
{
    // Обычный монстр. Игрок занимает 384px, рядовой враг заметно меньше него.
    public const float RegularEnemySize = 260f;

    // Одиночный босс — крупнее игрока, чтобы читаться как главный противник комнаты.
    public const float SoloBossSize = 518f;

    // Босс в связке из нескольких равных сущностей (Тени-Близнецы). Двое по 518 не помещаются
    // рядом и читаются как два отдельных босса, а не как одна связка.
    public const float PairedBossSize = 340f;

    // Спутник-якорь (Свечи Свечника). Он намеренно мелкий: якорь — это цель для клика, а не
    // участник схватки, и он не должен спорить за внимание с самим боссом.
    public const float AnchorSize = 150f;

    // stage — все враги боя. Размер зависит от состава сцены, поэтому считается по списку целиком,
    // а не по одному участнику.
    public static float SpriteSize(CombatantRuntime enemy, IReadOnlyList<CombatantRuntime> stage)
    {
        if (enemy == null)
        {
            return RegularEnemySize;
        }

        // Якорь проверяется ПЕРВЫМ: свеча не имеет кита, но и обычным монстром по размеру не является.
        if (enemy.IsBossAnchor)
        {
            return AnchorSize;
        }

        if (enemy.BossEncounter == null)
        {
            return RegularEnemySize;
        }

        return CountBossEntities(stage) >= 2 ? PairedBossSize : SoloBossSize;
    }

    // Считаем только носителей кита: свечи Свечника китов не имеют, поэтому он остаётся
    // «одиночным» боссом обычного размера, а место на сцене занимают мелкие якоря.
    static int CountBossEntities(IReadOnlyList<CombatantRuntime> stage)
    {
        if (stage == null)
        {
            return 1;
        }

        int count = 0;
        for (int i = 0; i < stage.Count; i++)
        {
            if (stage[i] != null && stage[i].BossEncounter != null) count++;
        }

        return count;
    }
}
