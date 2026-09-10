using System.Collections.Generic;

// Boss framework (минимальный слайс): тонкий runtime-state, ОДИН на боевого босса-CombatantRuntime
// (CombatantRuntime.BossEncounter), а не MonoBehaviour/подсистема. Владеет только тем, что нужно,
// чтобы данные из BossKitData превратились в поведение: текущая фаза, кулдауны способностей текущей
// фазы, ожидающий телеграф. Само исполнение эффекта (урон/щит/лог) остаётся в CombatManager —
// см. CombatManager.TickBossEncounters/ExecuteBossAbility, тем же паттерном, что и существующие
// TickMonsterPeriodicPassives/ApplyMonsterPassiveOnAttack.
public class BossEncounterState
{
    public readonly BossKitData Kit;
    public int CurrentPhaseIndex { get; private set; }

    readonly Dictionary<BossAbilityConfig, float> cooldownTimers = new Dictionary<BossAbilityConfig, float>();
    readonly HashSet<BossAbilityConfig> firedOnCombatStart = new HashSet<BossAbilityConfig>();

    // Позиция в жёстком цикле способностей фазы (BossPhaseData.cycleAbilities). Для обычных фаз
    // не используется вовсе.
    int cycleIndex;

    // Индекс шага цикла для UI: игрок должен видеть, где сейчас стрелка на циферблате.
    public int CurrentCycleIndex => CurrentPhase.cycleAbilities ? cycleIndex : -1;

    BossAbilityConfig pendingAbility;
    float pendingRemainingSeconds;
    float pendingTotalSeconds;

    public BossEncounterState(BossKitData kit)
    {
        Kit = kit;
        EnterPhase(0);
    }

    public BossPhaseData CurrentPhase => Kit.phases[CurrentPhaseIndex];

    // UI-friendly снимок текущего ожидающего телеграфа (без числовых деталей урона/формул — только
    // то, что нужно для баннера "готовит X" + доля прогресса для полоски-обратного отсчёта).
    public readonly struct TelegraphInfo
    {
        public readonly string DisplayName;
        public readonly float RemainingSeconds;
        public readonly float TotalSeconds;

        public TelegraphInfo(string displayName, float remainingSeconds, float totalSeconds)
        {
            DisplayName = displayName;
            RemainingSeconds = remainingSeconds;
            TotalSeconds = totalSeconds;
        }
    }

    public TelegraphInfo? PendingTelegraph => pendingAbility != null
        ? new TelegraphInfo(pendingAbility.displayName, UnityEngine.Mathf.Max(0f, pendingRemainingSeconds), pendingTotalSeconds)
        : (TelegraphInfo?)null;

    void EnterPhase(int index)
    {
        CurrentPhaseIndex = index;
        cooldownTimers.Clear();
        firedOnCombatStart.Clear();
        pendingAbility = null;
        pendingRemainingSeconds = 0f;
        pendingTotalSeconds = 0f;

        cycleIndex = 0;

        // Циклическая фаза: тикает ТОЛЬКО текущий шаг, поэтому заводим таймер одной первой
        // способности. Остальные получат свой таймер в момент, когда цикл до них дойдёт.
        if (CurrentPhase.cycleAbilities)
        {
            if (CurrentPhase.abilities.Count > 0)
            {
                cooldownTimers[CurrentPhase.abilities[0]] = CurrentPhase.abilities[0].initialDelaySeconds;
            }

            return;
        }

        foreach (var ability in CurrentPhase.abilities)
        {
            if (ability.triggerKind == BossAbilityTriggerKind.Periodic)
            {
                cooldownTimers[ability] = ability.initialDelaySeconds;
            }
        }
    }

