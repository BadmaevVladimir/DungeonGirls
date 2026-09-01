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
        if (ability.triggerKind == BossAbilityTriggerKind.Periodic)
        {
            cooldownTimers[ability] = ability.cooldownSeconds;
        }
    }
}
