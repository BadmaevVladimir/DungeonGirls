using System.Collections.Generic;
using UnityEngine;

// Боевые оверлеи: одна общая библиотека эффектов поверх спрайта бойца. Кадры лежат в
// Resources/VFX/Effect_{Kind}/frame_N.png и нарисованы в градациях серого — цвет задаёт tint
// отсюда, поэтому один набор кадров обслуживает и яд, и клеймо, и любой будущий эффект того же
// силуэта. Загрузчик тот же по устройству, что BossAnimationFrames: читает кадры по порядку,
// пока Resources.Load не вернёт null, число кадров нигде не прописано.
//
// Эффекты НЕ привязаны к боссам: ForStatus сопоставляет подписи из CombatantStatusEffects, а они
// одинаковы у игрока, обычных монстров и боссов. ForAbility добавляет к этому разовые эффекты
// боссовых способностей, у которых нет своего статуса (призыв, открытая защита).
public enum CombatVfxKind
{
    Impact,
    Shield,
    Rage,
    Summon,
    Chains,
    Frost,
    Opening,
    Debuff
}

public static class CombatVfx
{
    const int MaxFramesPerEffect = 32;

    static readonly Dictionary<CombatVfxKind, Sprite[]> cache = new Dictionary<CombatVfxKind, Sprite[]>();

    public static Sprite[] Frames(CombatVfxKind kind)
    {
        if (cache.TryGetValue(kind, out var cached))
        {
            return cached;
        }

        var frames = new List<Sprite>();
        for (int i = 0; i < MaxFramesPerEffect; i++)
        {
            var sprite = Resources.Load<Sprite>($"VFX/Effect_{kind}/frame_{i}");
            if (sprite == null) break;
            frames.Add(sprite);
        }

        var result = frames.ToArray();
        cache[kind] = result;
        return result;
    }

    // Разовый эффект в момент, когда способность босса резолвится. Только для того, что не
    // выражено статусом: щит и заморозка и так висят как статус и покажутся через ForStatus,
    // поэтому дублировать их вспышкой не нужно — исключение сделано для удара, у него статуса нет.
    public static CombatVfxKind? ForAbility(BossAbilityEffectKind kind) => kind switch
    {
        BossAbilityEffectKind.HeavyAttack => CombatVfxKind.Impact,
        BossAbilityEffectKind.SelfDamage => CombatVfxKind.Impact,
        BossAbilityEffectKind.SpawnMinions => CombatVfxKind.Summon,
        BossAbilityEffectKind.ConsumeMinion => CombatVfxKind.Summon,
        BossAbilityEffectKind.ReviveAnchor => CombatVfxKind.Summon,
        BossAbilityEffectKind.SpawnDefeatedBoss => CombatVfxKind.Summon,
        BossAbilityEffectKind.DisruptSkills => CombatVfxKind.Chains,
        BossAbilityEffectKind.SlotDisable => CombatVfxKind.Chains,
        BossAbilityEffectKind.DamageTakenBuff => CombatVfxKind.Opening,
        // RoomTick намеренно без эффекта: у него нет момента срабатывания, это фоновый урон
        // по комнате. Остальное (щит, ярость, заморозка, дебаффы) висит статусом и идёт
        // через ForStatus, иначе вспышка и удерживаемый оверлей наложились бы друг на друга.
        _ => null
    };

    // Удерживаемый эффект, пока статус висит на бойце. Подписи приходят из
    // CombatantStatusEffects.GetActiveEffects и часто содержат счётчик ("Заморозка ×3") или
    // число ("Барьер 40/40"), поэтому сопоставление идёт по началу строки.
    public static CombatVfxKind? ForStatus(string label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return null;
        }

        // Префикс намеренно обрывается до седьмой буквы: подписи расходятся ровно там —
        // «Заморо-жен» против «Заморо-зка ×3». Более длинный префикс ловит только одну из двух.
        if (label.StartsWith("Заморо")) return CombatVfxKind.Frost;
        if (label.StartsWith("Барьер")) return CombatVfxKind.Shield;
        if (label.StartsWith("Берсерк")) return CombatVfxKind.Rage;
        if (label.StartsWith("Оглушающий крик")) return CombatVfxKind.Debuff;
        if (label.StartsWith("Проклятие замедления")) return CombatVfxKind.Debuff;
        if (label.StartsWith("Запугивание")) return CombatVfxKind.Debuff;
        if (label.StartsWith("Скорость атаки снижена")) return CombatVfxKind.Debuff;
        if (label.StartsWith("Урон снижен")) return CombatVfxKind.Debuff;
        return null;
    }

    public static Color Tint(CombatVfxKind kind) => kind switch
    {
        CombatVfxKind.Impact => new Color(1f, 1f, 1f),
        CombatVfxKind.Shield => new Color(0.47f, 0.78f, 1f),
        CombatVfxKind.Rage => new Color(1f, 0.47f, 0.27f),
        CombatVfxKind.Summon => new Color(0.75f, 0.55f, 1f),
        CombatVfxKind.Chains => new Color(0.80f, 0.86f, 0.94f),
        CombatVfxKind.Frost => new Color(0.59f, 0.84f, 1f),
        CombatVfxKind.Opening => new Color(1f, 0.84f, 0.31f),
        CombatVfxKind.Debuff => new Color(0.78f, 0.59f, 1f),
        _ => Color.white
    };

    // Размещение оверлея в рамке бойца. Доля кадра важнее, чем кажется: барьер, нарисованный
    // на 70% рамки, оказывается ВНУТРИ силуэта и читается не как защита, а как краска на
    // фигуре — обволакивающие эффекты обязаны быть шире рамки.
    public struct Layout
    {
        public float SizePercent;
        public float TopPercent;
        public float Opacity;
        public float Fps;
        public bool Loop;
    }

    public static Layout LayoutFor(CombatVfxKind kind) => kind switch
    {
        CombatVfxKind.Impact => new Layout { SizePercent = 70f, TopPercent = 15f, Opacity = 0.92f, Fps = 14f, Loop = false },
        CombatVfxKind.Shield => new Layout { SizePercent = 115f, TopPercent = -0.5f, Opacity = 0.60f, Fps = 6f, Loop = true },
        CombatVfxKind.Rage => new Layout { SizePercent = 100f, TopPercent = -1f, Opacity = 0.80f, Fps = 8f, Loop = true },
        CombatVfxKind.Summon => new Layout { SizePercent = 100f, TopPercent = 0f, Opacity = 0.78f, Fps = 12f, Loop = false },
        CombatVfxKind.Chains => new Layout { SizePercent = 95f, TopPercent = 2.5f, Opacity = 0.82f, Fps = 12f, Loop = false },
        CombatVfxKind.Frost => new Layout { SizePercent = 95f, TopPercent = 2.5f, Opacity = 0.78f, Fps = 6f, Loop = true },
        CombatVfxKind.Opening => new Layout { SizePercent = 85f, TopPercent = 1.5f, Opacity = 0.85f, Fps = 8f, Loop = true },
        CombatVfxKind.Debuff => new Layout { SizePercent = 78f, TopPercent = 21f, Opacity = 0.80f, Fps = 8f, Loop = true },
        _ => new Layout { SizePercent = 80f, TopPercent = 10f, Opacity = 0.8f, Fps = 8f, Loop = false }
    };
}
