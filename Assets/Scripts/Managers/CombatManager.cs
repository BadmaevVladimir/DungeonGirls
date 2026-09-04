using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    ICombatRandom combatRandom = new UnityCombatRandom();
    bool suppressCombatLogs;

    public CombatantRuntime Player { get; private set; }
    public List<CombatantRuntime> Enemies { get; private set; } = new List<CombatantRuntime>();
    public bool IsCombatActive { get; private set; }

    // Принудительное завершение используется только при явном выходе игрока из забега через паузу.
    public void AbortCombat()
    {
        IsCombatActive = false;
        if (Player != null) Player.IsBerserkActive = false;
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

    void Log(string message)
    {
        if (!suppressCombatLogs) Debug.Log(message);
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
            });
        }
    }

    public bool IsSkillReady(int slotIndex) =>
        Player != null && Player.IsAlive && slotIndex >= 0 && slotIndex < ActiveSkills.Count &&
        ActiveSkills[slotIndex].Data.skillType == ActiveSkillType.Cooldown &&
        ActiveSkills[slotIndex].CooldownTimer <= 0f;

    public float SkillCooldownRemaining(int slotIndex) =>
        slotIndex >= 0 && slotIndex < ActiveSkills.Count ? Mathf.Max(0f, ActiveSkills[slotIndex].CooldownTimer) : 0f;

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

        // 3.11 "Дымовая граната" (уникальная активка Плута): при активации даёт Скрытность и
        // заряжает гарантированные криты на N последующих ОБЫЧНЫХ атак — не бьёт сама.
        if (slot.Data.skillId == SkillId.SmokeBomb)
        {
            GrantOrRefreshStealth(Player);
            Player.SmokeBombGuaranteedCritsRemaining = Player.UniqueSmokeBombLevel;
            Log($"[Combat] «Дымовая граната»: {Player.DisplayName} получает Скрытность и {Player.UniqueSmokeBombLevel} гарантированных крита(ов).");
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
        IsCombatActive = true;

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
            Player.CurrentHP = Mathf.Min(Player.MaxHP, Player.CurrentHP + Player.MaxHP * ItemEffectBalance.JustAScratchHealPercent(Player.ItemJustAScratchLevel) / 100f);
        }

        Log($"[Combat] Бой начался: {Player.DisplayName} (HP {Player.CurrentHP:F1}) против {Enemies.Count} противников.");
    }

    public void EndCombat()
    {
        if (!IsCombatActive)
        {
            return;
        }

        IsCombatActive = false;

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
                float tickDamage = Mathf.Max(1f, Player.CurrentHP * 0.01f);
                Player.CurrentHP = Mathf.Max(0f, Player.CurrentHP - tickDamage);
                Player.NotifyHpDamageResolved();
                Log($"[Combat] «Берсерк» наносит {tickDamage:F1} урона {Player.DisplayName} (HP {Player.CurrentHP:F1}/{Player.MaxHP:F1}).");
            }
        }

        TickMonsterPeriodicPassives(deltaTime); // "Тёмное исцеление" / "Двойной удар" (2.4)
        TickBossHeavyAttacks(deltaTime); // легаси-путь: боссы БЕЗ BossKitData (см. BossEncounter ниже)
        TickBossEncounters(deltaTime); // boss framework: боссы С BossKitData

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
            if (!enemy.IsAlive || !enemy.IsBoss || enemy.Weapons.Count == 0 || enemy.BossEncounter != null)
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
    void TickBossEncounters(float deltaTime)
    {
        foreach (var enemy in Enemies)
        {
            if (!IsCombatActive || !Player.IsAlive)
            {
                return;
            }

            if (!enemy.IsAlive || enemy.BossEncounter == null)
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

                ActiveSkillActivated?.Invoke(enemy, newPhase.phaseName);
                Log($"[Boss] {enemy.DisplayName} переходит в фазу «{newPhase.phaseName}» (HP {hpPercent:F0}%).");
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
                ResolveAttack(boss, boss.Weapons[0], ability.damageMultiplier, isRegularAttack: false);
                break;

            case BossAbilityEffectKind.ShieldPool:
                boss.ShieldPoolMax = ability.shieldAmount;
                boss.ShieldPoolCurrent = ability.shieldAmount;
                boss.ShieldPoolExpireTimer = ability.shieldDurationSeconds > 0f ? ability.shieldDurationSeconds : float.PositiveInfinity;
                ActiveSkillActivated?.Invoke(boss, ability.displayName);
                Log($"[Combat] {boss.DisplayName} активирует «{ability.displayName}»: щит {ability.shieldAmount:F0}.");
                break;
        }
    }

    // 3.9 "Амбидекстрия": у каждого оружия персонажа свой независимый таймер атаки по своей
    // собственной скорости — обрабатываются в отдельных циклах, а не слитно одним таймером.
    void TickCombatant(CombatantRuntime attacker, float deltaTime)
    {
        foreach (var weapon in attacker.Weapons)
            weapon.SecondsSinceLastAttack += Mathf.Max(0f, deltaTime);

        // 3.9 "Заморозка": замороженный участник не может атаковать; таймеры атаки не копятся.
        // AttackLocked — тот же эффект "не копится/не бьёт", но временно и по воле UI (см.
        // CombatantRuntime.AttackLocked) — обычная атака не должна прерывать анимацию скилла.
        if (!attacker.IsAlive || attacker.IsFrozen || attacker.AttackLocked)
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

            target.CurrentHP -= tickDamage;
            target.NotifyHpDamageResolved();
            HitResolved?.Invoke(target, tickDamage, isCriticalTick, false);
            Log($"[Combat] {target.DisplayName} получает {tickDamage:F1} урона от кровотечения{(isCriticalTick ? " (крит!)" : string.Empty)} (HP {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

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
    void ResolveAttack(CombatantRuntime attacker, WeaponAttackState weapon, float damageMultiplier = 1f, bool isRegularAttack = true)
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

        damage *= 1f + attacker.FoodDamagePercent / 100f;
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
            float convertedSources = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems;
            critMultiplier += convertedSources * 2f;
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
        DamageCalculator.DamageResult result;
        if (weapon.PrototypeEffect == WeaponPrototypeEffectId.SpellEater)
        {
            result = DamageCalculator.ApplySpellEaterPhysicalDamage(target,
                (damage + armorPenetrationDamage) * paranoiaMultiplier,
                weapon.ArmorIgnorePercent, out float shieldRemoved);
            if (magicShieldBeforeAttack > 0f && target.MagicShieldCurrent <= 0f)
                weapon.PrototypeAccumulatedDamage += shieldRemoved;
        }
        else if (weapon.PrototypeEffect == WeaponPrototypeEffectId.DayAndNight)
        {
            float physicalShare = Mathf.Clamp01(weapon.PrototypePrimaryValue / 100f);
            float magicalShare = Mathf.Clamp01(weapon.PrototypeSecondaryValue / 100f);
            float shareTotal = physicalShare + magicalShare;
            if (shareTotal <= 0f) { physicalShare = 0.5f; magicalShare = 0.5f; shareTotal = 1f; }
            physicalShare /= shareTotal;
            magicalShare /= shareTotal;
            var physical = DamageCalculator.ApplyDamage(target,
                (damage * physicalShare + armorPenetrationDamage) * paranoiaMultiplier,
                DamageType.Physical, weapon.ArmorIgnorePercent);
            var magical = DamageCalculator.ApplyDamage(target,
                damage * magicalShare * paranoiaMultiplier, DamageType.Magical);
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
            Log($"[Combat] {attacker.DisplayName} атакует {target.DisplayName}{(isCrit ? " (крит!)" : string.Empty)}: урон {damage:F1} полностью заблокирован{blockSuffix}.");
        }
        else
        {
            Log($"[Combat] {attacker.DisplayName} атакует {target.DisplayName}{(isCrit ? " (крит!)" : string.Empty)}: {result.DamageToHP:F1} урона по HP (осталось {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");
        }

        // 4.7: единая точка для всплывающих цифр урона и тряски спрайта цели — покрывает обычные
        // атаки оружием и каждый отдельный удар активного навыка (цикл в TryActivateUniqueActiveSkill
        // вызывает ResolveAttack по разу на удар, так что события уже приходят по одному, не суммарно).
        HitResolved?.Invoke(target, result.DamageToHP, isCrit, result.WasBlocked);

        if (isRegularAttack && weapon.PrototypeEffect == WeaponPrototypeEffectId.LightningSpear &&
            PrototypeWeaponRules.AdvanceLightningCounter(weapon) && target.IsAlive)
        {
            float lightningDamage = baseAttackDamage * Mathf.Max(0f, weapon.PrototypePrimaryValue) / 100f;
            var lightning = DamageCalculator.ApplyDamage(target, lightningDamage, DamageType.Magical);
            HitResolved?.Invoke(target, lightning.DamageToHP, false, lightning.WasBlocked);
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
            HitResolved?.Invoke(target, embraceResult.DamageToHP, false, embraceResult.WasBlocked);
        }

        // "Вампиризм" (3.10, Кровавый меч): при крите восстанавливает атакующему часть урона крита здоровьем.
        if (isCrit && weapon.VampirismLevel > 0)
        {
            float healAmount = damage * ItemEffectBalance.VampirismHealPercentOfCritDamage(weapon.VampirismLevel) / 100f;
            attacker.CurrentHP = Mathf.Min(attacker.MaxHP, attacker.CurrentHP + healAmount);
            Log($"[Combat] «Вампиризм» восстанавливает {attacker.DisplayName} {healAmount:F1} HP.");
        }

        // «Разрушение брони» (Рубило): после физического попадания есть 25/50/75/100/100% шанс
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
                    HitResolved?.Invoke(other, splashResult.DamageToHP, false, splashResult.WasBlocked);
                    Log($"[Combat] «Насквозь» задевает {other.DisplayName}: {splashResult.DamageToHP:F1} урона по HP.");
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
                attacker.CurrentHP -= reflectedDamage;
                attacker.NotifyHpDamageResolved();
                HitResolved?.Invoke(attacker, reflectedDamage, false, false);
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

        // 3.11 "Боевая регенерация" (Варвар): каждый N-й ПОЛУЧЕННЫЙ удар (блокированный или нет —
        // ГДД "каждые N полученных ударов", читаем как любую разрешённую атаку по цели, а не только
        // прошедшую по HP; [ПРЕДПОЛОЖЕНИЕ] — если задумывалось иначе, это расхождение с ГДД, не
        // угаданное молча) восстанавливает 6% MaxHP, если цель выжила. Урон -> потом восстановление
        // (ГДД: "сначала урон... затем, если персонаж выжил — восстановление").
        if (target.SkillCombatRegenLevel > 0)
        {
            target.HitsTakenSinceLastRegen++;
            int regenThreshold = BalanceClamps.CombatRegenHitsRequired(target.SkillCombatRegenLevel);
            if (target.HitsTakenSinceLastRegen >= regenThreshold && target.IsAlive && target.CombatRegenCooldownRemaining <= 0f)
            {
                target.HitsTakenSinceLastRegen = 0;
                float regenAmount = target.MaxHP * (BalanceClamps.CombatRegenHealPercent / 100f);
                target.CurrentHP = Mathf.Min(target.MaxHP, target.CurrentHP + regenAmount);
                target.CombatRegenCooldownRemaining = BalanceClamps.CombatRegenCooldownSeconds;
                Log($"[Combat] «Боевая регенерация» восстанавливает {target.DisplayName} {regenAmount:F1} HP (HP {target.CurrentHP:F1}/{target.MaxHP:F1}).");
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

    void ApplyFreezeOnHit(CombatantRuntime attacker, WeaponAttackState weapon, CombatantRuntime target, DamageCalculator.DamageResult result)
    {
        if (target.IsFrozen)
        {
            // Физический урон по замороженной цели "разбивает" заморозку: доп. магический урон,
            // равный фактически полученному (после защиты) физическому урону, затем иммунитет 5 сек.
            if (weapon.DamageType == DamageType.Physical && !target.FreezeImmune && !result.WasBlocked)
            {
                float bonusMagicDamage = result.DamageToHP;
                DamageCalculator.ApplyMagicalDamage(target, bonusMagicDamage);
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

        Log($"[Combat] {target.DisplayName} получает стак заморозки ({target.FreezeStacks}/{maxStacks}).");

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
        target.CurrentHP -= detonationDamage;
        target.NotifyHpDamageResolved();
        target.BleedTimer = target.AdjustNegativeStatusDuration(BleedRules.DurationForLevel(target.BleedLevel));
        HitResolved?.Invoke(target, detonationDamage, true, false);
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

        target.RoguePoisonStacksOnTarget = Mathf.Min(target.RoguePoisonStacksOnTarget + stacksToAdd, maxStacks);
        target.RoguePoisonTimer = target.AdjustNegativeStatusDuration(3f);
        Log($"[Combat] «Отравленный клинок» накладывает яд на {target.DisplayName} ({target.RoguePoisonStacksOnTarget}/{maxStacks} стаков).");
    }

    // "Отравленный клинок": тикает раз в секунду, урон/сек = текущее число стаков (1:1, В ОТЛИЧИЕ
    // от монстрового яда, где урон/сек = стаки×4 — см. TickPoison). Стаки истекают все разом по
    // общему таймеру, зеркально TickPoison, но на отдельных Rogue*-полях.
    void TickRoguePoison(CombatantRuntime target, float deltaTime)
    {
        if (target.RoguePoisonStacksOnTarget <= 0)
        {
            return;
        }

        target.RoguePoisonTimer -= deltaTime;
        target.RoguePoisonTickAccumulator += deltaTime;

        float damagePerSecond = target.RoguePoisonStacksOnTarget;
        while (target.RoguePoisonTickAccumulator >= 1f && target.RoguePoisonStacksOnTarget > 0 && target.IsAlive)
        {
            target.RoguePoisonTickAccumulator -= 1f;
            target.CurrentHP -= damagePerSecond;
            target.NotifyHpDamageResolved();
            HitResolved?.Invoke(target, damagePerSecond, false, false);
            Log($"[Combat] {target.DisplayName} получает {damagePerSecond:F1} урона от «Отравленного клинка» (HP {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

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
                float armorDamage = Mathf.Max(0f, attackDamage) * 0.15f;
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
                    Log($"[Combat] {target.DisplayName} получает яд ({target.PoisonStacks}/3 стаков).");
                }
                break;

            case SkillId.MonsterStunningScream:
                // "15% шанс при атаке снизить шанс крита персонажа на 20% на 4 сек." — на атаке, не на попадании.
                // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель игнорирует крит-дебафф.
                if (combatRandom.Value01() < 0.15f && !IgnoresDebuffs(target))
                {
                    target.CritChanceDebuffPercent = 20f;
                    target.CritChanceDebuffTimer = target.AdjustNegativeStatusDuration(4f);
                    Log($"[Combat] Оглушающий крик {attacker.DisplayName} снижает шанс крита {target.DisplayName}.");
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
            if (!enemy.IsAlive || enemy.MonsterPassiveSkillId == SkillId.None)
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
                        healTarget.CurrentHP = Mathf.Min(healTarget.MaxHP, healTarget.CurrentHP + healAmount);
                        Log($"[Combat] Тёмное исцеление {enemy.DisplayName} восстанавливает {healTarget.DisplayName} {healAmount:F1} HP.");
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
            target.CurrentHP -= damagePerSecond;
            target.NotifyHpDamageResolved();
            HitResolved?.Invoke(target, damagePerSecond, false, false);
            Log($"[Combat] {target.DisplayName} получает {damagePerSecond:F1} урона от яда (HP {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

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
