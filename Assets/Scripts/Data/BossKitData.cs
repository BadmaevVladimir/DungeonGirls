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
    ShieldPool,
    // Блокирует активный навык игрока на disruptSeconds (см. CombatManager.DisruptPlayerActiveSkills):
    // Cooldown-навыку добавляются секунды к кулдауну, Toggle гасится и сам возвращается после снятия.
    // Единственная механика роспиcи боссов, целящаяся не в HP игрока, а в его единственный рычаг в
    // автобое, поэтому длительность жёстко ограничена сверху — см. CombatManager.MaxBossSkillDisruptSeconds.
    DisruptSkills,
    // Временно повышает получаемый боссом урон (CombatantRuntime.DamageTakenBonusPercent) —
    // «открытая грудь». Единственный способ выразить в автобое окно, в которое игроку выгодно
    // нажать активный навык: окно даёт БОНУС К УРОНУ, а не требует защиты, поэтому им может
    // воспользоваться и Дженифер, у которой навык атакующий (см. спеку, раздел 0.4, правило 6).
    DamageTakenBuff,
    // Воскрешает одного мёртвого спутника-якоря на полное HP (Свечник заново зажигает свечу).
    // Если мёртвых якорей нет — способность просто ничего не делает и уходит на кулдаун.
    ReviveAnchor,
    // Выдаёт игроку сразу несколько зарядов Заморозки (существующая система: 10 зарядов = заморозка
    // на 5с, затем иммунитет). По одному заряду за каст босс копил бы их до конца боя, поэтому
    // количество задаётся ассетом. Иммунитет и «Упёртость» Саши уважаются, как и при заморозке от игрока.
    ApplyFreeze,
    // Вешает на игрока временный дебафф скорости атаки (тот же ActiveDebuff, что «Запугивание»
    // и замедление Колдуна) — в автобое это чистое замедление гонки, реагировать на него нечем.
    AttackSpeedDebuff,
    // Босс бьёт САМ СЕБЯ на долю своего максимального HP («Пошатнулся» Пьяного Великана).
    SelfDamage
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

    [Tooltip("DisruptSkills: на сколько секунд блокируется активный навык игрока. Клампится сверху " +
        "потолком CombatManager.MaxBossSkillDisruptSeconds — значение больше потолка не даёт эффекта.")]
    public float disruptSeconds = 5f;

    [Tooltip("DamageTakenBuff: на сколько процентов вырастает получаемый боссом урон (40 = +40%).")]
    public float damageTakenBonusPercent = 0f;

    [Tooltip("DamageTakenBuff: сколько секунд держится окно повышенного урона.")]
    public float damageTakenBonusSeconds = 0f;

    [Tooltip("ApplyFreeze: сколько зарядов Заморозки выдать за одно срабатывание (10 = мгновенная " +
        "заморозка). Клампится потолком заряда существующей системы.")]
    public int freezeStacks = 0;

    [Tooltip("AttackSpeedDebuff: множитель скорости атаки игрока (0.8 = −20%).")]
    public float attackSpeedMultiplier = 1f;

    [Tooltip("AttackSpeedDebuff: длительность дебаффа в секундах (до сокращения от еды/навыков).")]
    public float debuffSeconds = 0f;

    [Tooltip("SelfDamage: доля МАКСИМАЛЬНОГО HP босса, которую он снимает сам с себя (8 = 8%).")]
    public float selfDamagePercentOfMaxHp = 0f;
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

    [Tooltip("Ключ папки Resources/CharacterAnimations/Boss_<ключ>/{Idle,Attack,Heavy}/ с PixelLab-" +
        "анимацией этой фазы (см. BossAnimationFrames). Пусто = анимации нет, используется статичный " +
        "phaseSprite (старое поведение).")]
    public string animationFolderKey;

    [Tooltip("Компенсация 'зависания' в воздухе (2026-09-03) — доля высоты канваса phaseSprite, " +
        "занятая прозрачным отступом снизу. 0 = спрайт вплотную к низу холста (безопасный дефолт " +
        "для фаз, для которых анализатор ещё не запускался). Заполняется Assets/Editor/" +
        "SpriteFloorAnalyzer.cs, вручную не редактировать.")]
    public float floorPaddingFraction;

    [Tooltip("Способности фазы идут ЖЁСТКИМ ЦИКЛОМ по порядку списка, а не каждая на своём "+
        "независимом кулдауне. cooldownSeconds сработавшей способности = пауза ПЕРЕД следующей в "+
        "цикле. triggerKind в такой фазе игнорируется. Нужно для боссов с выучиваемым паттерном "+
        "(Часовой Титан).")]
    public bool cycleAbilities;

    [Tooltip("Множитель брони босса, применяемый ОДИН РАЗ при входе в эту фазу (0.5 = броня " +
        "падает вдвое). 1 = броня не меняется. Первая фаза это поле игнорирует — она активна с " +
        "начала боя, статы уже выставлены фабрикой.")]
    public float enterArmorMultiplier = 1f;

    [Tooltip("Множитель скорости атаки босса при входе в фазу (2 = бьёт вдвое чаще). 1 = не меняется.")]
    public float enterAttackSpeedMultiplier = 1f;

    public List<BossAbilityConfig> abilities = new List<BossAbilityConfig>();
}

