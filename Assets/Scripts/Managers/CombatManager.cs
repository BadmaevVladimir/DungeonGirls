using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    ICombatRandom combatRandom = new UnityCombatRandom();
    bool suppressCombatLogs;
    [SerializeField, Tooltip("Mirror the in-game combat journal to the Unity Console. Enable only when debugging combat.")]
    bool logCombatToConsole;

    public CombatantRuntime Player { get; private set; }
    public List<CombatantRuntime> Enemies { get; private set; } = new List<CombatantRuntime>();
    public bool IsCombatActive { get; private set; }
    public CombatTelemetrySnapshot LastCombatTelemetry { get; private set; }

    float currentCombatDurationSeconds;
    float currentCombatPlayerStartHP;
    float currentCombatPlayerMaxHP;
    float currentCombatPlayerDamageTaken;
    int currentCombatEnemyCount;
    bool currentCombatWasBoss;

    // Принудительное завершение используется только при явном выходе игрока из забега через паузу.
    public void AbortCombat()
    {
        IsCombatActive = false;
        if (Player != null) Player.IsBerserkActive = false;
        LastCombatTelemetry = null;
    }

    // Позволяет UI подписаться на текстовый лог боя (7.2), не читая консоль Unity.
    public event System.Action<string> LogMessage;

    // "Карманник" (2.4): (жертва, украденный % текущей валюты забега).

    // 4.7: визуальный фидбэк боя. (цель, урон по HP [0, если полностью заблокировано], крит?,
    // заблокировано?) — единая точка для всплывающих цифр урона и тряски спрайта цели.
    public event System.Action<CombatantRuntime, float, bool, bool> HitResolved;

    // 4.7: (участник, активировавший навык, название навыка) — для баннера активации навыка.
    // Архитектурно не привязано к игроку — на будущее, если у монстров появятся активные навыки.
    public event System.Action<CombatantRuntime, string> ActiveSkillActivated;

    // (доп., спрайтовая анимация): (атакующий, это обычная атака оружием?) — фиксирует момент
    // взмаха, ДО разрешения уклонения/урона (см. ResolveAttack). isRegularAttack=false для ударов
    // активного навыка — UI сам решает, проигрывать ли отдельную анимацию для них (см.
    // ActiveSkillActivated) вместо того, чтобы полагаться на этот ивент 3 раза подряд.
    public event System.Action<CombatantRuntime, bool> AttackPerformed;

    // Emitted after evasion has failed but before damage is applied. The attestation scenarios use
    // this to count real connected hits (blocked hits included) without duplicating hit resolution.
    public event System.Action<CombatantRuntime, CombatantRuntime> AttackConnected;

    void EmitHitResolved(CombatantRuntime target, float damageToHP, bool isCritical, bool wasBlocked)
    {
        if (target == Player) currentCombatPlayerDamageTaken += Mathf.Max(0f, damageToHP);
        HitResolved?.Invoke(target, damageToHP, isCritical, wasBlocked);
    }

    void Log(string message)
    {
        if (!suppressCombatLogs && logCombatToConsole) Debug.Log(message);
        LogMessage?.Invoke(message);
    }

    // Hidden attestation runs the real combat rules without flooding the Editor/player log. Keeping
    // this switch on CombatManager lets the simulator reuse the production attack/status pipeline
    // instead of maintaining a second, inevitably drifting implementation.
    public void SetHeadlessSimulationMode(bool enabled) => suppressCombatLogs = enabled;

    // Scripted trial attacks and cooldown skills still resolve through the exact live-combat path.
    public void ResolveScriptedAttack(CombatantRuntime attacker, WeaponAttackState weapon,
        float damageMultiplier = 1f, bool isRegularAttack = false)
    {
        if (!IsCombatActive || attacker == null || weapon == null) return;
        ResolveAttack(attacker, weapon, damageMultiplier, isRegularAttack);
        CheckCombatEnd();
    }

    static bool IgnoresDebuffs(CombatantRuntime target) =>
        CursedItemRules.IgnoresNewDebuffs(target) || target.TryBlockNegativeStatus();

    // Codex P1 (ФИКС, 2026-08-27): раньше CombatRoomFlow всегда передавал hitCount=3 и конфиг из
    // jenniferCharacter.uniqueActiveSkill — Плут получал бы конфигурацию Дженифер (неверный
    // hitCount/имя навыка), а Варвар вообще не имеет кулдаун-активки (Берсерк — ручной тумблер, см.
    // ниже). Единственный текущий кейс с hitCount != 3 — Дымовая граната Плута (не бьёт сама, см.
    // TryActivateUniqueActiveSkill, которое жёстко возвращает до hit-loop для неё независимо от
    // переданного числа) — hitCount=0 здесь просто отражает намерение корректно.
    public static int ResolveActiveSkillHitCount(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Rogue => 0, // Дымовая граната — не бьёт сама
        _ => 3 // "3 быстрые атаки" (Дженифер/Воин) — единственный hit-loop навык прототипа кроме Дымовой гранаты
    };

    // R05: восстановление после активного навыка — часть боевых правил, а не длительность
    // анимации. Число равно прежнему фактическому поведению Дженифер (11 кадров SkillBrightStrike
    // на 12 fps), чтобы фикс не менял баланс заодно с расхождением; UI обязан рисовать анимацию
    // ровно этой длины. Дымовая граната Плута замаха не имеет и блокировки не даёт.
    public const float ThreeQuickStrikesRecoverySeconds = 11f / 12f;

    // Правило честности роспиcи боссов (Docs/Design/2026-09-10-boss-concepts-roster.md, раздел 0.4):
    // активный навык — единственный рычаг игрока в автобое, поэтому отнимать его дольше чем на 8с
    // нельзя ни одному боссу. Потолок живёт в коде, а не в ассете, чтобы его нельзя было случайно
    // превысить при авторинге контента.
    public const float MaxBossSkillDisruptSeconds = 8f;

    // Правило честности №3: ни один одиночный удар босса не снимает больше половины максимального
    // HP цели ДО брони. Вайолет с 15 HP базы против Саши с 45 — без этого потолка одни и те же
    // числа для неё смертельны, а он их не замечает. Потолок в коде, а не в ассете, потому что это
    // условие проходимости, а не настройка.
    public const float MaxBossSingleHitPercentOfMaxHp = 50f;

    // Правило честности №5: лечение можно резать максимум вдвое. Полный запрет лечения выключил
    // бы «Боевую регенерацию» Саши как класс-механику, а не усложнил бой.
    public const float MaxBossHealCutPercent = 50f;

    // Правило честности №4: броню можно резать максимум вдвое. В ноль — нельзя: Дженифер живёт
    // бронёй, и обнуление выключает её как класс, а не усложняет бой.
    public const float MaxBossArmorDebuffPercent = 50f;

    public static float ResolveActiveSkillAttackLockSeconds(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Rogue => 0f,
        _ => ThreeQuickStrikesRecoverySeconds
    };

    // Активные-скилы-панель (2026-09-03): список сконфигурированных на текущий бой слотов —
    // сегодня всегда 1 элемент на класс (инфраструктура готова к N, контент не меняется).
    // Заменяет прежние плоские activeSkill*-поля/ConfigureUniqueActiveSkill.
    public List<ActiveSkillRuntimeState> ActiveSkills { get; } = new List<ActiveSkillRuntimeState>();

    public void SetRandomSource(ICombatRandom randomSource) =>
        combatRandom = randomSource ?? new UnityCombatRandom();

    public void ConfigureActiveSkills(IEnumerable<ActiveSkillConfigEntry> skills)
    {
        ActiveSkills.Clear();
        foreach (var entry in skills)
        {
            ActiveSkills.Add(new ActiveSkillRuntimeState
            {
                Data = entry.Data,
                HitCount = entry.HitCount,
                DamageMultiplierPerHit = entry.DamageMultiplierPerHit,
                // Активные-скилы-панель (2026-09-03): скилл готов СРАЗУ, не в полном кулдауне —
                // теперь активация ручная (клик/хоткей), а не автоматическая каждый кадр, так что
                // прежний риск "мгновенно снёс до того как игрок увидел" больше не применим.
                CooldownTimer = 0f,
                IsToggleActive = false,
                AutoMode = entry.AutoMode,
                AttackLockSeconds = entry.AttackLockSeconds,
            });
        }
    }

    public bool IsSkillReady(int slotIndex) =>
        Player != null && Player.IsAlive && !Player.IsFrozen && slotIndex >= 0 && slotIndex < ActiveSkills.Count &&
        ActiveSkills[slotIndex].Data.skillType == ActiveSkillType.Cooldown &&
        ActiveSkills[slotIndex].CooldownTimer <= 0f;

    public float SkillCooldownRemaining(int slotIndex) =>
        slotIndex >= 0 && slotIndex < ActiveSkills.Count
            ? Mathf.Max(0f, Mathf.Max(ActiveSkills[slotIndex].CooldownTimer, ActiveSkills[slotIndex].ForcedDisableRemaining)) : 0f;

    public void DisruptPlayerActiveSkills(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        foreach (var slot in ActiveSkills)
        {
            if (slot.Data.skillType == ActiveSkillType.Cooldown) slot.CooldownTimer += seconds;
            else
            {
                slot.ResumeToggleAfterDisable |= slot.IsToggleActive;
                slot.ForcedDisableRemaining = Mathf.Max(slot.ForcedDisableRemaining, seconds);
                slot.IsToggleActive = false;
                if (slot.Data.skillId == SkillId.Berserk) Player.IsBerserkActive = false;
            }
        }
    }

    void TickDisabledSkills(float deltaTime)
    {
        foreach (var slot in ActiveSkills)
        {
            if (slot.ForcedDisableRemaining <= 0f) continue;
            slot.ForcedDisableRemaining = Mathf.Max(0f, slot.ForcedDisableRemaining - deltaTime);
            if (slot.ForcedDisableRemaining > 0f || !Player.IsAlive) continue;
            slot.IsToggleActive = slot.ResumeToggleAfterDisable;
            if (slot.Data.skillId == SkillId.Berserk) Player.IsBerserkActive = slot.IsToggleActive;
            slot.ResumeToggleAfterDisable = false;
        }
    }

    public bool TryActivateSkill(int slotIndex)
    {
        if (!IsCombatActive || slotIndex < 0 || slotIndex >= ActiveSkills.Count)
        {
            return false;
        }

        var slot = ActiveSkills[slotIndex];
        return slot.Data.skillType == ActiveSkillType.Toggle ? TryToggleSkill(slot) : TryActivateCooldownSkill(slot);
    }

    public void SetSkillAutoMode(int slotIndex, bool autoMode)
    {
        if (slotIndex < 0 || slotIndex >= ActiveSkills.Count)
        {
            return;
        }

        ActiveSkills[slotIndex].AutoMode = autoMode;
    }

    // 3.11 (Варвар) "Берсерк" — ручной тумблер: нельзя ВКЛЮЧИТЬ без изученного уровня (безопасно
    // ВЫКЛЮЧАТЬ всегда — защитная логика перенесена без изменений из прежнего SetBerserkActive).
    // Диспатч по skillId — как и раньше, единственный toggle-скилл прототипа — Берсерк; если
    // появится другой Toggle-скилл, эффект добавляется сюда отдельной веткой.
    bool TryToggleSkill(ActiveSkillRuntimeState slot)
    {
        if (!IsCombatActive)
        {
            return false;
        }

        bool activate = !slot.IsToggleActive;
        if (activate && (Player.IsFrozen || slot.ForcedDisableRemaining > 0f)) return false;

        if (slot.Data.skillId == SkillId.Berserk)
        {
            if (activate && Player.UniqueBerserkLevel <= 0)
            {
                return false;
            }

            Player.IsBerserkActive = activate;
        }

        slot.IsToggleActive = activate;
        return true;
    }

    // 4.3: тело перенесено из прежнего TryActivateUniqueActiveSkill без изменений в поведении —
    // Берсерк сюда больше не заходит вовсе (диспатчится в TryToggleSkill по skillType), поэтому
    // прежний защитный бейл-аут на SkillId.Berserk убран как недостижимый.
    bool TryActivateCooldownSkill(ActiveSkillRuntimeState slot)
    {
        if (!IsCombatActive || !IsSkillReady(ActiveSkills.IndexOf(slot)) || Player.Weapons.Count == 0)
        {
            return false;
        }

        ActiveSkillActivated?.Invoke(Player, slot.Data.skillName);

        // R05: восстановление после замаха ставит сама боевая модель — одинаково в бою со сценой и
        // в headless-симуляции аттестации. Выставляется до hit-loop: ResolveAttack блокировку не
        // проверяет, зато следующий же Tick уже не начнёт обычную атаку.
        if (slot.AttackLockSeconds > 0f) Player.AttackLockRemaining = slot.AttackLockSeconds;

        // 3.11 "Дымовая граната" (уникальная активка Плута): при активации даёт Скрытность и
        // заряжает гарантированные криты на N последующих ОБЫЧНЫХ атак — не бьёт сама.
        if (slot.Data.skillId == SkillId.SmokeBomb)
        {
            GrantOrRefreshStealth(Player);
            Player.SmokeBombGuaranteedCritsRemaining = Player.UniqueSmokeBombLevel;
            Log($"[Combat] «Дымовая граната»: {Player.DisplayName} получает Скрытность и {Player.UniqueSmokeBombLevel} гарантированных критических атак.");
            slot.CooldownTimer = slot.Data.cooldownSeconds;
            return true;
        }

        var weapon = Player.Weapons[0];
        for (int i = 0; i < slot.HitCount; i++)
        {
            if (!IsCombatActive || !Player.IsAlive)
            {
                break;
            }

            ResolveAttack(Player, weapon, slot.DamageMultiplierPerHit, isRegularAttack: false);
        }

        slot.CooldownTimer = slot.Data.cooldownSeconds;
        return true;
    }

    // 3.11 (Варвар) — "Суеверность"/"Берсерк": сопротивления зависят от ЖИВОЙ Ярости, поэтому
    // пересчитываются каждый Tick, а не запекаются один раз в CombatantFactory.ApplyCharacterSkills.
    void UpdateResistances(CombatantRuntime combatant)
    {
        // ФИКС (код-ревью): было двойное деление на 100 (Rage×X/100, затем ещё раз /100 ниже) —
        // MagicalResistancePercent хранится в ПРОЦЕНТНЫХ единицах (DamageCalculator сам делит на 100
        // при применении, см. ApplyDamage), точно как соседнее PhysicalResistancePercent от Берсерка
        // (10f/20f/30f без деления). Лишнее /100f давало ~1% от нужной величины сопротивления.
        combatant.MagicalResistancePercent = combatant.SkillSuperstitionLevel > 0
            ? combatant.Rage * RageRules.SkillMultiplier(combatant.SkillSuperstitionLevel)
            : 0f;

        combatant.PhysicalResistancePercent = combatant.IsBerserkActive
            ? combatant.UniqueBerserkLevel switch { 1 => 20f, 2 => 30f, 3 => 40f, _ => 0f }
            : 0f;
    }

    public void StartCombat(CombatantRuntime player, List<CombatantRuntime> enemies)
    {
        Player = player;
        Enemies = enemies ?? new List<CombatantRuntime>();
        // Stable ordering: front-line guardians shield the other enemies by being targeted first.
        var orderedEnemies = Enemies.OrderByDescending(enemy => enemy.FrontlinePriority).ToList();
        Enemies.Clear();
        Enemies.AddRange(orderedEnemies);
        Player.MaxHP = DamageCalculator.RoundPoints(Player.MaxHP);
        Player.CurrentHP = DamageCalculator.RoundPoints(Player.CurrentHP);
        foreach (var enemy in Enemies)
        {
            enemy.MaxHP = DamageCalculator.RoundPoints(enemy.MaxHP);
            enemy.CurrentHP = DamageCalculator.RoundPoints(enemy.CurrentHP);
        }
        IsCombatActive = true;
        LastCombatTelemetry = null;
        currentCombatDurationSeconds = 0f;
        currentCombatPlayerStartHP = Player != null ? Player.CurrentHP : 0f;
        currentCombatPlayerMaxHP = Player != null ? Player.MaxHP : 1f;
        currentCombatPlayerDamageTaken = 0f;
        currentCombatEnemyCount = Enemies.Count;
        currentCombatWasBoss = Enemies.Any(enemy => enemy != null && enemy.IsBoss);

        ResetAttackTimers(Player);
        foreach (var enemy in Enemies)
        {
            ResetAttackTimers(enemy);
            if (enemy.IsBoss)
            {
                enemy.BossHeavyAttackTimer = 5f;
            }
        }

        Player.Target = GetDefaultTarget();

        // 3.11 (Task 6b, Эпический трофей): "Просто царапина" — разовое лечение РОВНО в начале боя,
        // только у игрока (у монстров предметов нет — ItemJustAScratchLevel всегда 0).
        if (Player.ItemJustAScratchLevel > 0)
        {
            Player.Heal(Player.MaxHP * ItemEffectBalance.JustAScratchHealPercent(Player.ItemJustAScratchLevel) / 100f);
        }

        Log($"[Combat] Бой начался: {Player.DisplayName} (здоровье {Player.CurrentHP:F1}) против {Enemies.Count} противников.");
    }

    public void EndCombat()
    {
        if (!IsCombatActive)
        {
            return;
        }

        IsCombatActive = false;

        LastCombatTelemetry = new CombatTelemetrySnapshot
        {
            DurationSeconds = currentCombatDurationSeconds,
            PlayerStartHP = currentCombatPlayerStartHP,
            PlayerEndHP = Player != null ? Mathf.Max(0f, Player.CurrentHP) : 0f,
            PlayerMaxHP = currentCombatPlayerMaxHP,
            PlayerDamageTaken = currentCombatPlayerDamageTaken,
            EnemyCount = currentCombatEnemyCount,
            WasBoss = currentCombatWasBoss,
            PlayerSurvived = Player != null && Player.IsAlive
        };

        // 3.3: магический щит восстанавливается до максимума после каждого боя; физ. защита — нет.
        Player.RestoreMagicShield();
        ResetTemporaryStatuses(Player);
        ResetPrototypeCombatState(Player);
        foreach (var enemy in Enemies) ResetPrototypeCombatState(enemy);

        Log(Player.IsAlive
            ? $"[Combat] Бой окончен. {Player.DisplayName} побеждает."
            : $"[Combat] Бой окончен. {Player.DisplayName} погибает.");
    }

    // 4.5: всё временное боевое состояние заканчивается вместе с боем. Физическая броня
    // намеренно не входит в этот список — её износ сохраняется на весь забег.
    static void ResetTemporaryStatuses(CombatantRuntime combatant)
    {
        if (combatant == null) return;
        // Equipment curses живут между боями, пока предмет надет; временные эффекты очищаются.
        combatant.ActiveDebuffs.RemoveAll(d => !d.IsEquipmentCurse);
        combatant.CritChanceDebuffPercent = 0f;
        combatant.CritChanceDebuffTimer = 0f;
        combatant.PoisonStacks = 0;
        combatant.PoisonTimer = 0f;
        combatant.PoisonTickAccumulator = 0f;
        combatant.RoguePoisonStacksOnTarget = 0;
        combatant.RoguePoisonTimer = 0f;
        combatant.RoguePoisonTickAccumulator = 0f;
        combatant.RoguePoisonSource = null;
        combatant.HasBleed = false;
        combatant.BleedDamagePerSecond = 0f;
        combatant.BleedTimer = 0f;
        combatant.BleedTickAccumulator = 0f;
        combatant.BleedLevel = 0;
        combatant.BleedSource = null;
        combatant.FreezeStacks = 0;
        combatant.FreezeStackTimer = 0f;
        combatant.IsFrozen = false;
        combatant.FreezeTimer = 0f;
        combatant.FreezeImmune = false;
        combatant.FreezeImmuneTimer = 0f;
        combatant.IsStealthed = false;
        combatant.StealthTimer = 0f;
        combatant.SmokeBombGuaranteedCritsRemaining = 0;
        combatant.RiposteArmed = false;
        combatant.HitsTakenSinceLastRegen = 0;
        combatant.CombatRegenCooldownRemaining = 0f;
        combatant.IsBerserkActive = false;
        combatant.BerserkTickAccumulator = 0f;
        // Boss framework: окно повышенного получаемого урона не должно протекать между боями.
        combatant.DamageTakenBonusPercent = 0f;
        combatant.DamageTakenBonusTimer = 0f;
        combatant.BossGroupDamageReductionPercent = 0f;
        combatant.BossSoloDamageBonusPercent = 0f;
        combatant.BossSoloAttackSpeedBonusPercent = 0f;
        combatant.BossSoloBonusApplied = false;
        combatant.IsInvulnerable = false;
        combatant.BossHealCutPercent = 0f;
        combatant.BossHealCutTimer = 0f;
        combatant.BossArmorDebuffPercent = 0f;
        combatant.BossArmorDebuffStacks = 0;
        combatant.BossArmorDebuffTimer = 0f;
        combatant.BossEnrageDamageBonusPercent = 0f;
        combatant.SelfInvulnerableTimer = 0f;
        combatant.ShieldRegenPerSecond = 0f;
        combatant.SecondsSinceShieldDamaged = 0f;
        combatant.ShieldRegenElapsedSeconds = 0f;
        combatant.LastKnownShieldValue = 0f;
        combatant.ShieldIntactDamageBonusPercent = 0f;
        combatant.ShieldBrokenPenaltyTimer = 0f;
        combatant.CursedParanoiaStacks = 0;
        combatant.CursedRecklessStacks = 0;
        combatant.CursedRecklessDecayTimer = 0f;
        foreach (var weapon in combatant.Weapons) weapon.CursedStacks = 0;
    }

    static void ResetPrototypeCombatState(CombatantRuntime combatant)
    {
        if (combatant == null) return;
        foreach (var weapon in combatant.Weapons)
        {
            weapon.PrototypeCounter = 0;
            weapon.PrototypeAccumulatedDamage = 0f;
            weapon.SecondsSinceLastAttack = 0f;
        }
    }

    void Update()
    {
        if (IsCombatActive)
        {
            Tick(Time.deltaTime);
        }
    }

    // 10.2: у каждого участника боя свой таймер атаки, срабатывающий по достижении
    // интервала = 1 / СкоростьАтаки. Вынесен из Update() отдельным методом, чтобы бой
    // можно было тикать и без сцены/плеймода (например, из редакторских тестов).
    public void Tick(float deltaTime)
    {
        if (!IsCombatActive)
        {
            return;
        }

        currentCombatDurationSeconds += Mathf.Max(0f, deltaTime);
        TickDisabledSkills(deltaTime);

        // 3.11 (Варвар) — "Суеверность"/"Берсерк" дают сопротивление, зависящее от ЖИВОЙ Ярости
        // (пересчитывается каждый тик, а не один раз при создании боевого юнита — см. комментарий
        // на CombatantRuntime.Rage).
        UpdateResistances(Player);
        foreach (var enemy in Enemies)
        {
            UpdateResistances(enemy);
        }

        UpdateStatusEffects(Player, deltaTime);
        foreach (var enemy in Enemies)
        {
            UpdateStatusEffects(enemy, deltaTime);
        }

        // 3.11 (Берсерк, Варвар) — ручной тумблер, только у игрока (см. SetBerserkActive). Тикает как
        // кровотечение/яд — накопитель на 1 секунду, БЕЗ защиты от смерти (ГДД явно это оговаривает).
        if (Player.IsBerserkActive && Player.IsAlive)
        {
            Player.BerserkTickAccumulator += deltaTime;
            while (Player.BerserkTickAccumulator >= 1f && Player.IsAlive)
            {
                Player.BerserkTickAccumulator -= 1f;
                // [ПРЕДПОЛОЖЕНИЕ, см. Global Constraints] — урон от ТЕКУЩЕГО HP, не максимума; ГДД
                // сам помечает точную базу как неподтверждённую.
                float tickDamage = DamageCalculator.ApplyDirectDamage(Player, Mathf.Max(1f, Player.CurrentHP * 0.01f));
                currentCombatPlayerDamageTaken += tickDamage;
                Log($"[Combat] «Берсерк» наносит {tickDamage:F1} урона {Player.DisplayName} (здоровье {Player.CurrentHP:F1}/{Player.MaxHP:F1}).");
            }
        }

        TickMonsterPeriodicPassives(deltaTime); // "Тёмное исцеление" / "Двойной удар" (2.4)
        TickBossHeavyAttacks(deltaTime); // легаси-путь: боссы БЕЗ BossKitData (см. BossEncounter ниже)
        TickBossEncounters(deltaTime); // boss framework: боссы С BossKitData
        TickBossGroups(); // boss framework: правила связки нескольких сущностей одного босс-боя
        TickBossShieldRegen(deltaTime); // boss framework: самовосстанавливающийся щит Ростовщика

        CheckCombatEnd();
        if (!IsCombatActive)
        {
            return;
        }

        TickCombatant(Player, deltaTime);

        foreach (var enemy in Enemies)
        {
            TickCombatant(enemy, deltaTime);
        }

        if (IsCombatActive && Player.IsAlive)
        {
            for (int i = 0; i < ActiveSkills.Count; i++)
            {
                var slot = ActiveSkills[i];
                if (slot.Data.skillType != ActiveSkillType.Cooldown)
                {
                    continue;
                }

                if (slot.CooldownTimer > 0f)
                {
                    slot.CooldownTimer -= deltaTime;
                }

                if (slot.AutoMode && slot.CooldownTimer <= 0f)
                {
                    TryActivateSkill(i);
                }
            }
        }

        CheckCombatEnd();
    }

    static void ResetAttackTimers(CombatantRuntime combatant)
    {
        // R05: CombatantRuntime игрока переиспользуется между боями. Незавершённое восстановление
        // после навыка (бой кончился прямо во время замаха) иначе украло бы первую секунду
        // следующего боя. Раньше от этого страховался UI в StopPlayerFlipbook — теперь это, как и
        // сама блокировка, забота боевой модели.
        combatant.AttackLockRemaining = 0f;
        foreach (var weapon in combatant.Weapons)
        {
            weapon.AttackTimer = 0f;
            weapon.PrototypeCounter = 0;
            weapon.PrototypeAccumulatedDamage = 0f;
            weapon.SecondsSinceLastAttack = 0f;
        }
    }

    // Боссы продолжают обычные атаки и параллельно готовят отдельную «Тяжёлую атаку».
    // Первый и каждый следующий удар происходят через 5 секунд; сила растёт 150% -> 175% ->
    // 200% на этажах 1-3 / 4-6 / 7-10.
    void TickBossHeavyAttacks(float deltaTime)
    {
        foreach (var enemy in Enemies)
        {
            if (!IsCombatActive || !Player.IsAlive)
            {
                return;
            }

            // Boss framework (минимальный слайс): боссы С BossKitData используют TickBossEncounters
            // вместо этого легаси-таймера — не бить дважды за один и тот же "тяжёлый удар".
            if (!enemy.IsAlive || enemy.IsFrozen || !enemy.IsBoss || enemy.Weapons.Count == 0 || enemy.BossEncounter != null)
            {
                continue;
            }

            enemy.BossHeavyAttackTimer -= deltaTime;
            if (enemy.BossHeavyAttackTimer > 0f)
            {
                continue;
            }

            enemy.BossHeavyAttackTimer += 5f;
            float multiplier = enemy.BossHeavyAttackDamageMultiplier;
            ActiveSkillActivated?.Invoke(enemy, "Тяжёлая атака");
            Log($"[Combat] {enemy.DisplayName} завершает подготовку «Тяжёлой атаки» ({multiplier * 100f:F0}% урона).");
            ResolveAttack(enemy, enemy.Weapons[0], multiplier, isRegularAttack: false);
        }
    }

    // Boss framework (минимальный слайс, см. Docs/Design/2026-09-01-floor-boss-system-design.md) —
    // для каждого живого боевого босса с назначенным BossKitData (enemy.BossEncounter != null):
    // 1) проверяет переход в следующую фазу по HP% (один раз за пересечение порога — см.
    //    BossEncounterState.TryEnterNextPhase), меняет спрайт и объявляет фазу баннером;
    // 2) иначе тикает BossEncounterState (кулдауны/ожидающий телеграф) и исполняет ровно одну готовую
    //    способность за кадр, тем же намеренно упрощённым паттерном, что и TickMonsterPeriodicPassives/
    //    TickBossHeavyAttacks выше.
    // Boss framework (групповой босс-бой): пересчитывается КАЖДЫЙ тик, а не по событию смерти —
    // участник может умереть от чего угодно (атака, кровотечение, яд, шипы), и единой точки
    // «кто-то умер» в бою не существует. Метод дешёвый: два вложенных прохода по Enemies, которых
    // в босс-бою единицы.
    // Boss framework (Ростовщик): щит восстанавливается сам, если по нему не били заданное время.
    // Факт попадания определяем по ПАДЕНИЮ значения щита между тиками, а не хуком в местах вызова
    // ApplyDamage: точек применения урона несколько, и любая забытая ломала бы механику молча.
    void TickBossShieldRegen(float deltaTime)
    {
        foreach (var enemy in Enemies)
        {
            // Условие входа — «этот босс вообще пользуется системой панциря», а не «у него есть
            // регенерация»: иначе босс со стабильным щитом и «Жадностью» молча не получал бы
            // ни окна после пробития, ни бонуса за целый щит.
            bool usesShieldSystem = enemy.ShieldRegenPerSecond > 0f
                || enemy.ShieldIntactDamageBonusPercent > 0f
                || enemy.ShieldBrokenDamagePenaltyPercent > 0f;
            if (!usesShieldSystem || !enemy.IsAlive)
            {
                continue;
            }

            enemy.ShieldRegenElapsedSeconds += deltaTime;

            bool wasIntact = enemy.LastKnownShieldValue > 0f;
            if (enemy.ShieldPoolCurrent < enemy.LastKnownShieldValue)
            {
                enemy.SecondsSinceShieldDamaged = 0f;
                // Щит только что пробит насквозь — открывается окно, ради которого игрок его и бил.
                if (wasIntact && enemy.ShieldPoolCurrent <= 0f && enemy.ShieldBrokenPenaltySeconds > 0f)
                {
                    enemy.ShieldBrokenPenaltyTimer = enemy.ShieldBrokenPenaltySeconds;
                    Log($"[Combat] Щит {enemy.DisplayName} пробит: он бьёт слабее {enemy.ShieldBrokenPenaltySeconds:F0} с.");
                }
            }
            else
            {
                enemy.SecondsSinceShieldDamaged += deltaTime;
            }

            if (enemy.ShieldBrokenPenaltyTimer > 0f)
            {
                enemy.ShieldBrokenPenaltyTimer = Mathf.Max(0f, enemy.ShieldBrokenPenaltyTimer - deltaTime);
            }

            if (enemy.SecondsSinceShieldDamaged >= enemy.ShieldRegenDelaySeconds && enemy.ShieldPoolCurrent < enemy.ShieldPoolMax)
            {
                // Регенерация слабеет со временем: страховка от непроходимости для самого
                // медленного билда. Без неё бой мог бы не иметь решения вообще.
                float decaySteps = enemy.ShieldRegenElapsedSeconds / 20f;
                float decay = Mathf.Pow(1f - enemy.ShieldRegenDecayPercentPer20Seconds / 100f, decaySteps);
                enemy.ShieldPoolCurrent = Mathf.Min(enemy.ShieldPoolMax,
                    enemy.ShieldPoolCurrent + enemy.ShieldRegenPerSecond * decay * deltaTime);
            }

            enemy.LastKnownShieldValue = enemy.ShieldPoolCurrent;
        }
    }

    // План 8: миньоны уходят вместе с боссом. Живут отдельным правилом, а не внутри TickBossGroups,
    // потому что миньоны Амальгама в группу НЕ входят (у него нет «Кокона»), а исчезать за боссом
    // обязаны всё равно. Иначе добитый босс оставляет на сцене живую мелочь, бой продолжается без
    // него, и победа перестаёт совпадать со смертью босса.
    void TickBossMinionOrphans()
    {
        bool anyMinionAlive = false;
        bool anyBossAlive = false;
        foreach (var enemy in Enemies)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            if (enemy.IsBossMinion) anyMinionAlive = true;
            else if (enemy.BossEncounter != null) anyBossAlive = true;
        }

        if (!anyMinionAlive || anyBossAlive) return;

        foreach (var enemy in Enemies)
        {
            if (enemy == null || !enemy.IsBossMinion || !enemy.IsAlive) continue;
            enemy.CurrentHP = 0f;
            Log($"[Combat] {enemy.DisplayName} рассыпается вслед за хозяином.");
        }
    }

    void TickBossGroups()
    {
        TickBossMinionOrphans();

        foreach (var enemy in Enemies)
        {
            if (!enemy.InBossGroup || !enemy.IsAlive)
            {
                continue;
            }

            bool allyAlive = false;
            foreach (var other in Enemies)
            {
                if (other == enemy || !other.InBossGroup || !other.IsAlive) continue;
                allyAlive = true;
                break;
            }

            // Свечник: неуязвим, пока горит хоть одна свеча. Пересчитывается тут же, потому что
            // это ровно то же наблюдение «кто из группы ещё жив».
            if (enemy.PendingInvulnerableWhileAnchorsAlive)
            {
                bool anchorAlive = false;
                foreach (var other in Enemies)
                {
                    if (other == enemy || !other.IsBossAnchor || !other.IsAlive) continue;
                    anchorAlive = true;
                    break;
                }

                enemy.IsInvulnerable = anchorAlive;
            }

            if (allyAlive)
            {
                enemy.BossGroupAllyEverSeen = true;
                enemy.BossGroupDamageReductionPercent = enemy.PendingGroupDamageReductionPercent;
                continue;
            }

            // Группа, в которую союзники приходят СПАВНОМ (Паучиха), на старте состоит из одного
            // босса. Без этой проверки «остался один» выдавалось бы ему на первом тике — до того,
            // как хоть один паучонок вообще появился на сцене.
            if (!enemy.BossGroupAllyEverSeen)
            {
                enemy.BossGroupDamageReductionPercent = 0f;
                continue;
            }

            // Связь разорвана: снижение урона спадает, и ровно один раз навсегда включается
            // усиление выжившего. Это единственное место, где оно выдаётся.
            enemy.BossGroupDamageReductionPercent = 0f;
            if (enemy.BossSoloBonusApplied)
            {
                continue;
            }

            enemy.BossSoloBonusApplied = true;
            enemy.BossSoloDamageBonusPercent = enemy.PendingSoloDamageBonusPercent;
            enemy.BossSoloAttackSpeedBonusPercent = enemy.PendingSoloAttackSpeedBonusPercent;

            if (enemy.BossSoloDamageBonusPercent > 0f || enemy.BossSoloAttackSpeedBonusPercent > 0f)
            {
                string banner = string.IsNullOrEmpty(enemy.SoloTransitionName) ? "Связь разорвана" : enemy.SoloTransitionName;
                ActiveSkillActivated?.Invoke(enemy, banner);
                Log($"[Boss] {enemy.DisplayName}: «{banner}» — урон +{enemy.BossSoloDamageBonusPercent:F0}%, " +
                    $"скорость атаки +{enemy.BossSoloAttackSpeedBonusPercent:F0}%.");
            }
        }
    }

    void TickBossEncounters(float deltaTime)
    {
        // Обход ПО ИНДЕКСУ с зафиксированной длиной, а не foreach: способность SpawnMinions (план 8)
        // дописывает миньонов в Enemies прямо отсюда, и foreach бросил бы «Collection was modified».
        // Спавн только дописывает в конец, поэтому индексы уже пройденных не съезжают, а сами
        // новички в этот же тик не обрабатываются — им нечего обрабатывать, кита у них нет.
        int count = Enemies.Count;
        for (int i = 0; i < count; i++)
        {
            var enemy = Enemies[i];
            if (!IsCombatActive || !Player.IsAlive)
            {
                return;
            }

            if (enemy == null)
            {
                continue;
            }

            if (!enemy.IsAlive || enemy.IsFrozen || enemy.BossEncounter == null)
            {
                continue;
            }

            var state = enemy.BossEncounter;
            float hpPercent = enemy.MaxHP > 0f ? enemy.CurrentHP / enemy.MaxHP * 100f : 0f;
            if (state.TryEnterNextPhase(hpPercent, out var newPhase))
            {
                if (newPhase.phaseSprite != null)
                {
                    enemy.Sprite = newPhase.phaseSprite;
                }

                // Фаза может менять статы самого босса, а не только его способности: Каменный Идол
                // трескается и теряет броню, Плакальщица разгоняется. Применяется РОВНО ОДИН РАЗ,
                // в момент перехода — TryEnterNextPhase монотонен и дважды в фазу не заводит.
                if (!Mathf.Approximately(newPhase.enterArmorMultiplier, 1f))
                {
                    float armorScale = Mathf.Max(0f, newPhase.enterArmorMultiplier);
                    enemy.PhysicalDefenseMax *= armorScale;
                    enemy.PhysicalDefenseCurrent *= armorScale;
                }

                if (!Mathf.Approximately(newPhase.enterAttackSpeedMultiplier, 1f))
                {
                    float speedScale = Mathf.Max(0.01f, newPhase.enterAttackSpeedMultiplier);
                    foreach (var weapon in enemy.Weapons)
                    {
                        // AttackSpeed — это ЧАСТОТА ударов (в тестах 0.01 = интервал 100с),
                        // поэтому «бить чаще» = умножать.
                        weapon.AttackSpeed *= speedScale;
                    }
                }

                ActiveSkillActivated?.Invoke(enemy, newPhase.phaseName);
                Log($"[Boss] {enemy.DisplayName} переходит в фазу «{newPhase.phaseName}» (здоровье {hpPercent:F0}%).");
                continue; // новая фаза резолвит свои кулдауны/телеграфы со следующего Tick.
            }

            state.Tick(deltaTime, out var readyAbility);
            if (readyAbility != null)
            {
                ExecuteBossAbility(enemy, readyAbility);
            }
        }
    }

    // Исполняет ОДИН эффект способности босса — resolves либо мгновенно (telegraphSeconds==0), либо
    // после того, как BossEncounterState.Tick досчитал pending-телеграф до нуля. Закрытый switch по
    // effectKind (см. BossAbilityEffectKind) — новый effectKind добавляется сюда только когда реально
    // появляется механика, которую нельзя выразить существующими двумя.
    void ExecuteBossAbility(CombatantRuntime boss, BossAbilityConfig ability)
    {
        switch (ability.effectKind)
        {
            case BossAbilityEffectKind.HeavyAttack:
                if (boss.Weapons.Count == 0)
                {
                    return;
                }

                // ResolveAttack сам бейлит, если игрок уже мёртв/цель недоступна (см. её начало) —
                // безопасно вызывать здесь без дополнительной проверки на "цель умерла во время
                // телеграфа".
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName} завершает подготовку «{ability.displayName}» ({ability.damageMultiplier * 100f:F0}% урона).");
                // Добивание: множитель растёт линейно от порога до нуля HP цели. Считается от
                // МАКСИМАЛЬНОГО HP — иначе порог полз бы вместе с уроном и вёл себя непредсказуемо.
                float heavyMultiplier = ability.damageMultiplier;
                if (ability.executeBelowHpPercent > 0f && ability.executeMaxDamageMultiplier > ability.damageMultiplier
                    && Player.MaxHP > 0f)
                {
                    float hpPercent = Player.CurrentHP / Player.MaxHP * 100f;
                    if (hpPercent < ability.executeBelowHpPercent)
                    {
                        float t = 1f - Mathf.Clamp01(hpPercent / ability.executeBelowHpPercent);
                        heavyMultiplier = Mathf.Lerp(ability.damageMultiplier, ability.executeMaxDamageMultiplier, t);
                    }
                }

                ResolveAttack(boss, boss.Weapons[0], heavyMultiplier, isRegularAttack: false,
                    damagePercentOfTargetMaxHp: ability.damagePercentOfTargetMaxHp,
                    maxHpPercentCap: MaxBossSingleHitPercentOfMaxHp);
                break;

            case BossAbilityEffectKind.ShieldPool:
                boss.ShieldPoolMax = ability.shieldAmount;
                boss.ShieldPoolCurrent = ability.shieldAmount;
                boss.ShieldPoolExpireTimer = ability.shieldDurationSeconds > 0f ? ability.shieldDurationSeconds : float.PositiveInfinity;
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName} активирует «{ability.displayName}»: щит {ability.shieldAmount:F0}.");
                break;

            case BossAbilityEffectKind.DisruptSkills:
                float disruptSeconds = Mathf.Clamp(ability.disruptSeconds, 0f, MaxBossSkillDisruptSeconds);
                // DisruptPlayerActiveSkills сам корректно обрабатывает пустой список слотов и
                // разницу Cooldown/Toggle — здесь только клампим длительность и логируем.
                DisruptPlayerActiveSkills(disruptSeconds);
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName} применяет «{ability.displayName}»: активный навык заблокирован на {disruptSeconds:F0} с.");
                break;

            case BossAbilityEffectKind.DamageTakenBuff:
                boss.DamageTakenBonusPercent = Mathf.Max(0f, ability.damageTakenBonusPercent);
                boss.DamageTakenBonusTimer = Mathf.Max(0f, ability.damageTakenBonusSeconds);
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName}: «{ability.displayName}» — получаемый урон +{boss.DamageTakenBonusPercent:F0}% на {boss.DamageTakenBonusTimer:F0} с.");
                break;

            case BossAbilityEffectKind.ReviveAnchor:
                CombatantRuntime revived = null;
                foreach (var candidate in Enemies)
                {
                    if (!candidate.IsBossAnchor || candidate.IsAlive) continue;
                    revived = candidate;
                    break;
                }

                if (revived == null)
                {
                    // Все якоря живы — зажигать нечего. Способность уходит на кулдаун вхолостую,
                    // и это нормальный ход боя, а не ошибка данных.
                    break;
                }

                revived.CurrentHP = revived.MaxHP;
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName} применяет «{ability.displayName}»: {revived.DisplayName} снова в строю.");
                break;

            case BossAbilityEffectKind.ApplyFreeze:
                ApplyBossFreeze(boss, ability);
                break;

            case BossAbilityEffectKind.AttackSpeedDebuff:
                // «Упёртость» Саши гасит новые дебаффы целиком — тот же контракт, что у заморозки
                // и «Запугивания», иначе вложение в неё выборочно не работало бы против боссов.
                if (IgnoresDebuffs(Player))
                {
                    Log($"[Combat] «Упёртость» защищает {Player.DisplayName} от «{ability.displayName}».");
                    break;
                }

                Player.ActiveDebuffs.Add(new ActiveDebuff
                {
                    Id = "boss_" + ability.displayName,
                    RemainingTime = Player.AdjustNegativeStatusDuration(Mathf.Max(0f, ability.debuffSeconds)),
                    AttackSpeedMultiplier = Mathf.Clamp(ability.attackSpeedMultiplier, 0.1f, 1f)
                });
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName}: «{ability.displayName}» — скорость атаки {Player.DisplayName} ×{Mathf.Clamp(ability.attackSpeedMultiplier, 0.1f, 1f):F2} на {ability.debuffSeconds:F0} с.");
                break;

            case BossAbilityEffectKind.SelfDamage:
                float selfDamage = boss.MaxHP * Mathf.Max(0f, ability.selfDamagePercentOfMaxHp) / 100f;
                if (selfDamage <= 0f)
                {
                    break;
                }

                // Прямой урон мимо брони и щитов: босс калечит себя сам, защищаться тут не от чего.
                float dealt = DamageCalculator.ApplyDirectDamage(boss, selfDamage);
                EmitHitResolved(boss, dealt, false, false);
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName}: «{ability.displayName}» — сам себе {dealt:F1} урона.");
                break;

            case BossAbilityEffectKind.ApplyDot:
                if (IgnoresDebuffs(Player))
                {
                    Log($"[Combat] «Упёртость» защищает {Player.DisplayName} от «{ability.displayName}».");
                    break;
                }

                // Потолок зарядов обязателен: очистки эффектов в игре нет ни у кого, поэтому
                // растущий без предела DoT — это не сложность, а отложенная смерть.
                int dotCap = Mathf.Max(1, ability.dotMaxStacks);
                Player.PoisonStacks = Mathf.Min(Player.PoisonStacks + Mathf.Max(1, ability.dotStacks), dotCap);
                Player.PoisonTimer = Player.AdjustNegativeStatusDuration(Mathf.Max(0.1f, ability.dotSeconds));
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName}: «{ability.displayName}» — {Player.DisplayName} отравлен ({Player.PoisonStacks}/{dotCap}).");
                break;

            case BossAbilityEffectKind.HealCut:
                Player.BossHealCutPercent = Mathf.Clamp(ability.healCutPercent, 0f, MaxBossHealCutPercent);
                // 0 секунд = на весь бой: у Инквизитора «Приговор» висит с первой секунды и до конца.
                Player.BossHealCutTimer = ability.healCutSeconds > 0f
                    ? Player.AdjustNegativeStatusDuration(ability.healCutSeconds)
                    : float.PositiveInfinity;
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName}: «{ability.displayName}» — лечение {Player.DisplayName} −{Player.BossHealCutPercent:F0}%.");
                break;

            case BossAbilityEffectKind.StatDebuff:
                if (IgnoresDebuffs(Player))
                {
                    Log($"[Combat] «Упёртость» защищает {Player.DisplayName} от «{ability.displayName}».");
                    break;
                }

                int armorCap = Mathf.Max(1, ability.armorDebuffMaxStacks);
                Player.BossArmorDebuffStacks = Mathf.Min(Player.BossArmorDebuffStacks + 1, armorCap);
                Player.BossArmorDebuffPercent = Mathf.Clamp(
                    ability.armorDebuffPercent * Player.BossArmorDebuffStacks, 0f, MaxBossArmorDebuffPercent);
                Player.BossArmorDebuffTimer = Player.AdjustNegativeStatusDuration(Mathf.Max(0.1f, ability.armorDebuffSeconds));
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName}: «{ability.displayName}» — броня {Player.DisplayName} −{Player.BossArmorDebuffPercent:F0}% ({Player.BossArmorDebuffStacks}/{armorCap}).");
                break;

            case BossAbilityEffectKind.RoomTick:
                float tickDamage = Player.MaxHP * Mathf.Max(0f, ability.roomTickPercentOfMaxHp) / 100f;
                if (tickDamage <= 0f)
                {
                    break;
                }

                // Мимо уклонения (это не атака), но через броню — поэтому обычный ApplyDamage, а не
                // прямой урон. Баннер намеренно НЕ показываем: фоновый тик каждые пару секунд
                // забил бы собой весь лог боя.
                var tickResult = DamageCalculator.ApplyDamage(Player, tickDamage, DamageType.Physical);
                EmitHitResolved(Player, tickResult.DamageToHP, false, tickResult.WasBlocked);
                break;

            case BossAbilityEffectKind.Enrage:
                boss.BossEnrageDamageBonusPercent += Mathf.Max(0f, ability.enrageDamagePercentPerTrigger);
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName}: «{ability.displayName}» — урон +{boss.BossEnrageDamageBonusPercent:F0}% суммарно.");
                break;

            case BossAbilityEffectKind.SelfInvulnerable:
                float invulnSeconds = Mathf.Max(0f, ability.selfInvulnerableSeconds);
                if (invulnSeconds <= 0f)
                {
                    break;
                }

                boss.IsInvulnerable = true;
                boss.SelfInvulnerableTimer = invulnSeconds;
                // Пауза честная: пока босс неуязвим, он и сам не бьёт (TickCombatant пропускает
                // участника с активным AttackLockRemaining).
                boss.AttackLockRemaining = Mathf.Max(boss.AttackLockRemaining, invulnSeconds);
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName}: «{ability.displayName}» — неуязвим и не атакует {invulnSeconds:F0} с.");
                break;

            case BossAbilityEffectKind.ShieldRegen:
                boss.ShieldPoolMax = ability.shieldAmount;
                boss.ShieldPoolCurrent = ability.shieldAmount;
                boss.ShieldPoolExpireTimer = float.PositiveInfinity;
                boss.ShieldRegenPerSecond = ability.shieldAmount * Mathf.Max(0f, ability.shieldRegenPercentPerSecond) / 100f;
                boss.ShieldRegenDelaySeconds = Mathf.Max(0f, ability.shieldRegenDelaySeconds);
                boss.ShieldRegenDecayPercentPer20Seconds = Mathf.Clamp(ability.shieldRegenDecayPercentPer20Seconds, 0f, 100f);
                boss.ShieldIntactDamageBonusPercent = Mathf.Max(0f, ability.shieldIntactDamageBonusPercent);
                boss.ShieldBrokenDamagePenaltyPercent = Mathf.Clamp(ability.shieldBrokenDamagePenaltyPercent, 0f, 100f);
                boss.ShieldBrokenPenaltySeconds = Mathf.Max(0f, ability.shieldBrokenPenaltySeconds);
                boss.SecondsSinceShieldDamaged = 0f;
                boss.ShieldRegenElapsedSeconds = 0f;
                boss.LastKnownShieldValue = boss.ShieldPoolCurrent;
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName} поднимает «{ability.displayName}»: щит {ability.shieldAmount:F0}, восстанавливается сам.");
                break;

            case BossAbilityEffectKind.SpawnMinions:
                SpawnBossMinions(boss, ability);
                break;

            case BossAbilityEffectKind.ConsumeMinion:
                ConsumeBossMinion(boss, ability);
                break;
        }
    }

    // План 8: ставит миньонов на сцену ПО ХОДУ боя. Потолок живых проверяется перед каждой единицей,
    // а не один раз на срабатывание, иначе spawnCount=2 при одном свободном месте перешагнул бы его.
    // Способность при выбранном потолке уходит на кулдаун вхолостую — это намеренно: так игрок,
    // который не чистит мелочь, получает передышку, а не бесконечно растущую сцену.
    void SpawnBossMinions(CombatantRuntime boss, BossAbilityConfig ability)
    {
        if (ability.spawnMonster == null) return;

        int cap = ability.spawnAliveCap > 0 ? ability.spawnAliveCap : int.MaxValue;
        int spawned = 0;
        for (int i = 0; i < Mathf.Max(0, ability.spawnCount); i++)
        {
            if (CountLivingBossMinions() >= cap) break;

            var minion = CombatantFactory.CreateMonsterCombatant(
                ability.spawnMonster, Mathf.Max(1, boss.SourceFloorNumber), suppressRandomModifiers: true);
            minion.IsBossMinion = true;
            minion.MaxHP = DamageCalculator.RoundPoints(minion.MaxHP);
            minion.CurrentHP = DamageCalculator.RoundPoints(minion.CurrentHP);
            ResetAttackTimers(minion);

            // Миньон входит в группу босса, если группа вообще есть: это и есть «Кокон» Паучихи,
            // выраженный механикой плана 3. Снижение урона при этом достаётся только боссу —
            // паучатам живучесть не положена, иначе чистка мелочи перестаёт быть решением.
            if (boss.InBossGroup)
            {
                minion.InBossGroup = true;
                minion.PendingGroupDamageReductionPercent = 0f;
                minion.PendingSoloDamageBonusPercent = 0f;
                minion.PendingSoloAttackSpeedBonusPercent = 0f;
            }

            Enemies.Add(minion);
            spawned++;
        }

        if (spawned == 0) return;

        ActiveSkillActivated?.Invoke(boss, ability.displayName);
        Log($"[Combat] {boss.DisplayName} применяет «{ability.displayName}»: на сцене +{spawned}.");
    }

    // План 8: Амальгам съедает собственного миньона и лечится. Без живых миньонов не лечит вообще —
    // именно поэтому переключение цели на слизня отнимает у босса лечение, а не просто «немного
    // помогает».
    void ConsumeBossMinion(CombatantRuntime boss, BossAbilityConfig ability)
    {
        CombatantRuntime prey = null;
        foreach (var enemy in Enemies)
        {
            if (enemy == null || !enemy.IsBossMinion || !enemy.IsAlive) continue;
            prey = enemy;
            break;
        }

        if (prey == null) return;

        prey.CurrentHP = 0f;
        float heal = boss.MaxHP * Mathf.Max(0f, ability.healPercentOfMaxHp) / 100f;
        boss.Heal(heal);
        ActiveSkillActivated?.Invoke(boss, ability.displayName);
        Log($"[Combat] {boss.DisplayName} применяет «{ability.displayName}»: съедает {prey.DisplayName}, +{heal:F0} HP.");
    }

    int CountLivingBossMinions()
    {
        int count = 0;
        foreach (var enemy in Enemies)
        {
            if (enemy != null && enemy.IsBossMinion && enemy.IsAlive) count++;
        }

        return count;
    }

    // 3.9 "Амбидекстрия": у каждого оружия персонажа свой независимый таймер атаки по своей
    // собственной скорости — обрабатываются в отдельных циклах, а не слитно одним таймером.
    void TickCombatant(CombatantRuntime attacker, float deltaTime)
    {
        foreach (var weapon in attacker.Weapons)
            weapon.SecondsSinceLastAttack += Mathf.Max(0f, deltaTime);

        // 3.9 "Заморозка": замороженный участник не может атаковать; таймеры атаки не копятся.
        // Восстановление после активного навыка — тот же эффект "не копится/не бьёт", но временно
        // и по таймеру боевой модели (см. CombatantRuntime.AttackLockRemaining).
        if (!attacker.IsAlive || attacker.IsFrozen || attacker.IsAttackLocked)
        {
            return;
        }

        foreach (var weapon in attacker.Weapons)
        {
            weapon.AttackTimer += deltaTime;

            while (IsCombatActive && attacker.IsAlive && !attacker.IsFrozen && weapon.AttackTimer >= attacker.GetEffectiveAttackInterval(weapon))
            {
                weapon.AttackTimer -= attacker.GetEffectiveAttackInterval(weapon);
                ResolveAttack(attacker, weapon);
            }
        }
    }

    // Дебаффы, влияющие на скорость атаки (Колдун и т.п.), стаки заморозки и кровотечение
    // тикают независимо от того, атакует ли участник в этом кадре.
    void UpdateStatusEffects(CombatantRuntime combatant, float deltaTime)
    {
        combatant.CombatRegenCooldownRemaining = Mathf.Max(0f, combatant.CombatRegenCooldownRemaining - deltaTime);
        // R05: восстановление после активного навыка истекает по боевому времени, а не по концу
        // UI-анимации — иначе бой без сцены (симуляция аттестации) блокировки вообще не видел.
        combatant.AttackLockRemaining = Mathf.Max(0f, combatant.AttackLockRemaining - deltaTime);

        // Boss framework: окно повышенного получаемого урона истекает по боевому времени и
        // ОБНУЛЯЕТ процент, а не только таймер — иначе просроченное окно продолжило бы работать.
        // Дебафф брони истекает по боевому времени и снимает ВСЕ стаки разом: он накладывается
        // целиком, поэтому и спадать должен целиком, иначе стаки жили бы вечно по одному.
        if (combatant.BossArmorDebuffTimer > 0f)
        {
            combatant.BossArmorDebuffTimer -= deltaTime;
            if (combatant.BossArmorDebuffTimer <= 0f)
            {
                combatant.BossArmorDebuffTimer = 0f;
                combatant.BossArmorDebuffPercent = 0f;
                combatant.BossArmorDebuffStacks = 0;
            }
        }

        // Добровольная неуязвимость («Погружение») снимается сама — иначе босс остался бы
        // неуязвимым до конца боя.
        if (combatant.SelfInvulnerableTimer > 0f)
        {
            combatant.SelfInvulnerableTimer -= deltaTime;
            if (combatant.SelfInvulnerableTimer <= 0f)
            {
                combatant.SelfInvulnerableTimer = 0f;
                combatant.IsInvulnerable = false;
            }
        }

        // Штраф к лечению истекает по боевому времени; бесконечный таймер (на весь бой) не тикает.
        if (combatant.BossHealCutTimer > 0f && !float.IsPositiveInfinity(combatant.BossHealCutTimer))
        {
            combatant.BossHealCutTimer -= deltaTime;
            if (combatant.BossHealCutTimer <= 0f)
            {
                combatant.BossHealCutTimer = 0f;
                combatant.BossHealCutPercent = 0f;
            }
        }

        if (combatant.DamageTakenBonusTimer > 0f)
        {
            combatant.DamageTakenBonusTimer -= deltaTime;
            if (combatant.DamageTakenBonusTimer <= 0f)
            {
                combatant.DamageTakenBonusTimer = 0f;
                combatant.DamageTakenBonusPercent = 0f;
            }
        }
        if (combatant.CursedRecklessStacks > 0)
        {
            combatant.CursedRecklessDecayTimer -= deltaTime;
            if (combatant.CursedRecklessDecayTimer <= 0f)
            {
                combatant.CursedRecklessStacks = 0;
                combatant.CursedRecklessDecayTimer = 0f;
            }
        }
        for (int i = combatant.ActiveDebuffs.Count - 1; i >= 0; i--)
        {
            var debuff = combatant.ActiveDebuffs[i];
            debuff.RemainingTime -= deltaTime;
            if (debuff.RemainingTime <= 0f)
            {
                combatant.ActiveDebuffs.RemoveAt(i);
            }
        }

        if (combatant.FreezeStacks > 0 && !combatant.IsFrozen)
        {
            combatant.FreezeStackTimer -= deltaTime;
            if (combatant.FreezeStackTimer <= 0f)
            {
                combatant.FreezeStacks = 0;
            }
        }

        if (combatant.IsFrozen)
        {
            combatant.FreezeTimer -= deltaTime;
            if (combatant.FreezeTimer <= 0f)
            {
                combatant.IsFrozen = false;
                combatant.FreezeStacks = 0;
            }
        }

        if (combatant.FreezeImmune)
        {
            combatant.FreezeImmuneTimer -= deltaTime;
            if (combatant.FreezeImmuneTimer <= 0f)
            {
                combatant.FreezeImmune = false;
            }
        }

        // 3.11 (Плут) — "Скрытность": простой таймер, независимый от таймера атаки, тикает
        // каждый кадр пока активна (StartCombat не устанавливает её — см. CombatantRuntime).
        if (combatant.IsStealthed)
        {
            combatant.StealthTimer -= deltaTime;
            if (combatant.StealthTimer <= 0f)
            {
                combatant.IsStealthed = false;
            }
        }

        TickBleed(combatant, deltaTime);
        TickPoison(combatant, deltaTime);
        TickRoguePoison(combatant, deltaTime);

        if (combatant.CritChanceDebuffTimer > 0f)
        {
            combatant.CritChanceDebuffTimer -= deltaTime;
            if (combatant.CritChanceDebuffTimer <= 0f)
            {
                combatant.CritChanceDebuffPercent = 0f;
            }
        }

        // Boss framework (минимальный слайс) — shield pool с ограниченным сроком (shieldDurationSeconds
        // > 0 в BossAbilityConfig) спадает по таймеру, даже если урон его не выбил полностью.
        // float.PositiveInfinity (бессрочный щит) никогда не проходит "<= 0f" — тикать нечего.
        if (combatant.ShieldPoolCurrent > 0f && !float.IsPositiveInfinity(combatant.ShieldPoolExpireTimer))
        {
            combatant.ShieldPoolExpireTimer -= deltaTime;
            if (combatant.ShieldPoolExpireTimer <= 0f)
            {
                combatant.ShieldPoolCurrent = 0f;
                combatant.ShieldPoolMax = 0f;
            }
        }
    }

    // 3.9 "Кровотечение": тикает раз в секунду, не зависит от таймера атаки. На ур. 5 каждый
    // тик может критовать текущим шансом источника: тогда он наносит и свой тик, и весь урон за
    // оставшееся время эффекта, после чего обновляет длительность.
    void TickBleed(CombatantRuntime target, float deltaTime)
    {
        if (!target.HasBleed)
        {
            return;
        }

        if (!float.IsPositiveInfinity(target.BleedTimer)) target.BleedTimer -= deltaTime;

        target.BleedTickAccumulator += deltaTime;
        while (target.BleedTickAccumulator >= 1f && target.HasBleed && target.IsAlive)
        {
            target.BleedTickAccumulator -= 1f;
            bool isCriticalTick = BleedRules.CanTickCritically(target.BleedLevel) && target.BleedSource != null &&
                combatRandom.Value01() * 100f < CombatCriticalRules.CalculateChancePercent(target.BleedSource);
            float tickDamage = target.BleedDamagePerSecond;
            if (isCriticalTick)
            {
                float detonationDamage = BleedRules.DetonationDamage(target.BleedDamagePerSecond, target.BleedTimer);
                tickDamage += detonationDamage;
                target.BleedTimer = target.AdjustNegativeStatusDuration(BleedRules.DurationForLevel(target.BleedLevel));
                Log($"[Combat] Критический тик кровотечения детонирует ещё {detonationDamage:F1} урона и обновляет длительность.");
            }

            tickDamage = DamageCalculator.ApplyDirectDamage(target, tickDamage);
            EmitHitResolved(target, tickDamage, isCriticalTick, false);
            Log($"[Combat] {target.DisplayName} получает {tickDamage:F1} урона от кровотечения{(isCriticalTick ? " (критический урон)" : string.Empty)} (здоровье {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

            if (!target.IsAlive)
            {
                Log($"[Combat] {target.DisplayName} погибает от кровотечения.");
            }
        }

        if (target.BleedTimer <= 0f)
        {
            target.HasBleed = false;
        }
    }

    // 4.2: игрок атакует выбранную вручную цель либо (по умолчанию) первого живого
    // противника в списке; противники всегда атакуют персонажа.
    // 3.9: сюда же подключены Уклонение, Критические атаки, Несгибаемый, Шипы, Заморозка, Кровотечение.
    // isRegularAttack: 3.11 "Дымовая граната" — true только для обычных, тикером атаки запущенных
    // ударов (TickCombatant); TryActivateUniqueActiveSkill передаёт false для СВОИХ ударов, чтобы
    // гарантированные криты гранаты не расходовались/не применялись к ним самим (по ГДД гарантия
    // распространяется только на "обычные атаки оружием").
    // damagePercentOfTargetMaxHp/maxHpPercentCap (2026-09-11): урон способности босса в ДОЛЯХ
    // максимального HP цели вместо урона оружия, и жёсткий потолок одиночного удара. Оба
    // применяются ПОСЛЕ проверки уклонения и крита, но ДО брони и щитов — то есть уклонение и
    // броня продолжают работать, а числа перестают зависеть от того, у кого 15 HP базы, а у кого 45.
    void ResolveAttack(CombatantRuntime attacker, WeaponAttackState weapon, float damageMultiplier = 1f,
        bool isRegularAttack = true, float damagePercentOfTargetMaxHp = 0f, float maxHpPercentCap = 0f)
    {
        CombatantRuntime target = attacker.IsPlayer ? GetPlayerTarget() : Player;

        if (target == null || !target.IsAlive)
        {
            return;
        }

        bool wasStealthedAtAttackStart = attacker.IsStealthed;
        AttackPerformed?.Invoke(attacker, isRegularAttack);

        float pendulumBonusPercent = weapon.PrototypeEffect == WeaponPrototypeEffectId.Pendulum
            ? PrototypeWeaponRules.PendulumBonusPercent(weapon.SecondsSinceLastAttack,
                weapon.PrototypePrimaryValue, weapon.PrototypeSecondaryValue)
            : 0f;
        weapon.SecondsSinceLastAttack = 0f;

        if (weapon.CursedEffect == CursedEffectId.RecklessCharge && CursedItemRules.IsCurseActive(attacker, CursedEffectId.RecklessCharge))
        {
            attacker.CursedRecklessStacks = Mathf.Min(CursedItemRules.RecklessMaxStacks, attacker.CursedRecklessStacks + 1);
            attacker.CursedRecklessDecayTimer = CursedItemRules.RecklessStackDecaySeconds;
        }

        if (wasStealthedAtAttackStart && weapon.CursedEffect == CursedEffectId.BetrayerAndAccomplice && CursedItemRules.IsCurseActive(attacker, CursedEffectId.BetrayerAndAccomplice))
        {
            attacker.StealthTimer = Mathf.Max(0f, attacker.StealthTimer - 0.25f);
            if (attacker.StealthTimer <= 0f) attacker.IsStealthed = false;
        }

        // "Уклонение" + пассивка предмета "Неуловимость" (3.10, Эфирный доспех) + бонусный стат
        // EvasionPercent (3.10 ФИКС, Кольцо ловкости/Амулет проворства — раньше игнорировался) +
        // "Ускользание" (3.11, Плут, собственный бонус шанса уклонения) + "Тень" (3.11, уникальная
        // пассивка Плута, только пока активна Скрытность) — складываются: шанс полностью
        // проигнорировать атаку (любого типа урона).
        float evadeChancePercent = CombatEvasionRules.CalculateChancePercent(target);

        if (evadeChancePercent > 0f && combatRandom.Value01() * 100f < evadeChancePercent)
        {
            Log($"[Combat] {target.DisplayName} уклоняется от атаки {attacker.DisplayName}.");

            // "Ускользание" (даёт Скрытность на 3с) и "На волоске" (даёт временный бафф скорости
            // атаки на 3с) — оба срабатывают на СТОРОНЕ ЗАЩИЩАЮЩЕГОСЯ (target), т.к. это ОН уклонился.
            if (target.SkillSlipAwayLevel > 0)
            {
                GrantOrRefreshStealth(target);
            }

            if (target.SkillByAThreadLevel > 0)
            {
                float byAThreadBonus = target.SkillByAThreadLevel * 0.03f; // 3/6/9/12/15%
                var existing = target.ActiveDebuffs.Find(d => d.Id == "by_a_thread");
                if (existing != null)
                {
                    existing.RemainingTime = 3f;
                    existing.AttackSpeedMultiplier = 1f + byAThreadBonus;
                    existing.IsBuff = true;
                }
                else
                {
                    // Финальный ревью-фикс #2: IsBuff=true — это БАФФ скорости атаки, не дебафф,
                    // несмотря на то что хранится в ActiveDebuffs (см. ActiveDebuff.IsBuff).
                    target.ActiveDebuffs.Add(new ActiveDebuff { Id = "by_a_thread", RemainingTime = 3f, AttackSpeedMultiplier = 1f + byAThreadBonus, IsBuff = true });
                }
                Log($"[Combat] «На волоске» повышает скорость атаки {target.DisplayName} на {byAThreadBonus * 100f:F0}% (3 сек).");
            }

            // 3.11 (Task 6b, Капюшон Дуэльянта): "Рипост" — успешное уклонение ВЗВОДИТ флаг (не бьёт
            // немедленно), бонус применится на следующей СОБСТВЕННОЙ атаке target (см. ResolveAttack
            // выше, attacker.RiposteArmed) — ГДД: "первая атака ПОСЛЕ успешного уклонения".
            if (target.ItemRiposteLevel > 0)
            {
                target.RiposteArmed = true;
            }

            if (target.FindCursedWeapon(CursedEffectId.ParanoiaBlades) != null)
                target.CursedParanoiaStacks = Mathf.Min(CursedItemRules.MaxStacks, target.CursedParanoiaStacks + 1);

            return;
        }

        AttackConnected?.Invoke(attacker, target);

        float baseAttackDamage = combatRandom.Range(weapon.DamageMin, weapon.DamageMax) * damageMultiplier;
        float damage = baseAttackDamage;
        if (pendulumBonusPercent > 0f)
            damage *= 1f + pendulumBonusPercent / 100f;
        if (weapon.PrototypeEffect == WeaponPrototypeEffectId.SpellEater)
            damage += weapon.PrototypeAccumulatedDamage;
        if (weapon.PrototypeEffect == WeaponPrototypeEffectId.ResonanceScimitar)
            damage *= PrototypeWeaponRules.ResonanceDamageMultiplier(attacker, weapon);
        if (weapon.PrototypeEffect == WeaponPrototypeEffectId.LastArgumentConversion)
            damage *= PrototypeWeaponRules.LastArgumentDamageMultiplier(attacker, weapon);

        if (weapon.CursedEffect == CursedEffectId.Executioner)
        {
            float executionerMultiplier = target.CurrentHP <= target.MaxHP * 0.25f
                ? 2f
                : CursedItemRules.IsCurseActive(attacker, CursedEffectId.Executioner) && target.CurrentHP >= target.MaxHP * 0.75f ? 0.75f : 1f;
            damage *= executionerMultiplier;
        }

        if (weapon.CursedEffect == CursedEffectId.LastArgument)
            damage += CursedItemRules.LastArgumentBonusDamage(attacker.MaxHP, weapon.ItemRank);

        if (wasStealthedAtAttackStart && weapon.CursedEffect == CursedEffectId.BetrayerAndAccomplice)
            damage *= 1f + CursedItemRules.StealthDamageBonusPercent(weapon.ItemRank) / 100f;

        // 1, п.3: постоянный бонус к магическому урону от основного пассивного навыка наставника ("Магнум Опус").
        if (attacker.IsPlayer && weapon.DamageType == DamageType.Magical && attacker.MentorMagicDamageBonusPercent > 0f)
        {
            damage *= 1f + attacker.MentorMagicDamageBonusPercent / 100f;
        }

        // "Несгибаемый": пока на атакующем есть активный дебафф, его урон увеличен.
        if (attacker.SkillUnyieldingLevel > 0 && attacker.HasActiveDebuff)
        {
            damage *= 1f + attacker.SkillUnyieldingLevel * 0.05f; // 5/10/15/20/25%
        }

        // 3.10 (ФИКС): бонусный стат DamagePercent (Стальной шлем/Корона Мидаса) — раньше
        // игнорировался, не был подключён нигде.
        if (attacker.ItemDamageBonusPercent > 0f)
        {
            damage *= 1f + attacker.ItemDamageBonusPercent / 100f;
        }

        // Boss framework: «Скорбь» — постоянный бонус урона выжившему участнику связки.
        if (attacker.BossSoloDamageBonusPercent > 0f)
            damage *= 1f + attacker.BossSoloDamageBonusPercent / 100f;

        // Boss framework: накопленная надбавка Enrage — предохранитель от затягивания боя.
        if (attacker.BossEnrageDamageBonusPercent > 0f)
            damage *= 1f + attacker.BossEnrageDamageBonusPercent / 100f;

        // Boss framework («Жадность» Ростовщика): щит цел — бьёт больнее; щит только что пробит —
        // слабее. Это и есть то, ради чего игрок вскрывает панцирь.
        if (attacker.ShieldIntactDamageBonusPercent > 0f && attacker.ShieldPoolCurrent > 0f)
            damage *= 1f + attacker.ShieldIntactDamageBonusPercent / 100f;
        if (attacker.ShieldBrokenPenaltyTimer > 0f)
            damage *= 1f - attacker.ShieldBrokenDamagePenaltyPercent / 100f;

        damage *= 1f + attacker.TotalDamageBonusPercent / 100f; // блюдо + бонус привала (Таверна ур.5)
        if (weapon.DamageType == DamageType.Physical)
            damage *= 1f + attacker.FoodPhysicalDamagePercent / 100f;
        if (target.IsBoss)
            damage *= 1f + attacker.FoodBossDamagePercent / 100f;

        // 3.11 (Task 6b, Моменто Мори): "Казнь" — доп. физ. урон = 1% недостающего HP ЦЕЛИ за
        // уровень оружия. Только физический урон, только если оружие реально несёт эту пассивку.
        if (weapon.ExecutionLevel > 0 && weapon.DamageType == DamageType.Physical)
        {
            float missingHpPercent = target.MaxHP > 0f ? (1f - target.CurrentHP / target.MaxHP) : 0f;
            damage += target.MaxHP * missingHpPercent * (ItemEffectBalance.ExecutionMissingHealthPercent(weapon.ExecutionLevel) / 100f);
        }

        // 3.11 (Task 6b, Головоруб): "Убийца великанов" — +5% урона за уровень против цели с БОЛЬШИМ
        // максимальным HP (не текущим — сравнение по MaxHP, чтобы избитая цель не "переставала" считаться великаном).
        if (weapon.GiantSlayerLevel > 0 && target.MaxHP > attacker.MaxHP)
        {
            damage *= 1f + weapon.GiantSlayerLevel * 0.05f;
        }

        // 3.11 (Task 6b, Капюшон Дуэльянта): "Рипост" — взведён на предыдущем успешном уклонении
        // этого атакующего (см. блок уклонения ниже), срабатывает РОВНО на следующей атаке и сразу
        // сбрасывается — не копится, не бьёт немедленно в момент уклонения.
        if (attacker.RiposteArmed)
        {
            damage += damage * ItemEffectBalance.RiposteDamageMultiplier(attacker.ItemRiposteLevel);
            attacker.RiposteArmed = false;
        }

        // 3.11 "Устранение" (Плут): переопределяет базовый крит-множитель 150%, если навык изучен.
        // Вычисляется ДО критChancePercent, т.к. "Чемпион племени" (Варвар) ниже может добавить к
        // нему конвертированные источники крит-шанса.
        float critMultiplier = attacker.CritDamageMultiplierOverridePercent ?? 150f;

        float critChancePercent = CombatCriticalRules.CalculateChancePercent(attacker);
        if (attacker.CritChanceReplacedByRage)
        {
            // 3.11 "Чемпион племени" (Варвар, уникальная пассивка): крит-шанс ВСЕГДА = Ярость×X%,
            // полностью заменяя обычную формулу. Остальные источники крит-шанса (навык "Критические
            // атаки" + бонус предметов — "В глаз"/крит-дебафф Гарпии сюда намеренно не входят, это
            // Rogue-специфика/дебафф шанса, а не источник шанса Варвара) конвертируются в крит-урон
            // по курсу 1%->+2% вместо суммирования в шанс.
            critMultiplier += CombatCriticalRules.ConvertedCritDamageBonus(attacker);
        }

        bool isCrit = critChancePercent > 0f && combatRandom.Value01() * 100f < critChancePercent;

        // 3.11 "Дымовая граната" (уникальная активка Плута): пока есть заряды гарантированного
        // крита от этого навыка, ОБЫЧНАЯ атака (isRegularAttack) гарантированно критует и расходует
        // заряд — независимо от ролла выше.
        if (isRegularAttack && attacker.SmokeBombGuaranteedCritsRemaining > 0)
        {
            isCrit = true;
            attacker.SmokeBombGuaranteedCritsRemaining--;
        }

        if (isCrit)
        {
            damage *= critMultiplier / 100f;

            if (weapon.CursedEffect == CursedEffectId.Oathbreaker)
                attacker.AddRunCurrency?.Invoke(CursedItemRules.OathbreakerCurrencyPerCrit);

            // "В глаз" (3.11, Плут): крит накладывает/обновляет Скрытность на 3с.
            if (attacker.SkillEyeForAnEyeLevel > 0)
            {
                GrantOrRefreshStealth(attacker);
            }

            // 3.11 "Запугивание" (Варвар): крит накладывает дебафф скорости атаки на цель,
            // пропорциональный Ярости атакующего. Подчиняется "Упёртости" цели, как любой дебафф.
            if (attacker.SkillIntimidationLevel > 0 && !IgnoresDebuffs(target))
            {
                float intimidationMultiplier = Mathf.Max(0.01f, 1f - (attacker.Rage * RageRules.SkillMultiplier(attacker.SkillIntimidationLevel) / 100f));
                var existingIntimidation = target.ActiveDebuffs.Find(d => d.Id == "intimidation");
                if (existingIntimidation != null)
                {
                    existingIntimidation.RemainingTime = target.AdjustNegativeStatusDuration(3f);
                    existingIntimidation.AttackSpeedMultiplier = intimidationMultiplier;
                }
                else
                {
                    target.ActiveDebuffs.Add(new ActiveDebuff { Id = "intimidation", RemainingTime = target.AdjustNegativeStatusDuration(3f), AttackSpeedMultiplier = intimidationMultiplier });
                }
                Log($"[Combat] «Запугивание» снижает скорость атаки {target.DisplayName} на {(1f - intimidationMultiplier) * 100f:F0}% (3 сек).");
            }
        }

        // 3.10 (ФИКС): "Пробивание" (Топор/Молот редкого+ тира, BonusStatType.ArmorPenetrationFlat) —
        // раньше игнорировалось. "Против бронированных целей урон считается на +N больше" — флэт-
        // бонус к урону только для целей ПРОБИТИЯ брони, добавляется прямо перед проверкой брони.
        float armorPenetrationDamage = weapon.DamageType == DamageType.Physical ? weapon.ArmorPenetrationFlat : 0f;
        float armorBeforeAttack = target.PhysicalDefenseCurrent;
        bool paranoiaCrash = target.CursedParanoiaStacks > 0 && CursedItemRules.IsCurseActive(target, CursedEffectId.ParanoiaBlades);
        float paranoiaMultiplier = paranoiaCrash ? CursedItemRules.ParanoiaIncomingMultiplier(target.CursedParanoiaStacks) : 1f;
        float magicShieldBeforeAttack = target.MagicShieldCurrent;
        // Процентный урон и потолок одиночного удара (см. сигнатуру). Считаются от МАКСИМАЛЬНОГО
        // HP цели, а не от текущего: иначе удар был бы тем слабее, чем хуже дела у игрока, и
        // добивание работало бы наоборот.
        if (damagePercentOfTargetMaxHp > 0f)
        {
            // Множитель НЕ теряется: иначе добивание и любые другие надбавки к тяжёлому удару
            // молча обнулялись бы, стоило перевести способность на проценты.
            damage = target.MaxHP * damagePercentOfTargetMaxHp / 100f * Mathf.Max(0f, damageMultiplier);
            armorPenetrationDamage = 0f;
        }

        if (maxHpPercentCap > 0f)
        {
            float cap = target.MaxHP * maxHpPercentCap / 100f;
            if (damage + armorPenetrationDamage > cap)
            {
                damage = Mathf.Max(0f, cap - armorPenetrationDamage);
            }
        }

        DamageCalculator.DamageResult result;
        if (weapon.PrototypeEffect == WeaponPrototypeEffectId.SpellEater)
        {
            result = DamageCalculator.ApplySpellEaterPhysicalDamage(target,
                (damage + armorPenetrationDamage) * paranoiaMultiplier,
                weapon.ArmorIgnorePercent, out float shieldRemoved);
            if (magicShieldBeforeAttack > 0f && target.MagicShieldCurrent <= 0f)
                weapon.PrototypeAccumulatedDamage += shieldRemoved * Mathf.Max(0f, weapon.PrototypePrimaryValue);
        }
        else if (weapon.PrototypeEffect == WeaponPrototypeEffectId.DayAndNight)
        {
            float physicalShare = Mathf.Clamp01(weapon.PrototypePrimaryValue / 100f);
            float magicalShare = Mathf.Clamp01(weapon.PrototypeSecondaryValue / 100f);
            float shareTotal = physicalShare + magicalShare;
            if (shareTotal <= 0f) { physicalShare = 0.5f; magicalShare = 0.5f; shareTotal = 1f; }
            physicalShare /= shareTotal;
            magicalShare /= shareTotal;
            float splitTotal = DamageCalculator.RoundPoints(damage * paranoiaMultiplier);
            float physicalPart = DamageCalculator.RoundPoints(splitTotal * physicalShare);
            var physical = DamageCalculator.ApplyDamage(target,
                physicalPart + armorPenetrationDamage * paranoiaMultiplier,
                DamageType.Physical, weapon.ArmorIgnorePercent);
            var magical = DamageCalculator.ApplyDamage(target,
                splitTotal - physicalPart, DamageType.Magical);
            result = PrototypeWeaponRules.Combine(physical, magical);
        }
        else
        {
            result = DamageCalculator.ApplyDamage(target, (damage + armorPenetrationDamage) * paranoiaMultiplier,
                weapon.DamageType, weapon.ArmorIgnorePercent);
        }
        if (paranoiaCrash) target.CursedParanoiaStacks = 0;
        float normalArmorLost = armorBeforeAttack - target.PhysicalDefenseCurrent;

        // «Бронебойный» снимает дополнительную гарантированную броню после любой неуклонённой
        // атаки, даже если обычный урон полностью заблокирован или был магическим.
        if (!attacker.IsPlayer && attacker.MonsterGuaranteedArmorDamage > 0f && target.PhysicalDefenseCurrent > 0f)
        {
            float armorBeforeModifier = target.PhysicalDefenseCurrent;
            target.PhysicalDefenseCurrent = Mathf.Max(0f, armorBeforeModifier - attacker.MonsterGuaranteedArmorDamage);
            float modifierArmorLost = armorBeforeModifier - target.PhysicalDefenseCurrent;
            if (modifierArmorLost > 0f)
            {
                Log($"[Combat] «Бронебойный» дополнительно снижает физ. защиту {target.DisplayName} на {modifierArmorLost:F1}.");
            }
        }

        if (result.WasBlocked)
        {
            string blockSuffix = normalArmorLost > 0f ? $", броня истёрлась (-{normalArmorLost:F0})" : string.Empty;
            Log($"[Combat] {attacker.DisplayName} атакует {target.DisplayName}{(isCrit ? " (критический удар)" : string.Empty)}: урон {damage:F1} полностью заблокирован{blockSuffix}.");
        }
        else
        {
            Log($"[Combat] {attacker.DisplayName} атакует {target.DisplayName}{(isCrit ? " (критический удар)" : string.Empty)}: {result.DamageToHP:F1} урона здоровью (осталось {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");
        }

        // 4.7: единая точка для всплывающих цифр урона и тряски спрайта цели — покрывает обычные
        // атаки оружием и каждый отдельный удар активного навыка (цикл в TryActivateUniqueActiveSkill
        // вызывает ResolveAttack по разу на удар, так что события уже приходят по одному, не суммарно).
        EmitHitResolved(target, result.DamageToHP, isCrit, result.WasBlocked);

        if (isRegularAttack && weapon.PrototypeEffect == WeaponPrototypeEffectId.LightningSpear &&
            PrototypeWeaponRules.AdvanceLightningCounter(weapon) && target.IsAlive)
        {
            float lightningDamage = baseAttackDamage * Mathf.Max(0f, weapon.PrototypePrimaryValue) / 100f;
            var lightning = DamageCalculator.ApplyDamage(target, lightningDamage, DamageType.Magical);
            EmitHitResolved(target, lightning.DamageToHP, false, lightning.WasBlocked);
            Log($"[Combat] Копьё молний наносит {lightning.DamageToHP:F1} дополнительного магического урона.");
        }

        if (weapon.CursedEffect == CursedEffectId.BerserkerAxe)
            weapon.CursedStacks = Mathf.Min(CursedItemRules.MaxStacks, weapon.CursedStacks + 1);

        if (isCrit && weapon.CursedEffect == CursedEffectId.ThornAxe && attacker.IsAlive)
            ApplyBleed(attacker, attacker, weapon.ItemRank);

        // Крит по уже кровоточащей цели немедленно наносит весь ожидаемый урон за оставшуюся
        // длительность и обновляет Кровотечение. Это не зависит от типа удара или брони: триггер —
        // именно критический удар по цели с эффектом.
        if (isCrit && target.IsAlive && target.HasBleed)
        {
            DetonateBleedFromCriticalHit(target);
        }

        // 3.11 (Task 6b, "Объятия ночи", Кожанка) — ОТДЕЛЬНЫЙ второй урон, только в Скрытности:
        // магический (проходит через щит, не через броню), поэтому не может быть просто добавлен в
        // физический `damage` — отдельный вызов DamageCalculator.ApplyDamage + отдельное всплывающее
        // число (HitResolved), но НЕ крит (isCrit жёстко false для этого удара).
        if (attacker.IsStealthed && attacker.ItemEmbraceOfNightLevel > 0)
        {
            float bonusMagicDamage = damage * ItemEffectBalance.EmbraceOfNightMagicDamagePercent(attacker.ItemEmbraceOfNightLevel) / 100f;
            var embraceResult = DamageCalculator.ApplyDamage(target, bonusMagicDamage, DamageType.Magical);
            EmitHitResolved(target, embraceResult.DamageToHP, false, embraceResult.WasBlocked);
        }

        // Вампиризм лечит от фактически снятого HP, с учётом всех модификаторов лечения.
        if (isCrit && weapon.VampirismLevel > 0 && result.DamageToHP > 0f && attacker.IsAlive)
        {
            float healAmount = attacker.Heal(result.DamageToHP * ItemEffectBalance.VampirismHealPercentOfCritDamage(weapon.VampirismLevel) / 100f);
            Log($"[Combat] «Вампиризм» восстанавливает {attacker.DisplayName} {healAmount:F1} здоровья.");
        }

        // «Разрушение брони» (Рубило): после физического попадания есть 20/40/60/80/100% шанс
        // снять ещё 1 ед. брони сверх обычной деградации DamageCalculator.
        if (!result.WasBlocked && weapon.DamageType == DamageType.Physical && weapon.ArmorBreakLevel > 0)
        {
            float extraWearChance = ItemEffectBalance.ArmorBreakExtraWearChancePercent(weapon.ArmorBreakLevel);
            if (combatRandom.Value01() * 100f < extraWearChance)
            {
                target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - 1f);
                Log($"[Combat] «Разрушение брони» снижает физ. защиту {target.DisplayName} ещё на 1.");
            }
        }

        // "Насквозь" (3.10, Стремительное копьё): часть урона дополнительно проходит по всем
        // остальным живым противникам в комнате, помимо выбранной цели.
        if (attacker.IsPlayer && weapon.PiercingLevel > 0)
        {
            float splashDamage = damage * ItemEffectBalance.PiercingSplashPercent(weapon.PiercingLevel) / 100f;
            if (splashDamage > 0f)
            {
                foreach (var other in Enemies)
                {
                    if (other == target || !other.IsAlive)
                    {
                        continue;
                    }

                    var splashResult = DamageCalculator.ApplyDamage(other, splashDamage, weapon.DamageType);
                    EmitHitResolved(other, splashResult.DamageToHP, false, splashResult.WasBlocked);
                    Log($"[Combat] «Насквозь» задевает {other.DisplayName}: {splashResult.DamageToHP:F1} урона здоровью.");
                }
            }
        }

        // "Шипы": если атака не пробила броню (полный блок) — отражается часть заблокированного
        // урона. Прогрессия 10/20/30/40/50%, жёсткий потолок — 50%.
        if (target.SkillThornsLevel > 0 && weapon.DamageType == DamageType.Physical)
        {
            float reflectPercent = BalanceClamps.ThornsReflectPercent(target.SkillThornsLevel) / 100f;
            float reflectedDamage = 0f;

            if (result.WasBlocked)
            {
                reflectedDamage = damage * reflectPercent;
            }
            else if (target.SkillThornsLevel >= 5)
            {
                reflectedDamage = damage * 0.5f;
            }

            if (reflectedDamage > 0f)
            {
                reflectedDamage = DamageCalculator.ApplyDirectDamage(attacker, reflectedDamage);
                EmitHitResolved(attacker, reflectedDamage, false, false);
                Log($"[Combat] Шипы {target.DisplayName} отражают {reflectedDamage:F1} урона по {attacker.DisplayName}.");
                if (!attacker.IsAlive)
                {
                    Log($"[Combat] {attacker.DisplayName} погибает от шипов.");
                }
            }
        }

        // "Заморозка": накладывается при любом уроне по HP; "разбивается" физическим уроном.
        if (attacker.SkillFreezeLevel > 0)
        {
            ApplyFreezeOnHit(attacker, weapon, target, result);
        }

        // "Кровотечение": только от физического урона, реально пробившего защиту (не от
        // минимального прохождения при полном блоке, см. 3.3).
        if (attacker.SkillBleedLevel > 0 && weapon.DamageType == DamageType.Physical && !result.WasBlocked && target.IsAlive)
        {
            ApplyBleed(attacker, target, attacker.SkillBleedLevel);
        }

        // "Отравленный клинок" (3.11, Плут): собственный яд Плута, отдельный от ядовитого укуса
        // монстров (PoisonStacks/PoisonTimer) — та же логическая точка, что и Кровотечение выше.
        if (attacker.SkillPoisonedBladeLevel > 0 && weapon.DamageType == DamageType.Physical && !result.WasBlocked)
        {
            ApplyRoguePoison(attacker, target);
        }

        // 2.4: пассивки монстров, срабатывающие ПРИ АТАКЕ (симметрично блоку выше, но для
        // не-игрока — у игрока MonsterPassiveSkillId всегда SkillId.None).
        ApplyMonsterPassiveOnAttack(attacker, target, result, damage);

        if (!target.IsAlive)
        {
            Log($"[Combat] {target.DisplayName} погибает.");
        }

        // 3.11 "Боевая регенерация" (Варвар): каждый N-й удар по здоровью.
        // С 10.09.2026 засчитываются только удары, снявшие HP. Восстановление 6% MaxHP, если цель выжила.
        // Урон -> потом восстановление
        // (ГДД: "сначала урон... затем, если персонаж выжил — восстановление").
        if (target.SkillCombatRegenLevel > 0 && result.DamageToHP > 0f)
        {
            target.HitsTakenSinceLastRegen++;
            int regenThreshold = BalanceClamps.CombatRegenHitsRequired(target.SkillCombatRegenLevel);
            if (target.HitsTakenSinceLastRegen >= regenThreshold && target.IsAlive && target.CombatRegenCooldownRemaining <= 0f)
            {
                target.HitsTakenSinceLastRegen = 0;
                float regenAmount = target.MaxHP * (BalanceClamps.CombatRegenHealPercent / 100f);
                regenAmount = target.Heal(regenAmount);
                target.CombatRegenCooldownRemaining = BalanceClamps.CombatRegenCooldownSeconds;
                Log($"[Combat] «Боевая регенерация» восстанавливает {target.DisplayName} {regenAmount:F1} здоровья (здоровье {target.CurrentHP:F1}/{target.MaxHP:F1}).");
            }
        }
    }

    static int FreezeMaxStacksByLevel(int level)
    {
        switch (level)
        {
            case 1: return 2;
            case 2: return 4;
            case 3: return 6;
            case 4: return 8;
            default: return 10;
        }
    }

    // Boss framework: заморозка от способности босса. Отличается от ApplyFreezeOnHit тем, что не
    // привязана к попаданию оружия и выдаёт СРАЗУ несколько зарядов: по одному за каст босс копил
    // бы до конца боя. Правила иммунитета и «Упёртости» те же — цепочки заморозок системно
    // невозможны, потому что после разморозки включается FreezeImmune.
    void ApplyBossFreeze(CombatantRuntime boss, BossAbilityConfig ability)
    {
        int stacks = Mathf.Max(0, ability.freezeStacks);
        if (stacks == 0 || Player.IsFrozen || Player.FreezeImmune)
        {
            return;
        }

        if (IgnoresDebuffs(Player))
        {
            Log($"[Combat] «Упёртость» защищает {Player.DisplayName} от «{ability.displayName}».");
            return;
        }

        Player.FreezeStacks = Mathf.Min(Player.FreezeStacks + stacks, 10);
        Player.FreezeStackTimer = Player.AdjustNegativeStatusDuration(3f);
        ActiveSkillActivated?.Invoke(boss, ability.displayName);
        Log($"[Combat] {boss.DisplayName}: «{ability.displayName}» — {Player.DisplayName} получает {stacks} зарядов заморозки ({Player.FreezeStacks}/10).");

        if (Player.FreezeStacks >= 10)
        {
            Player.IsFrozen = true;
            Player.FreezeTimer = Player.AdjustNegativeStatusDuration(5f);
            Log($"[Combat] {Player.DisplayName} замораживается на 5 секунд!");
        }
    }

    void ApplyFreezeOnHit(CombatantRuntime attacker, WeaponAttackState weapon, CombatantRuntime target, DamageCalculator.DamageResult result)
    {
        if (target.IsFrozen)
        {
            // Физический урон по замороженной цели "разбивает" заморозку: доп. магический урон,
            // равный фактически полученному (после защиты) физическому урону, затем иммунитет 5 сек.
            if (weapon.DamageType == DamageType.Physical && !target.FreezeImmune && !result.WasBlocked)
            {
                float bonusMagicDamage = result.DamageToHP;
                var shatter = DamageCalculator.ApplyDamage(target, bonusMagicDamage, DamageType.Magical);
                EmitHitResolved(target, shatter.DamageToHP, false, shatter.WasBlocked);
                Log($"[Combat] Заморозка {target.DisplayName} разбивается! +{bonusMagicDamage:F1} доп. магического урона.");

                target.IsFrozen = false;
                target.FreezeStacks = 0;
                target.FreezeImmune = true;
                target.FreezeImmuneTimer = 5f;
            }

            return;
        }

        if (target.FreezeImmune || result.WasBlocked)
        {
            return;
        }

        // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель полностью игнорирует НОВЫЕ стаки заморозки.
        if (IgnoresDebuffs(target))
        {
            Log($"[Combat] «Упёртость» защищает {target.DisplayName} от заморозки.");
            return;
        }

        int maxStacks = FreezeMaxStacksByLevel(attacker.SkillFreezeLevel);
        target.FreezeStacks = Mathf.Min(target.FreezeStacks + 1, maxStacks);
        target.FreezeStackTimer = target.AdjustNegativeStatusDuration(3f);

        Log($"[Combat] {target.DisplayName} получает заряд заморозки ({target.FreezeStacks}/{maxStacks}).");

        if (target.FreezeStacks >= 10)
        {
            target.IsFrozen = true;
            target.FreezeTimer = target.AdjustNegativeStatusDuration(5f);
            Log($"[Combat] {target.DisplayName} замораживается на 5 секунд!");
        }
    }

    void DetonateBleedFromCriticalHit(CombatantRuntime target)
    {
        float detonationDamage = BleedRules.DetonationDamage(target.BleedDamagePerSecond, target.BleedTimer);
        detonationDamage = DamageCalculator.ApplyDirectDamage(target, detonationDamage);
        target.BleedTimer = target.AdjustNegativeStatusDuration(BleedRules.DurationForLevel(target.BleedLevel));
        EmitHitResolved(target, detonationDamage, true, false);
        Log($"[Combat] Критический удар детонирует кровотечение на {target.DisplayName}: {detonationDamage:F1} урона; длительность обновлена.");
    }

    void ApplyBleed(CombatantRuntime source, CombatantRuntime target, int bleedLevel)
    {
        // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель полностью игнорирует новое кровотечение.
        if (IgnoresDebuffs(target))
        {
            Log($"[Combat] «Упёртость» защищает {target.DisplayName} от кровотечения.");
            return;
        }

        bool isFreshApplication = !target.HasBleed;

        target.HasBleed = true;
        target.BleedDamagePerSecond = BleedRules.DamagePerSecond(bleedLevel);
        target.BleedTimer = target.AdjustNegativeStatusDuration(BleedRules.DurationForLevel(bleedLevel)); // не стакается, обновляет длительность
        target.BleedLevel = bleedLevel;
        target.BleedSource = source;

        // Повторное наложение обновляет только длительность (см. 3.9), а не расписание тиков урона:
        // если сбрасывать аккумулятор на каждый удар, при атаках чаще раза в секунду кровотечение
        // никогда не успевало бы тикнуть.
        if (isFreshApplication)
        {
            target.BleedTickAccumulator = 0f;
            Log($"[Combat] {target.DisplayName} получает кровотечение ({target.BleedDamagePerSecond:F1}/сек).");
        }
    }

    // 3.11 (Плут) — "В глаз"/"Ускользание" накладывают/обновляют Скрытность: длительность всегда
    // фиксированные 3с (StealthStatus.DurationSeconds), повторное наложение просто обновляет таймер.
    void GrantOrRefreshStealth(CombatantRuntime combatant)
    {
        combatant.IsStealthed = true;
        combatant.StealthTimer = StealthStatus.DurationSeconds;
    }

    // 3.11 (Плут) "Отравленный клинок": собственный яд Плута на ЦЕЛИ, полностью отдельный от
    // PoisonStacks/PoisonTimer (яд Ядовитого паучка, 2.4) — см. RoguePoisonStacksOnTarget на
    // CombatantRuntime. В Скрытности стаки/максимум удваиваются.
    void ApplyRoguePoison(CombatantRuntime attacker, CombatantRuntime target)
    {
        // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель полностью игнорирует новый яд Плута.
        if (IgnoresDebuffs(target))
        {
            Log($"[Combat] «Упёртость» защищает {target.DisplayName} от «Отравленного клинка».");
            return;
        }

        int maxStacks = attacker.SkillPoisonedBladeLevel;
        int stacksToAdd = 1;
        if (attacker.IsStealthed)
        {
            maxStacks *= 2;
            stacksToAdd = 2;
        }

        if (target.RoguePoisonStacksOnTarget < maxStacks)
            target.RoguePoisonStacksOnTarget = Mathf.Min(target.RoguePoisonStacksOnTarget + stacksToAdd, maxStacks);
        target.RoguePoisonSource = attacker;
        target.RoguePoisonTimer = target.AdjustNegativeStatusDuration(3f);
        Log($"[Combat] «Отравленный клинок» накладывает яд на {target.DisplayName} ({target.RoguePoisonStacksOnTarget}/{maxStacks} зарядов).");
    }

    // "Отравленный клинок": каждый заряд раз в секунду наносит 1 + 2% среднего урона оружия
    // источника до критов и срабатываний. У парного оружия берётся среднее, а не сумма, чтобы
    // второй таймер атаки не удваивал ещё и силу тика. Весь тик округляется один раз.
    void TickRoguePoison(CombatantRuntime target, float deltaTime)
    {
        if (target.RoguePoisonStacksOnTarget <= 0)
        {
            return;
        }

        target.RoguePoisonTimer -= deltaTime;
        target.RoguePoisonTickAccumulator += deltaTime;

        while (target.RoguePoisonTickAccumulator >= 1f && target.RoguePoisonStacksOnTarget > 0 && target.IsAlive)
        {
            target.RoguePoisonTickAccumulator -= 1f;
            float damagePerStack = RoguePoisonDamagePerStack(target.RoguePoisonSource);
            float damagePerSecond = DamageCalculator.ApplyDirectDamage(target, target.RoguePoisonStacksOnTarget * damagePerStack);
            var source = target.RoguePoisonSource;
            if (source != null)
            {
                int currentCap = source.SkillPoisonedBladeLevel * (source.IsStealthed ? 2 : 1);
                if (target.RoguePoisonStacksOnTarget > currentCap) target.RoguePoisonStacksOnTarget--;
            }
            EmitHitResolved(target, damagePerSecond, false, false);
            Log($"[Combat] {target.DisplayName} получает {damagePerSecond:F1} урона от «Отравленного клинка» (здоровье {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

            if (!target.IsAlive)
            {
                Log($"[Combat] {target.DisplayName} погибает от «Отравленного клинка».");
            }
        }

        if (target.RoguePoisonTimer <= 0f)
        {
            target.RoguePoisonStacksOnTarget = 0;
        }
    }

    public static float RoguePoisonDamagePerStack(CombatantRuntime source)
    {
        if (source == null || source.Weapons == null || source.Weapons.Count == 0) return 1f;
        float totalAverageDamage = 0f;
        int weaponCount = 0;
        foreach (var weapon in source.Weapons)
        {
            if (weapon == null) continue;
            totalAverageDamage += (weapon.DamageMin + weapon.DamageMax) * 0.5f;
            weaponCount++;
        }
        return weaponCount > 0 ? 1f + totalAverageDamage / weaponCount * 0.02f : 1f;
    }

    // 2.4: пассивки монстров, срабатывающие ПРИ АТАКЕ (в отличие от периодических — см.
    // TickMonsterPeriodicPassives). attacker всегда монстр здесь (у игрока MonsterPassiveSkillId всегда None).
    void ApplyMonsterPassiveOnAttack(CombatantRuntime attacker, CombatantRuntime target, DamageCalculator.DamageResult result, float attackDamage)
    {
        if (attacker.IsPlayer || attacker.MonsterPassiveSkillId == SkillId.None)
        {
            return;
        }

        switch (attacker.MonsterPassiveSkillId)
        {
            case SkillId.MonsterCorrosion:
                // «Коррозия»: при каждой атаке паука 15% силы удара напрямую изнашивает
                // физическую защиту цели. Эффект не зависит от того, пробил ли сам удар броню.
                // Яд прежнего паучка сохранён как вторая часть этой пассивки и, как раньше,
                // накладывается только при попадании по HP.
                float armorDamage = DamageCalculator.RoundPoints(attackDamage * 0.15f);
                if (armorDamage > 0f)
                {
                    float armorBefore = target.PhysicalDefenseCurrent;
                    target.PhysicalDefenseCurrent = Mathf.Max(0f, armorBefore - armorDamage);
                    float armorLost = armorBefore - target.PhysicalDefenseCurrent;
                    if (armorLost > 0f)
                    {
                        Log($"[Combat] Коррозия {attacker.DisplayName} разъедает физ. защиту {target.DisplayName} на {armorLost:F1}.");
                    }
                }

                // При попадании по здоровью накладывает яд (3 сек, 4 урона/сек, до 3 стаков).
                // 3.11 «Упёртость»: при достаточной Ярости цель игнорирует только дебафф яда,
                // но не прямой урон коррозии по броне.
                if (!result.WasBlocked && !IgnoresDebuffs(target))
                {
                    target.PoisonStacks = Mathf.Min(target.PoisonStacks + 1, 3);
                    target.PoisonTimer = target.AdjustNegativeStatusDuration(3f);
                    Log($"[Combat] {target.DisplayName} получает яд ({target.PoisonStacks}/3 зарядов).");
                }
                break;

            case SkillId.MonsterStunningScream:
                // Separate magical scream. Only damage to HP disrupts active skills.
                if (combatRandom.Value01() < 0.15f && target.IsAlive)
                {
                    var scream = DamageCalculator.ApplyDamage(target, attackDamage, DamageType.Magical);
                    EmitHitResolved(target, scream.DamageToHP, false, scream.WasBlocked);
                    if (scream.DamageToHP > 0f && target.IsAlive && !IgnoresDebuffs(target))
                    {
                        DisruptPlayerActiveSkills(target.AdjustNegativeStatusDuration(4f));
                        Log($"[Combat] Оглушающий крик {attacker.DisplayName} задерживает активные навыки {target.DisplayName}.");
                    }
                }
                break;

            case SkillId.MonsterSlowCurse:
                // "Если урон Колдуна проходит по здоровью персонажа, скорость атаки персонажа снижается
                // на 30% на 3 секунды (не стакается, повторное попадание обновляет длительность)."
                // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель игнорирует новый дебафф скорости.
                if (!result.WasBlocked && !IgnoresDebuffs(target))
                {
                    var existing = target.ActiveDebuffs.Find(d => d.Id == "warlock_slow");
                    if (existing != null)
                    {
                        existing.RemainingTime = target.AdjustNegativeStatusDuration(3f);
                    }
                    else
                    {
                        target.ActiveDebuffs.Add(new ActiveDebuff { Id = "warlock_slow", RemainingTime = target.AdjustNegativeStatusDuration(3f), AttackSpeedMultiplier = 0.7f });
                    }
                    Log($"[Combat] Проклятие замедления {attacker.DisplayName} снижает скорость атаки {target.DisplayName} на 30% (3 сек).");
                }
                break;
        }
    }

    // "Тёмное исцеление" / "Двойной удар": пассивки на собственном периодическом таймере, независимом
    // от таймера атаки оружия (в отличие от обычных атак и мгновенных пассивок из ApplyMonsterPassiveOnAttack).
    void TickMonsterPeriodicPassives(float deltaTime)
    {
        foreach (var enemy in Enemies)
        {
            if (!enemy.IsAlive || enemy.IsFrozen || enemy.MonsterPassiveSkillId == SkillId.None)
            {
                continue;
            }

            if (enemy.MonsterPassiveSkillId == SkillId.MonsterDarkHeal)
            {
                enemy.MonsterPassiveCooldownTimer -= deltaTime;
                if (enemy.MonsterPassiveCooldownTimer <= 0f)
                {
                    enemy.MonsterPassiveCooldownTimer = 8f;
                    var healTarget = PickDarkHealTarget(enemy);
                    if (healTarget != null)
                    {
                        float healAmount = healTarget.MaxHP * 0.10f;
                        healAmount = healTarget.Heal(healAmount);
                        Log($"[Combat] Тёмное исцеление {enemy.DisplayName} восстанавливает {healTarget.DisplayName} {healAmount:F1} здоровья.");
                    }
                }
            }
            else if (enemy.MonsterPassiveSkillId == SkillId.MonsterDoubleStrike)
            {
                enemy.MonsterPassiveCooldownTimer -= deltaTime;
                if (enemy.MonsterPassiveCooldownTimer <= 0f && enemy.Weapons.Count > 0)
                {
                    enemy.MonsterPassiveCooldownTimer = 6f;
                    Log($"[Combat] {enemy.DisplayName} наносит двойной удар!");
                    ResolveAttack(enemy, enemy.Weapons[0], 1.5f);
                }
            }
        }
    }

    // "себе или ближайшему союзнику в комнате" — интерпретация: лечит того из (себя + живых союзников
    // в Enemies), у кого сейчас наименьший % HP от максимума (ближе всех к смерти = приоритетная цель
    // для лечения; при равенстве побеждает первый найденный в списке).
    CombatantRuntime PickDarkHealTarget(CombatantRuntime healer)
    {
        CombatantRuntime best = healer;
        float bestPercent = healer.MaxHP > 0f ? healer.CurrentHP / healer.MaxHP : 1f;

        foreach (var other in Enemies)
        {
            if (other == healer || !other.IsAlive)
            {
                continue;
            }

            float percent = other.MaxHP > 0f ? other.CurrentHP / other.MaxHP : 1f;
            if (percent < bestPercent)
            {
                best = other;
                bestPercent = percent;
            }
        }

        return best.CurrentHP < best.MaxHP ? best : null; // никто не ранен -> лечить некого
    }

    // "Яд": тикает раз в секунду, не зависит от таймера атаки; стаки истекают все разом по общему таймеру.
    void TickPoison(CombatantRuntime target, float deltaTime)
    {
        if (target.PoisonStacks <= 0)
        {
            return;
        }

        target.PoisonTimer -= deltaTime;
        target.PoisonTickAccumulator += deltaTime;

        float damagePerSecond = target.PoisonStacks * 4f;
        while (target.PoisonTickAccumulator >= 1f && target.PoisonStacks > 0 && target.IsAlive)
        {
            target.PoisonTickAccumulator -= 1f;
            float dealt = DamageCalculator.ApplyDirectDamage(target, damagePerSecond);
            EmitHitResolved(target, dealt, false, false);
            Log($"[Combat] {target.DisplayName} получает {damagePerSecond:F1} урона от яда (здоровье {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

            if (!target.IsAlive)
            {
                Log($"[Combat] {target.DisplayName} погибает от яда.");
            }
        }

        if (target.PoisonTimer <= 0f)
        {
            target.PoisonStacks = 0;
        }
    }

    CombatantRuntime GetPlayerTarget()
    {
        if (Player.Target == null || !Player.Target.IsAlive)
        {
            Player.Target = GetDefaultTarget();
        }

        return Player.Target;
    }

    CombatantRuntime GetDefaultTarget()
    {
        return Enemies.FirstOrDefault(e => e.IsAlive);
    }

    // 4.2: программный аналог клика по противнику — ручной выбор цели игроком.
    public void SetPlayerTarget(CombatantRuntime target)
    {
        if (target != null && target.IsAlive && Enemies.Contains(target))
        {
            Player.Target = target;
        }
    }

    void CheckCombatEnd()
    {
        if (!Player.IsAlive || Enemies.All(e => !e.IsAlive))
        {
            EndCombat();
        }
    }
}