    // Один раз за пересечение HP-порога следующей фазы — CurrentPhaseIndex растёт монотонно, поэтому
    // уже пройденная фаза не может сработать повторно (нет пути назад к меньшему индексу).
    public bool TryEnterNextPhase(float hpPercent, out BossPhaseData newPhase)
    {
        int nextIndex = CurrentPhaseIndex + 1;
        if (nextIndex < Kit.phases.Count && hpPercent <= Kit.phases[nextIndex].hpThresholdPercent)
        {
            EnterPhase(nextIndex);
            newPhase = CurrentPhase;
            return true;
        }

        newPhase = null;
        return false;
    }

    // Обрабатывает ОДНО событие за вызов (либо резолв ожидающего телеграфа, либо старт следующей
    // готовой способности) — тот же намеренно упрощённый паттерн "одно событие на Tick", что и
    // CombatManager.TickMonsterPeriodicPassives/TickBossHeavyAttacks: детерминированно, без риска
    // каскада способностей в один кадр, стоимость — соседние способности того же кадра ждут
    // следующего Tick (несущественно при обычных deltaTime боевого цикла).
    public void Tick(float deltaTime, out BossAbilityConfig executedAbility)
    {
        executedAbility = null;

        if (pendingAbility != null)
        {
            pendingRemainingSeconds -= deltaTime;
            if (pendingRemainingSeconds <= 0f)
            {
                executedAbility = pendingAbility;
                RestartCooldown(pendingAbility);
                pendingAbility = null;
            }

            return;
        }

        // Циклическая фаза (Часовой Титан): рассматриваем ровно один шаг — тот, на котором стоит
        // стрелка. Порядок детерминирован и одинаков каждый бой, в этом весь смысл такого босса.
        if (CurrentPhase.cycleAbilities)
        {
            if (CurrentPhase.abilities.Count == 0)
            {
                return;
            }

            var step = CurrentPhase.abilities[cycleIndex];
            float stepRemaining = cooldownTimers.TryGetValue(step, out float known) ? known : step.cooldownSeconds;
            stepRemaining -= deltaTime;
            cooldownTimers[step] = stepRemaining;
            if (stepRemaining <= 0f)
            {
                BeginOrExecute(step, out executedAbility);
            }

            return;
        }

        foreach (var ability in CurrentPhase.abilities)
        {
            if (ability.triggerKind == BossAbilityTriggerKind.OnCombatStart)
            {
                if (firedOnCombatStart.Contains(ability))
                {
                    continue;
                }

                firedOnCombatStart.Add(ability);
                BeginOrExecute(ability, out executedAbility);
                return;
            }

            float remaining = cooldownTimers.TryGetValue(ability, out float existing) ? existing : ability.cooldownSeconds;
            remaining -= deltaTime;
            cooldownTimers[ability] = remaining;

            if (remaining <= 0f)
            {
                BeginOrExecute(ability, out executedAbility);
                return;
            }
        }
    }

    void BeginOrExecute(BossAbilityConfig ability, out BossAbilityConfig executedNow)
    {
        if (ability.telegraphSeconds > 0f)
        {
            pendingAbility = ability;
            pendingRemainingSeconds = ability.telegraphSeconds;
            pendingTotalSeconds = ability.telegraphSeconds;
            executedNow = null;
        }
        else
        {
            executedNow = ability;
            RestartCooldown(ability);
        }
    }

    void RestartCooldown(BossAbilityConfig ability)
    {
        // В циклической фазе кулдаун сработавшей способности — это пауза ПЕРЕД следующим шагом
        // цикла, а не до её собственного повтора. Стрелка сдвигается здесь, то есть в момент
        // фактического исполнения (после телеграфа), а не когда телеграф только начался.
        if (CurrentPhase.cycleAbilities)
        {
            cycleIndex = (cycleIndex + 1) % CurrentPhase.abilities.Count;
            cooldownTimers[CurrentPhase.abilities[cycleIndex]] = ability.cooldownSeconds;
            return;
        }

        if (ability.triggerKind == BossAbilityTriggerKind.Periodic)
        {
            cooldownTimers[ability] = ability.cooldownSeconds;
        }
    }
}