// Boss framework (минимальный слайс, см. Docs/Design/2026-09-01-floor-boss-system-design.md):
// опциональный компаньон MonsterData (MonsterData.bossKit) — описывает фазы/способности/телеграфы/
// спрайты уникального босса данными, без bespoke-кода на каждого нового босса. Монстр без bossKit
// (isBoss=true, bossKit=null) продолжает работать через старую CombatManager.TickBossHeavyAttacks —
// см. CombatantFactory.CreateMonsterCombatant.
// Спутник босса: сущность, которая ставится на сцену ВМЕСТЕ с боссом при старте боя (не спавнится
// по ходу — для этого нужен отдельный механизм). Победа = все враги мертвы, это уже так работает
// в CombatManager.CheckCombatEnd.
[System.Serializable]
public class BossCompanionSpawn
{
    [Tooltip("Кого ставим рядом. Может ссылаться на того же монстра, что и сам босс — так делаются "+
        "симметричные связки вроде Теней-Близнецов.")]
    public MonsterData monster;

    [Tooltip("Сколько копий поставить.")]
    public int count = 1;

    [Tooltip("Якорь: пока жив, главный босс группы неуязвим (Свечи Свечника). Требует "+
        "bossInvulnerableWhileAnchorsAlive на ките.")]
    public bool isAnchor;
}

[CreateAssetMenu(fileName = "NewBossKit", menuName = "DungeonGirls/Boss Kit")]
public class BossKitData : ScriptableObject
{
    [Tooltip("Босс неуязвим, пока жив хотя бы один спутник-якорь. Весь бой сводится к выбору цели: "+
        "урон в босса при живом якоре пропадает впустую, и единственный правильный ответ — "+
        "переключиться кликом.")]
    public bool bossInvulnerableWhileAnchorsAlive;

    [Tooltip("Кто выходит на сцену вместе с боссом. Пусто = обычный бой один на один.")]
    public List<BossCompanionSpawn> companions = new List<BossCompanionSpawn>();

    [Tooltip("«Связь»: пока жив хотя бы один другой участник группы, каждый получает на столько "+
        "процентов меньше входящего урона. Клампится сверху 90% в DamageCalculator.")]
    public float groupDamageReductionPercent;

    [Tooltip("«Скорбь»: когда участник остаётся последним живым в группе, он НАВСЕГДА получает "+
        "этот бонус к урону.")]
    public float soloDamageBonusPercent;

    [Tooltip("«Скорбь»: и этот бонус к скорости атаки.")]
    public float soloAttackSpeedBonusPercent;

    [Tooltip("Текст баннера в момент разрыва связи. Пусто = «Связь разорвана».")]
    public string soloTransitionName;

    [Tooltip("Минимум одна фаза. phases[0].hpThresholdPercent должен быть 100 (активна с начала боя).")]
    public List<BossPhaseData> phases = new List<BossPhaseData>();
}
