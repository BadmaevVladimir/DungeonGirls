using System.Collections.Generic;
using UnityEngine;

// Boss framework (минимальный слайс): когда что делает.
public enum BossAbilityTriggerKind
{
    // Срабатывает один раз, сразу при входе в текущую фазу (в т.ч. фазу 0 = старт боя).
    OnCombatStart,
    // Срабатывает многократно на собственном кулдауне, независимо от таймера атаки оружием.
    Periodic
}

// Boss framework: какую именно механику выполняет способность при срабатывании. Закрытый список —
// добавлять новый effectKind только когда реально появляется механика, которую нельзя выразить
// существующими (см. Docs/Design/2026-09-01-floor-boss-system-design.md, раздел 5).
public enum BossAbilityEffectKind
{
    // Телеграфируемая тяжёлая атака: ResolveAttack по первому оружию босса с множителем урона.
    HeavyAttack,
    // Выдаёт/обновляет отдельный shield pool (CombatantRuntime.ShieldPoolCurrent/Max), поглощающий
    // входящий урон ДО HP, независимо от типа урона (см. DamageCalculator.ApplyDamage).
    ShieldPool
}

[System.Serializable]
public class BossAbilityConfig
{
    [Tooltip("Текст телеграфа/баннера активации — то, что видит игрок.")]
    public string displayName = "Способность";

    public BossAbilityEffectKind effectKind = BossAbilityEffectKind.HeavyAttack;
    public BossAbilityTriggerKind triggerKind = BossAbilityTriggerKind.Periodic;

    [Tooltip("Только Periodic: интервал между срабатываниями.")]
    public float cooldownSeconds = 10f;

    [Tooltip("Только Periodic: задержка до ПЕРВОГО срабатывания после входа в фазу — не даёт всем " +
        "периодическим способностям сработать одновременно в первый же кадр фазы.")]
    public float initialDelaySeconds = 3f;

    [Tooltip("0 = срабатывает мгновенно. >0 = сперва телеграф на N секунд (см. BossEncounterState." +
        "PendingTelegraph), способность резолвится по истечении.")]
    public float telegraphSeconds = 0f;

    [Tooltip("HeavyAttack: множитель урона обычной атаки босса (1.5 = 150%).")]
    public float damageMultiplier = 1.5f;

    [Tooltip("ShieldPool: величина выдаваемого щита (ShieldPoolMax=ShieldPoolCurrent=это значение).")]
    public float shieldAmount = 0f;

    [Tooltip("ShieldPool: 0 = щит живёт, пока не поглотит весь урон (бессрочно); >0 = принудительно " +
        "спадает через N секунд, даже если не выбит уроном.")]
    public float shieldDurationSeconds = 0f;
}

[System.Serializable]
public class BossPhaseData
{
    [Tooltip("Для лога/баннера смены фазы, не показывается как отдельный статус-эффект.")]
    public string phaseName = "Фаза 1";

    [Tooltip("Фаза активируется, когда HP% босса опускается НИЖЕ ИЛИ РАВНО этому значению. Первая " +
        "фаза (index 0) должна оставаться на 100 — она активна с начала боя безусловно.")]
    [Range(0f, 100f)]
    public float hpThresholdPercent = 100f;

    [Tooltip("Спрайт босса на время этой фазы. null = спрайт не меняется при входе в фазу.")]
    public Sprite phaseSprite;

    public List<BossAbilityConfig> abilities = new List<BossAbilityConfig>();
}

// Boss framework (минимальный слайс, см. Docs/Design/2026-09-01-floor-boss-system-design.md):
// опциональный компаньон MonsterData (MonsterData.bossKit) — описывает фазы/способности/телеграфы/
// спрайты уникального босса данными, без bespoke-кода на каждого нового босса. Монстр без bossKit
// (isBoss=true, bossKit=null) продолжает работать через старую CombatManager.TickBossHeavyAttacks —
// см. CombatantFactory.CreateMonsterCombatant.
[CreateAssetMenu(fileName = "NewBossKit", menuName = "DungeonGirls/Boss Kit")]
public class BossKitData : ScriptableObject
{
    [Tooltip("Минимум одна фаза. phases[0].hpThresholdPercent должен быть 100 (активна с начала боя).")]
    public List<BossPhaseData> phases = new List<BossPhaseData>();
}
